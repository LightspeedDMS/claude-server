using ClaudeBatchServer.Core.Models;

namespace ClaudeBatchServer.Core.Services;

/// <summary>
/// Callback interface for job status updates during execution
/// </summary>
public interface IJobStatusCallback
{
    Task OnStatusChangedAsync(Job job);
}

public interface IClaudeCodeExecutor
{
    Task<(int ExitCode, string Output)> ExecuteAsync(Job job, string username, CancellationToken cancellationToken);
    Task<(int ExitCode, string Output)> ExecuteAsync(Job job, string username, IJobStatusCallback? statusCallback, CancellationToken cancellationToken);
    Task<string> GenerateJobTitleAsync(string prompt, string? repositoryPath = null, CancellationToken cancellationToken = default);
    Task<(int ExitCode, string Output)> StopCidxAsync(string workspacePath, string username, CancellationToken cancellationToken = default);
    Task<(int ExitCode, string Output)> UninstallCidxAsync(string workspacePath, string username, CancellationToken cancellationToken = default);
    
    // PID monitoring and job recovery methods
    bool IsProcessRunning(int processId);
    Task<(bool IsComplete, string Output)> CheckJobCompletion(Job job);
    Task<List<Job>> RecoverCrashedJobsAsync(IEnumerable<Job> runningJobs);
}