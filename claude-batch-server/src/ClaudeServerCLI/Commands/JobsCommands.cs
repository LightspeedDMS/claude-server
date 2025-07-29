using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text.Json;
using Spectre.Console;
using ClaudeBatchServer.Core.DTOs;
using ClaudeServerCLI.Services;
using ClaudeServerCLI.Models;
using YamlDotNet.Serialization;

namespace ClaudeServerCLI.Commands;

public class JobsCommand : Command
{
    public JobsCommand() : base("jobs", """
        Job management commands for Claude Code batch processing
        
        Create, monitor, and manage AI-powered batch jobs that operate on registered
        repositories. Jobs run Claude Code with your prompts in isolated Copy-on-Write
        repository clones.
        
        JOB LIFECYCLE:
          1. Create job with repository and prompt
          2. Start job execution (or use --start flag)
          3. Monitor progress with logs/status
          4. Review results and output files
          5. Delete completed jobs to free resources
        
        EXAMPLES:
          # Create and start a job
          claude-server jobs create --repo myproject --prompt "Add unit tests" --start
          
          # List all jobs
          claude-server jobs list
          claude-server jobs list --status running --watch
          
          # Monitor a specific job
          claude-server jobs show abc123 --follow
          claude-server jobs logs abc123 --follow
          
          # Manage job lifecycle
          claude-server jobs start abc123
          claude-server jobs cancel abc123
          claude-server jobs delete abc123
          
          # Manage job files
          claude-server jobs files list abc123
          claude-server jobs files upload abc123 ./input.txt
          claude-server jobs files download abc123 output.txt
          
          # Advanced job creation
          claude-server jobs create --repo myproject --prompt "Analyze security" --git-aware --cidx-aware --timeout 3600
        """)
    {
        AddCommand(new JobsListCommand());
        AddCommand(new EnhancedJobsCreateCommand()); // Use enhanced version
        AddCommand(new JobsShowCommand());
        AddCommand(new JobsStartCommand());
        AddCommand(new JobsCancelCommand());
        AddCommand(new JobsDeleteCommand());
        AddCommand(new JobsLogsCommand());
        AddCommand(new JobFilesCommand());
    }
}

public class JobsListCommand : AuthenticatedCommand
{
    private readonly Option<string> _formatOption;
    private readonly Option<bool> _watchOption;
    private readonly Option<string> _statusOption;
    private readonly Option<string> _repositoryOption;
    private readonly Option<int> _limitOption;

    public JobsListCommand() : base("list", """
        List all batch jobs with filtering and real-time monitoring
        
        Shows job status, repository, creation time, and execution details.
        Supports filtering by status and repository, with various output formats.
        
        EXAMPLES:
          # List all jobs
          claude-server jobs list
          
          # Filter by status
          claude-server jobs list --status running
          claude-server jobs list --status completed
          claude-server jobs list --status failed
          
          # Filter by repository
          claude-server jobs list --repository myproject
          
          # Limit results
          claude-server jobs list --limit 10
          
          # Watch for real-time updates
          claude-server jobs list --watch
          claude-server jobs list --status running --watch
          
          # Export as JSON/YAML
          claude-server jobs list --format json
          claude-server jobs list --format yaml
        """)
    {
        _formatOption = new Option<string>(
            aliases: ["--format", "-f"],
            description: "Output format: 'table' (human-readable), 'json' (machine-readable), 'yaml' (structured)",
            getDefaultValue: () => "table"
        );

        _watchOption = new Option<bool>(
            aliases: ["--watch", "-w"],
            description: "Watch for changes and refresh display every 2 seconds. Press Ctrl+C to exit.",
            getDefaultValue: () => false
        );

        _statusOption = new Option<string>(
            aliases: ["--status", "-s"],
            description: "Filter by job status: 'pending', 'running', 'completed', 'failed', 'cancelled'"
        );

        _repositoryOption = new Option<string>(
            aliases: ["--repository", "--repo", "-r"],
            description: "Filter jobs by repository name. Shows only jobs for the specified repository."
        );

        _limitOption = new Option<int>(
            aliases: ["--limit", "-l"],
            description: "Maximum number of jobs to display. Useful for large job lists.",
            getDefaultValue: () => 50
        );

        AddOption(_formatOption);
        AddOption(_watchOption);
        AddOption(_statusOption);
        AddOption(_repositoryOption);
        AddOption(_limitOption);
    }

