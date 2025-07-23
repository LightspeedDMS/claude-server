using System.Diagnostics;
using System.Text.RegularExpressions;

namespace ClaudeBatchServer.Core.Services;

/// <summary>
/// Security utilities for input validation and sanitization
/// </summary>
public static class SecurityUtils
{
    // Regex patterns for validation
    private static readonly Regex ValidRepositoryNamePattern = new("^[a-zA-Z0-9._-]+$", RegexOptions.Compiled);
    private static readonly Regex ValidGitUrlPattern = new(@"^(https?://|git@)[a-zA-Z0-9._/-]+\.git$", RegexOptions.Compiled);
    private static readonly Regex PathTraversalPattern = new(@"\.\./|\.\.\\", RegexOptions.Compiled);
    
    // Dangerous characters that could enable injection (for shell sanitization)
    private static readonly char[] DangerousChars = { ';', '&', '|', '`', '$', '(', ')', '<', '>', '\'', '"', '\n', '\r', '/' };
    
    // Dangerous characters for repository names and Git URLs (excluding '/' for URLs)
    private static readonly char[] ValidationDangerousChars = { ';', '&', '|', '`', '$', '(', ')', '<', '>', '\'', '"', '\n', '\r' };

    /// <summary>
    /// Validates and sanitizes a repository name to prevent injection
    /// </summary>
    /// <param name="repositoryName">The repository name to validate</param>
    /// <returns>True if valid, false otherwise</returns>
    public static bool IsValidRepositoryName(string? repositoryName)
    {
        if (string.IsNullOrWhiteSpace(repositoryName))
            return false;

        // Check length limits
        if (repositoryName.Length > 100)
            return false;

        // Check for dangerous characters
        if (repositoryName.IndexOfAny(ValidationDangerousChars) >= 0)
            return false;

        // Check against allowed pattern
        return ValidRepositoryNamePattern.IsMatch(repositoryName);
    }

    /// <summary>
    /// Validates a Git URL to ensure it's safe for cloning
    /// </summary>
    /// <param name="gitUrl">The Git URL to validate</param>
    /// <returns>True if valid, false otherwise</returns>
    public static bool IsValidGitUrl(string? gitUrl)
    {
        if (string.IsNullOrWhiteSpace(gitUrl))
            return false;

        // Check length limits  
        if (gitUrl.Length > 500)
            return false;

        // Check for dangerous characters
        if (gitUrl.IndexOfAny(ValidationDangerousChars) >= 0)
            return false;

        // Must be a valid Git URL format
        return ValidGitUrlPattern.IsMatch(gitUrl) || 
               gitUrl.StartsWith("https://") || 
               gitUrl.StartsWith("http://") ||
               gitUrl.StartsWith("git@");
    }

    /// <summary>
    /// Validates a file system path to prevent path traversal attacks
    /// </summary>
    /// <param name="path">The path to validate</param>
    /// <returns>True if valid, false otherwise</returns>
    public static bool IsValidPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        // Check for path traversal attempts
        if (PathTraversalPattern.IsMatch(path))
            return false;

        // Check for dangerous characters in paths
        var pathDangerousChars = new[] { ';', '&', '|', '`', '$', '\'', '"' };
        if (path.IndexOfAny(pathDangerousChars) >= 0)
            return false;

        return true;
    }

    /// <summary>
    /// Sanitizes a string for safe use in shell commands by removing dangerous characters
    /// </summary>
    /// <param name="input">The input to sanitize</param>
    /// <returns>Sanitized string</returns>
    public static string SanitizeForShell(string input)
    {
        if (string.IsNullOrEmpty(input))
            return string.Empty;

        // Remove dangerous characters
        foreach (var dangerousChar in DangerousChars)
        {
            input = input.Replace(dangerousChar.ToString(), "");
        }

        return input;
    }

    /// <summary>
    /// Creates safe process arguments using ArgumentList to prevent injection
    /// </summary>
    /// <param name="command">The base command</param>
    /// <param name="args">Arguments to add safely</param>
    /// <returns>ProcessStartInfo configured safely</returns>
    public static ProcessStartInfo CreateSafeProcess(string command, params string[] args)
    {
        var processInfo = new ProcessStartInfo
        {
            FileName = command,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        // Use ArgumentList for safe parameter passing
        foreach (var arg in args)
        {
            if (!string.IsNullOrWhiteSpace(arg))
            {
                processInfo.ArgumentList.Add(arg);
            }
        }

        return processInfo;
    }
}