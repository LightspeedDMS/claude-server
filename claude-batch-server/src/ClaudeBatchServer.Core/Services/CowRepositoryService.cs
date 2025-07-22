using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ClaudeBatchServer.Core.Models;

namespace ClaudeBatchServer.Core.Services;

public class CowRepositoryService : IRepositoryService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<CowRepositoryService> _logger;
    private readonly string _repositoriesPath;
    private readonly string _workspacePath;
    private CowMethod _cowMethod = CowMethod.Unsupported;

    public CowRepositoryService(IConfiguration configuration, ILogger<CowRepositoryService> logger)
    {
        _configuration = configuration;
        _logger = logger;
        _repositoriesPath = _configuration["Workspace:RepositoriesPath"] ?? "/workspace/repos";
        _workspacePath = _configuration["Workspace:JobsPath"] ?? "/workspace/jobs";
        
        Task.Run(async () => await DetectCowMethodAsync());
    }

    public async Task<bool> ValidateCowSupportAsync()
    {
        await DetectCowMethodAsync();
        return _cowMethod != CowMethod.Unsupported;
    }

    public async Task<List<Repository>> GetRepositoriesAsync()
    {
        var repositories = new List<Repository>();
        
        if (!Directory.Exists(_repositoriesPath))
        {
            Directory.CreateDirectory(_repositoriesPath);
            return repositories;
        }

        var directories = Directory.GetDirectories(_repositoriesPath);
        
        foreach (var dir in directories)
        {
            var name = Path.GetFileName(dir);
            var settingsPath = Path.Combine(dir, ".claude-batch-settings.json");
            
            var repository = new Repository
            {
                Name = name,
                Path = dir,
                Description = $"Repository: {name}",
                LastUpdated = Directory.GetLastWriteTime(dir),
                IsActive = true
            };

            if (File.Exists(settingsPath))
            {
                try
                {
                    var settingsJson = await File.ReadAllTextAsync(settingsPath);
                    var settings = JsonSerializer.Deserialize<RepositorySettings>(settingsJson);
                    if (settings != null) repository.Settings = settings;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Failed to read repository settings for {Repository}: {Error}", name, ex.Message);
                }
            }

            repositories.Add(repository);
        }

        return repositories;
    }

    public async Task<Repository?> GetRepositoryAsync(string name)
    {
        var repositories = await GetRepositoriesAsync();
        return repositories.FirstOrDefault(r => r.Name == name);
    }

    public async Task<Repository> RegisterRepositoryAsync(string name, string gitUrl, string description = "")
    {
        _logger.LogInformation("Registering repository {Name} from {GitUrl}", name, gitUrl);

        // Validate input
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Repository name cannot be empty", nameof(name));
        if (string.IsNullOrWhiteSpace(gitUrl))
            throw new ArgumentException("Git URL cannot be empty", nameof(gitUrl));

        // Check if repository already exists
        var existing = await GetRepositoryAsync(name);
        if (existing != null)
            throw new InvalidOperationException($"Repository '{name}' already exists");

        var repositoryPath = Path.Combine(_repositoriesPath, name);
        
        // Ensure repositories directory exists
        Directory.CreateDirectory(_repositoriesPath);

        var repository = new Repository
        {
            Name = name,
            Path = repositoryPath,
            Description = description,
            GitUrl = gitUrl,
            RegisteredAt = DateTime.UtcNow,
            CloneStatus = "cloning"
        };

        try
        {
            // Clone the repository
            var processInfo = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = $"clone \"{gitUrl}\" \"{repositoryPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = processInfo };
            process.Start();
            await process.WaitForExitAsync();

            if (process.ExitCode == 0)
            {
                repository.CloneStatus = "completed";
                _logger.LogInformation("Successfully cloned repository {Name} to {Path}", name, repositoryPath);

                // Create .claude-batch-settings.json file
                var settingsPath = Path.Combine(repositoryPath, ".claude-batch-settings.json");
                var settings = new
                {
                    Name = name,
                    Description = description,
                    GitUrl = gitUrl,
                    RegisteredAt = repository.RegisteredAt,
                    CloneStatus = repository.CloneStatus
                };
                
                await File.WriteAllTextAsync(settingsPath, JsonSerializer.Serialize(settings, new JsonSerializerOptions 
                { 
                    WriteIndented = true 
                }));
            }
            else
            {
                repository.CloneStatus = "failed";
                var error = await process.StandardError.ReadToEndAsync();
                _logger.LogError("Failed to clone repository {Name}: {Error}", name, error);
                
                // Clean up failed clone directory if it exists
                if (Directory.Exists(repositoryPath))
                {
                    Directory.Delete(repositoryPath, true);
                }
                
                throw new InvalidOperationException($"Failed to clone repository: {error}");
            }
        }
        catch (Exception ex)
        {
            repository.CloneStatus = "failed";
            _logger.LogError(ex, "Error cloning repository {Name}", name);
            
            // Clean up on failure
            if (Directory.Exists(repositoryPath))
            {
                try
                {
                    Directory.Delete(repositoryPath, true);
                }
                catch (Exception cleanupEx)
                {
                    _logger.LogWarning(cleanupEx, "Failed to cleanup failed repository clone at {Path}", repositoryPath);
                }
            }
            
            throw;
        }

        return repository;
    }

    public async Task<bool> UnregisterRepositoryAsync(string name)
    {
        _logger.LogInformation("Unregistering repository {Name}", name);

        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Repository name cannot be empty", nameof(name));

        var repository = await GetRepositoryAsync(name);
        if (repository == null)
        {
            _logger.LogWarning("Repository {Name} not found for unregistration", name);
            return false;
        }

        try
        {
            // Remove the repository directory and all its contents
            if (Directory.Exists(repository.Path))
            {
                Directory.Delete(repository.Path, true);
                _logger.LogInformation("Successfully removed repository {Name} from disk at {Path}", name, repository.Path);
            }
            else
            {
                _logger.LogWarning("Repository {Name} directory not found at {Path}, but considering unregistration successful", name, repository.Path);
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to unregister repository {Name} at {Path}", name, repository.Path);
            throw new InvalidOperationException($"Failed to remove repository '{name}': {ex.Message}", ex);
        }
    }

    public async Task<string> CreateCowCloneAsync(string repositoryName, Guid jobId)
    {
        var repository = await GetRepositoryAsync(repositoryName);
        if (repository == null)
            throw new ArgumentException($"Repository '{repositoryName}' not found");

        var jobPath = Path.Combine(_workspacePath, jobId.ToString());
        Directory.CreateDirectory(Path.GetDirectoryName(jobPath)!);

        await DetectCowMethodAsync();

        switch (_cowMethod)
        {
            case CowMethod.XfsReflink:
            case CowMethod.Ext4Reflink:
                await CreateReflinkCloneAsync(repository.Path, jobPath);
                break;
            case CowMethod.BtrfsSnapshot:
                await CreateBtrfsSnapshotAsync(repository.Path, jobPath);
                break;
            case CowMethod.HardlinkFallback:
                await CreateHardlinkCloneAsync(repository.Path, jobPath);
                break;
            default:
                throw new NotSupportedException("No Copy-on-Write method available");
        }

        var imagesPath = Path.Combine(jobPath, "images");
        Directory.CreateDirectory(imagesPath);

        _logger.LogInformation("Created CoW clone of {Repository} at {JobPath} using {Method}", 
            repositoryName, jobPath, _cowMethod);

        return jobPath;
    }

    public async Task<bool> RemoveCowCloneAsync(string cowPath)
    {
        try
        {
            if (!Directory.Exists(cowPath)) return true;

            if (_cowMethod == CowMethod.BtrfsSnapshot)
            {
                var result = await ExecuteCommandAsync("btrfs", $"subvolume delete \"{cowPath}\"");
                return result.ExitCode == 0;
            }
            else
            {
                Directory.Delete(cowPath, true);
                return true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove CoW clone at {CowPath}", cowPath);
            return false;
        }
    }

    public async Task<List<Models.FileInfo>> GetFilesAsync(string cowPath, string? subPath = null)
    {
        var files = new List<Models.FileInfo>();
        var targetPath = string.IsNullOrEmpty(subPath) ? cowPath : Path.Combine(cowPath, subPath);

        if (!Directory.Exists(targetPath)) return files;

        var directories = Directory.GetDirectories(targetPath);
        var fileEntries = Directory.GetFiles(targetPath);

        foreach (var dir in directories)
        {
            var dirInfo = new DirectoryInfo(dir);
            files.Add(new Models.FileInfo
            {
                Name = dirInfo.Name,
                Type = "directory",
                Path = Path.GetRelativePath(cowPath, dir),
                Size = 0,
                Modified = dirInfo.LastWriteTime
            });
        }

        foreach (var file in fileEntries)
        {
            var fileInfo = new System.IO.FileInfo(file);
            files.Add(new Models.FileInfo
            {
                Name = fileInfo.Name,
                Type = "file",
                Path = Path.GetRelativePath(cowPath, file),
                Size = fileInfo.Length,
                Modified = fileInfo.LastWriteTime
            });
        }

        return files.OrderBy(f => f.Type).ThenBy(f => f.Name).ToList();
    }

    public async Task<byte[]?> DownloadFileAsync(string cowPath, string filePath)
    {
        try
        {
            var fullPath = Path.Combine(cowPath, filePath);
            if (!File.Exists(fullPath)) return null;

            return await File.ReadAllBytesAsync(fullPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download file {FilePath} from {CowPath}", filePath, cowPath);
            return null;
        }
    }

    public async Task<string?> GetFileContentAsync(string cowPath, string filePath)
    {
        try
        {
            var fullPath = Path.Combine(cowPath, filePath);
            if (!File.Exists(fullPath)) return null;

            return await File.ReadAllTextAsync(fullPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read file content {FilePath} from {CowPath}", filePath, cowPath);
            return null;
        }
    }

    private async Task DetectCowMethodAsync()
    {
        if (_cowMethod != CowMethod.Unsupported) return;

        try
        {
            var fsType = await GetFilesystemTypeAsync(_repositoriesPath);
            _logger.LogInformation("Detected filesystem type: {FsType} for {Path}", fsType, _repositoriesPath);

            switch (fsType?.ToLower())
            {
                case "xfs":
                    if (await TestReflinkSupportAsync())
                    {
                        _cowMethod = CowMethod.XfsReflink;
                        _logger.LogInformation("Using XFS reflink for CoW operations");
                    }
                    break;
                case "ext4":
                    if (await TestReflinkSupportAsync())
                    {
                        _cowMethod = CowMethod.Ext4Reflink;
                        _logger.LogInformation("Using ext4 reflink for CoW operations");
                    }
                    break;
                case "btrfs":
                    if (await TestBtrfsSupportAsync())
                    {
                        _cowMethod = CowMethod.BtrfsSnapshot;
                        _logger.LogInformation("Using Btrfs snapshots for CoW operations");
                    }
                    break;
            }

            if (_cowMethod == CowMethod.Unsupported)
            {
                _cowMethod = CowMethod.HardlinkFallback;
                _logger.LogWarning("No CoW support detected, falling back to hardlink copying");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to detect CoW method, using hardlink fallback");
            _cowMethod = CowMethod.HardlinkFallback;
        }
    }

    private async Task<string?> GetFilesystemTypeAsync(string path)
    {
        try
        {
            var result = await ExecuteCommandAsync("df", $"-T \"{path}\"");
            if (result.ExitCode == 0)
            {
                var lines = result.Output.Split('\n');
                if (lines.Length > 1)
                {
                    var parts = lines[1].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length > 1) return parts[1];
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to detect filesystem type for {Path}", path);
        }
        return null;
    }

    private async Task<bool> TestReflinkSupportAsync()
    {
        try
        {
            var testDir = Path.Combine(Path.GetTempPath(), $"cow-test-{Guid.NewGuid()}");
            Directory.CreateDirectory(testDir);
            
            var sourceFile = Path.Combine(testDir, "source.txt");
            var targetFile = Path.Combine(testDir, "target.txt");
            
            await File.WriteAllTextAsync(sourceFile, "test content");
            
            var result = await ExecuteCommandAsync("cp", $"--reflink=always \"{sourceFile}\" \"{targetFile}\"");
            
            Directory.Delete(testDir, true);
            return result.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> TestBtrfsSupportAsync()
    {
        try
        {
            var result = await ExecuteCommandAsync("btrfs", "filesystem show");
            return result.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private async Task CreateReflinkCloneAsync(string sourcePath, string targetPath)
    {
        var result = await ExecuteCommandAsync("cp", $"-r --reflink=always \"{sourcePath}\" \"{targetPath}\"");
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"Failed to create reflink clone: {result.Error}");
        }
    }

    private async Task CreateBtrfsSnapshotAsync(string sourcePath, string targetPath)
    {
        var result = await ExecuteCommandAsync("btrfs", $"subvolume snapshot \"{sourcePath}\" \"{targetPath}\"");
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"Failed to create Btrfs snapshot: {result.Error}");
        }
    }

    private async Task CreateHardlinkCloneAsync(string sourcePath, string targetPath)
    {
        var result = await ExecuteCommandAsync("rsync", $"-a --link-dest=\"{sourcePath}\" \"{sourcePath}/\" \"{targetPath}/\"");
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"Failed to create hardlink clone: {result.Error}");
        }
    }

    private async Task<(int ExitCode, string Output, string Error)> ExecuteCommandAsync(string command, string arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = command,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        
        await process.WaitForExitAsync();
        
        var output = await outputTask;
        var error = await errorTask;

        return (process.ExitCode, output, error);
    }
}

public enum CowMethod
{
    Unsupported,
    XfsReflink,
    Ext4Reflink,
    BtrfsSnapshot,
    HardlinkFallback
}