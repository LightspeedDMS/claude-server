using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ClaudeBatchServer.Core.Models;
using ClaudeBatchServer.Core.DTOs;
using ClaudeBatchServer.Core.Serialization;

namespace ClaudeBatchServer.Core.Services;

public class CowRepositoryService : IRepositoryService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<CowRepositoryService> _logger;
    private readonly IGitMetadataService _gitMetadataService;
    private readonly string _repositoriesPath;
    private readonly string _workspacePath;
    private CowMethod _cowMethod = CowMethod.Unsupported;

    public CowRepositoryService(IConfiguration configuration, ILogger<CowRepositoryService> logger, IGitMetadataService gitMetadataService)
    {
        _configuration = configuration;
        _logger = logger;
        _gitMetadataService = gitMetadataService;
        _repositoriesPath = ExpandPath(_configuration["Workspace:RepositoriesPath"] ?? "/workspace/repos");
        _workspacePath = ExpandPath(_configuration["Workspace:JobsPath"] ?? "/workspace/jobs");
        
        Task.Run(async () => await DetectCowMethodAsync());
    }

    /// <summary>
    /// Expand ~ to the user's home directory if the path starts with ~/
    /// </summary>
    private static string ExpandPath(string path)
    {
        if (path.StartsWith("~/"))
        {
            var homeDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(homeDirectory, path[2..]);
        }
        return path;
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

            // Use only internal settings file - eliminates dual file synchronization issues
            string? settingsToUse = File.Exists(settingsPath) ? settingsPath : null;
                
            if (settingsToUse != null)
            {
                try
                {
                    var settingsJson = await File.ReadAllTextAsync(settingsToUse);
                    var settings = JsonSerializer.Deserialize(settingsJson, AppJsonSerializerContext.Default.DictionaryStringObject);
                    
                    if (settings != null)
                    {
                        // Read CloneStatus and CidxAware from settings file like GetRepositoriesWithMetadataAsync does
                        if (settings.TryGetValue("CloneStatus", out var cloneStatus) && cloneStatus != null)
                            repository.CloneStatus = cloneStatus.ToString() ?? string.Empty;
                        if (settings.TryGetValue("CidxAware", out var cidxAware) && cidxAware != null)
                        {
                            // Handle JsonElement boolean values from deserialized JSON
                            if (cidxAware is System.Text.Json.JsonElement jsonElement && jsonElement.ValueKind == System.Text.Json.JsonValueKind.True)
                                repository.CidxAware = true;
                            else if (cidxAware is System.Text.Json.JsonElement jsonElement2 && jsonElement2.ValueKind == System.Text.Json.JsonValueKind.False)
                                repository.CidxAware = false;
                            else if (bool.TryParse(cidxAware.ToString(), out var cidxAwareBool))
                                repository.CidxAware = cidxAwareBool;
                        }
                        if (settings.TryGetValue("GitUrl", out var gitUrl) && gitUrl != null)
                            repository.GitUrl = gitUrl.ToString() ?? string.Empty;
                        if (settings.TryGetValue("RegisteredAt", out var regAt) && regAt != null && DateTime.TryParse(regAt.ToString(), out var registeredAt))
                            repository.RegisteredAt = registeredAt;
                        if (settings.TryGetValue("Description", out var desc) && desc != null)
                            repository.Description = desc.ToString() ?? string.Empty;
                            
                        // Also try to deserialize the RepositorySettings object for other settings
                        try
                        {
                            var repoSettings = JsonSerializer.Deserialize(settingsJson, AppJsonSerializerContext.Default.RepositorySettings);
                            if (repoSettings != null) repository.Settings = repoSettings;
                        }
                        catch
                        {
                            // Ignore RepositorySettings deserialization errors, we got the important fields above
                        }
                    }
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

    public async Task<List<RepositoryResponse>> GetRepositoriesWithMetadataAsync()
    {
        var repositories = new List<RepositoryResponse>();
        
        if (!Directory.Exists(_repositoriesPath))
        {
            Directory.CreateDirectory(_repositoriesPath);
            return repositories;
        }

        var directories = Directory.GetDirectories(_repositoriesPath);
        
        foreach (var dir in directories)
        {
            var name = Path.GetFileName(dir);
            var directoryInfo = new DirectoryInfo(dir);
            var isGitRepo = await _gitMetadataService.IsGitRepositoryAsync(dir);
            
            var repository = new RepositoryResponse
            {
                Name = name,
                Path = dir,
                Type = isGitRepo ? "git" : "folder",
                Size = await _gitMetadataService.CalculateFolderSizeAsync(dir),
                LastModified = directoryInfo.LastWriteTime,
                CidxAware = false // Default to false, will be overridden if found in settings
            };

            // Use only internal settings file - eliminates dual file synchronization issues
            var settingsPath = Path.Combine(dir, ".claude-batch-settings.json");
            string? settingsToUse = File.Exists(settingsPath) ? settingsPath : null;
                
            if (settingsToUse != null)
            {
                try
                {
                    var settingsJson = await File.ReadAllTextAsync(settingsToUse);
                    var settings = JsonSerializer.Deserialize(settingsJson, AppJsonSerializerContext.Default.DictionaryStringObject);
                    
                    if (settings != null)
                    {
                        if (settings.TryGetValue("Description", out var desc) && desc != null)
                            repository.Description = desc.ToString() ?? string.Empty;
                        if (settings.TryGetValue("GitUrl", out var gitUrl) && gitUrl != null)
                            repository.GitUrl = gitUrl.ToString() ?? string.Empty;
                        if (settings.TryGetValue("RegisteredAt", out var regAt) && regAt != null && DateTime.TryParse(regAt.ToString(), out var registeredAt))
                            repository.RegisteredAt = registeredAt;
                        if (settings.TryGetValue("CloneStatus", out var cloneStatus) && cloneStatus != null)
                            repository.CloneStatus = cloneStatus.ToString() ?? string.Empty;
                        if (settings.TryGetValue("CidxAware", out var cidxAware) && cidxAware != null)
                        {
                            // Handle JsonElement boolean values from deserialized JSON
                            if (cidxAware is System.Text.Json.JsonElement jsonElement && jsonElement.ValueKind == System.Text.Json.JsonValueKind.True)
                                repository.CidxAware = true;
                            else if (cidxAware is System.Text.Json.JsonElement jsonElement2 && jsonElement2.ValueKind == System.Text.Json.JsonValueKind.False)
                                repository.CidxAware = false;
                            else if (bool.TryParse(cidxAware.ToString(), out var cidxAwareBool))
                                repository.CidxAware = cidxAwareBool;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Failed to read repository settings for {Repository}: {Error}", name, ex.Message);
                }
            }
            else
            {
                // If no settings file exists but it's a git repository, it's likely being cloned
                if (isGitRepo)
                {
                    repository.CloneStatus = "cloning";
                    _logger.LogDebug("Repository {Name} has no settings file but is a git repo - assuming active clone", name);
                }
            }

            // If it's a Git repository, extract Git metadata
            if (isGitRepo)
            {
                var gitMetadata = await _gitMetadataService.GetGitMetadataAsync(dir);
                if (gitMetadata != null)
                {
                    repository.RemoteUrl = gitMetadata.RemoteUrl;
                    repository.CurrentBranch = gitMetadata.CurrentBranch;
                    repository.CommitHash = gitMetadata.CommitHash;
                    repository.CommitMessage = gitMetadata.CommitMessage;
                    repository.CommitAuthor = gitMetadata.CommitAuthor;
                    repository.CommitDate = gitMetadata.CommitDate;
                    repository.HasUncommittedChanges = gitMetadata.HasUncommittedChanges;
                    repository.AheadBehind = gitMetadata.AheadBehind;
                    
                    // Set last pull status based on clone status and registration info
                    if (repository.RegisteredAt.HasValue)
                    {
                        repository.LastPull = repository.RegisteredAt;
                        
                        // Map clone status to pull status
                        repository.LastPullStatus = repository.CloneStatus switch
                        {
                            "completed" => "success",
                            "failed" => "failed", 
                            "cloning" => "in_progress",
                            "cidx_indexing" => "in_progress",
                            "cidx_failed" => "partial", // Cloned but CIDX failed
                            _ => "never"
                        };
                    }
                    else
                    {
                        repository.LastPullStatus = "never";
                    }
                }
            }

            repositories.Add(repository);
        }

        return repositories.OrderBy(r => r.Name).ToList();
    }

    public async Task<Repository?> GetRepositoryAsync(string name)
    {
        var repositories = await GetRepositoriesAsync();
        return repositories.FirstOrDefault(r => r.Name == name);
    }

    public async Task<Repository> RegisterRepositoryAsync(string name, string gitUrl, string description = "", bool cidxAware = true)
    {
        _logger.LogInformation("Registering repository {Name} from {GitUrl} (CidxAware: {CidxAware})", name, gitUrl, cidxAware);

        // Validate input with security checks
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Repository name cannot be empty", nameof(name));
        if (string.IsNullOrWhiteSpace(gitUrl))
            throw new ArgumentException("Git URL cannot be empty", nameof(gitUrl));

        // Security validation to prevent injection attacks
        if (!SecurityUtils.IsValidRepositoryName(name))
            throw new ArgumentException($"Repository name '{name}' contains invalid characters or format", nameof(name));
        if (!SecurityUtils.IsValidGitUrl(gitUrl))
            throw new ArgumentException($"Git URL '{gitUrl}' is not in a valid format", nameof(gitUrl));

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

        // Note: We don't create the repository directory or settings file here anymore
        // The directory will be created by git clone, and the settings file will be created 
        // after successful cloning in ProcessRepositoryAsync

        // Start background processing (fire-and-forget)
        _ = Task.Run(async () => await ProcessRepositoryAsync(repository, cidxAware));

        return repository;
    }

    private async Task ProcessRepositoryAsync(Repository repository, bool cidxAware)
    {
        var repositoryPath = Path.Combine(_repositoriesPath, repository.Name);
        var settingsPath = Path.Combine(repositoryPath, ".claude-batch-settings.json");
        
        try
        {
            _logger.LogInformation("Starting background processing for repository {Name} (CidxAware: {CidxAware})", repository.Name, cidxAware);
            
            // Clone the repository using safe process execution
            var processInfo = SecurityUtils.CreateSafeProcess("git", "clone", repository.GitUrl, repository.Path);
            
            using var process = new Process { StartInfo = processInfo };
            process.Start();
            
            // Add timeout protection to prevent hanging processes (2 hours for large repos)
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromHours(2));
            await process.WaitForExitAsync(timeoutCts.Token);

            if (process.ExitCode == 0)
            {
                _logger.LogInformation("Successfully cloned repository {Name} to {Path}", repository.Name, repository.Path);

                // Create settings file with CIDX aware status AFTER successful clone
                var initialSettings = new Dictionary<string, object>
                {
                    ["Name"] = repository.Name,
                    ["Description"] = repository.Description,
                    ["GitUrl"] = repository.GitUrl,
                    ["RegisteredAt"] = repository.RegisteredAt,
                    ["CloneStatus"] = "cloning",
                    ["CidxAware"] = cidxAware
                };
                await File.WriteAllTextAsync(settingsPath, JsonSerializer.Serialize(initialSettings, AppJsonSerializerContext.Default.DictionaryStringObject));
                _logger.LogInformation("Created settings file for repository {Name} with CidxAware: {CidxAware}", repository.Name, cidxAware);

                // Run cidx indexing if enabled
                if (cidxAware)
                {
                    await UpdateRepositoryStatusAsync(repository.Name, "cidx_indexing");
                    _logger.LogInformation("Starting FULL cidx indexing for repository {Name} during registration", repository.Name);
                    
                    try
                    {
                        await RunCidxIndexingAsync(repository.Path, repository.Name);
                        _logger.LogInformation("Successfully completed FULL cidx indexing for repository {Name} - ready for CoW cloning", repository.Name);
                        await UpdateRepositoryStatusAsync(repository.Name, "completed");
                    }
                    catch (Exception cidxEx)
                    {
                        _logger.LogError(cidxEx, "FULL cidx indexing failed during registration for repository {Name}", repository.Name);
                        await UpdateRepositoryStatusAsync(repository.Name, "cidx_failed");
                    }
                }
                else
                {
                    await UpdateRepositoryStatusAsync(repository.Name, "completed");
                }

                // Create final .claude-batch-settings.json file in the repo directory
                var repoSettingsPath = Path.Combine(repository.Path, ".claude-batch-settings.json");
                var finalSettings = new Dictionary<string, object>
                {
                    ["Name"] = repository.Name,
                    ["Description"] = repository.Description,
                    ["GitUrl"] = repository.GitUrl,
                    ["RegisteredAt"] = repository.RegisteredAt,
                    ["CloneStatus"] = "completed",
                    ["CidxAware"] = cidxAware
                };
                
                await File.WriteAllTextAsync(repoSettingsPath, JsonSerializer.Serialize(finalSettings, AppJsonSerializerContext.Default.DictionaryStringObject));
            }
            else
            {
                var error = await process.StandardError.ReadToEndAsync();
                _logger.LogError("Failed to clone repository {Name}: {Error}", repository.Name, error);
                
                await UpdateRepositoryStatusAsync(repository.Name, "failed");
                
                // Clean up failed clone directory if it exists
                if (Directory.Exists(repository.Path))
                {
                    Directory.Delete(repository.Path, true);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing repository {Name}", repository.Name);
            await UpdateRepositoryStatusAsync(repository.Name, "failed");
            
            // Clean up on failure - remove both directory and settings file
            if (Directory.Exists(repository.Path))
            {
                try
                {
                    Directory.Delete(repository.Path, true);
                    _logger.LogInformation("Cleaned up failed repository directory at {Path}", repository.Path);
                }
                catch (Exception cleanupEx)
                {
                    _logger.LogWarning(cleanupEx, "Failed to cleanup failed repository clone at {Path}", repository.Path);
                }
            }
            
            // CRITICAL: Also remove the settings file when registration fails to prevent leaving crap behind
            try
            {
                if (File.Exists(settingsPath))
                {
                    File.Delete(settingsPath);
                    _logger.LogInformation("Cleaned up failed repository settings file at {SettingsPath}", settingsPath);
                }
                // Also clean up the repository directory if it was created
                if (Directory.Exists(repositoryPath) && !Directory.EnumerateFileSystemEntries(repositoryPath).Any())
                {
                    Directory.Delete(repositoryPath);
                    _logger.LogInformation("Cleaned up empty repository directory at {RepositoryPath}", repositoryPath);
                }
            }
            catch (Exception settingsCleanupEx)
            {
                _logger.LogError(settingsCleanupEx, "CRITICAL: Failed to cleanup failed repository settings file at {SettingsPath}", settingsPath);
            }
        }
    }

    private async Task UpdateRepositoryStatusAsync(string repositoryName, string status)
    {
        try
        {
            var repositoryPath = Path.Combine(_repositoriesPath, repositoryName);
            var settingsPath = Path.Combine(repositoryPath, ".claude-batch-settings.json");
            
            Dictionary<string, object>? settings = null;
            
            if (File.Exists(settingsPath))
            {
                var settingsJson = await File.ReadAllTextAsync(settingsPath);
                settings = JsonSerializer.Deserialize(settingsJson, AppJsonSerializerContext.Default.DictionaryStringObject);
            }
            
            // If settings file doesn't exist or is corrupted, create minimal settings for status tracking
            if (settings == null)
            {
                // Ensure directory exists for settings file
                Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
                
                // Try to get the original registration data from memory/cache or use defaults
                // This ensures we don't lose GitUrl and other metadata during status updates
                var gitMetadata = await _gitMetadataService.GetGitMetadataAsync(repositoryPath);
                
                settings = new Dictionary<string, object>
                {
                    ["Name"] = repositoryName,
                    ["CloneStatus"] = status,
                    ["RegisteredAt"] = DateTime.UtcNow, // Use current time if original not available
                    ["CidxAware"] = false, // Will be updated later when we know the actual value
                    ["GitUrl"] = gitMetadata?.RemoteUrl ?? "N/A", // Preserve Git URL from repository metadata
                    ["Description"] = "" // Default empty description
                };
            }
            else
            {
                settings["CloneStatus"] = status;
            }
            
            // If status indicates CIDX activity, ensure CidxAware is true
            if (status == "cidx_indexing" || status == "cidx_failed")
            {
                settings["CidxAware"] = true;
            }
            // If completing and CidxAware was already set, preserve it
            else if (status == "completed" && settings.ContainsKey("CidxAware"))
            {
                // Don't override existing CidxAware value when completing
                // This preserves the CIDX status set during indexing
            }
            
            await File.WriteAllTextAsync(settingsPath, JsonSerializer.Serialize(settings, AppJsonSerializerContext.Default.DictionaryStringObject));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update status for repository {Name}", repositoryName);
        }
    }

    private async Task UpdateRepositoryCidxAwareAsync(string repositoryName, bool cidxAware)
    {
        try
        {
            var repositoryPath = Path.Combine(_repositoriesPath, repositoryName);
            var settingsPath = Path.Combine(repositoryPath, ".claude-batch-settings.json");
            
            Dictionary<string, object>? settings = null;
            
            if (File.Exists(settingsPath))
            {
                var settingsJson = await File.ReadAllTextAsync(settingsPath);
                settings = JsonSerializer.Deserialize(settingsJson, AppJsonSerializerContext.Default.DictionaryStringObject);
            }
            
            if (settings != null)
            {
                settings["CidxAware"] = cidxAware;
                await File.WriteAllTextAsync(settingsPath, JsonSerializer.Serialize(settings, AppJsonSerializerContext.Default.DictionaryStringObject));
                _logger.LogInformation("Updated CidxAware status for repository {Name} to {CidxAware}", repositoryName, cidxAware);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update CidxAware status for repository {Name}", repositoryName);
        }
    }

    private async Task RunCidxIndexingAsync(string repositoryPath, string repositoryName)
    {
        _logger.LogInformation("Starting cidx indexing for repository {Name} at {Path}", repositoryName, repositoryPath);

        try
        {
            // Step 1: Initialize cidx with voyage-ai embedding provider
            var initResult = await ExecuteCidxCommand("init --embedding-provider voyage-ai", repositoryPath);
            if (initResult.ExitCode != 0)
            {
                throw new InvalidOperationException($"Cidx init failed: {initResult.Output}");
            }

            // Step 2: Start cidx service
            var startResult = await ExecuteCidxCommand("start", repositoryPath);
            if (startResult.ExitCode != 0)
            {
                throw new InvalidOperationException($"Cidx start failed: {startResult.Output}");
            }

            // Step 3: Run indexing
            var indexResult = await ExecuteCidxCommand("index --reconcile", repositoryPath);
            if (indexResult.ExitCode != 0)
            {
                // Try to stop cidx if indexing failed
                await ExecuteCidxCommand("stop", repositoryPath);
                throw new InvalidOperationException($"Cidx indexing failed: {indexResult.Output}");
            }

            // Step 4: Stop cidx service after successful indexing
            var stopResult = await ExecuteCidxCommand("stop", repositoryPath);
            if (stopResult.ExitCode != 0)
            {
                _logger.LogWarning("Failed to stop cidx after indexing for repository {Name}: {Error}", repositoryName, stopResult.Output);
            }

            _logger.LogInformation("Successfully completed cidx indexing and stopped service for repository {Name}", repositoryName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cidx indexing failed for repository {Name}", repositoryName);
            
            // Ensure cidx is stopped even if indexing failed
            try
            {
                await ExecuteCidxCommand("stop", repositoryPath);
            }
            catch (Exception stopEx)
            {
                _logger.LogWarning(stopEx, "Failed to stop cidx after indexing failure for repository {Name}", repositoryName);
            }
            
            throw;
        }
    }

    private async Task<(int ExitCode, string Output)> ExecuteCidxCommand(string cidxArgs, string workingDirectory)
    {
        var processInfo = new ProcessStartInfo
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

        using var process = new Process { StartInfo = processInfo };
        var outputBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();

        process.OutputDataReceived += (sender, e) => { if (e.Data != null) outputBuilder.AppendLine(e.Data); };
        process.ErrorDataReceived += (sender, e) => { if (e.Data != null) errorBuilder.AppendLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        // Add timeout for cidx operations (10 minutes)
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        await process.WaitForExitAsync(timeoutCts.Token);

        var output = outputBuilder.ToString();
        var error = errorBuilder.ToString();
        var combinedOutput = string.IsNullOrEmpty(error) ? output : $"{output}\n\nErrors:\n{error}";

        return (process.ExitCode, combinedOutput);
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
            // CRITICAL: If repository is CIDX-aware, run cidx uninstall first to clean up root-owned data and containers
            if (repository.CidxAware && Directory.Exists(repository.Path))
            {
                _logger.LogInformation("Repository {Name} is CIDX-aware, running cidx uninstall to clean up containers and root-owned data", name);
                
                try
                {
                    var uninstallResult = await ExecuteCidxCommand("uninstall", repository.Path);
                    if (uninstallResult.ExitCode == 0)
                    {
                        _logger.LogInformation("Successfully ran cidx uninstall for repository {Name}", name);
                    }
                    else
                    {
                        _logger.LogWarning("Cidx uninstall returned non-zero exit code {ExitCode} for repository {Name}: {Output}", 
                            uninstallResult.ExitCode, name, uninstallResult.Output);
                    }
                }
                catch (Exception cidxEx)
                {
                    _logger.LogError(cidxEx, "Failed to run cidx uninstall for repository {Name}, continuing with removal", name);
                    // Continue with removal even if cidx uninstall fails
                }
            }

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

            // Internal settings file is automatically removed with the repository directory
            // No separate external settings file cleanup needed anymore
            _logger.LogInformation("Successfully unregistered repository {Name} (internal settings automatically removed with directory)", name);

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
            case CowMethod.BtrfsReflink:
                await CreateReflinkCloneAsync(repository.Path, jobPath);
                break;
            case CowMethod.FullCopyFallback:
                await CreateFullCopyCloneAsync(repository.Path, jobPath);
                break;
            default:
                throw new NotSupportedException("No Copy-on-Write method available");
        }

        // Create files directory for uploaded file support
        var filesPath = Path.Combine(jobPath, "files");
        Directory.CreateDirectory(filesPath);

        _logger.LogInformation("Created CoW clone of {Repository} at {JobPath} using {Method}", 
            repositoryName, jobPath, _cowMethod);

        // Fix cidx configuration in the CoW clone before starting containers
        // NOTE: CoW clones inherit embedding provider config from original repo, so fix-config doesn't need embedding params
        try
        {
            _logger.LogInformation("Running cidx fix-config on CoW clone at {JobPath}", jobPath);
            var fixConfigResult = await ExecuteCidxCommand("fix-config --force", jobPath);
            if (fixConfigResult.ExitCode != 0)
            {
                _logger.LogWarning("Cidx fix-config failed on CoW clone {JobPath}: {Output}", jobPath, fixConfigResult.Output);
            }
            else
            {
                _logger.LogInformation("Successfully fixed cidx configuration for CoW clone at {JobPath}", jobPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to run cidx fix-config on CoW clone {JobPath}", jobPath);
        }

        return jobPath;
    }

    public Task<bool> RemoveCowCloneAsync(string cowPath)
    {
        try
        {
            if (!Directory.Exists(cowPath)) return Task.FromResult(true);

            if (_cowMethod == CowMethod.BtrfsReflink)
            {
                Directory.Delete(cowPath, true);
                return Task.FromResult(true);
            }
            else
            {
                Directory.Delete(cowPath, true);
                return Task.FromResult(true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove CoW clone at {CowPath}", cowPath);
            return Task.FromResult(false);
        }
    }

    public Task<List<Models.DirectoryMetadata>?> GetDirectoriesAsync(string cowPath, string subPath)
    {
        var directories = new List<Models.DirectoryMetadata>();
        var targetPath = Path.Combine(cowPath, subPath);

        if (!Directory.Exists(targetPath)) 
            return Task.FromResult<List<Models.DirectoryMetadata>?>(null);

        try
        {
            // Get only direct subdirectories (no recursion for scalability)
            var directoryEntries = Directory.GetDirectories(targetPath, "*", SearchOption.TopDirectoryOnly);
            
            foreach (var dir in directoryEntries)
            {
                var dirInfo = new System.IO.DirectoryInfo(dir);
                
                // Skip hidden directories (starting with .)
                if (dirInfo.Name.StartsWith('.'))
                    continue;

                // Check if this directory has subdirectories
                bool hasSubdirectories;
                int fileCount = 0;
                
                try
                {
                    hasSubdirectories = Directory.GetDirectories(dir, "*", SearchOption.TopDirectoryOnly).Any();
                    fileCount = Directory.GetFiles(dir, "*", SearchOption.TopDirectoryOnly).Length;
                }
                catch
                {
                    // If we can't access the directory, assume it has no subdirectories
                    hasSubdirectories = false;
                    fileCount = 0;
                }

                var relativePath = Path.GetRelativePath(cowPath, dir);
                
                directories.Add(new Models.DirectoryMetadata
                {
                    Name = dirInfo.Name,
                    Path = relativePath,
                    Modified = dirInfo.LastWriteTime,
                    HasSubdirectories = hasSubdirectories,
                    FileCount = fileCount
                });
            }

            return Task.FromResult<List<Models.DirectoryMetadata>?>(directories.OrderBy(d => d.Name).ToList());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error reading directories from {TargetPath}", targetPath);
            return Task.FromResult<List<Models.DirectoryMetadata>?>(new List<Models.DirectoryMetadata>());
        }
    }

    public Task<List<Models.FileInfo>?> GetFilesInDirectoryAsync(string cowPath, string subPath, string? mask = null)
    {
        var files = new List<Models.FileInfo>();
        var targetPath = Path.Combine(cowPath, subPath);

        if (!Directory.Exists(targetPath)) 
            return Task.FromResult<List<Models.FileInfo>?>(null);

        try
        {
            // Get only files in the specific directory (no recursion for scalability)
            var fileEntries = Directory.GetFiles(targetPath, "*", SearchOption.TopDirectoryOnly);
            
            foreach (var file in fileEntries)
            {
                var fileInfo = new System.IO.FileInfo(file);
                
                // Skip hidden files (starting with .)
                if (fileInfo.Name.StartsWith('.'))
                    continue;
                
                // Apply mask filtering if provided
                if (!string.IsNullOrEmpty(mask) && !MatchesFileMask(fileInfo.Name, mask))
                    continue;
                
                var relativePath = Path.GetRelativePath(cowPath, file);
                
                files.Add(new Models.FileInfo
                {
                    Name = fileInfo.Name,
                    Type = "file",
                    Path = relativePath,
                    Size = fileInfo.Length,
                    Modified = fileInfo.LastWriteTime
                });
            }

            return Task.FromResult<List<Models.FileInfo>?>(files.OrderBy(f => f.Name).ToList());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error reading files from {TargetPath}", targetPath);
            return Task.FromResult<List<Models.FileInfo>?>(new List<Models.FileInfo>());
        }
    }

    private bool MatchesFileMask(string fileName, string mask)
    {
        // Support multiple masks separated by commas
        var masks = mask.Split(',', StringSplitOptions.RemoveEmptyEntries);
        
        foreach (var singleMask in masks)
        {
            var trimmedMask = singleMask.Trim();
            
            // Convert wildcard pattern to regex
            var regexPattern = "^" + System.Text.RegularExpressions.Regex.Escape(trimmedMask)
                .Replace("\\*", ".*")
                .Replace("\\?", ".") + "$";
            
            if (System.Text.RegularExpressions.Regex.IsMatch(fileName, regexPattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                return true;
        }
        
        return false;
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
                    if (await TestReflinkSupportAsync())
                    {
                        _cowMethod = CowMethod.BtrfsReflink;
                        _logger.LogInformation("Using Btrfs reflink for CoW operations");
                    }
                    break;
            }

            if (_cowMethod == CowMethod.Unsupported)
            {
                _cowMethod = CowMethod.FullCopyFallback;
                _logger.LogWarning("No CoW support detected, falling back to full directory copying (slower but safe)");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to detect CoW method, using full copy fallback");
            _cowMethod = CowMethod.FullCopyFallback;
        }
    }

    private async Task<string?> GetFilesystemTypeAsync(string path)
    {
        try
        {
            var result = await ExecuteCommandAsync("df", "-T", path);
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
            
            var result = await ExecuteCommandAsync("cp", "--reflink=always", sourceFile, targetFile);
            
            Directory.Delete(testDir, true);
            return result.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }


    private async Task CreateReflinkCloneAsync(string sourcePath, string targetPath)
    {
        // If target already exists, remove it first to avoid permission issues with read-only files
        if (Directory.Exists(targetPath))
        {
            Directory.Delete(targetPath, true);
        }
        
        // Create target directory
        Directory.CreateDirectory(targetPath);
        
        // Copy contents of source directory to target directory (not the directory itself)
        // This prevents nested repository directories like /workspace/jobs/[id]/tries/tries/
        var result = await ExecuteCommandAsync("cp", "-r", "--reflink=always", $"{sourcePath}/.", targetPath);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"Failed to create reflink clone: {result.Error}");
        }
    }


    private async Task CreateFullCopyCloneAsync(string sourcePath, string targetPath)
    {
        // If target already exists, remove it first to avoid permission issues
        if (Directory.Exists(targetPath))
        {
            Directory.Delete(targetPath, true);
        }
        
        // Create target directory first
        Directory.CreateDirectory(targetPath);
        
        _logger.LogInformation("Creating full copy from {SourcePath} to {TargetPath} (this may take some time)", sourcePath, targetPath);
        
        // Use rsync for reliable full copying (not the directory itself)
        // This prevents nested repository directories like /workspace/jobs/[id]/tries/tries/
        // --archive preserves permissions, timestamps, etc.
        // --exclude='.git/objects/pack/*.idx' --exclude='.git/objects/pack/*.pack' can be added to speed up git repos if needed
        var result = await ExecuteCommandAsync("rsync", "-a", "--no-links", $"{sourcePath}/", $"{targetPath}/");
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"Failed to create full copy clone: {result.Error}");
        }
        
        _logger.LogInformation("Full copy completed successfully from {SourcePath} to {TargetPath}", sourcePath, targetPath);
    }

    /// <summary>
    /// Pull latest changes from remote repository before job execution
    /// This ensures jobs run against the most current version of the repository
    /// </summary>
    public async Task<(bool Success, string Status, string ErrorMessage)> PullRepositoryUpdatesAsync(string repositoryName)
    {
        try
        {
            var repository = await GetRepositoryAsync(repositoryName);
            if (repository == null)
                return (false, "failed", $"Repository '{repositoryName}' not found");

            if (!Directory.Exists(repository.Path))
                return (false, "failed", $"Repository path '{repository.Path}' does not exist");

            // Check if directory is a git repository
            var gitDir = Path.Combine(repository.Path, ".git");
            if (!Directory.Exists(gitDir))
            {
                _logger.LogInformation("Repository {Name} is not a git repository, skipping git pull", repositoryName);
                return (true, "not_git_repo", string.Empty);
            }

            _logger.LogInformation("Pulling latest changes for repository {Name} at {Path}", repositoryName, repository.Path);
            
            // Execute git pull
            var exitCode = await ExecuteGitPullAsync(repository.Path);
            
            if (exitCode == 0)
            {
                _logger.LogInformation("Successfully pulled latest changes for repository {Name}", repositoryName);
                return (true, "pulled", string.Empty);
            }
            else
            {
                _logger.LogError("Git pull failed for repository {Name} with exit code {ExitCode}", 
                    repositoryName, exitCode);
                return (false, "failed", $"Git pull failed with exit code {exitCode}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error pulling repository updates for {Name}", repositoryName);
            return (false, "failed", $"Git pull error: {ex.Message}");
        }
    }

    private async Task<int> ExecuteGitPullAsync(string repositoryPath)
    {
        try
        {
            var processInfo = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = "pull origin HEAD",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = repositoryPath
            };

            using var process = new Process { StartInfo = processInfo };
            process.Start();
            
            // Add timeout for git operations (5 minutes)
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            await process.WaitForExitAsync(timeoutCts.Token);

            return process.ExitCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute git pull for repository at {Path}", repositoryPath);
            return -1;
        }
    }

    private async Task<(int ExitCode, string Output, string Error)> ExecuteCommandAsync(string command, params string[] arguments)
    {
        // Use safe process creation to prevent injection
        var processInfo = SecurityUtils.CreateSafeProcess(command, arguments);
        
        using var process = new Process { StartInfo = processInfo };
        process.Start();
        
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        
        // Add timeout protection
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        await process.WaitForExitAsync(timeoutCts.Token);
        
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
    BtrfsReflink,
    FullCopyFallback
}