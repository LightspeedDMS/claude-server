# CIDX Watch Implementation Plan

## Executive Summary

Replace the current `cidx index --reconcile` approach with a `cidx watch` process that runs for the entire duration of each Claude Code job execution. This will maintain real-time index updates during job execution, eliminating the need for post-execution reconciliation and reducing job completion wait times.

## Current Implementation Analysis

### Current CIDX Integration Points (ClaudeCodeExecutor.cs)

**File**: `src/ClaudeBatchServer.Core/Services/ClaudeCodeExecutor.cs`

**Key Methods**:
1. `HandleCidxOperationsAsync()` (line 448-472) - Entry point for CIDX operations
2. `StartCidxServiceForPreIndexedRepository()` (line 503-552) - Current reconcile implementation
3. `ExecuteCidxCommandAsync()` (line 593-641) - CIDX command execution wrapper
4. `IsCidxReady()` (line 643-672) - Status checking

**Current Workflow**:
1. Check if repository is pre-indexed (`IsRepositoryPreIndexedAsync`)
2. Start CIDX service (`cidx start`)
3. Fix configuration (`cidx fix-config --force`)
4. **Run reconcile (`cidx index --reconcile`)** ← **This is what we're replacing**
5. Set status to "ready"

**Current Reconcile Implementation** (lines 532-541):
```csharp
// Run cidx index --reconcile to index any new changes from git pull
// This is fast since the repo was already fully indexed during registration
_logger.LogInformation("Running cidx index --reconcile for pre-indexed repository job {JobId} (indexing new changes only)", job.Id);
var reconcileResult = await ExecuteCidxCommandAsync("index --reconcile", job.CowPath, userInfo, cancellationToken);
if (reconcileResult.ExitCode != 0)
{
    // Try to stop cidx if reconcile failed
    await ExecuteCidxCommandAsync("stop", job.CowPath, userInfo, CancellationToken.None);
    return (false, "failed", $"Cidx reconcile failed: {reconcileResult.Output}");
}
```

## New CIDX Watch Architecture

### Design Overview

The new implementation will:
1. Launch `cidx watch` as a child process at the start of job execution
2. Keep the watch process running throughout Claude Code execution
3. Send `SIGTERM` to terminate watch when Claude Code finishes
4. Fallback to `--reconcile` if watch fails to start or crashes
5. Maintain process lifecycle management and proper cleanup

### Process Lifecycle Management

**Watch Process States**:
- `NotStarted` - Initial state
- `Starting` - Watch process launching
- `Running` - Watch actively monitoring file changes
- `Failed` - Watch process failed/crashed
- `Terminated` - Watch process properly terminated
- `FallbackReconcile` - Using reconcile fallback

**Process Management Requirements**:
- Track watch process PID for proper termination
- Handle watch process crashes gracefully
- Implement timeout for watch startup
- Ensure cleanup on job cancellation/failure
- No concurrent watch processes per job

## Implementation Plan

### Phase 1: Core Infrastructure (High Priority)

#### 1.1 Add Process Management Infrastructure

**File**: `src/ClaudeBatchServer.Core/Services/ClaudeCodeExecutor.cs`

**New Class Definition**:
```csharp
public class CidxWatchManager : IDisposable
{
    private Process? _watchProcess;
    private readonly ILogger _logger;
    private readonly string _workingDirectory;
    private readonly UserInfo _userInfo;
    private readonly CancellationToken _cancellationToken;
    
    public enum WatchState { NotStarted, Starting, Running, Failed, Terminated, FallbackReconcile }
    public WatchState State { get; private set; } = WatchState.NotStarted;
    public int? ProcessId => _watchProcess?.Id;
    
    public async Task<bool> StartWatchAsync(int timeoutSeconds = 30);
    public async Task<bool> StopWatchAsync(int timeoutSeconds = 10);
    public bool IsWatchRunning();
    public void Dispose();
}
```

**Integration Points**:
- Add `CidxWatchManager` field to `ClaudeCodeExecutor`
- Integrate with existing `ExecuteCidxCommandAsync` infrastructure
- Inherit VOYAGE_API_KEY environment variable handling

#### 1.2 Modify Job Execution Workflow

**Current Job Execution Flow** (`ExecuteAsync` method):
1. Validate input parameters
2. Handle git operations (if enabled)
3. **Handle CIDX operations (current reconcile approach)**
4. Execute Claude Code
5. Return results

