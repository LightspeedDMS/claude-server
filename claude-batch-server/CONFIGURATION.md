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
    "RetentionDays": "30",     // Long-term job retention policy (optional, defaults to 30 days)
    "UseNewWorkflow": "true"   // Enable enhanced job workflow
  }
}
```

**Job Queue Behavior**:

When the maximum number of concurrent jobs is reached, the system uses an intelligent queueing mechanism rather than rejecting new jobs:

- **Automatic Queueing**: Jobs exceeding the concurrent limit are automatically placed in a FIFO (First In, First Out) queue
- **No Job Rejection**: The system never rejects jobs - they wait in queue until resources become available
- **Queue Position Tracking**: Each queued job receives a position number that updates as jobs ahead complete
- **Resource Efficiency**: Queued jobs consume minimal memory and no system resources until execution begins
- **Persistent Queue**: The queue survives server restarts - queued jobs are restored from disk during initialization
- **Fair Processing**: Jobs execute in creation order, ensuring fair resource allocation

**Job Lifecycle**:
1. **Created** → Job is created but not yet started
2. **Queued** → Job is waiting for an available execution slot (position shown in status)
3. **Running** → Job is actively executing (consumes one concurrent slot)
4. **Completed/Failed** → Job finishes and releases its execution slot for the next queued job

**Timeout Behavior**:
- `TimeoutHours`: Maximum age for any job before automatic cleanup (applies to all states)
- Individual job timeout: Only starts counting when job reaches "Running" status
- Queued jobs can wait indefinitely without timing out (until a slot becomes available)

This approach ensures system stability while guaranteeing all submitted jobs will eventually execute, providing a better user experience than rejection-based systems.

**Job Expiration and Cleanup System**:

The system implements a comprehensive two-tier cleanup mechanism to manage disk space and system resources:

**1. Short-Term Expiration (`TimeoutHours`)**:
- **Trigger**: Every job queue processing cycle (every few seconds)
- **Target**: Jobs older than `TimeoutHours` (default: 24 hours) from creation time
- **Scope**: ALL jobs regardless of status (created, queued, running, completed, failed)
- **Actions Performed**:
  - Stop and remove CIDX Docker containers (if enabled for the job)
  - Remove entire CoW workspace directory via `btrfs subvolume delete` or `Directory.Delete`
  - Remove job from in-memory cache
  - **Note**: Job metadata files remain on disk for historical record

**2. Long-Term Retention Cleanup (`RetentionDays`)**:
- **Trigger**: Every 10 minutes during job queue processing
- **Target**: Finished jobs older than `RetentionDays` (default: 30 days) from completion time
- **Scope**: Only jobs with terminal status (`completed`, `failed`, `timeout`, `terminated`, `cancelled`)
- **Actions Performed**:
  - Permanently delete job metadata files from disk storage
  - Complete removal from job history and persistence layer

**Manual Job Deletion**:
When users delete jobs via `DELETE /jobs/{jobId}`:
- **Immediate Actions**:
  - Terminate running Claude Code processes (if job is running)
  - Stop and cleanup CIDX containers (prevents Docker container leaks)
  - Remove CoW workspace directory completely
  - Remove job from in-memory cache
  - Delete job metadata from persistent storage

**Workspace Cleanup Details**:
- **CoW Clones**: Removed via filesystem-specific commands:
  - Btrfs snapshots: `btrfs subvolume delete <cow-path>`
  - Regular directories: Recursive directory deletion
- **Staging Files**: Cleaned up after successful copy to CoW workspace
- **Uploaded Files**: Stored in CoW workspace, removed with the workspace
- **CIDX Containers**: Docker containers stopped via `cidx stop` command

**Resource Recovery**:
This cleanup system ensures:
- **Disk Space**: Automatic recovery of workspace storage
- **Memory**: Cleanup of in-memory job tracking
- **Docker Resources**: Prevention of orphaned CIDX containers
- **Process Resources**: Termination of running Claude Code processes

**Configuration Recommendations**:
- `TimeoutHours: "24"` - Suitable for most development workflows
- `RetentionDays: "30"` - Balances history retention with storage efficiency
- For high-volume environments, consider shorter retention periods
- For compliance requirements, consider longer retention periods

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