using ClaudeBatchServer.Core.Models;

namespace ClaudeBatchServer.Core.Services;

public interface IClaudeCodeExecutor
{
    Task<(int ExitCode, string Output)> ExecuteAsync(Job job, string username, CancellationToken cancellationToken);
    Task<string> GenerateJobTitleAsync(string prompt, CancellationToken cancellationToken = default);
    Task<(int ExitCode, string Output)> StopCidxAsync(string workspacePath, string username, CancellationToken cancellationToken = default);
}