    protected override async Task<int> ExecuteAuthenticatedAsync(InvocationContext context, string profile, IApiClient apiClient)
    {
        var format = context.ParseResult.GetValueForOption(_formatOption) ?? "table";
        var watch = context.ParseResult.GetValueForOption(_watchOption);
        var status = context.ParseResult.GetValueForOption(_statusOption);
        var repository = context.ParseResult.GetValueForOption(_repositoryOption);
        var limit = context.ParseResult.GetValueForOption(_limitOption);
        var cancellationToken = context.GetCancellationToken();        var filter = new CliJobFilter
        {
            Status = status,
            Repository = repository,
            Limit = limit
        };

        if (watch)
        {
            return await WatchJobsAsync(apiClient, filter, format, cancellationToken);
        }
        else
        {
            return await ListJobsOnceAsync(apiClient, filter, format, cancellationToken);
        }
    }

    private async Task<int> ListJobsOnceAsync(IApiClient apiClient, CliJobFilter filter, string format, CancellationToken cancellationToken)
    {
        try
        {
            var jobs = await apiClient.GetJobsAsync(filter, cancellationToken);
            DisplayJobs(jobs, format);
            return 0;
        }
        catch (Exception ex)
        {
            WriteError($"Failed to get jobs: {ex.Message}");
            return 1;
        }
    }

