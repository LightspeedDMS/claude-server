using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ClaudeBatchServer.Core.DTOs;
using ClaudeBatchServer.Core.Models;

namespace ClaudeBatchServer.Core.Services;

public class JobService : IJobService, IJobStatusCallback
{
    private readonly IRepositoryService _repositoryService;
    private readonly IClaudeCodeExecutor _claudeExecutor;
    private readonly IJobPersistenceService _jobPersistenceService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<JobService> _logger;
    
    private readonly ConcurrentDictionary<Guid, Job> _jobs = new();
    private readonly ConcurrentQueue<Guid> _jobQueue = new();
    private readonly SemaphoreSlim _concurrencyLimiter;
    private readonly int _maxConcurrentJobs;
    private readonly int _jobTimeoutHours;
    private DateTime _lastCleanupTime = DateTime.MinValue;

    public JobService(
        IRepositoryService repositoryService,
        IClaudeCodeExecutor claudeExecutor,
        IJobPersistenceService jobPersistenceService,
        IConfiguration configuration,
        ILogger<JobService> logger)
    {
        _repositoryService = repositoryService;
        _claudeExecutor = claudeExecutor;
        _jobPersistenceService = jobPersistenceService;
        _configuration = configuration;
        _logger = logger;
        
        _maxConcurrentJobs = int.Parse(_configuration["Jobs:MaxConcurrent"] ?? "5");
        _jobTimeoutHours = int.Parse(_configuration["Jobs:TimeoutHours"] ?? "24");
        _concurrencyLimiter = new SemaphoreSlim(_maxConcurrentJobs, _maxConcurrentJobs);
    }

