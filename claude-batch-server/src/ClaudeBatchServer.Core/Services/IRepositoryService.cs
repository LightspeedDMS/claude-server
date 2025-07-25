using ClaudeBatchServer.Core.Models;
using ClaudeBatchServer.Core.DTOs;

namespace ClaudeBatchServer.Core.Services;

public interface IRepositoryService
{
    Task<List<Repository>> GetRepositoriesAsync();
    Task<List<RepositoryResponse>> GetRepositoriesWithMetadataAsync();
    Task<Repository?> GetRepositoryAsync(string name);
    Task<Repository> RegisterRepositoryAsync(string name, string gitUrl, string description = "", bool cidxAware = true);
    Task<bool> UnregisterRepositoryAsync(string name);
    Task<(bool Success, string Status, string ErrorMessage)> PullRepositoryUpdatesAsync(string repositoryName);
    Task<string> CreateCowCloneAsync(string repositoryName, Guid jobId);
    Task<bool> RemoveCowCloneAsync(string cowPath);
    Task<List<Models.DirectoryMetadata>?> GetDirectoriesAsync(string cowPath, string subPath);
    Task<List<Models.FileInfo>?> GetFilesInDirectoryAsync(string cowPath, string subPath, string? mask = null);
    Task<byte[]?> DownloadFileAsync(string cowPath, string filePath);
    Task<string?> GetFileContentAsync(string cowPath, string filePath);
    Task<bool> ValidateCowSupportAsync();
}