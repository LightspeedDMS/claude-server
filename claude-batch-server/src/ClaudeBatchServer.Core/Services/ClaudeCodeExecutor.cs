using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ClaudeBatchServer.Core.Models;

namespace ClaudeBatchServer.Core.Services;

public class ClaudeCodeExecutor : IClaudeCodeExecutor
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<ClaudeCodeExecutor> _logger;
    private readonly string _claudeCommand;

    public ClaudeCodeExecutor(IConfiguration configuration, ILogger<ClaudeCodeExecutor> logger)
    {
        _configuration = configuration;
        _logger = logger;
        _claudeCommand = _configuration["Claude:Command"] ?? "claude --dangerously-skip-permissions --print";
    }

    public async Task<(int ExitCode, string Output)> ExecuteAsync(Job job, string username, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Executing Claude Code for job {JobId} as user {Username}", job.Id, username);

            var userInfo = GetUserInfo(username);
            if (userInfo == null)
                throw new InvalidOperationException($"User '{username}' not found");

            var claudeArgs = BuildClaudeArguments(job);
            var environment = BuildEnvironment(job, userInfo);

            var processInfo = new ProcessStartInfo
            {
                FileName = _claudeCommand.Split(' ')[0], // Get the command (e.g., "claude")
                Arguments = string.Join(" ", _claudeCommand.Split(' ').Skip(1).Concat(claudeArgs.Split(' '))), // Get all args
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true, // Enable stdin redirection to pipe the prompt
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = job.CowPath
            };

            foreach (var env in environment)
            {
                processInfo.Environment[env.Key] = env.Value;
            }

            using var process = new Process { StartInfo = processInfo };
            
            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();

            process.OutputDataReceived += (sender, e) =>
            {
                if (e.Data != null)
                {
                    outputBuilder.AppendLine(e.Data);
                    _logger.LogDebug("Claude output: {Data}", e.Data);
                }
            };

            process.ErrorDataReceived += (sender, e) =>
            {
                if (e.Data != null)
                {
                    errorBuilder.AppendLine(e.Data);
                    _logger.LogDebug("Claude error: {Data}", e.Data);
                }
            };

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                ImpersonateUser(processInfo, userInfo);
            }

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // Write the prompt to stdin and close it
            if (!string.IsNullOrEmpty(job.Prompt))
            {
                await process.StandardInput.WriteAsync(job.Prompt);
                process.StandardInput.Close();
            }

            await process.WaitForExitAsync(cancellationToken);

            var output = outputBuilder.ToString();
            var error = errorBuilder.ToString();
            var combinedOutput = string.IsNullOrEmpty(error) ? output : $"{output}\n\nErrors:\n{error}";

            _logger.LogInformation("Claude Code execution completed for job {JobId} with exit code {ExitCode}", 
                job.Id, process.ExitCode);

            return (process.ExitCode, combinedOutput);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Claude Code execution was cancelled for job {JobId}", job.Id);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute Claude Code for job {JobId}", job.Id);
            return (-1, $"Execution failed: {ex.Message}");
        }
    }

    private string BuildClaudeArguments(Job job)
    {
        var args = new List<string>();

        // Don't include the prompt here since we're piping it to stdin

        foreach (var image in job.Images)
        {
            var imagePath = Path.Combine(job.CowPath, "images", image);
            args.Add($"--image \"{imagePath}\"");
        }

        // Note: Claude CLI doesn't support --timeout option
        // Timeout should be handled by the job service itself

        return string.Join(" ", args);
    }

    private Dictionary<string, string> BuildEnvironment(Job job, UserInfo userInfo)
    {
        var environment = new Dictionary<string, string>
        {
            ["HOME"] = userInfo.HomeDirectory,
            ["USER"] = userInfo.Username,
            ["USERNAME"] = userInfo.Username,
            ["LOGNAME"] = userInfo.Username,
            ["SHELL"] = userInfo.Shell,
            ["PATH"] = Environment.GetEnvironmentVariable("PATH") ?? "/usr/local/bin:/usr/bin:/bin",
            ["PWD"] = job.CowPath,
            ["CLAUDE_BATCH_JOB_ID"] = job.Id.ToString(),
            ["CLAUDE_BATCH_REPOSITORY"] = job.Repository
        };

        foreach (var env in job.Options.Environment)
        {
            environment[env.Key] = env.Value;
        }

        return environment;
    }

    private UserInfo? GetUserInfo(string username)
    {
        try
        {
            var passwdContent = File.ReadAllText("/etc/passwd");
            var passwdLine = passwdContent.Split('\n')
                .FirstOrDefault(line => line.StartsWith($"{username}:"));

            if (passwdLine == null) return null;

            var parts = passwdLine.Split(':');
            if (parts.Length < 7) return null;

            return new UserInfo
            {
                Username = parts[0],
                Uid = int.Parse(parts[2]),
                Gid = int.Parse(parts[3]),
                HomeDirectory = parts[5],
                Shell = parts[6]
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get user info for {Username}", username);
            return null;
        }
    }

    private void ImpersonateUser(ProcessStartInfo processInfo, UserInfo userInfo)
    {
        try
        {
            if (Environment.UserName != "root")
            {
                _logger.LogWarning("Not running as root, user impersonation may not work properly");
                return;
            }

            // Wrap the original command with sudo
            var originalCommand = processInfo.FileName;
            var originalArgs = processInfo.Arguments;
            
            processInfo.FileName = "sudo";
            processInfo.Arguments = $"-u {userInfo.Username} -H -- {originalCommand} {originalArgs}";

            _logger.LogDebug("Executing as user {Username} with command: sudo {Arguments}", 
                userInfo.Username, processInfo.Arguments);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set up user impersonation for {Username}", userInfo.Username);
            throw;
        }
    }

    private class UserInfo
    {
        public string Username { get; set; } = string.Empty;
        public int Uid { get; set; }
        public int Gid { get; set; }
        public string HomeDirectory { get; set; } = string.Empty;
        public string Shell { get; set; } = "/bin/bash";
    }
}