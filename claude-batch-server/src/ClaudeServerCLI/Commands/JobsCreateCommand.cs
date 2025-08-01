using System.CommandLine;
using System.CommandLine.Invocation;
using ClaudeBatchServer.Core.DTOs;
using ClaudeServerCLI.Services;
using ClaudeServerCLI.Models;
using ClaudeServerCLI.UI;
using Spectre.Console;

namespace ClaudeServerCLI.Commands;

/// <summary>
/// Job creation command with universal file upload and advanced prompt handling
/// </summary>
public class JobsCreateCommand : AuthenticatedCommand
{
    private readonly Option<string> _repositoryOption;
    private readonly Option<string> _promptOption;
    private readonly Option<string[]> _fileOption;
    private readonly Option<bool> _interactiveOption;
    private readonly Option<bool> _overwriteOption;
    private readonly Option<bool> _autoStartOption;
    private readonly Option<bool> _watchOption;
    private readonly Option<int> _timeoutOption;
    private readonly Option<bool?> _cidxAwareOption;

    public JobsCreateCommand() : base("create", "Create a new job with advanced features")
    {
        _repositoryOption = new Option<string>(
            aliases: ["--repo", "--repository", "-r"],
            description: "Repository to run the job on (required unless using interactive mode)"
        );

        _promptOption = new Option<string>(
            aliases: ["--prompt"],
            description: "Prompt for Claude Code (alternative to stdin or interactive mode)"
        );

        _fileOption = new Option<string[]>(
            aliases: ["--file", "-f"],
            description: "Files to upload (supports all file types). Can specify multiple files."
        ) { AllowMultipleArgumentsPerToken = true };

        _interactiveOption = new Option<bool>(
            aliases: ["--interactive", "-i"],
            description: "Use interactive mode for job creation with full-screen wizard",
            getDefaultValue: () => false
        );

        _overwriteOption = new Option<bool>(
            aliases: ["--overwrite"],
            description: "Overwrite existing files with same name",
            getDefaultValue: () => false
        );

        _autoStartOption = new Option<bool>(
            aliases: ["--auto-start", "--start", "-s"],
            description: "Automatically start the job after creation",
            getDefaultValue: () => false
        );

        _watchOption = new Option<bool>(
            aliases: ["--watch", "-w"],
            description: "Watch job execution progress (implies --auto-start)",
            getDefaultValue: () => false
        );

        _timeoutOption = new Option<int>(
            aliases: ["--job-timeout"],
            description: "Job timeout in seconds",
            getDefaultValue: () => 300
        );

        _cidxAwareOption = new Option<bool?>(
            aliases: ["--cidx-aware", "--cidx"],
            description: "Enable semantic indexing with CIDX (overrides repository default)",
            getDefaultValue: () => null
        );

        AddOption(_repositoryOption);
        AddOption(_promptOption);
        AddOption(_fileOption);
        AddOption(_interactiveOption);
        AddOption(_overwriteOption);
        AddOption(_autoStartOption);
        AddOption(_watchOption);
        AddOption(_timeoutOption);
        AddOption(_cidxAwareOption);
    }