**New Job Execution Flow**:
1. Validate input parameters
2. Handle git operations (if enabled)
3. **Start CIDX watch process**
4. Execute Claude Code (with watch running)
5. **Stop CIDX watch process**
6. **Fallback to reconcile if watch failed**
7. Return results

#### 1.3 Replace StartCidxServiceForPreIndexedRepository Method

**Current Method** (lines 503-552): Replace with new watch-based implementation

**New Method Structure**:
```csharp
private async Task<(bool Success, string Status, string ErrorMessage)> StartCidxWatchForPreIndexedRepository(
    Job job, 
    UserInfo userInfo, 
    IJobStatusCallback? statusCallback, 
    CancellationToken cancellationToken)
{
    try
    {
        // 1. Start CIDX service (same as current)
        // 2. Fix configuration (same as current)
        // 3. Launch cidx watch process (NEW)
        // 4. Verify watch is running (NEW)
        // 5. Set status to "watching" (NEW)
        return (true, "watching", string.Empty);
    }
    catch (Exception ex)
    {
        // Log error and fallback to reconcile
        return await FallbackToReconcileAsync(job, userInfo, statusCallback, cancellationToken);
    }
}
```

### Phase 2: Watch Process Implementation (High Priority)

#### 2.1 Implement Watch Process Launching

**New Method**:
```csharp
private async Task<Process?> LaunchCidxWatchAsync(
    string workingDirectory, 
    UserInfo userInfo, 
    CancellationToken cancellationToken)
{
    var processInfo = new ProcessStartInfo
    {
        FileName = "cidx",
        Arguments = "watch", // Simple watch command
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true,
        WorkingDirectory = workingDirectory
    };
    
    // Inherit VOYAGE_API_KEY from existing ExecuteCidxCommandAsync implementation
    var voyageApiKey = _configuration["Cidx:VoyageApiKey"];
    if (!string.IsNullOrEmpty(voyageApiKey))
    {
        processInfo.EnvironmentVariables["VOYAGE_API_KEY"] = voyageApiKey;
    }
    
    if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
    {
        ImpersonateUser(processInfo, userInfo);
    }
    
    var process = new Process { StartInfo = processInfo };
    process.Start();
    
    // Verify watch starts successfully within timeout
    await Task.Delay(2000, cancellationToken); // Give watch 2 seconds to start
    
    if (process.HasExited)
    {
        _logger.LogError("CIDX watch process exited immediately with code {ExitCode}", process.ExitCode);
        return null;
    }
    
    _logger.LogInformation("CIDX watch process started successfully with PID {ProcessId}", process.Id);
    return process;
}
```

#### 2.2 Implement Watch Process Termination

**New Method**:
```csharp
private async Task<bool> TerminateCidxWatchAsync(Process watchProcess, int timeoutSeconds = 10)
{
    try
    {
        if (watchProcess.HasExited)
        {
            _logger.LogInformation("CIDX watch process {ProcessId} already exited", watchProcess.Id);
            return true;
        }
        
        _logger.LogInformation("Sending SIGTERM to CIDX watch process {ProcessId}", watchProcess.Id);
        
        // Send SIGTERM (graceful termination)
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            watchProcess.Kill(false); // SIGTERM on Linux
        }
        else
        {
            watchProcess.Kill(); // Terminate on Windows
        }
        
        // Wait for graceful termination
        var timeout = TimeSpan.FromSeconds(timeoutSeconds);
        var exited = await watchProcess.WaitForExitAsync(timeout);
        
        if (!exited)
        {
            _logger.LogWarning("CIDX watch process {ProcessId} did not terminate gracefully, forcing kill", watchProcess.Id);
            watchProcess.Kill(true); // SIGKILL
            await watchProcess.WaitForExitAsync(TimeSpan.FromSeconds(5));
        }
        
        _logger.LogInformation("CIDX watch process {ProcessId} terminated successfully", watchProcess.Id);
        return true;
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to terminate CIDX watch process {ProcessId}", watchProcess.Id);
        return false;
    }
}
```

### Phase 3: Fallback and Error Handling (High Priority)

#### 3.1 Implement Reconcile Fallback

