using ClaudeBatchServer.Core.DTOs;
using ClaudeBatchServer.Core.Models;

namespace ClaudeBatchServer.Core.Services;

public interface IJobService
{
    Task InitializeAsync();
    Task<CreateJobResponse> CreateJobAsync(CreateJobRequest request, string username);
    Task<StartJobResponse> StartJobAsync(Guid jobId, string username);
    Task<JobStatusResponse?> GetJobStatusAsync(Guid jobId, string username);
    Task<DeleteJobResponse> DeleteJobAsync(Guid jobId, string username);
    Task<CancelJobResponse> CancelJobAsync(Guid jobId, string username);
    Task<List<JobListResponse>> GetUserJobsAsync(string username);
    Task<ImageUploadResponse> UploadImageAsync(Guid jobId, string username, string filename, Stream imageStream);
    Task<FileUploadResponse> UploadFileAsync(Guid jobId, string username, string filename, Stream fileStream, bool overwrite = false);
    Task DeleteFileAsync(Guid jobId, string username, string filename);
    Task<List<Models.FileInfo>> GetJobFilesAsync(Guid jobId, string username);
    Task ProcessJobQueueAsync(CancellationToken cancellationToken);
}