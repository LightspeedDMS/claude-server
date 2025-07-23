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

            // Handle git operations if gitAware is enabled
            if (job.Options.GitAware)
            {
                var gitResult = await HandleGitOperationsAsync(job, userInfo, cancellationToken);
                if (!gitResult.Success)
                {
                    job.Status = JobStatus.GitFailed;
                    job.GitStatus = gitResult.Status;
                    return (-1, gitResult.ErrorMessage);
                }
                job.GitStatus = gitResult.Status;
            }

            // Handle cidx operations if cidxAware is enabled
            if (job.Options.CidxAware)
            {
                var cidxResult = await HandleCidxOperationsAsync(job, userInfo, cancellationToken);
                if (!cidxResult.Success)
                {
                    job.CidxStatus = cidxResult.Status;
                    _logger.LogWarning("Cidx operations failed for job {JobId}: {Error}", job.Id, cidxResult.ErrorMessage);
                    // Don't fail the job for cidx issues, just continue without it
                }
                else
                {
                    job.Status = JobStatus.CidxReady;
                    job.CidxStatus = cidxResult.Status;
                }
            }

            var claudeArgs = await BuildClaudeArgumentsAsync(job);
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

    private async Task<string> BuildClaudeArgumentsAsync(Job job)
    {
        var args = new List<string>();

        // Don't include the prompt here since we're piping it to stdin

        // Claude Code automatically detects and processes images in the working directory
        // No explicit --image flag needed - images are uploaded to job.CowPath/images/
        // and Claude Code will find them automatically when analyzing the workspace

        // Note: Claude CLI doesn't support --timeout option
        // Timeout should be handled by the job service itself

        // Add cidx-aware system prompt based on cidx configuration
        if (job.Options.CidxAware)
        {
            // If cidx is enabled, check if it's ready and generate appropriate prompt
            var systemPrompt = await GenerateCidxSystemPromptAsync(job.CowPath);
            args.Add($"--append-system-prompt \"{systemPrompt}\"");
        }
        else
        {
            // If cidx is explicitly disabled, add a prompt to NOT mention cidx
            var disabledPrompt = "CIDX SEMANTIC SEARCH DISABLED\\n\\nCidx semantic search has been disabled for this task. Use traditional search tools only:\\n- grep -r \"pattern\" .\\n- find . -name \"*.ext\" -exec grep \"pattern\" {} \\;\\n- rg \"pattern\" --type language\\n\\nDo NOT mention cidx, semantic search, or any cidx-related commands in your response.";
            args.Add($"--append-system-prompt \"{disabledPrompt}\"");
        }

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

    private async Task<(bool Success, string Status, string ErrorMessage)> HandleGitOperationsAsync(Job job, UserInfo userInfo, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Starting git operations for job {JobId}", job.Id);
            job.Status = JobStatus.GitPulling;
            job.GitStatus = "checking";

            // Check if directory is a git repository
            var gitDir = Path.Combine(job.CowPath, ".git");
            if (!Directory.Exists(gitDir))
            {
                _logger.LogInformation("Job {JobId} workspace is not a git repository, skipping git operations", job.Id);
                return (true, "not_git_repo", string.Empty);
            }

            // Execute git pull
            var gitPullResult = await ExecuteGitCommandAsync("pull", job.CowPath, userInfo, cancellationToken);
            
            if (gitPullResult.ExitCode == 0)
            {
                _logger.LogInformation("Git pull successful for job {JobId}", job.Id);
                return (true, "pulled", string.Empty);
            }
            else
            {
                _logger.LogError("Git pull failed for job {JobId} with exit code {ExitCode}: {Output}", 
                    job.Id, gitPullResult.ExitCode, gitPullResult.Output);
                return (false, "failed", $"Git pull failed: {gitPullResult.Output}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Git operations failed for job {JobId}", job.Id);
            return (false, "failed", $"Git operations error: {ex.Message}");
        }
    }

    private async Task<(bool Success, string Status, string ErrorMessage)> HandleCidxOperationsAsync(Job job, UserInfo userInfo, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Starting cidx operations for job {JobId}", job.Id);
            job.Status = JobStatus.CidxIndexing;
            job.CidxStatus = "starting";

            // Fix cidx configuration for the newly CoWed repository
            _logger.LogInformation("Fixing cidx configuration for job {JobId}", job.Id);
            var fixConfigResult = await ExecuteCidxCommandAsync("fix-config --force", job.CowPath, userInfo, cancellationToken);
            if (fixConfigResult.ExitCode != 0)
            {
                _logger.LogWarning("Cidx fix-config failed for job {JobId}: {Output}", job.Id, fixConfigResult.Output);
                // Continue anyway as this might not be critical
            }

            // Start cidx container
            var startResult = await ExecuteCidxCommandAsync("start", job.CowPath, userInfo, cancellationToken);
            if (startResult.ExitCode != 0)
            {
                return (false, "failed", $"Cidx start failed: {startResult.Output}");
            }

            job.CidxStatus = "indexing";

            // Run cidx index --reconcile
            var indexResult = await ExecuteCidxCommandAsync("index --reconcile", job.CowPath, userInfo, cancellationToken);
            if (indexResult.ExitCode != 0)
            {
                // Try to stop cidx if indexing failed
                await ExecuteCidxCommandAsync("stop", job.CowPath, userInfo, CancellationToken.None);
                return (false, "failed", $"Cidx indexing failed: {indexResult.Output}");
            }

            _logger.LogInformation("Cidx indexing successful for job {JobId}", job.Id);
            return (true, "ready", string.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cidx operations failed for job {JobId}", job.Id);
            return (false, "failed", $"Cidx operations error: {ex.Message}");
        }
    }

    private async Task<(int ExitCode, string Output)> ExecuteGitCommandAsync(string gitArgs, string workingDirectory, UserInfo userInfo, CancellationToken cancellationToken)
    {
        var processInfo = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = gitArgs,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory
        };

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            ImpersonateUser(processInfo, userInfo);
        }

        using var process = new Process { StartInfo = processInfo };
        var outputBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();

        process.OutputDataReceived += (sender, e) => { if (e.Data != null) outputBuilder.AppendLine(e.Data); };
        process.ErrorDataReceived += (sender, e) => { if (e.Data != null) errorBuilder.AppendLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync(cancellationToken);

        var output = outputBuilder.ToString();
        var error = errorBuilder.ToString();
        var combinedOutput = string.IsNullOrEmpty(error) ? output : $"{output}\n\nErrors:\n{error}";

        return (process.ExitCode, combinedOutput);
    }

    private async Task<(int ExitCode, string Output)> ExecuteCidxCommandAsync(string cidxArgs, string workingDirectory, UserInfo userInfo, CancellationToken cancellationToken)
    {
        var processInfo = new ProcessStartInfo
        {
            FileName = "cidx",
            Arguments = cidxArgs,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory
        };

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            ImpersonateUser(processInfo, userInfo);
        }

        using var process = new Process { StartInfo = processInfo };
        var outputBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();

        process.OutputDataReceived += (sender, e) => { if (e.Data != null) outputBuilder.AppendLine(e.Data); };
        process.ErrorDataReceived += (sender, e) => { if (e.Data != null) errorBuilder.AppendLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync(cancellationToken);

        var output = outputBuilder.ToString();
        var error = errorBuilder.ToString();
        var combinedOutput = string.IsNullOrEmpty(error) ? output : $"{output}\n\nErrors:\n{error}";

        return (process.ExitCode, combinedOutput);
    }

    private bool IsCidxReady(string workspacePath)
    {
        try
        {
            var processInfo = new ProcessStartInfo
            {
                FileName = "cidx",
                Arguments = "status",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = workspacePath
            };

            using var process = new Process { StartInfo = processInfo };
            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            // Check for healthy cidx status indicators
            return process.ExitCode == 0 && 
                   output.Contains("Running") && 
                   (output.Contains("Ready") || output.Contains("Not needed"));
        }
        catch
        {
            return false;
        }
    }

    private async Task<string> GenerateCidxSystemPromptAsync(string workspacePath)
    {
        var cidxStatus = GetCidxStatus(workspacePath);
        var isCidxReady = IsCidxReady(workspacePath);

        string templatePath;
        if (isCidxReady)
        {
            templatePath = _configuration["SystemPrompts:CidxAvailableTemplatePath"] ?? "SystemPrompts/cidx-system-prompt-template.txt";
        }
        else
        {
            templatePath = _configuration["SystemPrompts:CidxUnavailableTemplatePath"] ?? "SystemPrompts/cidx-unavailable-system-prompt-template.txt";
        }

        // Try to resolve the template path relative to the application base directory
        var fullPath = Path.IsPathRooted(templatePath) ? templatePath : Path.Combine(AppContext.BaseDirectory, templatePath);
        
        try
        {
            if (File.Exists(fullPath))
            {
                var template = await File.ReadAllTextAsync(fullPath);
                return template.Replace("{cidxStatus}", cidxStatus);
            }
            else
            {
                _logger.LogWarning("System prompt template not found at {Path}, using fallback", fullPath);
                return GetFallbackSystemPrompt(isCidxReady, cidxStatus);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load system prompt template from {Path}, using fallback", fullPath);
            return GetFallbackSystemPrompt(isCidxReady, cidxStatus);
        }
    }

    private string GetFallbackSystemPrompt(bool isCidxReady, string cidxStatus)
    {
        if (isCidxReady)
        {
            return $@"CIDX SEMANTIC SEARCH AVAILABLE

Your primary code exploration tool is cidx (semantic search). Always prefer cidx over grep/find/rg when available.

CURRENT STATUS: {cidxStatus}

USAGE PRIORITY:
1. FIRST: Check cidx status with: cidx status
2. IF all services show ""Running/Ready/Not needed/Ready"": Use cidx for all code searches
3. IF any service shows failures: Fall back to grep/find/rg

CIDX EXAMPLES:
- Find authentication: cidx query ""authentication function"" --quiet
- Find error handling: cidx query ""error handling patterns"" --language python --quiet
- Find database code: cidx query ""database connection"" --path */services/* --quiet

TRADITIONAL FALLBACK:
- Use grep/find/rg only when cidx status shows service failures
- Example: grep -r ""function"" . (when cidx unavailable)

Remember: cidx understands intent and context, not just literal text matches.";
        }
        else
        {
            return $@"CIDX SEMANTIC SEARCH UNAVAILABLE

Cidx services are not ready. Use traditional search tools for code exploration.

CURRENT STATUS: {cidxStatus}

USE TRADITIONAL TOOLS:
- grep -r ""pattern"" .
- find . -name ""*.ext"" -exec grep ""pattern"" {{}} \;
- rg ""pattern"" --type language

Check cidx status periodically with: cidx status";
        }
    }

    private string GetCidxStatus(string workspacePath)
    {
        try
        {
            var processInfo = new ProcessStartInfo
            {
                FileName = "cidx",
                Arguments = "status",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = workspacePath
            };

            using var process = new Process { StartInfo = processInfo };
            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            return process.ExitCode == 0 ? output.Trim() : "Service unavailable";
        }
        catch
        {
            return "Service unavailable";
        }
    }

    public async Task<string> GenerateJobTitleAsync(string prompt, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Generating job title for prompt");

            var titlePrompt = $@"Please create a short, descriptive title (max 60 characters) for this task/prompt. The title should capture the main intent or action being requested. Return only the title, nothing else.

Prompt to summarize:
{prompt}";

            var processInfo = new ProcessStartInfo
            {
                FileName = _claudeCommand.Split(' ')[0], // Get the command (e.g., "claude")
                Arguments = string.Join(" ", _claudeCommand.Split(' ').Skip(1)), // Get all args
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = processInfo };
            
            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();

            process.OutputDataReceived += (sender, e) =>
            {
                if (e.Data != null)
                {
                    outputBuilder.AppendLine(e.Data);
                }
            };

            process.ErrorDataReceived += (sender, e) =>
            {
                if (e.Data != null)
                {
                    errorBuilder.AppendLine(e.Data);
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // Write the title generation prompt to stdin
            await process.StandardInput.WriteAsync(titlePrompt);
            process.StandardInput.Close();

            await process.WaitForExitAsync(cancellationToken);

            var output = outputBuilder.ToString().Trim();
            var error = errorBuilder.ToString();

            if (process.ExitCode == 0 && !string.IsNullOrEmpty(output))
            {
                // Clean up the title - take first line and limit to 60 characters
                var title = output.Split('\n')[0].Trim();
                if (title.Length > 60)
                {
                    title = title.Substring(0, 57) + "...";
                }
                
                _logger.LogInformation("Generated job title: {Title}", title);
                return title;
            }
            else
            {
                _logger.LogWarning("Failed to generate job title, using fallback. Exit code: {ExitCode}, Error: {Error}", 
                    process.ExitCode, error);
                return GenerateFallbackTitle(prompt);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating job title, using fallback");
            return GenerateFallbackTitle(prompt);
        }
    }

    private string GenerateFallbackTitle(string prompt)
    {
        // Create a simple fallback title from the first part of the prompt
        var title = prompt.Length > 60 ? prompt.Substring(0, 57) + "..." : prompt;
        
        // Remove newlines and clean up
        title = title.Replace('\n', ' ').Replace('\r', ' ');
        while (title.Contains("  "))
        {
            title = title.Replace("  ", " ");
        }
        
        return title.Trim();
    }

    public async Task<(int ExitCode, string Output)> StopCidxAsync(string workspacePath, string username, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Stopping cidx containers for workspace {WorkspacePath} by user {Username}", workspacePath, username);
            
            var userInfo = GetUserInfo(username);
            if (userInfo == null)
            {
                _logger.LogError("User '{Username}' not found for cidx stop operation", username);
                return (-1, $"User '{username}' not found");
            }

            var result = await ExecuteCidxCommandAsync("stop", workspacePath, userInfo, cancellationToken);
            
            if (result.ExitCode == 0)
            {
                _logger.LogInformation("Successfully stopped cidx containers for workspace {WorkspacePath}", workspacePath);
            }
            else
            {
                _logger.LogWarning("Failed to stop cidx containers for workspace {WorkspacePath}: {Output}", workspacePath, result.Output);
            }
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping cidx containers for workspace {WorkspacePath}", workspacePath);
            return (-1, $"Error stopping cidx: {ex.Message}");
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