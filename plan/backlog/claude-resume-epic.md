# Claude Code --resume Epic - Job Session Continuation

## Overview
Implement job session continuation capability that allows users to resume Claude Code sessions from completed jobs by submitting new prompts in the context of the existing workspace. This enables iterative development workflows where users can build upon previous Claude Code interactions within the same repository context.

## Business Context
Currently, each job creates a new CoW workspace and starts a fresh Claude Code session. Users who want to iterate on previous work must either:
1. Create entirely new jobs, losing the context of previous interactions
2. Manually recreate their workspace state

The --resume feature enables true iterative workflows by allowing users to continue Claude Code sessions from completed jobs, maintaining full context from previous interactions while allowing new prompts and file uploads.

## Architecture Analysis

### Current System Architecture
Based on deep codebase exploration, the current system:

1. **Job Lifecycle**: `Created` → `Queued` → `Running` → `Completed/Failed`
2. **Workspace Management**: Each job gets a CoW clone in `{JobsPath}/{jobId}`
3. **Claude Code Execution**: Via `ClaudeCodeExecutor.cs` using shell scripts
4. **Session Tracking**: `ClaudeCodeSessionService` reads session IDs from `~/.claude/projects/`
5. **Job Persistence**: Jobs persisted as JSON files with configurable retention

### Key Components for --resume
1. **IClaudeCodeSessionService**: Already implemented - retrieves session IDs from `~/.claude/projects/{encoded-path}/{session-uuid}.jsonl`
2. **Job State**: Jobs maintain `CowPath` workspace until cleanup
3. **CIDX Integration**: Semantic search containers managed per job
4. **File Management**: Files persist in job workspace until cleanup

## High-Level Features

### 1. Resume Job API Endpoint
**Feature**: `POST /jobs/{jobId}/resume` endpoint for session continuation
- **Description**: Allows submitting new prompts and files to continue previous Claude Code sessions using the same job creation UX pattern
- **Preconditions**: 
  - Original job must be in `completed` status with `exitCode: 0`
  - Job workspace (CoW clone) must still exist and be accessible
  - Claude Code session must be available in `~/.claude/projects/`
  - No existing resume lock file for the job (prevent concurrent resumes)
- **API Requirements**:
  - Accept new prompt text via same multipart/form-data as job creation
  - Support file uploads using existing `/jobs/{jobId}/files` endpoint
  - Validate all preconditions before allowing resume
  - Append new output to existing job's output array
  - Update job title with latest prompt (overwrite previous title)

### 2. Enhanced Job Output Management
**Feature**: Multi-session output tracking and memo-style display
- **Description**: Store and display output from multiple resume sessions as a memo/chat-like conversation
- **Requirements**:
  - Change Job.Output from string to array of output objects
  - Each output object contains: timestamp, prompt, output, sessionNumber, exitCode
  - Display outputs in scrollable memo box with session separators
  - Auto-scroll to bottom of conversation
  - Visual separators between sessions: "--- Resume Session X at timestamp ---"
  - Show original prompt and each resume prompt with their respective outputs

### 3. Claude Code Session Resumption
**Feature**: Integration with Claude Code --resume flag
- **Description**: Execute Claude Code with session continuation and concurrent resume prevention
- **Requirements**:
  - Retrieve latest session ID using `IClaudeCodeSessionService`
  - Modify `ClaudeCodeExecutor` to support --resume parameter
  - Implement resume lock mechanism using `{jobId}.resume.lock` files
  - Store Claude Code PID in lock file during execution
  - Restart CIDX containers with `cidx index --reconcile` to catch file changes
  - Append new results to job's output array with session metadata

### 4. UI Enhancement for Job Cards
**Feature**: Simple resume button with reused job creation UX
- **Description**: Minimal UI changes leveraging existing job creation form
- **Requirements**:
  - Add "Resume" button to completed successful job cards
  - Clicking Resume opens same job creation form (prompt + file upload)
  - Form submits to `/jobs/{jobId}/resume` instead of `/jobs`
  - Replace single output display with memo-style conversation view
  - Show all sessions in chat format with clear visual separators
  - Maintain existing Browse Files and Delete buttons

## Technical Implementation

### API Design

