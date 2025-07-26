# Claude Code Session Service

## Overview

The `ClaudeCodeSessionService` is a C# module that allows you to programmatically retrieve Claude Code session IDs for any directory. This enables automated resumption of Claude Code sessions in print mode.

## The Problem

Claude Code's `--print --resume` mode requires an explicit session ID, but there's no built-in way to retrieve session IDs from previous runs. This module solves that by reverse-engineering Claude Code's session storage structure.

## How It Works

Claude Code stores session data in `~/.claude/projects/` using an encoded directory path structure:

- Directory path: `/home/user/project` → Encoded: `home-user-project`
- Session files: `{session-uuid}.jsonl`
- Storage location: `~/.claude/projects/home-user-project/{session-uuid}.jsonl`

The service reads this directory structure and extracts session IDs, ordered by file modification time (newest first).

## Usage

### Basic Usage

```csharp
using ClaudeBatchServer.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// Setup DI
var services = new ServiceCollection();
services.AddLogging();
services.AddTransient<IClaudeCodeSessionService, ClaudeCodeSessionService>();
var serviceProvider = services.BuildServiceProvider();

var sessionService = serviceProvider.GetRequiredService<IClaudeCodeSessionService>();

// Get latest session ID for a directory
var directoryPath = "/home/user/myproject";
var latestSessionId = await sessionService.GetLatestSessionIdAsync(directoryPath);

if (latestSessionId != null)
{
    // Use with Claude Code print mode
    var command = $"claude --print --resume {latestSessionId} \"your prompt here\"";
    Console.WriteLine($"Command: {command}");
}
```

### Get All Sessions

```csharp
// Get all session IDs (newest first)
var allSessions = await sessionService.GetAllSessionIdsAsync(directoryPath);
foreach (var sessionId in allSessions)
{
    Console.WriteLine($"Session: {sessionId}");
}
```

### Check Session Existence

```csharp
// Verify a specific session exists
var exists = await sessionService.SessionExistsAsync(directoryPath, "some-session-id");
Console.WriteLine($"Session exists: {exists}");
```

### Extension Methods

The module includes helpful extension methods:

```csharp
using ClaudeBatchServer.Examples;

// Get session for current working directory
var sessionId = await sessionService.GetLatestSessionIdForCurrentDirectoryAsync();

// Build complete Claude command
var command = await sessionService.BuildClaudeCommandAsync(
    "/path/to/project", 
    "Implement authentication feature"
);
// Returns: claude --print --resume {session-id} "Implement authentication feature"
```

## API Reference

### IClaudeCodeSessionService

#### Methods

- **`GetLatestSessionIdAsync(string directoryPath)`**
  - Returns the most recent session ID for the given directory
  - Returns `null` if no sessions found

- **`GetAllSessionIdsAsync(string directoryPath)`**
  - Returns all session IDs ordered by creation time (newest first)
  - Returns empty collection if no sessions found

- **`SessionExistsAsync(string directoryPath, string sessionId)`**
  - Checks if a specific session exists for the directory
  - Returns `bool` indicating existence

## Directory Path Encoding

The service automatically handles Claude Code's directory path encoding:

| Original Path | Encoded Path |
|---------------|--------------|
| `/home/user/project` | `home-user-project` |
| `/home/user/project/sub` | `home-user-project-sub` |
| `C:\Users\test\project` | `C:-Users-test-project` |

## Error Handling

The service includes comprehensive error handling:

- Logs warnings for missing directories
- Logs errors for file system access issues
- Returns safe defaults (null/empty) on errors
- Never throws exceptions

## Testing

The module includes comprehensive unit tests covering:

- Session retrieval with multiple sessions
- Timestamp-based ordering
- Non-session file filtering
- Directory path encoding
- Error scenarios

Run tests with:
```bash
dotnet test --filter "ClaudeCodeSessionServiceTests"
```

## Integration

Add to your DI container:

```csharp
services.AddTransient<IClaudeCodeSessionService, ClaudeCodeSessionService>();
```

## Real-World Example

```csharp
public async Task<string> RunClaudeOnProject(string projectPath, string prompt)
{
    var sessionId = await _sessionService.GetLatestSessionIdAsync(projectPath);
    
    if (sessionId == null)
    {
        throw new InvalidOperationException($"No Claude Code sessions found for {projectPath}");
    }
    
    var process = new Process
    {
        StartInfo = new ProcessStartInfo
        {
            FileName = "claude",
            Arguments = $"--print --resume {sessionId} \"{prompt}\"",
            WorkingDirectory = projectPath,
            RedirectStandardOutput = true,
            UseShellExecute = false
        }
    };
    
    process.Start();
    var output = await process.StandardOutput.ReadToEndAsync();
    await process.WaitForExitAsync();
    
    return output;
}
```

This module enables seamless automation of Claude Code sessions, allowing you to build sophisticated workflows that leverage Claude's contextual understanding across multiple interactions.