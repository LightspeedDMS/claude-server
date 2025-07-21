using ClaudeBatchServer.Core.DTOs;
using ClaudeBatchServer.Core.Models;

namespace ClaudeBatchServer.Core.Services;

public interface IJobService
{
    Task<CreateJobResponse> CreateJobAsync(CreateJobRequest request, string username);
    Task<StartJobResponse> StartJobAsync(Guid jobId, string username);
    Task<JobStatusResponse?> GetJobStatusAsync(Guid jobId, string username);
    Task<DeleteJobResponse> DeleteJobAsync(Guid jobId, string username);
    Task<List<JobListResponse>> GetUserJobsAsync(string username);
    Task<ImageUploadResponse> UploadImageAsync(Guid jobId, string username, string filename, Stream imageStream);
    Task ProcessJobQueueAsync(CancellationToken cancellationToken);
}