#### Resume Job Endpoint
```http
POST /jobs/{jobId}/resume
Authorization: Bearer {jwt-token}
Content-Type: multipart/form-data

{
  "prompt": "New prompt to continue the session",
  "files": [uploaded files - handled via existing /jobs/{jobId}/files endpoint]
}
```

**Implementation Note**: File uploads use the existing `POST /jobs/{jobId}/files` endpoint before submitting the resume request, maintaining consistency with current job creation workflow.

**Response**:
```json
{
  "jobId": "same-job-guid-resumed",
  "status": "queued",
  "title": "Updated title from new prompt",
  "sessionId": "claude-session-uuid",
  "sessionNumber": 2,
  "cowPath": "/workspace/jobs/original-job-guid"
}
```

#### Job Eligibility Validation
```csharp
public async Task<bool> CanResumeJobAsync(Guid jobId, string username)
{
    var job = await GetJobAsync(jobId, username);
    if (job == null || job.Status != JobStatus.Completed) return false;
    
    // Must have successful exit code
    if (job.ExitCode != 0) return false;
    
    // Check workspace exists and is accessible
    if (!Directory.Exists(job.CowPath)) return false;
    
    // Check Claude session exists
    var sessionExists = await _sessionService.GetLatestSessionIdAsync(job.CowPath);
    if (sessionExists == null) return false;
    
    // Check no existing resume lock
    var lockFile = Path.Combine(_jobsPath, $"{jobId}.resume.lock");
    if (File.Exists(lockFile))
    {
        // Validate if PID in lock file is still running
        if (await IsResumeLockActiveAsync(lockFile)) return false;
        
        // Clean up stale lock
        try { File.Delete(lockFile); } catch { }
    }
    
    return true;
}
```

### Backend Implementation Changes

#### 1. Job Model Enhancement
```csharp
// Job.cs - Change output structure for multi-session support
public class Job
{
    // Existing properties...
    public List<JobOutput> Outputs { get; set; } = new();
    public int CurrentSessionNumber { get; set; } = 1;
    
    // Legacy support - map to latest output
    [JsonIgnore]
    public string Output => Outputs.LastOrDefault()?.Output ?? string.Empty;
}

public class JobOutput
{
    public DateTime Timestamp { get; set; }
    public string Prompt { get; set; } = string.Empty;
    public string Output { get; set; } = string.Empty;
    public int SessionNumber { get; set; }
    public int? ExitCode { get; set; }
}
```

#### 2. JobService Resume Methods
```csharp
// IJobService.cs - Add resume interface
public interface IJobService
{
    // Existing methods...
    Task<bool> CanResumeJobAsync(Guid jobId, string username);
    Task<ResumeJobResponse> ResumeJobAsync(Guid jobId, ResumeJobRequest request, string username);
}

// JobService.cs - Resume implementation
public async Task<ResumeJobResponse> ResumeJobAsync(Guid jobId, ResumeJobRequest request, string username)
{
    // 1. Validate job can be resumed
    if (!await CanResumeJobAsync(jobId, username))
        throw new InvalidOperationException("Job cannot be resumed");
    
    var job = await GetJobAsync(jobId, username);
    
    // 2. Create resume lock
    var lockFile = Path.Combine(_jobsPath, $"{jobId}.resume.lock");
    await File.WriteAllTextAsync(lockFile, "INITIALIZING");
    
    try
    {
        // 3. Retrieve session ID
        var sessionId = await _sessionService.GetLatestSessionIdAsync(job.CowPath);
        if (sessionId == null)
            throw new InvalidOperationException("No Claude session found for resume");
        
        // 4. Update job for resume
        job.CurrentSessionNumber++;
        job.Title = await _claudeExecutor.GenerateJobTitleAsync(request.Prompt, job.Repository);
        job.Status = JobStatus.Queued;
        
        // 5. Handle file uploads to existing workspace (overwrite existing)
        if (request.Files?.Any() == true)
        {
            var filesPath = Path.Combine(job.CowPath, "files");
            Directory.CreateDirectory(filesPath);
            
            foreach (var file in request.Files)
            {
                var filePath = Path.Combine(filesPath, file.FileName);
                using var stream = file.OpenReadStream();
                using var fileStream = new FileStream(filePath, FileMode.Create);
                await stream.CopyToAsync(fileStream);
            }
        }
        
        // 6. Queue job for execution with resume context
        job.ResumePrompt = request.Prompt;
        job.ResumeSessionId = sessionId;
        _jobQueue.Enqueue(jobId);
        
        await _jobPersistenceService.SaveJobAsync(job);
        
        return new ResumeJobResponse
        {
            JobId = jobId,
            Status = job.Status.ToString().ToLower(),
            Title = job.Title,
            SessionId = sessionId,
            SessionNumber = job.CurrentSessionNumber,
            CowPath = job.CowPath
        };
    }
    catch
    {
        // Clean up lock on failure
        try { File.Delete(lockFile); } catch { }
        throw;
    }
}
```

