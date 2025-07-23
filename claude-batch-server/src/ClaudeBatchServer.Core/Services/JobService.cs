using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ClaudeBatchServer.Core.DTOs;
using ClaudeBatchServer.Core.Models;

namespace ClaudeBatchServer.Core.Services;

public class JobService : IJobService
{
    private readonly IRepositoryService _repositoryService;
    private readonly IClaudeCodeExecutor _claudeExecutor;
    private readonly IConfiguration _configuration;
    private readonly ILogger<JobService> _logger;
    
    private readonly ConcurrentDictionary<Guid, Job> _jobs = new();
    private readonly ConcurrentQueue<Guid> _jobQueue = new();
    private readonly SemaphoreSlim _concurrencyLimiter;
    private readonly int _maxConcurrentJobs;
    private readonly int _jobTimeoutHours;

    public JobService(
        IRepositoryService repositoryService,
        IClaudeCodeExecutor claudeExecutor,
        IConfiguration configuration,
        ILogger<JobService> logger)
    {
        _repositoryService = repositoryService;
        _claudeExecutor = claudeExecutor;
        _configuration = configuration;
        _logger = logger;
        
        _maxConcurrentJobs = int.Parse(_configuration["Jobs:MaxConcurrent"] ?? "5");
        _jobTimeoutHours = int.Parse(_configuration["Jobs:TimeoutHours"] ?? "24");
        _concurrencyLimiter = new SemaphoreSlim(_maxConcurrentJobs, _maxConcurrentJobs);
    }

