using ClaudeBatchServer.Core.Models;
using ClaudeBatchServer.Core.DTOs;

namespace ClaudeBatchServer.Core.Services;

public interface IRepositoryService
{
    Task<List<Repository>> GetRepositoriesAsync();
    Task<List<RepositoryResponse>> GetRepositoriesWithMetadataAsync();
    Task<Repository?> GetRepositoryAsync(string name);
    Task<Repository> RegisterRepositoryAsync(string name, string gitUrl, string description = "");
    Task<bool> UnregisterRepositoryAsync(string name);
    Task<string> CreateCowCloneAsync(string repositoryName, Guid jobId);
    Task<bool> RemoveCowCloneAsync(string cowPath);
    Task<List<Models.FileInfo>> GetFilesAsync(string cowPath, string? subPath = null);
    Task<byte[]?> DownloadFileAsync(string cowPath, string filePath);
    Task<string?> GetFileContentAsync(string cowPath, string filePath);
    Task<bool> ValidateCowSupportAsync();
}