#### 3. ClaudeCodeExecutor Enhancement
```csharp
// ClaudeCodeExecutor.cs - Add resume support
private async Task<string> BuildClaudeArgumentsAsync(Job job)
{
    var args = new List<string>();
    
    // Add resume parameter if this is a resume execution
    if (!string.IsNullOrEmpty(job.ResumeSessionId))
    {
        args.Add($"--resume {job.ResumeSessionId}");
    }
    
    // Existing CIDX and other argument logic...
    
    return string.Join(" ", args);
}

private async Task<(int ExitCode, string Output)> LaunchClaudeCodeWithRedirection(
    Job job, UserInfo userInfo, string outputFilePath, string pidFilePath, CancellationToken cancellationToken)
{
    try
    {
        // For resumed jobs, restart CIDX with reconcile to catch file changes
        if (!string.IsNullOrEmpty(job.ResumePrompt) && job.Options.CidxAware)
        {
            await RestartCidxForResumedJobAsync(job.CowPath);
        }
        
        // Update lock file with actual PID once process starts
        var lockFile = Path.Combine(Path.GetDirectoryName(_jobsPath), $"{job.Id}.resume.lock");
        
        // Get prompt - use resume prompt if this is a resume, otherwise original
        var prompt = !string.IsNullOrEmpty(job.ResumePrompt) ? job.ResumePrompt : job.Prompt;
        
        // Build and execute script with existing logic but using current prompt
        // ... existing process launch logic ...
        
        // Update lock file with PID
        if (File.Exists(lockFile) && process != null)
        {
            await File.WriteAllTextAsync(lockFile, process.Id.ToString());
        }
        
        return (0, "Process launched successfully");
    }
    catch (Exception ex)
    {
        // Clean up lock file on failure
        var lockFile = Path.Combine(Path.GetDirectoryName(_jobsPath), $"{job.Id}.resume.lock");
        try { File.Delete(lockFile); } catch { }
        
        _logger.LogError(ex, "Failed to launch Claude Code for job {JobId}", job.Id);
        return (-1, $"Launch failed: {ex.Message}");
    }
}

private async Task RestartCidxForResumedJobAsync(string workspacePath)
{
    try
    {
        _logger.LogInformation("Restarting CIDX with reconcile for resumed job in workspace: {WorkspacePath}", workspacePath);
        
        // Stop any existing cidx containers
        await ExecuteCidxCommandAsync("stop", workspacePath);
        
        // Start cidx service
        var startResult = await ExecuteCidxCommandAsync("start", workspacePath);
        if (startResult.ExitCode != 0)
        {
            _logger.LogWarning("Failed to start CIDX for resumed job: {Output}", startResult.Output);
            return;
        }
        
        // Run index reconcile to catch any new files
        var reconcileResult = await ExecuteCidxCommandAsync("index --reconcile", workspacePath);
        if (reconcileResult.ExitCode != 0)
        {
            _logger.LogWarning("Failed to reconcile CIDX index for resumed job: {Output}", reconcileResult.Output);
        }
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error restarting CIDX for resumed job in workspace: {WorkspacePath}", workspacePath);
    }
}

// Add method to complete job and append to outputs array
public async Task CompleteResumedJobAsync(Job job, string output, int exitCode)
{
    try
    {
        // Create output entry for this session
        var jobOutput = new JobOutput
        {
            Timestamp = DateTime.UtcNow,
            Prompt = job.ResumePrompt ?? job.Prompt,
            Output = output,
            SessionNumber = job.CurrentSessionNumber,
            ExitCode = exitCode
        };
        
        job.Outputs.Add(jobOutput);
        job.Status = exitCode == 0 ? JobStatus.Completed : JobStatus.Failed;
        job.ExitCode = exitCode;
        job.CompletedAt = DateTime.UtcNow;
        
        // Clear resume context
        job.ResumePrompt = null;
        job.ResumeSessionId = null;
        
        // Remove lock file
        var lockFile = Path.Combine(Path.GetDirectoryName(_jobsPath), $"{job.Id}.resume.lock");
        try { File.Delete(lockFile); } catch { }
        
        await _jobPersistenceService.SaveJobAsync(job);
        
        _logger.LogInformation("Completed resumed job {JobId} session {SessionNumber} with exit code {ExitCode}", 
            job.Id, job.CurrentSessionNumber, exitCode);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error completing resumed job {JobId}", job.Id);
    }
}
```