**New Method**:
```csharp
private async Task<(bool Success, string Status, string ErrorMessage)> FallbackToReconcileAsync(
    Job job, 
    UserInfo userInfo, 
    IJobStatusCallback? statusCallback, 
    CancellationToken cancellationToken)
{
    _logger.LogWarning("CIDX watch failed for job {JobId}, falling back to reconcile approach", job.Id);
    
    job.CidxStatus = "reconciling_fallback";
    if (statusCallback != null)
        await statusCallback.OnStatusChangedAsync(job);
    
    // Use existing reconcile implementation as fallback
    _logger.LogInformation("Running cidx index --reconcile as fallback for job {JobId}", job.Id);
    var reconcileResult = await ExecuteCidxCommandAsync("index --reconcile", job.CowPath, userInfo, cancellationToken);
    
    if (reconcileResult.ExitCode != 0)
    {
        await ExecuteCidxCommandAsync("stop", job.CowPath, userInfo, CancellationToken.None);
        return (false, "failed", $"Cidx reconcile fallback failed: {reconcileResult.Output}");
    }
    
    job.CidxStatus = "ready_via_fallback";
    _logger.LogInformation("Cidx reconcile fallback completed successfully for job {JobId}", job.Id);
    return (true, "ready_via_fallback", string.Empty);
}
```

#### 3.2 Enhanced Error Handling and Logging

**Logging Enhancements**:
- Add structured logging for watch process lifecycle events
- Log watch process PID for debugging
- Add performance metrics (watch startup time, termination time)
- Log fallback triggers and success rates

**Error Scenarios**:
1. Watch process fails to start → Immediate fallback to reconcile
2. Watch process crashes during execution → Continue with job, reconcile at end
3. Watch process termination fails → Log warning, continue with job completion
4. CIDX service not available → Skip watch, use reconcile (existing behavior)

### Phase 4: Integration and Testing (Medium Priority)

#### 4.1 Update Job Status and Callbacks

**New CIDX Status Values**:
- `"watching"` - Watch process is actively monitoring
- `"reconciling_fallback"` - Using reconcile due to watch failure
- `"ready_via_fallback"` - Ready via reconcile fallback
- `"watch_terminated"` - Watch process successfully terminated

**Status Callback Integration**:
- Update job status when watch starts
- Update job status when watch terminates
- Update job status during fallback operations

#### 4.2 Configuration Options

**New Configuration Keys** (`appsettings.json`):
```json
{
  "Cidx": {
    "WatchEnabled": "true",
    "WatchStartupTimeoutSeconds": "30",
    "WatchTerminationTimeoutSeconds": "10",
    "FallbackToReconcileOnWatchFailure": "true"
  }
}
```

**Feature Toggle**:
- Allow disabling watch feature via configuration
- Fallback to current reconcile approach when disabled
- Enable gradual rollout and quick rollback

### Phase 5: Advanced Features (Low Priority)

#### 5.1 Process Monitoring and Health Checks

**Watch Process Health Monitoring**:
- Periodic health checks during job execution
- Automatic restart of crashed watch processes
- Metrics collection (watch uptime, crash frequency)

#### 5.2 Performance Optimization

**Optimization Opportunities**:
- Cache watch process startup validation
- Optimize watch termination timing
- Implement watch process pooling for frequent jobs

#### 5.3 Enhanced Logging and Metrics

**Metrics to Track**:
- Watch process success rate
- Fallback frequency
- Performance comparison (watch vs reconcile timing)
- Resource usage (watch process memory/CPU)

## Risk Assessment and Mitigation

### High Risk Areas

#### 1. Process Management Complexity
**Risk**: Watch process lifecycle management introduces complexity
**Mitigation**: 
- Implement comprehensive error handling
- Use existing process management patterns from `ExecuteCidxCommandAsync`
- Include robust fallback mechanisms

#### 2. Resource Leaks
**Risk**: Orphaned watch processes consuming system resources
**Mitigation**:
- Implement proper disposal patterns (`IDisposable`)
- Track process PIDs for cleanup
- Add process monitoring and cleanup on service restart

#### 3. Compatibility Issues
**Risk**: CIDX watch behavior varies across environments
**Mitigation**:
- Comprehensive testing across target platforms (Rocky Linux, Ubuntu)
- Feature toggle for quick rollback
- Maintain reconcile fallback for all failure scenarios

### Medium Risk Areas

#### 1. Performance Impact
**Risk**: Additional process overhead affects job performance
**Mitigation**:
- Monitor resource usage during implementation
- Compare performance metrics (watch vs reconcile)
- Optimize process management if needed

