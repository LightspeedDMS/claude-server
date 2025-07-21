using ClaudeBatchServer.Core.Models;

namespace ClaudeBatchServer.Core.Services;

public interface IClaudeCodeExecutor
{
    Task<(int ExitCode, string Output)> ExecuteAsync(Job job, string username, CancellationToken cancellationToken);
}