#### 4. Controllers Update
```csharp
// JobsController.cs - Add resume endpoint
[HttpPost("{jobId}/resume")]
public async Task<ActionResult<ResumeJobResponse>> ResumeJob(Guid jobId, [FromForm] ResumeJobRequest request)
{
    try
    {
        var username = GetCurrentUsername();
        if (string.IsNullOrEmpty(username))
            return Unauthorized();

        // Validate prompt is provided
        if (string.IsNullOrWhiteSpace(request.Prompt))
            return BadRequest("Prompt is required for resume operation");

        if (!await _jobService.CanResumeJobAsync(jobId, username))
            return BadRequest("Job cannot be resumed. Job must be completed successfully, workspace must exist, session must be available, and no concurrent resume operations allowed.");

        var result = await _jobService.ResumeJobAsync(jobId, request, username);
        return Ok(result);
    }
    catch (InvalidOperationException ex)
    {
        return BadRequest(ex.Message);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error resuming job {JobId}", jobId);
        return StatusCode(500, "Internal server error");
    }
}

// DTOs for resume functionality
public class ResumeJobRequest
{
    public string Prompt { get; set; } = string.Empty;
    public List<IFormFile> Files { get; set; } = new();
}

public class ResumeJobResponse
{
    public Guid JobId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? SessionId { get; set; }
    public int SessionNumber { get; set; }
    public string CowPath { get; set; } = string.Empty;
}
```

#### 5. Lock File Cleanup Integration
```csharp
// JobService.cs - Integrate lock cleanup with existing mechanisms
private async Task CleanupExpiredJobsAsync()
{
    var expiredJobs = _jobs.Values
        .Where(j => j.CreatedAt < DateTime.UtcNow.AddHours(-_jobTimeoutHours))
        .ToList();

    foreach (var job in expiredJobs)
    {
        _logger.LogInformation("Cleaning up expired job {JobId}", job.Id);
        
        // Cleanup cidx if it was enabled for this job
        if (job.Options.CidxAware && job.CidxStatus == "ready")
        {
            await CleanupCidxAsync(job);
        }
        
        // ADDED: Clean up resume lock files for expired jobs
        var lockFile = Path.Combine(_jobsPath, $"{job.Id}.resume.lock");
        try 
        { 
            if (File.Exists(lockFile))
            {
                File.Delete(lockFile);
                _logger.LogDebug("Cleaned up resume lock file for expired job {JobId}", job.Id);
            }
        } 
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to clean up resume lock file for job {JobId}", job.Id);
        }
        
        await _repositoryService.RemoveCowCloneAsync(job.CowPath);
        _jobs.TryRemove(job.Id, out _);
    }
    
    // ADDED: Clean up orphaned lock files (locks without corresponding jobs)
    await CleanupOrphanedLockFilesAsync();
}

private async Task CleanupOrphanedLockFilesAsync()
{
    try
    {
        var lockFiles = Directory.GetFiles(_jobsPath, "*.resume.lock");
        foreach (var lockFile in lockFiles)
        {
            var fileName = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(lockFile));
            if (Guid.TryParse(fileName, out var jobId))
            {
                // If job doesn't exist in memory or is too old, remove lock
                if (!_jobs.ContainsKey(jobId) || 
                    _jobs[jobId].CreatedAt < DateTime.UtcNow.AddHours(-_jobTimeoutHours))
                {
                    try
                    {
                        File.Delete(lockFile);
                        _logger.LogDebug("Cleaned up orphaned resume lock file: {LockFile}", lockFile);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to clean up orphaned lock file: {LockFile}", lockFile);
                    }
                }
            }
        }
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error during orphaned lock file cleanup");
    }
}
```

