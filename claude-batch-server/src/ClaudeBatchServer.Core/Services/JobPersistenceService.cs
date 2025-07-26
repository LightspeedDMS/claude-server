using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ClaudeBatchServer.Core.Models;
using ClaudeBatchServer.Core.Serialization;

namespace ClaudeBatchServer.Core.Services;

/// <summary>
/// File-based job persistence service for crash resilience and job history
/// Stores jobs as individual JSON files with configurable retention policy
/// </summary>
public class JobPersistenceService : IJobPersistenceService
{
    private readonly string _jobsPath;
    private readonly IConfiguration _configuration;
    private readonly ILogger<JobPersistenceService> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public JobPersistenceService(
        string workspacePath,
        IConfiguration configuration,
        ILogger<JobPersistenceService> logger)
    {
        _jobsPath = Path.Combine(workspacePath, "jobs");
        _configuration = configuration;
        _logger = logger;
        
        // Ensure jobs directory exists
        Directory.CreateDirectory(_jobsPath);
        
        // Configure JSON serialization for readable files
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true
            // Don't use camelCase to avoid issues with property mapping
        };
    }

    /// <summary>
    /// Constructor for production use with default workspace path
    /// DEPRECATED: This constructor should not be used directly. Use DI container registration instead.
    /// </summary>
    [Obsolete("This constructor uses a hardcoded workspace path and should not be used. Use DI container registration with proper configuration instead.")]
    public JobPersistenceService(IConfiguration configuration, ILogger<JobPersistenceService> logger)
        : this(Path.Combine(Directory.GetCurrentDirectory(), "workspace"), configuration, logger)
    {
        logger.LogWarning("JobPersistenceService instantiated with deprecated constructor. Using fallback workspace path: {WorkspacePath}. This should be configured properly via DI.", Path.Combine(Directory.GetCurrentDirectory(), "workspace"));
    }

    public async Task SaveJobAsync(Job job)
    {
        if (job == null)
            throw new ArgumentNullException(nameof(job));

        try
        {
            var filePath = GetJobFilePath(job.Id);
            var jsonContent = JsonSerializer.Serialize(job, AppJsonSerializerContext.Default.Job);
            
            await File.WriteAllTextAsync(filePath, jsonContent);
            
            _logger.LogDebug("Job {JobId} saved to {FilePath}", job.Id, filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save job {JobId}", job.Id);
            throw;
        }
    }

    public async Task<Job?> LoadJobAsync(Guid jobId)
    {
        try
        {
            var filePath = GetJobFilePath(jobId);
            
            if (!File.Exists(filePath))
            {
                return null;
            }

            var jsonContent = await File.ReadAllTextAsync(filePath);
            var job = JsonSerializer.Deserialize(jsonContent, AppJsonSerializerContext.Default.Job);
            
            _logger.LogDebug("Job {JobId} loaded from {FilePath}", jobId, filePath);
            return job;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load job {JobId}", jobId);
            return null;
        }
    }

    public async Task<List<Job>> LoadAllJobsAsync()
    {
        var jobs = new List<Job>();
        
        try
        {
            if (!Directory.Exists(_jobsPath))
            {
                return jobs;
            }

            var jobFiles = Directory.GetFiles(_jobsPath, "*.job.json");
            
            foreach (var filePath in jobFiles)
            {
                try
                {
                    var jsonContent = await File.ReadAllTextAsync(filePath);
                    var job = JsonSerializer.Deserialize(jsonContent, AppJsonSerializerContext.Default.Job);
                    
                    if (job != null)
                    {
                        jobs.Add(job);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Skipping corrupted job file: {FilePath}", filePath);
                }
            }
            
            _logger.LogDebug("Loaded {JobCount} jobs from persistence", jobs.Count);
            return jobs.OrderByDescending(j => j.CreatedAt).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load jobs from persistence");
            return jobs;
        }
    }

    public async Task<List<Job>> LoadJobsForUserAsync(string username)
    {
        if (string.IsNullOrWhiteSpace(username))
            throw new ArgumentException("Username cannot be null or empty", nameof(username));

        var allJobs = await LoadAllJobsAsync();
        return allJobs
            .Where(job => job.Username.Equals(username, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public Task DeleteJobAsync(Guid jobId)
    {
        try
        {
            var filePath = GetJobFilePath(jobId);
            
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                _logger.LogDebug("Job {JobId} deleted from persistence", jobId);
            }
            
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete job {JobId}", jobId);
            throw;
        }
    }

    /// <summary>
    /// Get the staging directory path for a job where files are uploaded before CoW clone completion
    /// </summary>
    /// <param name="jobId">Job ID</param>
    /// <returns>Staging directory path</returns>
    public string GetJobStagingPath(Guid jobId)
    {
        // CRITICAL FIX: Staging area must be OUTSIDE the job path because the job path becomes the CoW clone
        // Put staging in a separate staging directory to avoid conflicts with CoW cloning
        var stagingRootPath = Path.Combine(Path.GetDirectoryName(_jobsPath) ?? "/workspace", "staging");
        Directory.CreateDirectory(stagingRootPath);
        return Path.Combine(stagingRootPath, jobId.ToString());
    }

    /// <summary>
    /// Get all staged files for a job
    /// </summary>
    /// <param name="jobId">Job ID</param>
    /// <returns>List of staged file paths</returns>
    public List<string> GetStagedFiles(Guid jobId)
    {
        var stagingPath = GetJobStagingPath(jobId);
        
        if (!Directory.Exists(stagingPath))
            return new List<string>();

        try
        {
            return Directory.GetFiles(stagingPath, "*", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(stagingPath, f))
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get staged files for job {JobId}", jobId);
            return new List<string>();
        }
    }

    /// <summary>
    /// Copy staged files to CoW workspace after clone completion
    /// </summary>
    /// <param name="jobId">Job ID</param>
    /// <param name="cowPath">CoW workspace path</param>
    /// <returns>Number of files copied</returns>
    public async Task<int> CopyStagedFilesToCowWorkspaceAsync(Guid jobId, string cowPath)
    {
        var stagingPath = GetJobStagingPath(jobId);
        
        _logger.LogInformation("STAGING DEBUG: Starting file copy for job {JobId}", jobId);
        _logger.LogInformation("STAGING DEBUG: Staging path: {StagingPath}", stagingPath);
        _logger.LogInformation("STAGING DEBUG: CoW path: {CowPath}", cowPath);
        
        if (!Directory.Exists(stagingPath))
        {
            _logger.LogWarning("STAGING DEBUG: No staging directory found for job {JobId} at {StagingPath}", jobId, stagingPath);
            return 0;
        }

        var targetFilesPath = Path.Combine(cowPath, "files");
        _logger.LogInformation("STAGING DEBUG: Target files path: {TargetFilesPath}", targetFilesPath);
        Directory.CreateDirectory(targetFilesPath);

        var stagedFiles = Directory.GetFiles(stagingPath, "*", SearchOption.AllDirectories);
        _logger.LogInformation("STAGING DEBUG: Found {FileCount} staged files: {Files}", stagedFiles.Length, string.Join(", ", stagedFiles));
        
        var copiedCount = 0;

        foreach (var stagedFile in stagedFiles)
        {
            try
            {
                var stagedFileName = Path.GetFileName(stagedFile);
                
                // CRITICAL FIX: Remove hash from filename to restore original name
                // Staged files have format: "filename_12345678.ext", we need to remove "_12345678" part
                var originalFileName = RestoreOriginalFilename(stagedFileName);
                var targetPath = Path.Combine(targetFilesPath, originalFileName);
                
                _logger.LogInformation("STAGING DEBUG: Copying {StagedFile} to {TargetPath} (restoring original name: {OriginalName})", stagedFile, targetPath, originalFileName);
                
                // Ensure target directory exists
                var targetDir = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrEmpty(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                }

                await Task.Run(() => File.Copy(stagedFile, targetPath, overwrite: true));
                
                // Verify file was actually copied
                if (File.Exists(targetPath))
                {
                    var sourceSize = new System.IO.FileInfo(stagedFile).Length;
                    var targetSize = new System.IO.FileInfo(targetPath).Length;
                    _logger.LogInformation("STAGING DEBUG: Successfully copied {StagedFile} to {TargetPath} (size: {Size} bytes)", stagedFile, targetPath, targetSize);
                    
                    if (sourceSize != targetSize)
                    {
                        _logger.LogError("STAGING DEBUG: File size mismatch! Source: {SourceSize}, Target: {TargetSize}", sourceSize, targetSize);
                    }
                    else
                    {
                        copiedCount++;
                    }
                }
                else
                {
                    _logger.LogError("STAGING DEBUG: File copy failed - target file does not exist: {TargetPath}", targetPath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "STAGING DEBUG: Failed to copy staged file {StagedFile} for job {JobId}", stagedFile, jobId);
            }
        }

        _logger.LogInformation("STAGING DEBUG: Copy operation completed. {CopiedCount}/{TotalCount} files copied successfully for job {JobId}", copiedCount, stagedFiles.Length, jobId);
        
        // Verify target directory has files
        if (Directory.Exists(targetFilesPath))
        {
            var targetFiles = Directory.GetFiles(targetFilesPath, "*", SearchOption.AllDirectories);
            _logger.LogInformation("STAGING DEBUG: Target directory now contains {TargetFileCount} files: {TargetFiles}", targetFiles.Length, string.Join(", ", targetFiles));
        }
        
        return copiedCount;
    }

    /// <summary>
    /// Restore original filename by removing the hash suffix added during staging
    /// Format: "filename_12345678.ext" -> "filename.ext"
    /// </summary>
    private string RestoreOriginalFilename(string stagedFileName)
    {
        var extension = Path.GetExtension(stagedFileName);
        var nameWithoutExt = Path.GetFileNameWithoutExtension(stagedFileName);
        
        // Check if filename has the hash pattern: ends with underscore + 8 characters
        if (nameWithoutExt.Length > 9 && nameWithoutExt[^9] == '_')
        {
            // Remove the "_12345678" suffix to get original name
            var originalName = nameWithoutExt[..^9];
            return originalName + extension;
        }
        
        // If no hash pattern found, return as-is (probably original filename without hash)
        return stagedFileName;
    }

    /// <summary>
    /// Clean up staging directory after successful copy
    /// </summary>
    /// <param name="jobId">Job ID</param>
    public void CleanupStagingDirectory(Guid jobId)
    {
        var stagingPath = GetJobStagingPath(jobId);
        
        if (Directory.Exists(stagingPath))
        {
            try
            {
                Directory.Delete(stagingPath, recursive: true);
                _logger.LogDebug("Cleaned up staging directory for job {JobId}", jobId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to cleanup staging directory for job {JobId}", jobId);
            }
        }
    }

    public async Task CleanupOldJobsAsync()
    {
        try
        {
            var retentionDays = int.Parse(_configuration["Jobs:RetentionDays"] ?? "30");
            var cutoffDate = DateTime.UtcNow.AddDays(-retentionDays);
            
            _logger.LogInformation("Starting job cleanup with {RetentionDays} days retention (cutoff: {CutoffDate})", 
                retentionDays, cutoffDate);

            var allJobs = await LoadAllJobsAsync();
            var jobsToDelete = allJobs
                .Where(job => IsJobFinished(job.Status) && 
                             (job.CompletedAt ?? job.CreatedAt) < cutoffDate)
                .ToList();

            var deletedCount = 0;
            foreach (var job in jobsToDelete)
            {
                await DeleteJobAsync(job.Id);
                deletedCount++;
            }
            
            _logger.LogInformation("Cleaned up {DeletedCount} old jobs (out of {TotalJobs} total jobs)", 
                deletedCount, allJobs.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cleanup old jobs");
            throw;
        }
    }

    private string GetJobFilePath(Guid jobId)
    {
        return Path.Combine(_jobsPath, $"{jobId}.job.json");
    }

    private static bool IsJobFinished(JobStatus status)
    {
        return status is JobStatus.Completed or 
                       JobStatus.Failed or 
                       JobStatus.Timeout or 
                       JobStatus.Terminated or 
                       JobStatus.Cancelled;
    }
}