#### 2. Timing Issues
**Risk**: Race conditions between watch termination and job completion
**Mitigation**:
- Implement proper synchronization
- Use timeout-based termination
- Log detailed timing information for debugging

## Implementation Timeline

### Week 1: Core Infrastructure
- [ ] Implement `CidxWatchManager` class
- [ ] Add watch process launching functionality
- [ ] Implement basic watch termination
- [ ] Unit tests for process management

### Week 2: Integration
- [ ] Replace `StartCidxServiceForPreIndexedRepository` with watch-based approach
- [ ] Integrate watch lifecycle with job execution flow
- [ ] Implement fallback mechanisms
- [ ] Integration tests

### Week 3: Error Handling and Polish
- [ ] Enhanced error handling and logging
- [ ] Configuration options and feature toggles
- [ ] Performance monitoring
- [ ] End-to-end testing

### Week 4: Documentation and Deployment
- [ ] Update deployment documentation
- [ ] Performance benchmarking
- [ ] Production rollout planning
- [ ] Post-deployment monitoring

## Testing Strategy

### Unit Tests
- `CidxWatchManager` process lifecycle management
- Watch process launching and termination
- Fallback mechanism triggering
- Configuration handling

### Integration Tests
- Full job execution with watch processes
- Error scenarios and fallback behavior
- Process cleanup on job cancellation
- Cross-platform compatibility (Rocky Linux, Ubuntu)

### End-to-End Tests
- Complete workflow with real repositories
- Performance comparison (watch vs reconcile)
- Resource usage monitoring
- Concurrent job handling

### Test Scenarios
1. **Happy Path**: Watch starts, runs successfully, terminates cleanly
2. **Watch Startup Failure**: Immediate fallback to reconcile
3. **Watch Process Crash**: Mid-execution failure, reconcile at end
4. **Termination Timeout**: Force kill after timeout
5. **Job Cancellation**: Proper cleanup of watch process
6. **Concurrent Jobs**: Multiple watch processes without interference

## Success Criteria

### Functional Requirements
- ✅ Watch process starts successfully for CIDX-aware jobs
- ✅ Watch process monitors file changes during job execution
- ✅ Watch process terminates cleanly when job completes
- ✅ Fallback to reconcile works in all failure scenarios
- ✅ No resource leaks or orphaned processes

### Performance Requirements
- ⚡ Job completion time improved due to eliminated reconcile wait
- ⚡ Watch startup time < 30 seconds
- ⚡ Watch termination time < 10 seconds
- ⚡ No significant impact on Claude Code execution performance

### Reliability Requirements
- 🛡️ >= 95% watch process success rate
- 🛡️ 100% fallback success rate when watch fails
- 🛡️ Zero data corruption or index inconsistencies
- 🛡️ Graceful handling of all error scenarios

## Breaking Changes and Compatibility

### API Compatibility
- ✅ No breaking changes to public APIs
- ✅ Job creation and execution endpoints unchanged
- ✅ Authentication and authorization unchanged

### Configuration Compatibility
- ✅ All existing configuration options preserved
- ✅ New configuration options are optional with sensible defaults
- ✅ Feature toggle allows reverting to current behavior

### Database Compatibility
- ✅ No database schema changes required
- ✅ Job status values are additive (new statuses added)
- ✅ Existing job data remains valid

## Deployment Considerations

### Prerequisites
- CIDX tool must support `watch` command (verify version compatibility)
- Sufficient system resources for additional watch processes
- No changes to Docker or systemd configurations required

### Rollback Plan
1. Set `Cidx:WatchEnabled` to `false` in configuration
2. Restart service to apply configuration
3. All jobs will use current reconcile approach
4. Monitor for normal operation

### Monitoring
- Add watch process metrics to existing monitoring
- Alert on high fallback rates
- Monitor system resource usage
- Track job completion time improvements

---

## Notes

**Implementation Priority**: This plan addresses a direct performance optimization request from the user. The watch approach eliminates the reconcile wait time at job completion, providing immediate user feedback when Claude Code finishes.

**Backward Compatibility**: The implementation maintains 100% backward compatibility by preserving the existing reconcile approach as a fallback mechanism.

**Risk Mitigation**: The comprehensive fallback strategy ensures that even if watch processes fail completely, job execution continues with the proven reconcile approach.

**User Impact**: Users will experience faster job completion times due to eliminated reconcile wait, while maintaining the same reliability and functionality.