### Frontend UI Implementation

#### Simplified Job Card Enhancement
Reusing existing job creation UX pattern with minimal changes:

```html
<!-- Enhanced Job Card for Completed Jobs -->
<div class="job-card completed-job" data-job-id="{jobId}">
    <div class="job-header">
        <h3 class="job-title">{jobTitle}</h3>
        <span class="job-status completed">Completed</span>
    </div>
    
    <!-- MODIFIED: Memo-style Output Display (replaces single output) -->
    <div class="job-conversation">
        <div class="conversation-container memo-box" data-job-id="{jobId}">
            <!-- Populated via JavaScript with session separators -->
        </div>
    </div>
    
    <!-- MODIFIED: Action Buttons with Resume -->
    <div class="job-actions">
        <button class="btn btn-primary resume-job" data-job-id="{jobId}">
            🔄 Resume
        </button>
        <button class="btn btn-secondary browse-files" data-job-id="{jobId}">
            📁 Browse Files
        </button>
        <button class="btn btn-danger delete-job" data-job-id="{jobId}">
            🗑️ Delete
        </button>
    </div>
</div>
```

#### JavaScript Implementation
```javascript
// resume-job.js - Simplified approach using existing job creation form
class JobResumeManager {
    constructor() {
        this.initializeEventListeners();
        this.loadAllJobConversations();
    }
    
    initializeEventListeners() {
        document.addEventListener('click', (e) => {
            if (e.target.classList.contains('resume-job')) {
                this.openResumeModal(e.target.dataset.jobId);
            }
        });
    }
    
    openResumeModal(jobId) {
        // Reuse existing job creation modal/form
        const modal = document.getElementById('job-creation-modal');
        const form = document.getElementById('job-form');
        
        // Configure form for resume
        form.dataset.resumeJobId = jobId;
        form.action = `/jobs/${jobId}/resume`;
        
        // Update modal title
        const modalTitle = modal.querySelector('.modal-title');
        modalTitle.textContent = 'Resume Job Session';
        
        // Show modal
        modal.style.display = 'block';
    }
    
    async loadAllJobConversations() {
        // Load memo-style conversations for all visible job cards
        const jobCards = document.querySelectorAll('.job-card[data-job-id]');
        for (const card of jobCards) {
            const jobId = card.dataset.jobId;
            await this.loadJobConversation(jobId);
        }
    }
    
    async loadJobConversation(jobId) {
        try {
            const response = await fetch(`/api/jobs/${jobId}`, {
                headers: {
                    'Authorization': `Bearer ${AuthService.getToken()}`
                }
            });
            
            if (!response.ok) return;
            
            const job = await response.json();
            this.displayMemoConversation(jobId, job);
            
            // Enable resume button only for successful jobs
            if (job.status === 'completed' && job.exitCode === 0) {
                this.enableResumeButton(jobId);
            }
            
        } catch (error) {
            console.warn('Failed to load job conversation:', error);
        }
    }
    
    displayMemoConversation(jobId, job) {
        const container = document.querySelector(`.conversation-container[data-job-id="${jobId}"]`);
        if (!container) return;
        
        // Convert job outputs to memo format with session separators
        let memoHtml = '';
        
        if (job.outputs && job.outputs.length > 0) {
            // Display all sessions in memo style
            job.outputs.forEach((output, index) => {
                // Add session separator for resume sessions (not first)
                if (index > 0) {
                    memoHtml += `
                        <div class="session-separator">
                            --- Resume Session ${output.sessionNumber} at ${formatTimestamp(output.timestamp)} ---
                        </div>
                    `;
                }
                
                // Session content in memo style
                memoHtml += `
                    <div class="session-block">
                        <div class="session-header">
                            <strong>Prompt:</strong> ${formatTimestamp(output.timestamp)}
                            <span class="exit-code ${output.exitCode === 0 ? 'success' : 'error'}">
                                Exit: ${output.exitCode || 0}
                            </span>
                        </div>
                        <div class="session-prompt">${escapeHtml(output.prompt)}</div>
                        <div class="session-output">
                            <pre>${escapeHtml(output.output)}</pre>
                        </div>
                    </div>
                `;
            });
        } else {
            // Legacy support - single output
            memoHtml = `
                <div class="session-block">
                    <div class="session-header">
                        <strong>Original Job:</strong> ${formatTimestamp(job.createdAt)}
                        <span class="exit-code ${job.exitCode === 0 ? 'success' : 'error'}">
                            Exit: ${job.exitCode || 0}
                        </span>
                    </div>
                    <div class="session-prompt">${escapeHtml(job.prompt || 'Original job prompt')}</div>
                    <div class="session-output">
                        <pre>${escapeHtml(job.output || 'No output available')}</pre>
                    </div>
                </div>
            `;
        }
        
        container.innerHTML = memoHtml;
        
        // Auto-scroll to bottom
        container.scrollTop = container.scrollHeight;
    }
    
    enableResumeButton(jobId) {
        // Enable the resume button for successful jobs
        const resumeBtn = document.querySelector(`.resume-job[data-job-id="${jobId}"]`);
        if (resumeBtn) {
            resumeBtn.disabled = false;
            resumeBtn.style.display = 'inline-block';
        }
    }
    
    // Note: Resume submission is handled by existing job creation form
    // When user clicks Resume button, it opens the job creation modal
    // The modal submits to /jobs/{jobId}/resume instead of /jobs
    // File uploads are handled via existing /jobs/{jobId}/files endpoint
    
    async monitorResumedJob(jobId) {
        // Poll for job status updates and refresh memo display
        const pollInterval = setInterval(async () => {
            try {
                const response = await fetch(`/api/jobs/${jobId}`, {
                    headers: {
                        'Authorization': `Bearer ${AuthService.getToken()}`
                    }
                });
                
                if (response.ok) {
                    const job = await response.json();
                    
                    // Update memo display
                    this.displayMemoConversation(jobId, job);
                    
                    // Update job status in header
                    const statusElement = document.querySelector(`.job-card[data-job-id="${jobId}"] .job-status`);
                    if (statusElement) {
                        statusElement.textContent = job.status;
                        statusElement.className = `job-status ${job.status}`;
                    }
                    
                    // Update title if changed (from latest prompt)
                    const titleElement = document.querySelector(`.job-card[data-job-id="${jobId}"] .job-title`);
                    if (titleElement) {
                        titleElement.textContent = job.title;
                    }
                    
                    // Stop polling when job completes
                    if (['completed', 'failed', 'timeout', 'cancelled'].includes(job.status)) {
                        clearInterval(pollInterval);
                        
                        // Re-enable resume if completed successfully
                        if (job.status === 'completed' && job.exitCode === 0) {
                            this.enableResumeButton(jobId);
                        }
                    }
                }
            } catch (error) {
                console.error('Error monitoring resumed job:', error);
            }
        }, 2000);
    }
}

// Utility functions
function formatTimestamp(timestamp) {
    return new Date(timestamp).toLocaleString();
}

function escapeHtml(text) {
    const div = document.createElement('div');
    div.textContent = text;
    return div.innerHTML;
}

// Initialize resume manager
const resumeManager = new JobResumeManager();
```

