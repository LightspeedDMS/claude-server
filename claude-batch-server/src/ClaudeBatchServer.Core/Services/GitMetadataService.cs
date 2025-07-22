using System.Diagnostics;
using System.Text;
using ClaudeBatchServer.Core.DTOs;
using Microsoft.Extensions.Logging;

namespace ClaudeBatchServer.Core.Services;

public interface IGitMetadataService
{
    Task<bool> IsGitRepositoryAsync(string path);
    Task<GitMetadata?> GetGitMetadataAsync(string repoPath);
    Task<long> CalculateFolderSizeAsync(string folderPath);
}

public class GitMetadata
{
    public string? RemoteUrl { get; set; }
    public string? CurrentBranch { get; set; }
    public string? CommitHash { get; set; }
    public string? CommitMessage { get; set; }
    public string? CommitAuthor { get; set; }
    public DateTime? CommitDate { get; set; }
    public bool HasUncommittedChanges { get; set; }
    public AheadBehindStatus? AheadBehind { get; set; }
}

public class GitMetadataService : IGitMetadataService
{
    private readonly ILogger<GitMetadataService> _logger;

    public GitMetadataService(ILogger<GitMetadataService> logger)
    {
        _logger = logger;
    }

    public async Task<bool> IsGitRepositoryAsync(string path)
    {
        try
        {
            var gitDir = Path.Combine(path, ".git");
            return Directory.Exists(gitDir) || File.Exists(gitDir);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error checking if {Path} is a git repository", path);
            return false;
        }
    }

    public async Task<GitMetadata?> GetGitMetadataAsync(string repoPath)
    {
        try
        {
            if (!await IsGitRepositoryAsync(repoPath))
            {
                return null;
            }

            var metadata = new GitMetadata();

            // Get remote URL
            metadata.RemoteUrl = await ExecuteGitCommandAsync(repoPath, "config --get remote.origin.url");

            // Get current branch
            metadata.CurrentBranch = await ExecuteGitCommandAsync(repoPath, "branch --show-current");

            // Get latest commit info
            metadata.CommitHash = await ExecuteGitCommandAsync(repoPath, "rev-parse HEAD");
            metadata.CommitMessage = await ExecuteGitCommandAsync(repoPath, "log -1 --pretty=format:%s");
            metadata.CommitAuthor = await ExecuteGitCommandAsync(repoPath, "log -1 --pretty=format:%an");
            
            var commitDateStr = await ExecuteGitCommandAsync(repoPath, "log -1 --pretty=format:%ai");
            if (!string.IsNullOrEmpty(commitDateStr) && DateTime.TryParse(commitDateStr, out var commitDate))
            {
                metadata.CommitDate = commitDate;
            }

            // Check for uncommitted changes
            var statusOutput = await ExecuteGitCommandAsync(repoPath, "status --porcelain");
            metadata.HasUncommittedChanges = !string.IsNullOrWhiteSpace(statusOutput);

            // Get ahead/behind status
            metadata.AheadBehind = await GetAheadBehindStatusAsync(repoPath, metadata.CurrentBranch);

            return metadata;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error extracting git metadata from {RepoPath}", repoPath);
            return null;
        }
    }

    public async Task<long> CalculateFolderSizeAsync(string folderPath)
    {
        try
        {
            if (!Directory.Exists(folderPath))
                return 0;

            long totalSize = 0;
            var directoryInfo = new DirectoryInfo(folderPath);

            // Calculate size of all files recursively
            var files = directoryInfo.EnumerateFiles("*", SearchOption.AllDirectories);
            
            await Task.Run(() =>
            {
                foreach (var file in files)
                {
                    try
                    {
                        totalSize += file.Length;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Error getting size for file {FilePath}", file.FullName);
                        // Continue with other files
                    }
                }
            });

            return totalSize;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error calculating folder size for {FolderPath}", folderPath);
            return 0;
        }
    }

    private async Task<string?> ExecuteGitCommandAsync(string workingDirectory, string arguments)
    {
        try
        {
            var processInfo = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(processInfo);
            if (process == null)
                return null;

            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            
            await process.WaitForExitAsync();

            if (process.ExitCode == 0)
            {
                return output.Trim();
            }

            _logger.LogDebug("Git command failed: git {Arguments}. Error: {Error}", arguments, error);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error executing git command: git {Arguments}", arguments);
            return null;
        }
    }

    private async Task<AheadBehindStatus?> GetAheadBehindStatusAsync(string repoPath, string? currentBranch)
    {
        try
        {
            if (string.IsNullOrEmpty(currentBranch))
                return null;

            // First, try to update remote tracking info
            await ExecuteGitCommandAsync(repoPath, "fetch --dry-run");

            // Get ahead/behind count
            var aheadBehindStr = await ExecuteGitCommandAsync(repoPath, $"rev-list --left-right --count origin/{currentBranch}...HEAD");
            
            if (string.IsNullOrEmpty(aheadBehindStr))
            {
                // Fallback: try without origin prefix
                aheadBehindStr = await ExecuteGitCommandAsync(repoPath, $"rev-list --left-right --count {currentBranch}@{{upstream}}...HEAD");
            }

            if (!string.IsNullOrEmpty(aheadBehindStr))
            {
                var parts = aheadBehindStr.Split('\t');
                if (parts.Length == 2 && 
                    int.TryParse(parts[0], out var behind) && 
                    int.TryParse(parts[1], out var ahead))
                {
                    return new AheadBehindStatus
                    {
                        Ahead = ahead,
                        Behind = behind
                    };
                }
            }

            return new AheadBehindStatus { Ahead = 0, Behind = 0 };
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error getting ahead/behind status for {RepoPath}", repoPath);
            return new AheadBehindStatus { Ahead = 0, Behind = 0 };
        }
    }
}