    public async Task<CreateJobResponse> CreateJobAsync(CreateJobRequest request, string username)
    {
        var repository = await _repositoryService.GetRepositoryAsync(request.Repository);
        if (repository == null)
            throw new ArgumentException($"Repository '{request.Repository}' not found");

        // Generate job title using Claude Code
        var jobTitle = await _claudeExecutor.GenerateJobTitleAsync(request.Prompt);

        var job = new Job
        {
            Id = Guid.NewGuid(),
            Username = username,
            Prompt = request.Prompt,
            Title = jobTitle,
            Repository = request.Repository,
            Images = request.Images.ToList(),
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

        var cowPath = await _repositoryService.CreateCowCloneAsync(request.Repository, job.Id);
        job.CowPath = cowPath;

        _jobs[job.Id] = job;

        _logger.LogInformation("Created job {JobId} for user {Username} with repository {Repository}", 
            job.Id, username, request.Repository);

        return new CreateJobResponse
        {
            JobId = job.Id,
            Status = job.Status.ToString().ToLower(),
            User = username,
            CowPath = cowPath,
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

        // CRITICAL FIX: Stop cidx containers before removing CoW clone
        // This prevents Docker container leaks when jobs used cidx
        bool cidxStopped = false;
        if (job.Options.CidxAware && Directory.Exists(job.CowPath))
        {
            try
            {
                _logger.LogInformation("Stopping cidx containers for job {JobId} before cleanup", jobId);
                var stopResult = await _claudeExecutor.StopCidxAsync(job.CowPath, username, CancellationToken.None);
                cidxStopped = stopResult.ExitCode == 0;
                if (!cidxStopped)
                {
                    _logger.LogWarning("Failed to stop cidx containers for job {JobId}: {Output}", jobId, stopResult.Output);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping cidx containers for job {JobId}", jobId);
            }
        }

        var cowRemoved = await _repositoryService.RemoveCowCloneAsync(job.CowPath);
        _jobs.TryRemove(jobId, out _);

        _logger.LogInformation("Deleted job {JobId} for user {Username}, terminated: {Terminated}, cidx stopped: {CidxStopped}, CoW removed: {CowRemoved}", 
            jobId, username, terminated, cidxStopped, cowRemoved);

        return new DeleteJobResponse
        {
            Success = true,
            Terminated = terminated,
            CowRemoved = cowRemoved
        };
    }

    public Task<CancelJobResponse> CancelJobAsync(Guid jobId, string username)
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

        _logger.LogInformation("Job {JobId} marked for cancellation by user {Username}", jobId, username);

        return Task.FromResult(new CancelJobResponse
        {
            JobId = jobId,
            Status = job.Status.ToString().ToLower(),
            Success = true,
            Message = "Job has been marked for cancellation and will be terminated shortly",
            CancelledAt = job.CancelledAt
        });
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
        if (!_jobs.TryGetValue(jobId, out var job))
            throw new ArgumentException("Job not found");

        if (job.Username != username)
            throw new UnauthorizedAccessException("Access denied");

        var imagesPath = Path.Combine(job.CowPath, "images");
        Directory.CreateDirectory(imagesPath);

        var extension = Path.GetExtension(filename);
        var safeFilename = $"{Guid.NewGuid()}{extension}";
        var fullPath = Path.Combine(imagesPath, safeFilename);

        using var fileStream = new FileStream(fullPath, FileMode.Create);
        await imageStream.CopyToAsync(fileStream);

        job.Images.Add(safeFilename);

        _logger.LogInformation("Uploaded image {Filename} for job {JobId}", safeFilename, jobId);

        return new ImageUploadResponse
        {
            Filename = safeFilename,
            Path = $"/workspace/jobs/{jobId}/images/"
        };
    }

    public async Task<FileUploadResponse> UploadFileAsync(Guid jobId, string username, string filename, Stream fileStream, bool overwrite = false)
    {
        if (!_jobs.TryGetValue(jobId, out var job))
            throw new ArgumentException("Job not found");

        if (job.Username != username)
            throw new UnauthorizedAccessException("Access denied");

        // Create files directory instead of images
        var filesPath = Path.Combine(job.CowPath, "files");
        Directory.CreateDirectory(filesPath);

        var extension = Path.GetExtension(filename);
        var safeFilename = filename;

        // Preserve original filename but ensure safety
        if (!overwrite)
        {
            // If not overwriting, create unique filename
            var nameWithoutExtension = Path.GetFileNameWithoutExtension(filename);
            safeFilename = $"{nameWithoutExtension}_{Guid.NewGuid().ToString("N")[..8]}{extension}";
        }

        var fullPath = Path.Combine(filesPath, safeFilename);
        bool wasOverwritten = File.Exists(fullPath);

        using var fileWriteStream = new FileStream(fullPath, FileMode.Create);
        await fileStream.CopyToAsync(fileWriteStream);

        // Update job to track file instead of adding to Images list (keep backward compatibility)
        if (!job.Images.Contains(safeFilename))
        {
            job.Images.Add(safeFilename);
        }

        _logger.LogInformation("Uploaded file {Filename} for job {JobId} (overwritten: {Overwritten})", safeFilename, jobId, wasOverwritten);

        return new FileUploadResponse
        {
            Filename = safeFilename,
            Path = $"/workspace/jobs/{jobId}/files/",
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
                _logger.LogInformation("Job {JobId} was cancelled before execution", job.Id);
                return;
            }

            _logger.LogInformation("Starting job {JobId} for user {Username}", job.Id, job.Username);
            
            job.Status = JobStatus.Running;
            job.StartedAt = DateTime.UtcNow;
            job.QueuePosition = 0;

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(job.Options.TimeoutSeconds));
            
            var (exitCode, output) = await _claudeExecutor.ExecuteAsync(job, job.Username, timeoutCts.Token);
            
            job.ExitCode = exitCode;
            job.Output = output;
            job.Status = exitCode == 0 ? JobStatus.Completed : JobStatus.Failed;
            job.CompletedAt = DateTime.UtcNow;

            _logger.LogInformation("Completed job {JobId} with exit code {ExitCode}", job.Id, exitCode);

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
            _logger.LogError(ex, "Job {JobId} failed with exception", job.Id);

            // Cleanup cidx if it was enabled for this job
            if (job.Options.CidxAware)
            {
                await CleanupCidxAsync(job);
            }
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
}