#### CSS Styling
```css
/* resume-job.css - Simplified memo-style display */
.memo-box {
    max-height: 400px;
    overflow-y: auto;
    border: 1px solid #e9ecef;
    border-radius: 6px;
    padding: 1rem;
    background: #f8f9fa;
    margin: 1rem 0;
}

.session-separator {
    text-align: center;
    color: #6c757d;
    font-style: italic;
    margin: 1.5rem 0;
    padding: 0.5rem;
    border-top: 2px dashed #dee2e6;
    border-bottom: 2px dashed #dee2e6;
    font-size: 0.9rem;
}

.session-block {
    margin-bottom: 2rem;
}

.session-header {
    display: flex;
    justify-content: space-between;
    align-items: center;
    margin-bottom: 0.5rem;
    padding-bottom: 0.25rem;
    border-bottom: 1px solid #dee2e6;
    font-size: 0.9rem;
}

.session-prompt {
    background: #e3f2fd;
    padding: 0.75rem;
    border-radius: 4px;
    margin-bottom: 0.5rem;
    border-left: 4px solid #2196f3;
}

.session-output {
    background: #fff;
    border: 1px solid #dee2e6;
    border-radius: 4px;
}

.session-output pre {
    margin: 0;
    padding: 1rem;
    font-size: 0.85rem;
    line-height: 1.4;
    white-space: pre-wrap;
    word-wrap: break-word;
}

.exit-code {
    font-size: 0.8rem;
    padding: 0.25rem 0.5rem;
    border-radius: 3px;
    font-weight: 600;
}

.exit-code.success {
    background: #d4edda;
    color: #155724;
}

.exit-code.error {
    background: #f8d7da;
    color: #721c24;
}

.job-actions {
    display: flex;
    gap: 0.5rem;
    margin-top: 1rem;
}

.btn.btn-primary {
    background: #007bff;
    color: white;
}

.btn.btn-primary:hover {
    background: #0056b3;
}

.btn.btn-primary:disabled {
    background: #6c757d;
    cursor: not-allowed;
}
```