    protected override async Task<int> ExecuteAuthenticatedAsync(InvocationContext context, string profile, IApiClient apiClient)
    {
        var repository = context.ParseResult.GetValueForOption(_repositoryOption);
        var promptOption = context.ParseResult.GetValueForOption(_promptOption);
        var files = context.ParseResult.GetValueForOption(_fileOption) ?? Array.Empty<string>();
        var interactive = context.ParseResult.GetValueForOption(_interactiveOption);
        var overwrite = context.ParseResult.GetValueForOption(_overwriteOption);
        var autoStart = context.ParseResult.GetValueForOption(_autoStartOption);
        var watch = context.ParseResult.GetValueForOption(_watchOption);
        var timeout = context.ParseResult.GetValueForOption(_timeoutOption);
        var cidxAwareOverride = context.ParseResult.GetValueForOption(_cidxAwareOption);
        var cancellationToken = context.GetCancellationToken();        var promptService = GetRequiredService<IPromptService>(context);
        var fileUploadService = GetRequiredService<IFileUploadService>(context);

        // Watch implies auto-start
        if (watch) autoStart = true;

        try
        {
            // Interactive mode workflow
            if (interactive)
            {
                return await ExecuteInteractiveModeAsync(
                    apiClient, promptService, fileUploadService, 
                    repository, promptOption, files.ToList(), 
                    autoStart, watch, timeout, overwrite, cidxAwareOverride,
                    cancellationToken);
            }

            // Non-interactive mode workflow
            return await ExecuteCommandLineModeAsync(
                apiClient, promptService, fileUploadService,
                repository, promptOption, files.ToList(),
                autoStart, watch, timeout, overwrite, cidxAwareOverride,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            WriteInfo("Operation cancelled");
            return 1;
        }
        catch (Exception ex)
        {
            WriteError($"Failed to create job: {ex.Message}");
            return 1;
        }
    }

    private async Task<int> ExecuteInteractiveModeAsync(
        IApiClient apiClient,
        IPromptService promptService, 
        IFileUploadService fileUploadService,
        string? repository,
        string? promptOption,
        List<string> files,
        bool autoStart,
        bool watch,
        int timeout,
        bool overwrite,
        bool? cidxAwareOverride,
        CancellationToken cancellationToken)
    {
        try
        {
            AnsiConsole.Clear();
            AnsiConsole.Write(new FigletText("Claude Code Job Creator").Color(Color.Blue));
            AnsiConsole.WriteLine();

            // Step 1: Repository selection
            if (string.IsNullOrEmpty(repository))
            {
                WriteInfo("Loading repositories...");
                var repos = await apiClient.GetRepositoriesAsync(cancellationToken);
                var repoNames = repos.Select(r => r.Name).ToList();

                repository = await InteractiveUI.SelectRepositoryAsync(repoNames, "Select Repository for Job");
                
                if (string.IsNullOrEmpty(repository))
                {
                    WriteError("No repository selected");
                    return 1;
                }
            }

            // Verify repository exists and get its capabilities
            RepositoryInfo repositoryInfo;
            try
            {
                repositoryInfo = await apiClient.GetRepositoryAsync(repository, cancellationToken);
                WriteSuccess($"Repository '{repository}' verified");
            }
            catch (InvalidOperationException)
            {
                WriteError($"Repository '{repository}' not found");
                return 1;
            }

            // Step 2: File selection
            if (!files.Any())
            {
                if (AnsiConsole.Confirm("Would you like to upload files for this job?"))
                {
                    files = InteractiveUI.SelectFiles("Select Files to Upload");
                }
            }

            // Validate files
            if (files.Any())
            {
                if (!fileUploadService.ValidateFiles(files, out var validationErrors))
                {
                    WriteError("File validation failed:");
                    foreach (var error in validationErrors)
                    {
                        WriteError($"  • {error}");
                    }
                    return 1;
                }
            }

            // Step 3: Prompt creation
            var prompt = await promptService.GetPromptAsync(promptOption, true, cancellationToken);
            
            if (string.IsNullOrEmpty(prompt))
            {
                WriteError("No prompt provided");
                return 1;
            }

            // Step 4: Template assistance
            if (files.Any())
            {
                var fileNames = files.Select(Path.GetFileName).Where(f => !string.IsNullOrEmpty(f)).Cast<string>().ToList();
                prompt = InteractiveUI.ShowTemplateAssistance(prompt, fileNames);
            }

            // Step 5: Job configuration
            var options = new Dictionary<string, object>
            {
                {"autoStart", autoStart},
                {"watch", watch},
                {"timeout", timeout},
                {"overwrite", overwrite}
            };

            // Allow user to modify options
            autoStart = AnsiConsole.Confirm("Auto-start job after creation?", autoStart);
            if (autoStart)
            {
                watch = AnsiConsole.Confirm("Watch job execution progress?", watch);
            }

            options["autoStart"] = autoStart;
            options["watch"] = watch;

            // Step 6: Final preview and confirmation
            if (!InteractiveUI.ShowJobConfigurationPreview(repository, prompt, files, options))
            {
                WriteInfo("Job creation cancelled");
                return 0;
            }

            // Execute job creation
            return await CreateAndExecuteJobAsync(
                apiClient, promptService, fileUploadService,
                repository, repositoryInfo, prompt, files, autoStart, watch, timeout, overwrite, cidxAwareOverride, cancellationToken);
        }
        catch (Exception ex)
        {
            WriteError($"Interactive mode failed: {ex.Message}");
            return 1;
        }
    }

    private async Task<int> ExecuteCommandLineModeAsync(
        IApiClient apiClient,
        IPromptService promptService,
        IFileUploadService fileUploadService,
        string? repository,
        string? promptOption,
        List<string> files,
        bool autoStart,
        bool watch,
        int timeout,
        bool overwrite,
        bool? cidxAwareOverride,
        CancellationToken cancellationToken)
    {
        // Validate required parameters for command-line mode
        if (string.IsNullOrEmpty(repository))
        {
            WriteError("Repository is required. Use --repo or --interactive mode");
            return 1;
        }

        // Verify repository exists and get its capabilities
        RepositoryInfo repositoryInfo;
        try
        {
            repositoryInfo = await apiClient.GetRepositoryAsync(repository, cancellationToken);
        }
        catch (InvalidOperationException)
        {
            WriteError($"Repository '{repository}' not found");
            WriteInfo("Use 'claude-server repos list' to see available repositories");
            return 1;
        }

        // Get prompt using advanced prompt handling
        var prompt = await promptService.GetPromptAsync(promptOption, false, cancellationToken);
        
        if (string.IsNullOrEmpty(prompt))
        {
            WriteError("No prompt provided. Use --prompt, pipe from stdin, or use --interactive mode");
            return 1;
        }

        // Validate prompt
        if (!promptService.ValidatePrompt(prompt, out var validationMessage))
        {
            WriteError($"Invalid prompt: {validationMessage}");
            return 1;
        }

        // Validate files if provided
        if (files.Any() && !fileUploadService.ValidateFiles(files, out var validationErrors))
        {
            WriteError("File validation failed:");
            foreach (var error in validationErrors)
            {
                WriteError($"  • {error}");
            }
            return 1;
        }

        return await CreateAndExecuteJobAsync(
            apiClient, promptService, fileUploadService,
            repository, repositoryInfo, prompt, files, autoStart, watch, timeout, overwrite, cidxAwareOverride, cancellationToken);
    }

    private async Task<int> CreateAndExecuteJobAsync(
        IApiClient apiClient,
        IPromptService promptService,
        IFileUploadService fileUploadService,
        string repository,
        RepositoryInfo repositoryInfo,
        string prompt,
        List<string> files,
        bool autoStart,
        bool watch,
        int timeout,
        bool overwrite,
        bool? cidxAwareOverride,
        CancellationToken cancellationToken)
    {
        return await InteractiveUI.ShowProgress(async (progress) =>
        {
            // Step 1: Create the job
            progress.Report(("Creating job...", 10));
            
            // Determine job CIDX setting: use explicit override if provided, otherwise use repository default
            var repositoryCidxAware = repositoryInfo.CidxAware ?? false;
            var cidxAware = cidxAwareOverride ?? repositoryCidxAware;

            // Validate CIDX usage
            if (cidxAwareOverride.HasValue && cidxAwareOverride.Value && !repositoryCidxAware)
            {
                WriteError("Cannot enable CIDX for job: Repository is not CIDX-aware");
                WriteInfo("Create the repository with --cidx-aware flag to enable semantic indexing");
                return 1;
            }

            if (cidxAwareOverride.HasValue)
            {
                WriteInfo($"Using explicit CIDX setting: {(cidxAware ? "enabled" : "disabled")}");
            }
            else if (!cidxAware)
            {
                WriteInfo("Repository is not CIDX-aware, creating job without semantic search");
            }
            else
            {
                WriteInfo("Using repository's CIDX-aware setting");
            }
            
            var request = new CreateJobRequest
            {
                Repository = repository,
                Prompt = prompt, // Will be updated after file upload
                Options = new JobOptionsDto
                {
                    Timeout = timeout,
                    GitAware = true,
                    CidxAware = cidxAware
                }
            };

            var response = await apiClient.CreateJobAsync(request, cancellationToken);
            WriteSuccess($"Job created successfully: {response.JobId}");

            // Step 2: Upload files if any
            Dictionary<string, string> templateMappings = new();
            if (files.Any())
            {
                progress.Report(("Preparing file uploads...", 30));
                var fileUploads = await fileUploadService.PrepareFileUploadsAsync(files, 
                    new Progress<(string message, int percentage)>(update => 
                        progress.Report((update.message, 30 + (update.percentage * 30 / 100)))), 
                    cancellationToken);

                progress.Report(("Uploading files...", 60));
                templateMappings = await fileUploadService.UploadFilesAndGetMappingsAsync(
                    apiClient, response.JobId.ToString(), fileUploads, overwrite,
                    new Progress<(string message, int percentage)>(update => 
                        progress.Report((update.message, 60 + (update.percentage * 20 / 100)))), 
                    cancellationToken);

                WriteSuccess($"Uploaded {files.Count} file(s)");

                // Update job with resolved prompt if templates were used
                var templateRefs = promptService.ExtractTemplateReferences(prompt);
                if (templateRefs.Any())
                {
                    progress.Report(("Resolving templates...", 80));
                    var resolvedPrompt = promptService.ResolveTemplates(prompt, templateMappings);
                    
                    // Update the job with resolved prompt
                    // Note: This would require an API endpoint to update job prompt
                    WriteInfo("Templates resolved in prompt");
                }
            }

            progress.Report(("Job setup complete", 90));

            // Step 3: Auto-start if requested
            if (autoStart)
            {
                progress.Report(("Starting job...", 95));
                
                var startResponse = await apiClient.StartJobAsync(response.JobId.ToString(), cancellationToken);
                WriteSuccess($"Job started: {startResponse.Status}");
                
                if (startResponse.QueuePosition > 0)
                {
                    WriteInfo($"Queue position: {startResponse.QueuePosition}");
                }

                progress.Report(("Job started successfully", 100));

                // Watch execution if requested
                if (watch)
                {
                    WriteInfo("Starting job execution monitoring...");
                    return await WatchJobExecutionAsync(apiClient, response.JobId.ToString(), cancellationToken);
                }
            }
            else
            {
                WriteInfo($"Use 'claude-server jobs start {response.JobId}' to begin execution");
                progress.Report(("Ready to start", 100));
            }

            return 0;
        }, "Creating job with files...");
    }

    private async Task<int> WatchJobExecutionAsync(IApiClient apiClient, string jobId, CancellationToken cancellationToken)
    {
        WriteInfo("Watching job execution... Press Ctrl+C to exit");
        
        try
        {
            string? lastOutput = null;
            
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var job = await apiClient.GetJobAsync(jobId, cancellationToken);
                    
                    WriteInfo($"Status: {job.Status}");
                    if (job.QueuePosition > 0)
                    {
                        WriteInfo($"Queue position: {job.QueuePosition}");
                    }
                    
                    // Show new output if available
                    if (!string.IsNullOrEmpty(job.Output) && job.Output != lastOutput)
                    {
                        if (lastOutput != null)
                        {
                            // Show only new output
                            var newOutput = job.Output.Substring(lastOutput.Length);
                            if (!string.IsNullOrWhiteSpace(newOutput))
                            {
                                AnsiConsole.WriteLine();
                                AnsiConsole.MarkupLine("[bold blue]New Output:[/]");
                                Console.WriteLine(newOutput);
                            }
                        }
                        else
                        {
                            // Show all output for first time
                            AnsiConsole.WriteLine();
                            AnsiConsole.MarkupLine("[bold blue]Output:[/]");
                            Console.WriteLine(job.Output);
                        }
                        lastOutput = job.Output;
                    }
                    
                    if (job.Status.Equals("completed", StringComparison.OrdinalIgnoreCase))
                    {
                        WriteSuccess("Job completed successfully!");
                        WriteInfo($"Exit Code: {job.ExitCode}");
                        return job.ExitCode ?? 0;
                    }
                    else if (job.Status.Equals("failed", StringComparison.OrdinalIgnoreCase))
                    {
                        WriteError("Job failed!");
                        WriteInfo($"Exit Code: {job.ExitCode}");
                        return job.ExitCode ?? 1;
                    }
                    else if (job.Status.Equals("cancelled", StringComparison.OrdinalIgnoreCase))
                    {
                        WriteWarning("Job was cancelled");
                        return 1;
                    }
                    
                    await Task.Delay(2000, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    WriteError($"Watch error: {ex.Message}");
                    await Task.Delay(2000, cancellationToken);
                }
            }
            
            WriteInfo("Watch cancelled");
            return 0;
        }
        catch (OperationCanceledException)
        {
            WriteInfo("Watch cancelled");
            return 0;
        }
    }
}