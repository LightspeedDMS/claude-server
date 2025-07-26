using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ClaudeBatchServer.Core.Models;
using ClaudeBatchServer.Core.Serialization;

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
        return await ExecuteAsync(job, username, null, cancellationToken);
    }

    public async Task<(int ExitCode, string Output)> ExecuteAsync(Job job, string username, IJobStatusCallback? statusCallback, CancellationToken cancellationToken)
    {
        // Check if we should use the new fire-and-forget approach (for production) or old direct execution (for tests)
        var useFireAndForget = _configuration["Claude:UseFireAndForget"]?.ToLower() != "false";
        
        if (useFireAndForget && statusCallback != null)
        {
            return await ExecuteWithFireAndForgetAsync(job, username, statusCallback, cancellationToken);
        }
        else
        {
            return await ExecuteDirectAsync(job, username, statusCallback, cancellationToken);
        }
    }

    private async Task<(int ExitCode, string Output)> ExecuteWithFireAndForgetAsync(Job job, string username, IJobStatusCallback statusCallback, CancellationToken cancellationToken)
    {
        // Create output file path for crash resilience
        var outputFilePath = Path.Combine(job.CowPath, $".claude-job-{job.Id}.output");
        var pidFilePath = Path.Combine(job.CowPath, $".claude-job-{job.Id}.pid");
        
        try
        {
            _logger.LogInformation("Executing Claude Code for job {JobId} as user {Username} (fire-and-forget)", job.Id, username);

            var userInfo = GetUserInfo(username);
            if (userInfo == null)
                throw new InvalidOperationException($"User '{username}' not found");

            // Handle git operations if gitAware is enabled AND not using new workflow
            // NEW WORKFLOW: Git operations are now handled in JobService before CoW cloning
            var useNewWorkflow = _configuration["Jobs:UseNewWorkflow"]?.ToLower() != "false";
            if (job.Options.GitAware && !useNewWorkflow)
            {
                var gitResult = await HandleGitOperationsAsync(job, userInfo, statusCallback, cancellationToken);
                if (!gitResult.Success)
                {
                    job.Status = JobStatus.GitFailed;
                    job.GitStatus = gitResult.Status ?? "unknown";
                    await statusCallback.OnStatusChangedAsync(job);
                    return (-1, gitResult.ErrorMessage);
                }
                job.GitStatus = gitResult.Status ?? "unknown";
            }
            else if (job.Options.GitAware && useNewWorkflow)
            {
                // NEW WORKFLOW: Git pull already completed in JobService, just set status
                job.GitStatus = "skipped_new_workflow";
                _logger.LogInformation("Skipping git operations for job {JobId} - using new workflow (git pull already done on source repository)", job.Id);
            }

            // Handle cidx operations if cidxAware is enabled
            if (job.Options.CidxAware)
            {
                var cidxResult = await HandleCidxOperationsAsync(job, userInfo, statusCallback, cancellationToken);
                if (!cidxResult.Success)
                {
                    job.CidxStatus = cidxResult.Status;
                    await statusCallback.OnStatusChangedAsync(job);
                    _logger.LogWarning("Cidx operations failed for job {JobId}: {Error}", job.Id, cidxResult.ErrorMessage);
                    // Don't fail the job for cidx issues, just continue without it
                }
                else
                {
                    job.Status = JobStatus.CidxReady;
                    job.CidxStatus = cidxResult.Status;
                    await statusCallback.OnStatusChangedAsync(job);
                }
            }

            // Set status to running and notify
            job.Status = JobStatus.Running;
            await statusCallback.OnStatusChangedAsync(job);

            // Launch Claude Code with output redirection - LAUNCH AND FORGET approach
            return await LaunchClaudeCodeWithRedirection(job, userInfo, outputFilePath, pidFilePath, cancellationToken);
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

    private async Task<(int ExitCode, string Output)> ExecuteDirectAsync(Job job, string username, IJobStatusCallback? statusCallback, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Executing Claude Code for job {JobId} as user {Username} (direct)", job.Id, username);

            var userInfo = GetUserInfo(username);
            if (userInfo == null)
                throw new InvalidOperationException($"User '{username}' not found");

            // Handle git operations if gitAware is enabled AND not using new workflow
            // NEW WORKFLOW: Git operations are now handled in JobService before CoW cloning
            var useNewWorkflow = _configuration["Jobs:UseNewWorkflow"]?.ToLower() != "false";
            if (job.Options.GitAware && !useNewWorkflow)
            {
                var gitResult = await HandleGitOperationsAsync(job, userInfo, statusCallback, cancellationToken);
                if (!gitResult.Success)
                {
                    job.Status = JobStatus.GitFailed;
                    job.GitStatus = gitResult.Status ?? "unknown";
                    if (statusCallback != null)
                        await statusCallback.OnStatusChangedAsync(job);
                    return (-1, gitResult.ErrorMessage);
                }
                job.GitStatus = gitResult.Status ?? "unknown";
            }
            else if (job.Options.GitAware && useNewWorkflow)
            {
                // NEW WORKFLOW: Git pull already completed in JobService, just set status
                job.GitStatus = "skipped_new_workflow";
                _logger.LogInformation("Skipping git operations for job {JobId} - using new workflow (git pull already done on source repository)", job.Id);
            }

            // Handle cidx operations if cidxAware is enabled
            if (job.Options.CidxAware)
            {
                var cidxResult = await HandleCidxOperationsAsync(job, userInfo, statusCallback, cancellationToken);
                if (!cidxResult.Success)
                {
                    job.CidxStatus = cidxResult.Status;
                    if (statusCallback != null)
                        await statusCallback.OnStatusChangedAsync(job);
                    _logger.LogWarning("Cidx operations failed for job {JobId}: {Error}", job.Id, cidxResult.ErrorMessage);
                    // Don't fail the job for cidx issues, just continue without it
                }
                else
                {
                    job.Status = JobStatus.CidxReady;
                    job.CidxStatus = cidxResult.Status;
                    if (statusCallback != null)
                        await statusCallback.OnStatusChangedAsync(job);
                }
            }

            // Set status to running and notify
            job.Status = JobStatus.Running;
            if (statusCallback != null)
                await statusCallback.OnStatusChangedAsync(job);

            // Direct execution (original approach for backward compatibility)
            return await ExecuteClaudeDirectly(job, userInfo, cancellationToken);
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

    /// <summary>
    /// Launch Claude Code with shell output redirection for crash resilience
    /// </summary>
    private async Task<(int ExitCode, string Output)> LaunchClaudeCodeWithRedirection(
        Job job, UserInfo userInfo, string outputFilePath, string pidFilePath, CancellationToken cancellationToken)
    {
        try
        {
            var claudeArgs = await BuildClaudeArgumentsAsync(job);
            var environment = BuildEnvironment(job, userInfo);

            // Create a shell script that will run Claude Code with output redirection
            var scriptPath = Path.Combine(job.CowPath, $".claude-job-{job.Id}.sh");
            var claudeCommand = _claudeCommand.Split(' ')[0]; // Get the command (e.g., "claude")
            var claudeBaseArgs = string.Join(" ", _claudeCommand.Split(' ').Skip(1)); // Get base args
            var fullArgs = string.IsNullOrEmpty(claudeArgs.Trim()) ? claudeBaseArgs : $"{claudeBaseArgs} {claudeArgs}";

            // Process placeholder replacements in the prompt before execution
            var processedPrompt = ProcessPromptPlaceholders(job.Prompt, job.UploadedFiles);

            // Build shell script content with proper output redirection and Unix line endings
            var lines = new[]
            {
                "#!/bin/bash",
                $"# Auto-generated script for job {job.Id}",
                "set -e",
                "",
                "# Set environment variables"
            }
            .Concat(environment.Select(kv => $"export {kv.Key}=\"{kv.Value}\""))
            .Concat(new[]
            {
                "",
                "# Change to job directory", 
                $"cd \"{job.CowPath}\"",
                "",
                "# Save PID and run Claude Code with output redirection",
                $"echo $$ > \"{pidFilePath}\"",
                $"echo \"{processedPrompt.Replace("\"", "\\\"")}\" | {claudeCommand} {fullArgs} >> \"{outputFilePath}\" 2>&1",
                $"echo \"Exit code: $?\" >> \"{outputFilePath}\""
            });
            
            var scriptContent = string.Join("\n", lines) + "\n";

            // Write script to file with explicit UTF-8 encoding and Unix line endings
            await File.WriteAllTextAsync(scriptPath, scriptContent, new UTF8Encoding(false), cancellationToken);
            
            // Make script executable
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                var chmodProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "chmod",
                        Arguments = $"+x \"{scriptPath}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                chmodProcess.Start();
                await chmodProcess.WaitForExitAsync(cancellationToken);
            }

            // Launch the script and detach (fire and forget)  
            var processInfo = new ProcessStartInfo
            {
                FileName = "/bin/bash",
                Arguments = $"\"{scriptPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = job.CowPath,
                RedirectStandardOutput = false,
                RedirectStandardError = false
            };

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                ImpersonateUser(processInfo, userInfo);
            }

            try
            {
                var process = Process.Start(processInfo);
                if (process == null)
                {
                    throw new InvalidOperationException("Failed to start Claude Code process");
                }

                // Store the process ID in the job for monitoring
                job.ClaudeProcessId = process.Id;
                
                // Give the process a moment to start and potentially fail
                await Task.Delay(100, cancellationToken);
                
                // Check if process is still running (not immediately failed)
                if (!process.HasExited)
                {
                    _logger.LogInformation("Claude Code process started successfully for job {JobId} with PID {ProcessId}", 
                        job.Id, process.Id);
                    return (0, "Process launched successfully");
                }
                else
                {
                    var exitCode = process.ExitCode;
                    _logger.LogError("Claude Code process exited immediately for job {JobId} with exit code {ExitCode}", 
                        job.Id, exitCode);
                    return (exitCode, $"Process exited immediately with code {exitCode}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start Claude Code process for job {JobId}", job.Id);
                return (-1, $"Failed to start process: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to launch Claude Code for job {JobId}", job.Id);
            return (-1, $"Launch failed: {ex.Message}");
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
        // Note: User impersonation is no longer needed since the service runs as the current user
        // instead of root. The service user already has the necessary permissions to execute commands.
        _logger.LogDebug("Service running as user {CurrentUser}, no impersonation needed", 
            Environment.UserName);
    }

    private async Task<(bool Success, string Status, string ErrorMessage)> HandleGitOperationsAsync(Job job, UserInfo userInfo, IJobStatusCallback? statusCallback, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Starting git operations for job {JobId}", job.Id);
            job.Status = JobStatus.GitPulling;
            job.GitStatus = "checking";
            if (statusCallback != null)
                await statusCallback.OnStatusChangedAsync(job);

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

    private async Task<(bool Success, string Status, string ErrorMessage)> HandleCidxOperationsAsync(Job job, UserInfo userInfo, IJobStatusCallback? statusCallback, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Starting cidx operations for job {JobId} on repository {Repository}", job.Id, job.Repository);
            
            // Verify that repository is cidx-aware and pre-indexed
            bool isRepositoryPreIndexed = await IsRepositoryPreIndexedAsync(job.Repository);
            
            if (!isRepositoryPreIndexed)
            {
                var errorMessage = $"Repository '{job.Repository}' is not cidx-aware or was not properly indexed during registration. Cannot run cidx-aware job.";
                _logger.LogError(errorMessage);
                return (false, "failed", errorMessage);
            }

            _logger.LogInformation("Repository {Repository} is pre-indexed. Starting cidx reconciliation for job {JobId}", job.Repository, job.Id);
            return await StartCidxServiceForPreIndexedRepository(job, userInfo, statusCallback, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cidx operations failed for job {JobId}", job.Id);
            return (false, "failed", $"Cidx operations error: {ex.Message}");
        }
    }

    private async Task<bool> IsRepositoryPreIndexedAsync(string repositoryName)
    {
        try
        {
            // Check if repository has completed status and is cidx-aware
            // Repository settings file should exist in the repository directory
            var repositoriesPath = Path.Combine(Directory.GetCurrentDirectory(), "workspace", "repos");
            var repositoryPath = Path.Combine(repositoriesPath, repositoryName);
            var settingsFile = Path.Combine(repositoryPath, ".claude-batch-settings.json");
            
            if (!File.Exists(settingsFile))
            {
                _logger.LogWarning("Repository settings file not found for {Repository}: {Path}", repositoryName, settingsFile);
                return false;
            }

            var settingsJson = await File.ReadAllTextAsync(settingsFile);
            var settings = System.Text.Json.JsonSerializer.Deserialize(settingsJson, AppJsonSerializerContext.Default.DictionaryStringObject);
            
            if (settings == null) return false;

            // Check if repository is cidx-aware and completed
            bool isCidxAware = settings.ContainsKey("CidxAware") && (settings["CidxAware"]?.ToString() ?? "").Equals("true", StringComparison.OrdinalIgnoreCase);
            bool isCompleted = settings.ContainsKey("CloneStatus") && (settings["CloneStatus"]?.ToString() ?? "").Equals("completed", StringComparison.OrdinalIgnoreCase);
            
            _logger.LogInformation("Repository {Repository} cidx status - CidxAware: {CidxAware}, CloneStatus: {CloneStatus}", 
                repositoryName, isCidxAware, settings.GetValueOrDefault("CloneStatus", "unknown"));
            
            return isCidxAware && isCompleted;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking if repository {Repository} is pre-indexed", repositoryName);
            return false;
        }
    }

    private async Task<(bool Success, string Status, string ErrorMessage)> StartCidxServiceForPreIndexedRepository(Job job, UserInfo userInfo, IJobStatusCallback? statusCallback, CancellationToken cancellationToken)
    {
        try
        {
            job.Status = JobStatus.CidxIndexing;
            job.CidxStatus = "starting";
            if (statusCallback != null)
                await statusCallback.OnStatusChangedAsync(job);

            // Fix cidx configuration for the newly CoWed repository (this is safe and needed for CoW)
            // NOTE: CoW clones inherit embedding provider config from original repo, so fix-config doesn't need embedding params
            _logger.LogInformation("Fixing cidx configuration for pre-indexed repository job {JobId}", job.Id);
            var fixConfigResult = await ExecuteCidxCommandAsync("fix-config --force", job.CowPath, userInfo, cancellationToken);
            if (fixConfigResult.ExitCode != 0)
            {
                _logger.LogWarning("Cidx fix-config failed for job {JobId}: {Output}", job.Id, fixConfigResult.Output);
                // Continue anyway as this might not be critical
            }

            // Start cidx container
            _logger.LogInformation("Starting cidx service for pre-indexed repository job {JobId}", job.Id);
            var startResult = await ExecuteCidxCommandAsync("start", job.CowPath, userInfo, cancellationToken);
            if (startResult.ExitCode != 0)
            {
                return (false, "failed", $"Cidx start failed: {startResult.Output}");
            }

            job.CidxStatus = "reconciling";

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

            job.CidxStatus = "ready";
            _logger.LogInformation("Cidx reconcile completed successfully for pre-indexed repository job {JobId}", job.Id);
            return (true, "ready", string.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reconcile cidx for pre-indexed repository job {JobId}", job.Id);
            return (false, "failed", $"Cidx reconcile error: {ex.Message}");
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
        
        // CRITICAL: Pass through VOYAGE_API_KEY from configuration for voyage-ai embedding provider
        var voyageApiKey = _configuration["Cidx:VoyageApiKey"];
        if (!string.IsNullOrEmpty(voyageApiKey))
        {
            processInfo.EnvironmentVariables["VOYAGE_API_KEY"] = voyageApiKey;
            _logger.LogDebug("Set VOYAGE_API_KEY environment variable for cidx command: {Command}", cidxArgs);
        }
        else
        {
            _logger.LogWarning("VOYAGE_API_KEY not configured - cidx may default to ollama");
        }

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

    public async Task<string> GenerateJobTitleAsync(string prompt, string? repositoryPath = null, CancellationToken cancellationToken = default)
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
                CreateNoWindow = true,
                WorkingDirectory = repositoryPath ?? Environment.CurrentDirectory // Set working directory to repository context
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

    /// <summary>
    /// Check if a process is still running by PID
    /// </summary>
    public bool IsProcessRunning(int processId)
    {
        try
        {
            var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            // Process not found
            return false;
        }
        catch (InvalidOperationException)
        {
            // Process has exited
            return false;
        }
    }

    /// <summary>
    /// Check if a job has completed by looking at output file and PID
    /// </summary>
    public async Task<(bool IsComplete, string Output)> CheckJobCompletion(Job job)
    {
        var outputFilePath = Path.Combine(job.CowPath, $".claude-job-{job.Id}.output");
        var pidFilePath = Path.Combine(job.CowPath, $".claude-job-{job.Id}.pid");

        try
        {
            // If no process ID is stored, check if output file exists and completed
            if (!job.ClaudeProcessId.HasValue)
            {
                if (File.Exists(outputFilePath))
                {
                    var output = await File.ReadAllTextAsync(outputFilePath);
                    // Check if output contains "Exit code:" which indicates completion
                    if (output.Contains("Exit code:"))
                    {
                        return (true, output);
                    }
                }
                return (false, string.Empty);
            }

            // Check if process is still running
            var isRunning = IsProcessRunning(job.ClaudeProcessId.Value);
            
            if (!isRunning && File.Exists(outputFilePath))
            {
                // Process finished, read the output
                var output = await File.ReadAllTextAsync(outputFilePath);
                return (true, output);
            }

            return (false, string.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking job completion for job {JobId}", job.Id);
            return (false, string.Empty);
        }
    }

    /// <summary>
    /// Recover jobs that were running when the server crashed
    /// </summary>
    public async Task<List<Job>> RecoverCrashedJobsAsync(IEnumerable<Job> runningJobs)
    {
        var recoveredJobs = new List<Job>();

        foreach (var job in runningJobs)
        {
            try
            {
                _logger.LogInformation("Checking crashed job recovery for job {JobId}", job.Id);

                var outputFilePath = Path.Combine(job.CowPath, $".claude-job-{job.Id}.output");
                var pidFilePath = Path.Combine(job.CowPath, $".claude-job-{job.Id}.pid");

                // Check if output file exists and is complete
                if (File.Exists(outputFilePath))
                {
                    var output = await File.ReadAllTextAsync(outputFilePath);
                    
                    // If output contains "Exit code:" it means the job completed
                    if (output.Contains("Exit code:"))
                    {
                        // Extract exit code from output
                        var exitCodeMatch = System.Text.RegularExpressions.Regex.Match(output, @"Exit code: (\d+)");
                        var exitCode = exitCodeMatch.Success ? int.Parse(exitCodeMatch.Groups[1].Value) : 0;

                        // Mark job as completed
                        job.Status = exitCode == 0 ? JobStatus.Completed : JobStatus.Failed;
                        job.Output = output.Replace($"Exit code: {exitCode}", "").Trim();
                        job.ExitCode = exitCode;
                        job.CompletedAt = DateTime.UtcNow;
                        job.ClaudeProcessId = null;

                        recoveredJobs.Add(job);
                        _logger.LogInformation("Recovered completed job {JobId} with exit code {ExitCode}", job.Id, exitCode);
                        continue;
                    }
                }

                // Check if PID file exists and process is still running
                if (File.Exists(pidFilePath))
                {
                    var pidContent = await File.ReadAllTextAsync(pidFilePath);
                    if (int.TryParse(pidContent.Trim(), out var pid))
                    {
                        if (IsProcessRunning(pid))
                        {
                            // Process is still running, update job with PID
                            job.ClaudeProcessId = pid;
                            _logger.LogInformation("Found running Claude Code process for job {JobId} with PID {ProcessId}", job.Id, pid);
                            continue;
                        }
                        else
                        {
                            // Process died but no complete output, mark as failed
                            job.Status = JobStatus.Failed;
                            job.Output = File.Exists(outputFilePath) ? await File.ReadAllTextAsync(outputFilePath) : "Process died unexpectedly during execution";
                            job.ExitCode = -1;
                            job.CompletedAt = DateTime.UtcNow;
                            job.ClaudeProcessId = null;

                            recoveredJobs.Add(job);
                            _logger.LogWarning("Recovered failed job {JobId} - process died unexpectedly", job.Id);
                        }
                    }
                }
                else
                {
                    // No PID file, assume job failed to start properly
                    job.Status = JobStatus.Failed;
                    job.Output = "Job failed to start properly - no process information found";
                    job.ExitCode = -1;
                    job.CompletedAt = DateTime.UtcNow;
                    job.ClaudeProcessId = null;

                    recoveredJobs.Add(job);
                    _logger.LogWarning("Recovered failed job {JobId} - no process information found", job.Id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during job recovery for job {JobId}", job.Id);
                
                // Mark as failed on recovery error
                job.Status = JobStatus.Failed;
                job.Output = $"Recovery error: {ex.Message}";
                job.ExitCode = -1;
                job.CompletedAt = DateTime.UtcNow;
                job.ClaudeProcessId = null;
                
                recoveredJobs.Add(job);
            }
        }

        _logger.LogInformation("Job recovery completed: {RecoveredCount} jobs recovered", recoveredJobs.Count);
        return recoveredJobs;
    }

    /// <summary>
    /// Execute Claude Code directly (original approach for backward compatibility with tests)
    /// </summary>
    private async Task<(int ExitCode, string Output)> ExecuteClaudeDirectly(Job job, UserInfo userInfo, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Executing Claude Code directly for job {JobId} (backward compatibility mode)", job.Id);

            var args = new List<string>();

            // Add repository-specific arguments
            if (job.Options.GitAware)
            {
                args.Add("--git");
            }

            // Add uploaded files if provided
            if (job.UploadedFiles?.Any() == true)
            {
                foreach (var uploadedFile in job.UploadedFiles)
                {
                    var filePath = Path.Combine(job.CowPath, "files", uploadedFile);
                    if (File.Exists(filePath))
                    {
                        args.Add($"--file \"{filePath}\"");
                    }
                }
            }

            var fullArgs = string.Join(" ", args);
            var claudeCommand = _claudeCommand;

            if (!string.IsNullOrEmpty(fullArgs))
            {
                claudeCommand = $"{_claudeCommand} {fullArgs}";
            }

            _logger.LogDebug("Executing command: {Command} (working directory: {WorkingDirectory})", 
                claudeCommand, job.CowPath);

            // Process placeholder replacements in the prompt before execution
            var processedPrompt = ProcessPromptPlaceholders(job.Prompt, job.UploadedFiles);

            var processInfo = new ProcessStartInfo
            {
                FileName = "bash",
                Arguments = $"-c \"echo '{processedPrompt.Replace("'", "'\"'\"'")}' | {claudeCommand}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = job.CowPath
            };

            // Set environment variables
            foreach (var envVar in job.Options.Environment)
            {
                processInfo.Environment[envVar.Key] = envVar.Value;
            }

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

            // Wait for process completion or cancellation
            try
            {
                await process.WaitForExitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited)
                {
                    try
                    {
                        process.Kill(true);
                        await Task.Delay(100, CancellationToken.None); // Give time for cleanup
                    }
                    catch (Exception killEx)
                    {
                        _logger.LogWarning(killEx, "Failed to kill process during cancellation for job {JobId}", job.Id);
                    }
                }
                throw;
            }

            var output = outputBuilder.ToString();
            var error = errorBuilder.ToString();

            if (!string.IsNullOrEmpty(error))
            {
                output += $"\nErrors:\n{error}";
            }

            var exitCode = process.ExitCode;
            
            _logger.LogInformation("Claude Code execution completed for job {JobId} with exit code {ExitCode}", 
                job.Id, exitCode);

            return (exitCode, output);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Direct Claude Code execution failed for job {JobId}", job.Id);
            return (-1, $"Execution failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Process placeholder replacements in the prompt
    /// Supports {{filename}} patterns that get replaced with ./files/filename
    /// </summary>
    private string ProcessPromptPlaceholders(string prompt, List<string>? uploadedFiles)
    {
        if (string.IsNullOrEmpty(prompt) || uploadedFiles == null || !uploadedFiles.Any())
            return prompt;

        var processedPrompt = prompt;
        
        // Process each uploaded file for placeholder replacement
        foreach (var filename in uploadedFiles)
        {
            var placeholder = $"{{{{{filename}}}}}"; // {{filename}}
            var replacement = $"./files/{filename}";
            
            if (processedPrompt.Contains(placeholder))
            {
                processedPrompt = processedPrompt.Replace(placeholder, replacement);
                _logger.LogInformation("Replaced placeholder {Placeholder} with {Replacement} in prompt for uploaded file", placeholder, replacement);
            }
        }
        
        // Also support generic {{filename}} pattern - replace with list of all files
        var genericPlaceholder = "{{filename}}";
        if (processedPrompt.Contains(genericPlaceholder))
        {
            var allFiles = string.Join(" ", uploadedFiles.Select(f => $"./files/{f}"));
            processedPrompt = processedPrompt.Replace(genericPlaceholder, allFiles);
            _logger.LogInformation("Replaced generic {{filename}} placeholder with all uploaded files: {AllFiles}", allFiles);
        }

        return processedPrompt;
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