## Implementation Timeline

### Phase 1: Backend Foundation (Week 1)
- ✅ Enhance Job model with resume tracking fields
- ✅ Implement `CanResumeJobAsync` validation logic
- ✅ Create `ResumeJobAsync` method in JobService
- ✅ Add resume endpoint to JobsController
- ✅ Unit tests for resume validation and job creation

### Phase 2: Claude Code Integration (Week 1)
- ✅ Modify `BuildClaudeArgumentsAsync` to support --resume flag
- ✅ Integrate `IClaudeCodeSessionService` for session ID retrieval
- ✅ Implement CIDX restart logic for resumed jobs
- ✅ Add output appending with timestamp separators
- ✅ Integration tests with real Claude Code execution

### Phase 3: UI Implementation (Week 2)
- ✅ Design and implement enhanced job card UI
- ✅ Add resume prompt input and file upload interface
- ✅ Implement JavaScript for resume functionality
- ✅ Add job chain display for tracking relationships
- ✅ Responsive design for mobile devices

### Phase 4: Testing & Polish (Week 2)
- ✅ Comprehensive E2E tests for resume workflows
- ✅ Performance testing with multiple resumed jobs
- ✅ UI/UX testing and refinements
- ✅ Documentation updates
- ✅ Error handling and edge case coverage

## Success Criteria

### Backend Requirements
- ✅ Users can resume completed jobs with new prompts
- ✅ Resumed jobs maintain workspace context and files
- ✅ Claude Code sessions continue with full context
- ✅ CIDX containers restart properly for semantic search
- ✅ Job relationships are tracked for audit trail
- ✅ Original job outputs are preserved with clear separation

### Frontend Requirements  
- ✅ Completed job cards show resume interface
- ✅ Users can enter new prompts and upload additional files
- ✅ Submit button creates resumed job and monitors progress
- ✅ Job chain history is displayed for multi-resume sessions
- ✅ UI clearly indicates relationship between original and resumed jobs

### Technical Requirements
- ✅ Session IDs correctly retrieved from Claude projects directory
- ✅ Workspace reuse prevents unnecessary CoW clones
- ✅ File uploads work correctly in existing workspaces
- ✅ Job cleanup considers resumed job relationships
- ✅ API responses include parent/child job relationships

## Security Considerations

### Access Control
- Resume functionality respects user isolation (can only resume own jobs)
- JWT authentication required for all resume operations
- File upload security maintained in existing workspaces

### Workspace Security
- Resumed jobs reuse existing workspace without creating new CoW clones
- File uploads validated and secured in existing workspace
- CIDX containers properly managed to prevent resource leaks

### Data Integrity
- Job relationships properly tracked and validated
- Session IDs validated before attempting resume
- Workspace existence verified before allowing resume

## Testing Strategy

