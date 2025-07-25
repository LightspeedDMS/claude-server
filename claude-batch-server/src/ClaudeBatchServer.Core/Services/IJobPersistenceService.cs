using ClaudeBatchServer.Core.Models;

namespace ClaudeBatchServer.Core.Services;

/// <summary>
/// Service for persisting job data to disk for crash resilience and job history
/// </summary>
public interface IJobPersistenceService
{
    /// <summary>
    /// Save a job to persistent storage
    /// </summary>
    Task SaveJobAsync(Job job);
    
    /// <summary>
    /// Load a specific job from persistent storage
    /// </summary>
    Task<Job?> LoadJobAsync(Guid jobId);
    
    /// <summary>
    /// Load all jobs from persistent storage
    /// </summary>
    Task<List<Job>> LoadAllJobsAsync();
    
    /// <summary>
    /// Load jobs for a specific user from persistent storage
    /// </summary>
    Task<List<Job>> LoadJobsForUserAsync(string username);
    
    /// <summary>
    /// Delete a job from persistent storage
    /// </summary>
    Task DeleteJobAsync(Guid jobId);
    
    /// <summary>
    /// Clean up old completed jobs based on retention policy
    /// </summary>
    Task CleanupOldJobsAsync();
    
    /// <summary>
    /// Get the staging directory path for a job where files are uploaded before CoW clone completion
    /// </summary>
    string GetJobStagingPath(Guid jobId);
    
    /// <summary>
    /// Get all staged files for a job
    /// </summary>
    List<string> GetStagedFiles(Guid jobId);
    
    /// <summary>
    /// Copy staged files to CoW workspace after clone completion
    /// </summary>
    Task<int> CopyStagedFilesToCowWorkspaceAsync(Guid jobId, string cowPath);
    
    /// <summary>
    /// Clean up staging directory after successful copy
    /// </summary>
    void CleanupStagingDirectory(Guid jobId);
}