    /// <summary>
    /// Initialize job service by loading persisted jobs for crash recovery
    /// </summary>
    public async Task InitializeAsync()
    {
        try
        {
            _logger.LogInformation("Initializing JobService - loading persisted jobs...");
            
            // Load all persisted jobs
            var persistedJobs = await _jobPersistenceService.LoadAllJobsAsync();
            
            // Rebuild in-memory state
            foreach (var job in persistedJobs)
            {
                _jobs[job.Id] = job;
                
                // Rebuild queue for queued jobs - maintain original order
                if (job.Status == JobStatus.Queued)
                {
                    _jobQueue.Enqueue(job.Id);
                }
            }
            
            // Recover jobs that were running when server crashed
            var runningJobs = persistedJobs.Where(j => j.Status == JobStatus.Running).ToList();
            if (runningJobs.Any())
            {
                _logger.LogInformation("Found {RunningJobCount} jobs that were running during server crash - attempting recovery", runningJobs.Count);
                
                try
                {
                    var recoveredJobs = await _claudeExecutor.RecoverCrashedJobsAsync(runningJobs);
                    
                    foreach (var recoveredJob in recoveredJobs)
                    {
                        // Update in-memory state
                        _jobs[recoveredJob.Id] = recoveredJob;
                        
                        // Persist the recovered job status
                        await _jobPersistenceService.SaveJobAsync(recoveredJob);
                        
                        _logger.LogInformation("Recovered job {JobId} with status {Status}", recoveredJob.Id, recoveredJob.Status);
                    }
                    
                    _logger.LogInformation("Successfully recovered {RecoveredCount} crashed jobs", recoveredJobs.Count);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to recover crashed jobs - they will remain in running state");
                }
            }
            
            // Clean up old jobs based on retention policy
            await _jobPersistenceService.CleanupOldJobsAsync();
            
            _logger.LogInformation("JobService initialized successfully - loaded {JobCount} jobs ({QueuedCount} queued)", 
                persistedJobs.Count, _jobQueue.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize JobService");
            throw;
        }
    }

    public async Task<CreateJobResponse> CreateJobAsync(CreateJobRequest request, string username)
    {
        var repository = await _repositoryService.GetRepositoryAsync(request.Repository);
        if (repository == null)
            throw new ArgumentException($"Repository '{request.Repository}' not found");

        // DEBUG: Add extensive logging to understand the CIDX validation issue
        _logger.LogInformation("DEBUG: CreateJobAsync - repo.Name: '{Name}', repo.CidxAware: {CidxAware}, repo.CloneStatus: '{CloneStatus}', request.CidxAware: {RequestCidxAware}", 
            repository.Name, repository.CidxAware, repository.CloneStatus ?? "null", request.Options.CidxAware);

        // Validate cidx-aware job request with user-friendly error messages
        if (request.Options.CidxAware)
        {
            if (!repository.CidxAware)
            {
                throw new ArgumentException($"⚠️ CIDX Error: The repository '{request.Repository}' is not set up for semantic search.\n\n" +
                    "To use CIDX features, please:\n" +
                    "1. Re-register the repository with CIDX enabled, or\n" + 
                    "2. Disable the 'Use semantic search (CIDX)' option in the job settings\n\n" +
                    "CIDX provides AI-powered code understanding but requires initial setup.");
            }
            
            if (repository.CloneStatus != "completed")
            {
                throw new ArgumentException($"⚠️ Repository Not Ready: The repository '{request.Repository}' is still being prepared (status: {repository.CloneStatus}).\n\n" +
                    "Please wait for the repository to finish cloning and indexing, then try again.\n" +
                    "You can check the repository status on the Repositories page.");
            }
        }

        // Generate job title using Claude Code in the repository context
        var jobTitle = await _claudeExecutor.GenerateJobTitleAsync(request.Prompt, repository.Name, CancellationToken.None);

        var job = new Job
        {
            Id = Guid.NewGuid(),
            Username = username,
            Prompt = request.Prompt,
            Title = jobTitle,
            Repository = request.Repository,
            UploadedFiles = request.Images.ToList(),
            Status = JobStatus.Created,
            Options = new JobOptions 
            { 
                TimeoutSeconds = request.Options.Timeout,
                AutoCleanup = true,
                GitAware = request.Options.GitAware,
                CidxAware = request.Options.CidxAware
            },
            CreatedAt = DateTime.UtcNow
        };

        // CRITICAL FIX: Don't create CoW clone during job creation
        // CoW clone will be created during job execution after all staged files are ready
        // This prevents the "bad request" error when trying to upload files before CoW workspace exists

        _jobs[job.Id] = job;
        
        // Persist job to disk for crash resilience
        await _jobPersistenceService.SaveJobAsync(job);

        _logger.LogInformation("Created job {JobId} for user {Username} with repository {Repository}", 
            job.Id, username, request.Repository);

        return new CreateJobResponse
        {
            JobId = job.Id,
            Status = job.Status.ToString().ToLower(),
            User = username,
            CowPath = string.Empty, // CoW path will be available after job execution starts
            Title = job.Title
        };
    }

    public async Task<StartJobResponse> StartJobAsync(Guid jobId, string username)
    {
        if (!_jobs.TryGetValue(jobId, out var job))
            throw new ArgumentException("Job not found");

        if (job.Username != username)
            throw new UnauthorizedAccessException("Access denied");

        if (job.Status != JobStatus.Created)
            throw new InvalidOperationException($"Job is in {job.Status} status and cannot be started");

        job.Status = JobStatus.Queued;
        job.QueuePosition = GetQueuePosition();
        _jobQueue.Enqueue(jobId);
        
        // Persist job status change
        await _jobPersistenceService.SaveJobAsync(job);

        _logger.LogInformation("Queued job {JobId} for user {Username}, position: {Position}", 
            jobId, username, job.QueuePosition);

        return new StartJobResponse
        {
            JobId = jobId,
            Status = job.Status.ToString().ToLower(),
            QueuePosition = job.QueuePosition
        };
    }

    public Task<JobStatusResponse?> GetJobStatusAsync(Guid jobId, string username)
    {
        if (!_jobs.TryGetValue(jobId, out var job))
            return Task.FromResult<JobStatusResponse?>(null);

        if (job.Username != username)
            throw new UnauthorizedAccessException("Access denied");

        return Task.FromResult<JobStatusResponse?>(new JobStatusResponse
        {
            JobId = job.Id,
            Status = job.Status.ToString().ToLower(),
            Output = job.Output,
            ExitCode = job.ExitCode,
            CowPath = job.CowPath,
            QueuePosition = job.QueuePosition,
            CreatedAt = job.CreatedAt,
            StartedAt = job.StartedAt,
            CompletedAt = job.CompletedAt,
            GitPullStatus = job.GitPullStatus,
            GitStatus = job.GitStatus,
            CidxStatus = job.CidxStatus,
            Title = job.Title
        });
    }

    public async Task<DeleteJobResponse> DeleteJobAsync(Guid jobId, string username)
    {
        if (!_jobs.TryGetValue(jobId, out var job))
            throw new ArgumentException("Job not found");

        if (job.Username != username)
            throw new UnauthorizedAccessException("Access denied");

        bool terminated = false;
        if (job.Status == JobStatus.Running)
        {
            job.Status = JobStatus.Terminated;
            terminated = true;
        }

        // CRITICAL: Run cidx uninstall before removing CoW clone
        // This prevents Docker container leaks and cleans up root-owned data when jobs used cidx
        bool cidxUninstalled = false;
        if (job.Options.CidxAware && Directory.Exists(job.CowPath))
        {
            try
            {
                _logger.LogInformation("Running cidx uninstall for job {JobId} to clean up containers and root-owned data", jobId);
                var uninstallResult = await _claudeExecutor.UninstallCidxAsync(job.CowPath, username, CancellationToken.None);
                cidxUninstalled = uninstallResult.ExitCode == 0;
                if (!cidxUninstalled)
                {
                    _logger.LogWarning("Failed to run cidx uninstall for job {JobId}: {Output}", jobId, uninstallResult.Output);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error running cidx uninstall for job {JobId}, continuing with removal", jobId);
                // Continue with removal even if cidx uninstall fails
            }
        }

        var cowRemoved = await _repositoryService.RemoveCowCloneAsync(job.CowPath);
        _jobs.TryRemove(jobId, out _);
        
        // Remove job from persistent storage
        await _jobPersistenceService.DeleteJobAsync(jobId);

        _logger.LogInformation("Deleted job {JobId} for user {Username}, terminated: {Terminated}, cidx uninstalled: {CidxUninstalled}, CoW removed: {CowRemoved}", 
            jobId, username, terminated, cidxUninstalled, cowRemoved);

        return new DeleteJobResponse
        {
            Success = true,
            Terminated = terminated,
            CowRemoved = cowRemoved
        };
    }

    public async Task<CancelJobResponse> CancelJobAsync(Guid jobId, string username)
    {
        if (!_jobs.TryGetValue(jobId, out var job))
            throw new ArgumentException("Job not found");

        if (job.Username != username)
            throw new UnauthorizedAccessException("Access denied");

        // Check if job can be cancelled
        var cancellableStates = new[] 
        { 
            JobStatus.Created, 
            JobStatus.Queued, 
            JobStatus.GitPulling, 
            JobStatus.CidxIndexing,
            JobStatus.Running 
        };

        if (!cancellableStates.Contains(job.Status))
        {
            throw new InvalidOperationException($"Cannot cancel job in status '{job.Status}'. Only jobs that are created, queued, or running can be cancelled.");
        }

        // Set cancellation info
        job.Status = JobStatus.Cancelling;
        job.CancelledAt = DateTime.UtcNow;
        job.CancelReason = "User cancellation";
        
        // Persist cancellation status
        await _jobPersistenceService.SaveJobAsync(job);

        _logger.LogInformation("Job {JobId} marked for cancellation by user {Username}", jobId, username);

        return new CancelJobResponse
        {
            JobId = jobId,
            Status = job.Status.ToString().ToLower(),
            Success = true,
            Message = "Job has been marked for cancellation and will be terminated shortly",
            CancelledAt = job.CancelledAt
        };
    }

    public Task<List<JobListResponse>> GetUserJobsAsync(string username)
    {
        var userJobs = _jobs.Values
            .Where(j => j.Username == username)
            .OrderByDescending(j => j.CreatedAt)
            .Select(j => new JobListResponse
            {
                JobId = j.Id,
                User = j.Username,
                Status = j.Status.ToString().ToLower(),
                Started = j.StartedAt ?? j.CreatedAt,
                Repository = j.Repository,
                Title = j.Title
            })
            .ToList();

        return Task.FromResult(userJobs);
    }

    public async Task<ImageUploadResponse> UploadImageAsync(Guid jobId, string username, string filename, Stream imageStream)
    {
        // CONSOLIDATED: Use same file upload logic for everything - no distinction between files and images
        var fileResult = await UploadFileAsync(jobId, username, filename, imageStream, false);
        
        return new ImageUploadResponse
        {
            Filename = fileResult.Filename,
            Path = fileResult.Path
        };
    }

    public async Task<FileUploadResponse> UploadFileAsync(Guid jobId, string username, string filename, Stream fileStream, bool overwrite = false)
    {
        if (!_jobs.TryGetValue(jobId, out var job))
            throw new ArgumentException("Job not found");

        if (job.Username != username)
            throw new UnauthorizedAccessException("Access denied");

        // CRITICAL FIX: Use staging area instead of CoW workspace for file uploads
        // CoW workspace may not exist yet if job is still in created/queued state
        var stagingPath = _jobPersistenceService.GetJobStagingPath(jobId);
        Directory.CreateDirectory(stagingPath);

        var extension = Path.GetExtension(filename);
        var safeFilename = filename;

        // Preserve original filename but ensure safety
        if (!overwrite)
        {
            // If not overwriting, create unique filename
            var nameWithoutExtension = Path.GetFileNameWithoutExtension(filename);
            safeFilename = $"{nameWithoutExtension}_{Guid.NewGuid().ToString("N")[..8]}{extension}";
        }

        var fullPath = Path.Combine(stagingPath, safeFilename);
        bool wasOverwritten = File.Exists(fullPath);

        using var fileWriteStream = new FileStream(fullPath, FileMode.Create);
        await fileStream.CopyToAsync(fileWriteStream);

        // Track uploaded file with original filename (not the hashed staging filename)
        // This ensures Claude Code sees the original filename in the files/ directory
        if (!job.UploadedFiles.Contains(filename))
        {
            job.UploadedFiles.Add(filename);
        }

        // Persist job updates
        await _jobPersistenceService.SaveJobAsync(job);

        _logger.LogInformation("Uploaded file {Filename} to staging area for job {JobId} (overwritten: {Overwritten})", safeFilename, jobId, wasOverwritten);

        return new FileUploadResponse
        {
            Filename = safeFilename,
            Path = $"/staging/jobs/{jobId}/files/", // Updated to reflect staging location
            FileType = extension,
            FileSize = fileStream.Length,
            Overwritten = wasOverwritten
        };
    }

    public async Task ProcessJobQueueAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (_jobQueue.TryDequeue(out var jobId))
                {
                    if (_jobs.TryGetValue(jobId, out var job) && job.Status == JobStatus.Queued)
                    {
                        await _concurrencyLimiter.WaitAsync(cancellationToken);
                        
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await ProcessJobAsync(job);
                            }
                            finally
                            {
                                _concurrencyLimiter.Release();
                            }
                        }, cancellationToken);
                    }
                }
                else
                {
                    await Task.Delay(1000, cancellationToken);
                }

                await CleanupExpiredJobsAsync();
                UpdateQueuePositions();
                
                // Check for completed running jobs via PID monitoring
                await CheckRunningJobsAsync();
                
                // Periodically clean up old completed jobs (every 10 minutes)
                var now = DateTime.UtcNow;
                if (now - _lastCleanupTime > TimeSpan.FromMinutes(10))
                {
                    await _jobPersistenceService.CleanupOldJobsAsync();
                    _lastCleanupTime = now;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in job queue processing");
                await Task.Delay(5000, cancellationToken);
            }
        }
    }

    private async Task ProcessJobAsync(Job job)
    {
        try
        {
            // Check for cancellation before starting
            if (job.Status == JobStatus.Cancelling)
            {
                job.Status = JobStatus.Cancelled;
                job.CompletedAt = DateTime.UtcNow;
                job.Output = job.CancelReason;
                await _jobPersistenceService.SaveJobAsync(job);
                _logger.LogInformation("Job {JobId} was cancelled before execution", job.Id);
                return;
            }

            _logger.LogInformation("Starting job {JobId} for user {Username} with NEW WORKFLOW", job.Id, job.Username);
            
            job.StartedAt = DateTime.UtcNow;
            job.QueuePosition = 0;
            
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(job.Options.TimeoutSeconds));

            // NEW WORKFLOW STEP 1: Git pull on source repository (if GitAware)
            if (job.Options.GitAware)
            {
                job.Status = JobStatus.GitPulling;
                job.GitPullStatus = "pulling";
                await _jobPersistenceService.SaveJobAsync(job);
                
                var gitPullResult = await _repositoryService.PullRepositoryUpdatesAsync(job.Repository);
                job.GitPullStatus = gitPullResult.Status;
                
                if (!gitPullResult.Success)
                {
                    job.Status = JobStatus.GitFailed;
                    job.Output = gitPullResult.ErrorMessage;
                    job.CompletedAt = DateTime.UtcNow;
                    await _jobPersistenceService.SaveJobAsync(job);
                    _logger.LogError("Git pull failed for job {JobId}: {Error}", job.Id, gitPullResult.ErrorMessage);
                    return;
                }
                
                _logger.LogInformation("Git pull completed for job {JobId} with status: {Status}", job.Id, gitPullResult.Status);
            }

            // NEW WORKFLOW STEP 2-4: CIDX operations on source repository (if CidxAware)
            if (job.Options.CidxAware)
            {
                job.Status = JobStatus.CidxIndexing;
                job.CidxStatus = "indexing_source";
                await _jobPersistenceService.SaveJobAsync(job);
                
                var cidxResult = await ExecuteSourceRepositoryCidxOperationsAsync(job.Repository);
                
                if (!cidxResult.Success)
                {
                    _logger.LogWarning("CIDX operations failed on source repository for job {JobId}: {Error}", 
                        job.Id, cidxResult.ErrorMessage);
                    job.CidxStatus = "source_failed";
                }
                else
                {
                    job.CidxStatus = "source_ready";
                    _logger.LogInformation("CIDX operations completed on source repository for job {JobId}", job.Id);
                }
                
                await _jobPersistenceService.SaveJobAsync(job);
            }

            // NEW WORKFLOW STEP 5: Create CoW clone (existing functionality)
            _logger.LogInformation("Creating CoW clone for job {JobId}", job.Id);
            job.CowPath = await _repositoryService.CreateCowCloneAsync(job.Repository, job.Id);
            await _jobPersistenceService.SaveJobAsync(job);

            // NEW WORKFLOW STEP 5.1: Copy staged files to CoW workspace
            var copiedFileCount = await _jobPersistenceService.CopyStagedFilesToCowWorkspaceAsync(job.Id, job.CowPath);
            _logger.LogInformation("STAGING DEBUG: Copy operation returned {CopiedFileCount} files for job {JobId}", copiedFileCount, job.Id);
            
            if (copiedFileCount > 0)
            {
                _logger.LogInformation("Successfully copied {CopiedFileCount} staged files to CoW workspace for job {JobId}", copiedFileCount, job.Id);
                
                // Only clean up staging directory after successful copy
                _jobPersistenceService.CleanupStagingDirectory(job.Id);
                _logger.LogInformation("STAGING DEBUG: Cleaned up staging directory for job {JobId}", job.Id);
            }
            else
            {
                _logger.LogWarning("STAGING DEBUG: No files were copied from staging to CoW workspace for job {JobId} - staging directory preserved", job.Id);
            }

            // NEW WORKFLOW STEP 6-8: Launch Claude Code with new approach
            job.Status = JobStatus.Running;
            await _jobPersistenceService.SaveJobAsync(job);
            
            // Launch Claude Code with fire-and-forget approach (CIDX in CoW workspace handled by executor)
            var (exitCode, output) = await _claudeExecutor.ExecuteAsync(job, job.Username, this, timeoutCts.Token);
            
            // Check if the launch was successful
            if (exitCode != 0)
            {
                // Launch failed, mark job as failed
                job.ExitCode = exitCode;
                job.Output = output;
                job.Status = JobStatus.Failed;
                job.CompletedAt = DateTime.UtcNow;
                await _jobPersistenceService.SaveJobAsync(job);
                _logger.LogError("Failed to launch Claude Code for job {JobId}: {Output}", job.Id, output);
                return;
            }

            // Job launched successfully, the monitoring loop will check for completion
            _logger.LogInformation("Successfully launched Claude Code for job {JobId} with PID {ProcessId}", 
                job.Id, job.ClaudeProcessId);

            // Cleanup cidx if it was enabled for this job
            if (job.Options.CidxAware && job.CidxStatus == "ready")
            {
                await CleanupCidxAsync(job);
            }
        }
        catch (OperationCanceledException)
        {
            job.Status = JobStatus.Timeout;
            job.CompletedAt = DateTime.UtcNow;
            await _jobPersistenceService.SaveJobAsync(job);
            _logger.LogWarning("Job {JobId} timed out", job.Id);

            // Cleanup cidx if it was enabled for this job
            if (job.Options.CidxAware)
            {
                await CleanupCidxAsync(job);
            }
        }
        catch (Exception ex)
        {
            job.Status = JobStatus.Failed;
            job.Output = $"Execution error: {ex.Message}";
            job.CompletedAt = DateTime.UtcNow;
            await _jobPersistenceService.SaveJobAsync(job);
            _logger.LogError(ex, "Job {JobId} failed with exception", job.Id);

            // Cleanup cidx if it was enabled for this job
            if (job.Options.CidxAware)
            {
                await CleanupCidxAsync(job);
            }
        }
    }

    /// <summary>
    /// Callback implementation for job status changes - ensures all status changes are persisted to disk
    /// </summary>
    public async Task OnStatusChangedAsync(Job job)
    {
        try
        {
            await _jobPersistenceService.SaveJobAsync(job);
            _logger.LogDebug("Persisted status change for job {JobId}: {Status}", job.Id, job.Status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist status change for job {JobId}", job.Id);
        }
    }

    /// <summary>
    /// Execute CIDX operations on source repository before CoW cloning
    /// This follows the new workflow: start -> index --reconcile -> stop
    /// </summary>
    private async Task<(bool Success, string Status, string ErrorMessage)> ExecuteSourceRepositoryCidxOperationsAsync(string repositoryName)
    {
        try
        {
            var repository = await _repositoryService.GetRepositoryAsync(repositoryName);
            if (repository == null)
                return (false, "failed", $"Repository '{repositoryName}' not found");

            _logger.LogInformation("Starting CIDX operations on source repository {Repository} at {Path}", 
                repositoryName, repository.Path);

            // Step 1: Start CIDX service in source repository
            var startResult = await ExecuteCidxCommandAsync("start", repository.Path);
            if (startResult.ExitCode != 0)
            {
                return (false, "failed", $"CIDX start failed: {startResult.Output}");
            }

            // Step 2: Run index reconcile in source repository
            var indexResult = await ExecuteCidxCommandAsync("index --reconcile", repository.Path);
            if (indexResult.ExitCode != 0)
            {
                // Try to stop CIDX if indexing failed
                await ExecuteCidxCommandAsync("stop", repository.Path);
                return (false, "failed", $"CIDX indexing failed: {indexResult.Output}");
            }

            // Step 3: Stop CIDX service in source repository
            var stopResult = await ExecuteCidxCommandAsync("stop", repository.Path);
            if (stopResult.ExitCode != 0)
            {
                _logger.LogWarning("Failed to stop CIDX after indexing in source repository {Repository}: {Error}", 
                    repositoryName, stopResult.Output);
                // Don't fail the job if stop fails, indexing was successful
            }

            _logger.LogInformation("Successfully completed CIDX operations on source repository {Repository}", repositoryName);
            return (true, "indexed", string.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during CIDX operations on source repository {Repository}", repositoryName);
            return (false, "failed", $"CIDX operations error: {ex.Message}");
        }
    }

    /// <summary>
    /// Execute a CIDX command in the specified working directory
    /// </summary>
    private async Task<(int ExitCode, string Output)> ExecuteCidxCommandAsync(string cidxArgs, string workingDirectory)
    {
        try
        {
            var processInfo = new System.Diagnostics.ProcessStartInfo
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

            using var process = new System.Diagnostics.Process { StartInfo = processInfo };
            var outputBuilder = new System.Text.StringBuilder();
            var errorBuilder = new System.Text.StringBuilder();

            process.OutputDataReceived += (sender, e) => { if (e.Data != null) outputBuilder.AppendLine(e.Data); };
            process.ErrorDataReceived += (sender, e) => { if (e.Data != null) errorBuilder.AppendLine(e.Data); };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // Add timeout for CIDX operations (10 minutes)
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
            await process.WaitForExitAsync(timeoutCts.Token);

            var output = outputBuilder.ToString();
            var error = errorBuilder.ToString();
            var combinedOutput = string.IsNullOrEmpty(error) ? output : $"{output}\n\nErrors:\n{error}";

            return (process.ExitCode, combinedOutput);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute CIDX command '{Command}' in {WorkingDirectory}", cidxArgs, workingDirectory);
            return (-1, $"Command execution failed: {ex.Message}");
        }
    }

    private async Task CleanupCidxAsync(Job job)
    {
        try
        {
            _logger.LogInformation("Cleaning up cidx container for job {JobId}", job.Id);
            
            var processInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "cidx",
                Arguments = "stop",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = job.CowPath
            };

            using var process = new System.Diagnostics.Process { StartInfo = processInfo };
            process.Start();
            await process.WaitForExitAsync();

            if (process.ExitCode == 0)
            {
                job.CidxStatus = "stopped";
                _logger.LogInformation("Successfully stopped cidx container for job {JobId}", job.Id);
            }
            else
            {
                _logger.LogWarning("Failed to stop cidx container for job {JobId} with exit code {ExitCode}", 
                    job.Id, process.ExitCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cleaning up cidx container for job {JobId}", job.Id);
        }
    }

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
            
            await _repositoryService.RemoveCowCloneAsync(job.CowPath);
            _jobs.TryRemove(job.Id, out _);
        }
    }

    private void UpdateQueuePositions()
    {
        var queuedJobs = _jobs.Values
            .Where(j => j.Status == JobStatus.Queued)
            .OrderBy(j => j.CreatedAt)
            .ToList();

        for (int i = 0; i < queuedJobs.Count; i++)
        {
            queuedJobs[i].QueuePosition = i + 1;
        }
    }

    private int GetQueuePosition()
    {
        return _jobs.Values.Count(j => j.Status == JobStatus.Queued) + 1;
    }

    /// <summary>
    /// Check running jobs to see if any have completed naturally via PID monitoring
    /// </summary>
    private async Task CheckRunningJobsAsync()
    {
        try
        {
            var runningJobs = _jobs.Values.Where(j => j.Status == JobStatus.Running).ToList();
            
            if (!runningJobs.Any())
                return;

            foreach (var job in runningJobs)
            {
                try
                {
                    var (isComplete, output) = await _claudeExecutor.CheckJobCompletion(job);
                    
                    if (isComplete)
                    {
                        // Parse exit code from output
                        var exitCodeMatch = System.Text.RegularExpressions.Regex.Match(output, @"Exit code: (\d+)");
                        var exitCode = exitCodeMatch.Success ? int.Parse(exitCodeMatch.Groups[1].Value) : 0;

                        // Update job status based on exit code
                        job.Status = exitCode == 0 ? JobStatus.Completed : JobStatus.Failed;
                        job.Output = output.Replace($"Exit code: {exitCode}", "").Trim();
                        job.ExitCode = exitCode;
                        job.CompletedAt = DateTime.UtcNow;
                        job.ClaudeProcessId = null;

                        // Persist the completion
                        await _jobPersistenceService.SaveJobAsync(job);

                        _logger.LogInformation("Job {JobId} completed naturally with exit code {ExitCode}", job.Id, exitCode);

                        // Cleanup cidx if it was enabled for this job
                        if (job.Options.CidxAware)
                        {
                            await CleanupCidxAsync(job);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error checking completion status for running job {JobId}", job.Id);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in CheckRunningJobsAsync");
        }
    }
}