### Unit Tests
```csharp
[Test]
public async Task CanResumeJobAsync_CompletedJobWithWorkspace_ReturnsTrue()
{
    // Arrange
    var job = CreateCompletedJob();
    MockWorkspaceExists(job.CowPath, true);
    MockSessionExists(job.CowPath, "session-123");
    
    // Act
    var canResume = await _jobService.CanResumeJobAsync(job.Id, job.Username);
    
    // Assert
    Assert.IsTrue(canResume);
}

[Test]
public async Task ResumeJobAsync_ValidRequest_CreatesResumedJob()
{
    // Arrange
    var parentJob = CreateCompletedJob();
    var request = new ResumeJobRequest 
    { 
        Prompt = "Continue with analysis" 
    };
    
    // Act
    var result = await _jobService.ResumeJobAsync(parentJob.Id, request, "testuser");
    
    // Assert
    Assert.AreEqual(parentJob.Id, result.ParentJobId);
    Assert.IsTrue(result.Title.Contains("Continue with analysis"));
}
```

### Integration Tests
```csharp
[Test]
public async Task ResumeJob_E2E_ExecutesWithPreviousContext()
{
    // Test complete resume workflow:
    // 1. Create and complete original job
    // 2. Resume with new prompt
    // 3. Verify Claude Code executes with --resume
    // 4. Verify output appended with separator
    // 5. Verify CIDX restarted if applicable
}
```

### E2E Tests
```javascript
test('should resume job with new prompt and files', async ({ page }) => {
    // 1. Login and navigate to completed job
    await loginAsUser(page, 'testuser');
    await page.goto('/jobs/completed');
    
    // 2. Enter resume prompt
    await page.fill('[data-testid="resume-prompt"]', 'Add error handling to the code');
    
    // 3. Upload additional files
    await page.setInputFiles('[data-testid="resume-files"]', ['error-handling-guide.md']);
    
    // 4. Submit resume
    await page.click('[data-testid="submit-resume"]');
    
    // 5. Verify new job created and monitored
    await expect(page.locator('[data-testid="job-status"]')).toContainText('running');
    await expect(page.locator('[data-testid="parent-job-link"]')).toBeVisible();
});
```

## Questions for Stakeholders

### Business Questions
1. **Resume Limits**: Should there be a limit on how many times a job can be resumed?
2. **Workspace Retention**: How long should workspaces be retained to allow resumption?
3. **Billing**: Should resumed jobs count as new jobs for billing purposes?

### Technical Questions  
1. **Session Timeout**: How to handle Claude Code sessions that have expired?
2. **Concurrent Resumes**: Should multiple resumes of the same job be allowed simultaneously?
3. **Large Workspaces**: How to handle resume operations on very large workspaces?

### UI/UX Questions
1. **Job History**: How should we display complex job chain relationships in the UI?
2. **Resume Permissions**: Should there be admin controls for enabling/disabling resume capability?
3. **Mobile Experience**: How should the resume interface work on mobile devices?

## Risk Assessment

### Technical Risks
- **Session ID Retrieval**: Claude session files might be cleaned up or corrupted
- **Workspace State**: Workspace might be modified externally between completion and resume
- **CIDX Restart**: CIDX containers might fail to restart in existing workspace

### Mitigation Strategies
- **Validation**: Comprehensive validation before allowing resume operations
- **Error Handling**: Graceful fallback if session or workspace unavailable
- **Monitoring**: Enhanced logging for resume operations and failure tracking

## Documentation Updates Required

### API Documentation
- Add resume endpoint documentation with examples
- Update job lifecycle documentation to include resume states
- Document new DTOs and response formats

### User Documentation
- Add resume workflow guide with screenshots
- Update job management documentation
- Create troubleshooting guide for resume failures

### Developer Documentation
- Update architecture documentation with resume flow
- Add code examples for resume implementation
- Document session management integration

## Conclusion

The Claude Code --resume epic provides a comprehensive solution for iterative development workflows by enabling users to continue Claude Code sessions from completed jobs. The implementation leverages existing infrastructure while adding minimal complexity, ensuring robust and maintainable functionality.

Key benefits:
- **Iterative Workflows**: Users can build upon previous Claude Code interactions
- **Context Preservation**: Full workspace and session context maintained
- **User Experience**: Intuitive UI integration with existing job management
- **Technical Soundness**: Leverages proven Claude Code session management
- **Scalability**: Minimal impact on existing job processing architecture

The epic is technically feasible and provides significant value for users who need iterative development capabilities with Claude Code.