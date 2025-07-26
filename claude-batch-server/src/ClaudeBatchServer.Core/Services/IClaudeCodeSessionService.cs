namespace ClaudeBatchServer.Core.Services;

/// <summary>
/// Service for managing Claude Code session information
/// </summary>
public interface IClaudeCodeSessionService
{
    /// <summary>
    /// Gets the latest session ID for a given directory path
    /// </summary>
    /// <param name="directoryPath">The directory path where Claude Code was run</param>
    /// <returns>The latest session ID, or null if no sessions found</returns>
    Task<string?> GetLatestSessionIdAsync(string directoryPath);
    
    /// <summary>
    /// Gets all session IDs for a given directory path, ordered by creation time (newest first)
    /// </summary>
    /// <param name="directoryPath">The directory path where Claude Code was run</param>
    /// <returns>List of session IDs ordered by creation time</returns>
    Task<IEnumerable<string>> GetAllSessionIdsAsync(string directoryPath);
    
    /// <summary>
    /// Checks if a session exists for the given directory and session ID
    /// </summary>
    /// <param name="directoryPath">The directory path where Claude Code was run</param>
    /// <param name="sessionId">The session ID to check</param>
    /// <returns>True if session exists, false otherwise</returns>
    Task<bool> SessionExistsAsync(string directoryPath, string sessionId);
}