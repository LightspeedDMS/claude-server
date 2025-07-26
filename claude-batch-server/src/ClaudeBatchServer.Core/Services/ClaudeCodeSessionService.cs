using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace ClaudeBatchServer.Core.Services;

/// <summary>
/// Service for managing Claude Code session information by reading from ~/.claude/projects directory structure
/// </summary>
public class ClaudeCodeSessionService : IClaudeCodeSessionService
{
    private readonly ILogger<ClaudeCodeSessionService> _logger;
    private static readonly Regex SessionIdPattern = new(@"^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\.jsonl$", RegexOptions.IgnoreCase);

    public ClaudeCodeSessionService(ILogger<ClaudeCodeSessionService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Gets the latest session ID for a given directory path
    /// </summary>
    /// <param name="directoryPath">The directory path where Claude Code was run</param>
    /// <returns>The latest session ID, or null if no sessions found</returns>
    public async Task<string?> GetLatestSessionIdAsync(string directoryPath)
    {
        try
        {
            var sessionIds = await GetAllSessionIdsAsync(directoryPath);
            return sessionIds.FirstOrDefault();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get latest session ID for directory: {DirectoryPath}", directoryPath);
            return null;
        }
    }

    /// <summary>
    /// Gets all session IDs for a given directory path, ordered by creation time (newest first)
    /// </summary>
    /// <param name="directoryPath">The directory path where Claude Code was run</param>
    /// <returns>List of session IDs ordered by creation time</returns>
    public async Task<IEnumerable<string>> GetAllSessionIdsAsync(string directoryPath)
    {
        try
        {
            var claudeProjectsPath = GetClaudeProjectsPath();
            if (!Directory.Exists(claudeProjectsPath))
            {
                _logger.LogWarning("Claude projects directory not found: {Path}", claudeProjectsPath);
                return Enumerable.Empty<string>();
            }

            var encodedPath = EncodeDirectoryPath(directoryPath);
            var sessionDirectory = Path.Combine(claudeProjectsPath, encodedPath);

            if (!Directory.Exists(sessionDirectory))
            {
                _logger.LogDebug("No session directory found for path: {DirectoryPath} (encoded: {EncodedPath})", directoryPath, encodedPath);
                return Enumerable.Empty<string>();
            }

            var sessionFiles = Directory.GetFiles(sessionDirectory, "*.jsonl")
                .Where(file => SessionIdPattern.IsMatch(Path.GetFileName(file)))
                .Select(file => new FileInfo(file))
                .OrderByDescending(file => file.LastWriteTime)
                .Select(file => Path.GetFileNameWithoutExtension(file.Name))
                .ToList();

            _logger.LogDebug("Found {Count} session files for directory: {DirectoryPath}", sessionFiles.Count, directoryPath);
            return await Task.FromResult(sessionFiles);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get session IDs for directory: {DirectoryPath}", directoryPath);
            return Enumerable.Empty<string>();
        }
    }

    /// <summary>
    /// Checks if a session exists for the given directory and session ID
    /// </summary>
    /// <param name="directoryPath">The directory path where Claude Code was run</param>
    /// <param name="sessionId">The session ID to check</param>
    /// <returns>True if session exists, false otherwise</returns>
    public async Task<bool> SessionExistsAsync(string directoryPath, string sessionId)
    {
        try
        {
            var claudeProjectsPath = GetClaudeProjectsPath();
            if (!Directory.Exists(claudeProjectsPath))
            {
                return false;
            }

            var encodedPath = EncodeDirectoryPath(directoryPath);
            var sessionFilePath = Path.Combine(claudeProjectsPath, encodedPath, $"{sessionId}.jsonl");

            var exists = File.Exists(sessionFilePath);
            _logger.LogDebug("Session {SessionId} exists for directory {DirectoryPath}: {Exists}", sessionId, directoryPath, exists);
            
            return await Task.FromResult(exists);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check if session exists. Directory: {DirectoryPath}, SessionId: {SessionId}", directoryPath, sessionId);
            return false;
        }
    }

    /// <summary>
    /// Gets the Claude projects directory path (~/.claude/projects)
    /// </summary>
    /// <returns>Full path to Claude projects directory</returns>
    private static string GetClaudeProjectsPath()
    {
        var homeDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(homeDirectory, ".claude", "projects");
    }

    /// <summary>
    /// Encodes a directory path using Claude Code's encoding scheme
    /// Converts forward slashes to hyphens and removes leading slash
    /// Example: "/home/user/project" becomes "home-user-project"
    /// </summary>
    /// <param name="directoryPath">The directory path to encode</param>
    /// <returns>Encoded path suitable for Claude's storage structure</returns>
    private static string EncodeDirectoryPath(string directoryPath)
    {
        if (string.IsNullOrEmpty(directoryPath))
        {
            throw new ArgumentException("Directory path cannot be null or empty", nameof(directoryPath));
        }

        // Normalize path separators to forward slashes
        var normalizedPath = directoryPath.Replace('\\', '/');
        
        // Replace forward slashes with hyphens
        var encodedPath = normalizedPath.Replace('/', '-');
        
        // Remove leading hyphen if present (from leading slash)
        if (encodedPath.StartsWith('-'))
        {
            encodedPath = encodedPath[1..];
        }

        return encodedPath;
    }
}