    private async Task<int> WatchJobsAsync(IApiClient apiClient, CliJobFilter filter, string format, CancellationToken cancellationToken)
    {
        WriteInfo("Watching jobs... Press Ctrl+C to exit");
        
        try
        {
            var lastCount = 0;
            var lastRunningCount = 0;

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var jobs = await apiClient.GetJobsAsync(filter, cancellationToken);
                    var jobList = jobs.ToList();
                    var runningCount = jobList.Count(j => j.Status.Equals("running", StringComparison.OrdinalIgnoreCase));
                    
                    // Clear console and redisplay if using table format
                    if (format == "table")
                    {
                        Console.Clear();
                        AnsiConsole.MarkupLine("[bold blue]Jobs (Live Updates - Press Ctrl+C to exit)[/]");
                        AnsiConsole.WriteLine();
                    }
                    
                    DisplayJobs(jobList, format);
                    
                    if (format == "table")
                    {
                        AnsiConsole.WriteLine();
                        AnsiConsole.MarkupLine($"[grey]🔄 Refreshing every 2 seconds... Found {jobList.Count} jobs ({runningCount} running)[/]");
                        AnsiConsole.MarkupLine("[grey]Press Ctrl+C to exit[/]");
                    }
                    
                    if (jobList.Count != lastCount || runningCount != lastRunningCount)
                    {
                        lastCount = jobList.Count;
                        lastRunningCount = runningCount;
                        if (format != "table")
                        {
                            WriteInfo($"Job counts changed: {lastCount} total ({runningCount} running)");
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (format == "table")
                    {
                        AnsiConsole.MarkupLine($"[red]Error: {ex.Message}[/]");
                    }
                    else
                    {
                        WriteError($"Watch error: {ex.Message}");
                    }
                }

                try
                {
                    await Task.Delay(2000, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            WriteInfo("Watch mode stopped");
            return 0;
        }
        catch (OperationCanceledException)
        {
            WriteInfo("Watch mode cancelled");
            return 0;
        }
    }

    private static void DisplayJobs(IEnumerable<JobInfo> jobs, string format)
    {
        UI.ModernDisplay.DisplayJobs(jobs, format);
    }

    private static string GetJobStatusDisplay(string status)
    {
        return status.ToLowerInvariant() switch
        {
            "pending" => "[yellow]⏸️ Pending[/]",
            "running" => "[green]⚡ Running[/]",
            "completed" => "[green]✅ Complete[/]",
            "failed" => "[red]❌ Failed[/]",
            "cancelled" => "[grey]⏹️ Cancelled[/]",
            _ => "[grey]❓ Unknown[/]"
        };
    }

    private static string GetJobDurationDisplay(JobInfo job)
    {
        if (job.StartedAt == null) return "-";
        
        var endTime = job.CompletedAt ?? DateTime.UtcNow;
        var duration = endTime - job.StartedAt.Value;
        
        return duration.TotalSeconds < 60 
            ? $"{duration.TotalSeconds:0}s"
            : $"{duration.TotalMinutes:0}m {duration.Seconds}s";
    }
}

public class JobsCreateCommand : AuthenticatedCommand
{
    private readonly Option<string> _repositoryOption;
    private readonly Option<string> _promptOption;
    private readonly Option<bool> _autoStartOption;
    private readonly Option<bool> _watchOption;
    private readonly Option<int> _timeoutOption;

    public JobsCreateCommand() : base("create", "Create a new job")
    {
        _repositoryOption = new Option<string>(
            aliases: ["--repo", "--repository", "-r"],
            description: "Repository to run the job on (required)"
        ) { IsRequired = true };

        _promptOption = new Option<string>(
            aliases: ["--prompt"],
            description: "Prompt for Claude Code (required)"
        ) { IsRequired = true };

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

        AddOption(_repositoryOption);
        AddOption(_promptOption);
        AddOption(_autoStartOption);
        AddOption(_watchOption);
        AddOption(_timeoutOption);
    }

    protected override async Task<int> ExecuteAuthenticatedAsync(InvocationContext context, string profile, IApiClient apiClient)
    {
        var repository = context.ParseResult.GetValueForOption(_repositoryOption)!;
        var prompt = context.ParseResult.GetValueForOption(_promptOption)!;
        var autoStart = context.ParseResult.GetValueForOption(_autoStartOption);
        var watch = context.ParseResult.GetValueForOption(_watchOption);
        var timeout = context.ParseResult.GetValueForOption(_timeoutOption);
        var cancellationToken = context.GetCancellationToken();        // Watch implies auto-start
        if (watch) autoStart = true;

        try
        {
            // Verify repository exists
            try
            {
                await apiClient.GetRepositoryAsync(repository, cancellationToken);
            }
            catch (InvalidOperationException)
            {
                WriteError($"Repository '{repository}' not found");
                WriteInfo("Use 'claude-server repos list' to see available repositories");
                return 1;
            }

            WriteInfo($"Creating job for repository '{repository}'...");
            
            var request = new CreateJobRequest
            {
                Repository = repository,
                Prompt = prompt,
                Options = new JobOptionsDto
                {
                    Timeout = timeout,
                    GitAware = true,
                    CidxAware = true
                }
            };

            var response = await apiClient.CreateJobAsync(request, cancellationToken);
            
            WriteSuccess($"Job created successfully: {response.JobId}");
            WriteInfo($"Status: {response.Status}");
            WriteInfo($"Cow Path: {response.CowPath}");

            if (autoStart)
            {
                WriteInfo("Starting job execution...");
                var startResponse = await apiClient.StartJobAsync(response.JobId.ToString(), cancellationToken);
                WriteSuccess($"Job started: {startResponse.Status}");
                
                if (startResponse.QueuePosition > 0)
                {
                    WriteInfo($"Queue position: {startResponse.QueuePosition}");
                }

                if (watch)
                {
                    return await WatchJobExecutionAsync(apiClient, response.JobId.ToString(), cancellationToken);
                }
            }
            else
            {
                WriteInfo($"Use 'claude-server jobs start {response.JobId}' to begin execution");
            }

            return 0;
        }
        catch (Exception ex)
        {
            WriteError($"Failed to create job: {ex.Message}");
            return 1;
        }
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

public class JobsShowCommand : AuthenticatedCommand
{
    private readonly Argument<string> _jobIdArgument;
    private readonly Option<string> _formatOption;
    private readonly Option<bool> _watchOption;

    public JobsShowCommand() : base("show", "Show detailed job information")
    {
        _jobIdArgument = new Argument<string>(
            name: "jobId",
            description: "Job ID (full or partial)"
        );

        _formatOption = new Option<string>(
            aliases: ["--format", "-f"],
            description: "Output format (table, json, yaml)",
            getDefaultValue: () => "table"
        );

        _watchOption = new Option<bool>(
            aliases: ["--watch", "-w"],
            description: "Watch for changes and update display in real-time",
            getDefaultValue: () => false
        );

        AddArgument(_jobIdArgument);
        AddOption(_formatOption);
        AddOption(_watchOption);
    }

    protected override async Task<int> ExecuteAuthenticatedAsync(InvocationContext context, string profile, IApiClient apiClient)
    {
        var jobId = context.ParseResult.GetValueForArgument(_jobIdArgument);
        var format = context.ParseResult.GetValueForOption(_formatOption) ?? "table";
        var watch = context.ParseResult.GetValueForOption(_watchOption);
        var cancellationToken = context.GetCancellationToken();        // If partial ID provided, try to find full ID
        var fullJobId = await ResolveJobIdAsync(apiClient, jobId, cancellationToken);
        if (string.IsNullOrEmpty(fullJobId))
        {
            WriteError($"Job '{jobId}' not found");
            return 1;
        }

        if (watch)
        {
            return await WatchJobAsync(apiClient, fullJobId, format, cancellationToken);
        }
        else
        {
            return await ShowJobOnceAsync(apiClient, fullJobId, format, cancellationToken);
        }
    }

    private async Task<string?> ResolveJobIdAsync(IApiClient apiClient, string partialId, CancellationToken cancellationToken)
    {
        try
        {
            // Try as full GUID first
            if (Guid.TryParse(partialId, out _))
            {
                await apiClient.GetJobAsync(partialId, cancellationToken);
                return partialId;
            }

            // Search for partial match
            var jobs = await apiClient.GetJobsAsync(new CliJobFilter { Limit = 100 }, cancellationToken);
            var matches = jobs.Where(j => j.JobId.ToString().StartsWith(partialId, StringComparison.OrdinalIgnoreCase)).ToList();

            if (matches.Count == 0)
                return null;

            if (matches.Count > 1)
            {
                WriteWarning($"Multiple jobs found matching '{partialId}':");
                foreach (var match in matches.Take(5))
                {
                    WriteInfo($"  {match.JobId} - {match.Repository} ({match.Status})");
                }
                WriteError("Please provide a more specific job ID");
                return null;
            }

            return matches[0].JobId.ToString();
        }
        catch
        {
            return null;
        }
    }

    private async Task<int> ShowJobOnceAsync(IApiClient apiClient, string jobId, string format, CancellationToken cancellationToken)
    {
        try
        {
            var job = await apiClient.GetJobAsync(jobId, cancellationToken);
            DisplayJob(job, format);
            return 0;
        }
        catch (InvalidOperationException)
        {
            WriteError($"Job '{jobId}' not found");
            return 1;
        }
        catch (Exception ex)
        {
            WriteError($"Failed to get job: {ex.Message}");
            return 1;
        }
    }

    private async Task<int> WatchJobAsync(IApiClient apiClient, string jobId, string format, CancellationToken cancellationToken)
    {
        WriteInfo($"Watching job '{jobId[..8]}'... Press Ctrl+C to exit");
        
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var job = await apiClient.GetJobAsync(jobId, cancellationToken);
                    
                    // Clear console and redisplay if using table format
                    if (format == "table")
                    {
                        Console.Clear();
                        AnsiConsole.MarkupLine($"[bold blue]Job: {jobId[..8]} (Live Updates - Press Ctrl+C to exit)[/]");
                        AnsiConsole.WriteLine();
                    }
                    
                    DisplayJob(job, format);
                    
                    if (format == "table")
                    {
                        AnsiConsole.WriteLine();
                        AnsiConsole.MarkupLine("[grey]🔄 Refreshing every 2 seconds... Press Ctrl+C to exit[/]");
                    }

                    // Exit watch if job is completed
                    if (job.Status.Equals("completed", StringComparison.OrdinalIgnoreCase) ||
                        job.Status.Equals("failed", StringComparison.OrdinalIgnoreCase) ||
                        job.Status.Equals("cancelled", StringComparison.OrdinalIgnoreCase))
                    {
                        WriteInfo($"Job finished with status: {job.Status}");
                        return job.ExitCode ?? (job.Status.Equals("completed", StringComparison.OrdinalIgnoreCase) ? 0 : 1);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (format == "table")
                    {
                        AnsiConsole.MarkupLine($"[red]Error: {ex.Message}[/]");
                    }
                    else
                    {
                        WriteError($"Watch error: {ex.Message}");
                    }
                }

                try
                {
                    await Task.Delay(2000, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            WriteInfo("Watch mode stopped");
            return 0;
        }
        catch (OperationCanceledException)
        {
            WriteInfo("Watch mode cancelled");
            return 0;
        }
    }

    private static void DisplayJob(JobStatusResponse job, string format)
    {
        UI.ModernDisplay.DisplayJobDetails(job, format);
    }

    private static string GetJobStatusDisplay(string status)
    {
        return status.ToLowerInvariant() switch
        {
            "pending" => "[yellow]⏸️ Pending[/]",
            "running" => "[green]⚡ Running[/]",
            "completed" => "[green]✅ Completed[/]",
            "failed" => "[red]❌ Failed[/]",
            "cancelled" => "[grey]⏹️ Cancelled[/]",
            _ => "[grey]❓ Unknown[/]"
        };
    }

    private static string GetStatusDisplay(string status)
    {
        return status.ToLowerInvariant() switch
        {
            "ready" => "[green]✅ Ready[/]",
            "not_checked" => "[grey]❓ Not Checked[/]",
            "not_started" => "[grey]⏸️ Not Started[/]",
            "indexing" => "[yellow]⚡ Indexing[/]",
            "failed" => "[red]❌ Failed[/]",
            _ => $"[grey]{status}[/]"
        };
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalDays >= 1)
            return $"{duration.Days}d {duration.Hours}h {duration.Minutes}m";
        else if (duration.TotalHours >= 1)
            return $"{duration.Hours}h {duration.Minutes}m {duration.Seconds}s";
        else if (duration.TotalMinutes >= 1)
            return $"{duration.Minutes}m {duration.Seconds}s";
        else
            return $"{duration.TotalSeconds:0.0}s";
    }
}

public class JobsStartCommand : AuthenticatedCommand
{
    private readonly Argument<string> _jobIdArgument;

    public JobsStartCommand() : base("start", "Start job execution")
    {
        _jobIdArgument = new Argument<string>(
            name: "jobId",
            description: "Job ID (full or partial)"
        );

        AddArgument(_jobIdArgument);
    }

    protected override async Task<int> ExecuteAuthenticatedAsync(InvocationContext context, string profile, IApiClient apiClient)
    {
        var jobId = context.ParseResult.GetValueForArgument(_jobIdArgument);
        var cancellationToken = context.GetCancellationToken();        try
        {
            WriteInfo($"Starting job '{jobId}'...");
            var response = await apiClient.StartJobAsync(jobId, cancellationToken);
            
            WriteSuccess($"Job started successfully");
            WriteInfo($"Status: {response.Status}");
            
            if (response.QueuePosition > 0)
            {
                WriteInfo($"Queue position: {response.QueuePosition}");
            }
            
            WriteInfo($"Use 'claude-server jobs show {jobId} --watch' to monitor progress");
            return 0;
        }
        catch (InvalidOperationException)
        {
            WriteError($"Job '{jobId}' not found");
            return 1;
        }
        catch (Exception ex)
        {
            WriteError($"Failed to start job: {ex.Message}");
            return 1;
        }
    }
}

public class JobsCancelCommand : AuthenticatedCommand
{
    private readonly Argument<string> _jobIdArgument;

    public JobsCancelCommand() : base("cancel", "Cancel job execution")
    {
        _jobIdArgument = new Argument<string>(
            name: "jobId",
            description: "Job ID (full or partial)"
        );

        AddArgument(_jobIdArgument);
    }

    protected override async Task<int> ExecuteAuthenticatedAsync(InvocationContext context, string profile, IApiClient apiClient)
    {
        var jobId = context.ParseResult.GetValueForArgument(_jobIdArgument);
        var cancellationToken = context.GetCancellationToken();        try
        {
            WriteInfo($"Cancelling job '{jobId}'...");
            var response = await apiClient.CancelJobAsync(jobId, cancellationToken);
            
            if (response.Success)
            {
                WriteSuccess($"Job cancelled successfully");
                WriteInfo($"Status: {response.Status}");
                if (response.CancelledAt.HasValue)
                {
                    WriteInfo($"Cancelled at: {response.CancelledAt.Value:yyyy-MM-dd HH:mm:ss}");
                }
                if (!string.IsNullOrEmpty(response.Message))
                {
                    WriteInfo($"Message: {response.Message}");
                }
                return 0;
            }
            else
            {
                WriteError($"Failed to cancel job: {response.Message}");
                return 1;
            }
        }
        catch (InvalidOperationException)
        {
            WriteError($"Job '{jobId}' not found");
            return 1;
        }
        catch (Exception ex)
        {
            WriteError($"Failed to cancel job: {ex.Message}");
            return 1;
        }
    }
}

public class JobsDeleteCommand : AuthenticatedCommand
{
    private readonly Argument<string> _jobIdArgument;
    private readonly Option<bool> _forceOption;

    public JobsDeleteCommand() : base("delete", "Delete a job")
    {
        _jobIdArgument = new Argument<string>(
            name: "jobId",
            description: "Job ID (full or partial)"
        );

        _forceOption = new Option<bool>(
            aliases: ["--force", "-f"],
            description: "Force deletion without confirmation",
            getDefaultValue: () => false
        );

        AddArgument(_jobIdArgument);
        AddOption(_forceOption);
    }

    protected override async Task<int> ExecuteAuthenticatedAsync(InvocationContext context, string profile, IApiClient apiClient)
    {
        var jobId = context.ParseResult.GetValueForArgument(_jobIdArgument);
        var force = context.ParseResult.GetValueForOption(_forceOption);
        var cancellationToken = context.GetCancellationToken();        try
        {
            // Check if job exists first
            JobStatusResponse? job;
            try
            {
                job = await apiClient.GetJobAsync(jobId, cancellationToken);
            }
            catch (InvalidOperationException)
            {
                WriteError($"Job '{jobId}' not found");
                return 1;
            }

            if (!force)
            {
                WriteWarning($"This will delete job '{jobId[..8]}' and all associated data");
                WriteWarning($"Status: {job.Status}");
                WriteWarning($"Repository: {job.CowPath}");
                
                if (!AnsiConsole.Confirm("Are you sure you want to continue?"))
                {
                    WriteInfo("Operation cancelled");
                    return 0;
                }
            }

            WriteInfo($"Deleting job '{jobId}'...");
            var response = await apiClient.DeleteJobAsync(jobId, cancellationToken);
            
            if (response.Success)
            {
                WriteSuccess($"Job '{jobId[..8]}' deleted successfully");
                if (response.Terminated)
                {
                    WriteInfo("Running job was terminated");
                }
                if (response.CowRemoved)
                {
                    WriteInfo("Job files were removed");
                }
                return 0;
            }
            else
            {
                WriteError("Failed to delete job");
                return 1;
            }
        }
        catch (Exception ex)
        {
            WriteError($"Failed to delete job: {ex.Message}");
            return 1;
        }
    }
}

public class JobsLogsCommand : AuthenticatedCommand
{
    private readonly Argument<string> _jobIdArgument;
    private readonly Option<bool> _watchOption;
    private readonly Option<int> _tailOption;

    public JobsLogsCommand() : base("logs", "View job execution logs")
    {
        _jobIdArgument = new Argument<string>(
            name: "jobId",
            description: "Job ID (full or partial)"
        );

        _watchOption = new Option<bool>(
            aliases: ["--watch", "-w", "--follow", "-f"],
            description: "Stream logs in real-time until job completes",
            getDefaultValue: () => false
        );

        _tailOption = new Option<int>(
            aliases: ["--tail"],
            description: "Number of lines to show from the end",
            getDefaultValue: () => 50
        );

        AddArgument(_jobIdArgument);
        AddOption(_watchOption);
        AddOption(_tailOption);
    }

    protected override async Task<int> ExecuteAuthenticatedAsync(InvocationContext context, string profile, IApiClient apiClient)
    {
        var jobId = context.ParseResult.GetValueForArgument(_jobIdArgument);
        var watch = context.ParseResult.GetValueForOption(_watchOption);
        var tail = context.ParseResult.GetValueForOption(_tailOption);
        var cancellationToken = context.GetCancellationToken();        if (watch)
        {
            return await WatchJobLogsAsync(apiClient, jobId, cancellationToken);
        }
        else
        {
            return await ShowJobLogsOnceAsync(apiClient, jobId, tail, cancellationToken);
        }
    }

    private async Task<int> ShowJobLogsOnceAsync(IApiClient apiClient, string jobId, int tail, CancellationToken cancellationToken)
    {
        try
        {
            var job = await apiClient.GetJobAsync(jobId, cancellationToken);
            
            if (string.IsNullOrEmpty(job.Output))
            {
                WriteInfo("No logs available yet");
                return 0;
            }

            var lines = job.Output.Split('\n');
            var displayLines = tail > 0 && lines.Length > tail
                ? lines[^tail..]
                : lines;

            if (tail > 0 && lines.Length > tail)
            {
                AnsiConsole.MarkupLine($"[grey]... showing last {tail} lines of {lines.Length} total lines ...[/]");
            }

            foreach (var line in displayLines)
            {
                Console.WriteLine(line);
            }

            return 0;
        }
        catch (InvalidOperationException)
        {
            WriteError($"Job '{jobId}' not found");
            return 1;
        }
        catch (Exception ex)
        {
            WriteError($"Failed to get job logs: {ex.Message}");
            return 1;
        }
    }

    private async Task<int> WatchJobLogsAsync(IApiClient apiClient, string jobId, CancellationToken cancellationToken)
    {
        WriteInfo($"Streaming logs for job '{jobId[..8]}'... Press Ctrl+C to exit");
        
        try
        {
            string? lastOutput = null;
            
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var job = await apiClient.GetJobAsync(jobId, cancellationToken);
                    
                    // Show new output if available
                    if (!string.IsNullOrEmpty(job.Output) && job.Output != lastOutput)
                    {
                        if (lastOutput != null)
                        {
                            // Show only new output
                            var newOutput = job.Output.Substring(lastOutput.Length);
                            if (!string.IsNullOrWhiteSpace(newOutput))
                            {
                                Console.Write(newOutput);
                            }
                        }
                        else
                        {
                            // Show all output for first time
                            Console.Write(job.Output);
                        }
                        lastOutput = job.Output;
                    }
                    
                    // Exit if job is finished
                    if (job.Status.Equals("completed", StringComparison.OrdinalIgnoreCase) ||
                        job.Status.Equals("failed", StringComparison.OrdinalIgnoreCase) ||
                        job.Status.Equals("cancelled", StringComparison.OrdinalIgnoreCase))
                    {
                        Console.WriteLine();
                        WriteInfo($"Job finished with status: {job.Status}");
                        return job.ExitCode ?? (job.Status.Equals("completed", StringComparison.OrdinalIgnoreCase) ? 0 : 1);
                    }
                    
                    await Task.Delay(1000, cancellationToken); // More frequent updates for logs
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
            
            Console.WriteLine();
            WriteInfo("Log streaming stopped");
            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine();
            WriteInfo("Log streaming cancelled");
            return 0;
        }
    }
}