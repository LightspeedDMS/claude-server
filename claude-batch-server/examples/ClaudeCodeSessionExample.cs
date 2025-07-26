using ClaudeBatchServer.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ClaudeBatchServer.Examples;

/// <summary>
/// Example showing how to use the ClaudeCodeSessionService to retrieve session IDs
/// </summary>
public class ClaudeCodeSessionExample
{
    public static async Task RunExample()
    {
        // Setup dependency injection
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());
        services.AddTransient<IClaudeCodeSessionService, ClaudeCodeSessionService>();
        
        var serviceProvider = services.BuildServiceProvider();
        var sessionService = serviceProvider.GetRequiredService<IClaudeCodeSessionService>();

        // Example directory path where Claude Code was run
        var directoryPath = "/home/user/myproject";
        
        Console.WriteLine($"Checking Claude Code sessions for directory: {directoryPath}");
        
        // Get the latest session ID
        var latestSessionId = await sessionService.GetLatestSessionIdAsync(directoryPath);
        if (latestSessionId != null)
        {
            Console.WriteLine($"Latest session ID: {latestSessionId}");
            
            // You can now use this session ID with Claude Code print mode:
            // claude --print --resume {latestSessionId} "your prompt here"
            Console.WriteLine($"Usage: claude --print --resume {latestSessionId} \"your prompt here\"");
        }
        else
        {
            Console.WriteLine("No sessions found for this directory");
        }
        
        // Get all session IDs (newest first)
        var allSessions = await sessionService.GetAllSessionIdsAsync(directoryPath);
        var sessionList = allSessions.ToList();
        
        Console.WriteLine($"\nFound {sessionList.Count} total sessions:");
        for (int i = 0; i < sessionList.Count; i++)
        {
            Console.WriteLine($"  {i + 1}. {sessionList[i]}");
        }
        
        // Check if a specific session exists
        if (latestSessionId != null)
        {
            var exists = await sessionService.SessionExistsAsync(directoryPath, latestSessionId);
            Console.WriteLine($"\nSession {latestSessionId} exists: {exists}");
        }
    }
}

/// <summary>
/// Extension methods for easier integration into your application
/// </summary>
public static class ClaudeCodeSessionExtensions
{
    /// <summary>
    /// Gets the latest Claude Code session ID for the current working directory
    /// </summary>
    /// <param name="sessionService">The session service</param>
    /// <returns>Latest session ID or null if none found</returns>
    public static async Task<string?> GetLatestSessionIdForCurrentDirectoryAsync(
        this IClaudeCodeSessionService sessionService)
    {
        var currentDirectory = Directory.GetCurrentDirectory();
        return await sessionService.GetLatestSessionIdAsync(currentDirectory);
    }
    
    /// <summary>
    /// Builds a Claude Code print mode command for the latest session
    /// </summary>
    /// <param name="sessionService">The session service</param>
    /// <param name="directoryPath">Directory path where Claude Code was run</param>
    /// <param name="prompt">The prompt to execute</param>
    /// <returns>Complete command string or null if no session found</returns>
    public static async Task<string?> BuildClaudeCommandAsync(
        this IClaudeCodeSessionService sessionService,
        string directoryPath,
        string prompt)
    {
        var sessionId = await sessionService.GetLatestSessionIdAsync(directoryPath);
        if (sessionId == null)
        {
            return null;
        }
        
        return $"claude --print --resume {sessionId} \"{prompt.Replace("\"", "\\\"")}\"";
    }
}