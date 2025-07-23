using ClaudeBatchServer.Core.DTOs;
using ClaudeServerCLI.Models;

namespace ClaudeServerCLI.Services;

public interface IApiClient
{
    // Authentication
    Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);
    Task<LogoutResponse> LogoutAsync(CancellationToken cancellationToken = default);
    
    // Repository Management  
    Task<RegisterRepositoryResponse> CreateRepositoryAsync(RegisterRepositoryRequest request, CancellationToken cancellationToken = default);
    Task<IEnumerable<RepositoryInfo>> GetRepositoriesAsync(CancellationToken cancellationToken = default);
    Task<RepositoryInfo> GetRepositoryAsync(string name, CancellationToken cancellationToken = default);
    Task<UnregisterRepositoryResponse> DeleteRepositoryAsync(string name, CancellationToken cancellationToken = default);
    Task<IEnumerable<FileInfoResponse>> GetRepositoryFilesAsync(string repoName, string? path = null, CancellationToken cancellationToken = default);
    Task<FileContentResponse> GetRepositoryFileContentAsync(string repoName, string filePath, CancellationToken cancellationToken = default);
    
    // Job Management
    Task<CreateJobResponse> CreateJobAsync(CreateJobRequest request, CancellationToken cancellationToken = default);
    Task<JobStatusResponse> GetJobAsync(string jobId, CancellationToken cancellationToken = default);
    Task<IEnumerable<JobInfo>> GetJobsAsync(CliJobFilter? filter = null, CancellationToken cancellationToken = default);
    Task<StartJobResponse> StartJobAsync(string jobId, CancellationToken cancellationToken = default);
    Task<CancelJobResponse> CancelJobAsync(string jobId, CancellationToken cancellationToken = default);
    Task<DeleteJobResponse> DeleteJobAsync(string jobId, CancellationToken cancellationToken = default);
    
    // File Upload
    Task<IEnumerable<FileUploadResponse>> UploadFilesAsync(string jobId, IEnumerable<FileUpload> files, bool overwrite = false, CancellationToken cancellationToken = default);
    Task<IEnumerable<JobFile>> GetJobFilesAsync(string jobId, CancellationToken cancellationToken = default);
    Task<Stream> DownloadJobFileAsync(string jobId, string fileName, CancellationToken cancellationToken = default);

    // Image Upload
    Task<ImageUploadResponse> UploadImageAsync(string jobId, byte[] imageData, string fileName, CancellationToken cancellationToken = default);

    // Configuration
    void SetBaseUrl(string baseUrl);
    void SetTimeout(TimeSpan timeout);
    void SetAuthToken(string token);
    void ClearAuthToken();
    
    // Health Check
    Task<bool> IsServerHealthyAsync(CancellationToken cancellationToken = default);
}