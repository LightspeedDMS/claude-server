# Claude Batch Server Configuration Guide

This document explains all available configuration options for the Claude Batch Server.

## Configuration Files

The server uses ASP.NET Core configuration with these files:
- `appsettings.json` - Main configuration (production)
- `appsettings.Development.json` - Development overrides
- `appsettings.example.json` - Complete example with all options

## Configuration Sections

### Logging

Controls application logging behavior:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",        // Default log level
      "Microsoft.AspNetCore": "Warning" // Framework-specific level
    }
  }
}
```

### Serilog (Advanced Logging)

Structured logging configuration with tilde expansion support:

```json
{
  "Serilog": {
    "Using": ["Serilog.Sinks.Console", "Serilog.Sinks.File"],
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft": "Warning",
        "System": "Warning"
      }
    },
    "WriteTo": [
      {
        "Name": "Console",
        "Args": {
          "outputTemplate": "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}"
        }
      },
      {
        "Name": "File",
        "Args": {
          "path": "~/claude-batch-server-logs/app-.log",  // Supports tilde expansion
          "rollingInterval": "Day",
          "retainedFileCountLimit": 7,
          "outputTemplate": "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}"
        }
      }
    ]
  }
}
```

### JWT Authentication

JSON Web Token configuration for API authentication:

```json
{
  "Jwt": {
    "Key": "YourSuperSecretJwtKeyThatShouldBe32CharactersOrLonger!",
    "ExpiryHours": "24"
  }
}
```

**Important**: Change the `Key` to a secure random string for production!

### User Authentication

Shadow file-based user authentication with tilde expansion support:

```json
{
  "Auth": {
    "ShadowFilePath": "~/Dev/claude-server/claude-batch-server/claude-server-shadow",
    "PasswdFilePath": "~/Dev/claude-server/claude-batch-server/claude-server-passwd"
  }
}
```

These files contain user credentials in Unix shadow/passwd format. The system supports both plaintext password authentication and pre-computed hash authentication for secure login over HTTP.

### Workspace Paths

File system paths for repositories and jobs with tilde expansion support:

```json
{
  "Workspace": {
    "RepositoriesPath": "~/claude-code-server-workspace/repos",  // Where cloned repos are stored
    "JobsPath": "~/claude-code-server-workspace/jobs"           // Where job workspaces are created
  }
}
```

**Tilde Expansion**: All paths starting with `~/` are automatically expanded to the user's home directory at runtime.

### Job Processing

Job execution and queue configuration:

```json
{
  "Jobs": {
    "MaxConcurrent": "5",      // Maximum concurrent jobs
    "TimeoutHours": "24",      // Job timeout in hours
    "UseNewWorkflow": "true"   // Enable enhanced job workflow
  }
}
```

### Claude Code Integration

Claude Code CLI configuration:

```json
{
  "Claude": {
    "Command": "claude"         // Command to run Claude Code
  }
}
```

### System Prompts

Template paths for CIDX-aware and non-CIDX system prompts:

```json
{
  "SystemPrompts": {
    "CidxAvailableTemplatePath": "SystemPrompts/cidx-system-prompt-template.txt",
    "CidxUnavailableTemplatePath": "SystemPrompts/cidx-unavailable-system-prompt-template.txt"
  }
}
```

### CIDX Configuration

Code indexing configuration for semantic search:

```json
{
  "Cidx": {
    "VoyageApiKey": "your-voyage-ai-api-key-here"
  }
}
```

**Required for semantic search**: Get your API key from [VoyageAI](https://www.voyageai.com/).

### File Upload & Staging

The system includes a staging area for file uploads that is separate from job workspaces:

- **Upload endpoint**: `POST /jobs/{jobId}/files`
- **Staging location**: `{JobsPath}/staging/` 
- **Hash-based naming**: Files are stored with hash suffixes to prevent conflicts
- **Placeholder support**: `{{filename}}` in prompts are replaced with uploaded filenames
- **Automatic cleanup**: Staging files are copied to job workspace and cleaned up after job execution

## Environment-Specific Configuration

### Development (`appsettings.Development.json`)

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information"  // Detailed logging for development
    }
  },
  "Auth": {
    "ShadowFilePath": "~/Dev/claude-server/claude-batch-server/claude-server-shadow",
    "PasswdFilePath": "~/Dev/claude-server/claude-batch-server/claude-server-passwd"
  },
  "Workspace": {
    "RepositoriesPath": "~/claude-code-server-workspace/repos",
    "JobsPath": "~/claude-code-server-workspace/jobs"
  }
}
```

### Production

- Use absolute paths for all file locations
- Set secure JWT key
- Configure proper log file paths
- Use `Information` or `Warning` log levels

## Security Considerations

1. **JWT Key**: Must be at least 32 characters and cryptographically secure
2. **File Paths**: Use absolute paths and ensure proper permissions
3. **API Keys**: Store VoyageAI key securely, consider environment variables
4. **Log Files**: Ensure log directory has appropriate write permissions

## Getting Started

1. Copy `appsettings.example.json` to `appsettings.json`
2. Update all paths to match your system
3. Set a secure JWT key
4. Configure user authentication files
5. Add your VoyageAI API key for semantic search
6. Adjust logging levels as needed

## Troubleshooting

- **500 Errors**: Check log files for detailed error messages
- **Authentication Issues**: Verify shadow/passwd file paths and permissions
- **CIDX Issues**: Ensure VoyageAI API key is valid and configured
- **File Permissions**: Ensure server can read/write to workspace directories

For more help, see the main README.md file.