using ClaudeBatchServer.Core.DTOs;

namespace ClaudeServerCLI.Models;

public class CliLoginRequest : LoginRequest
{
    public bool IsHashedPassword { get; set; } = false;
}

public class CliJobFilter
{
    public string? Repository { get; set; }
    public string? Status { get; set; }
    public string? User { get; set; }
    public DateTime? CreatedAfter { get; set; }
    public DateTime? CreatedBefore { get; set; }
    public int? Limit { get; set; }
    public int? Skip { get; set; }
}

public class FileUpload
{
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public byte[] Content { get; set; } = Array.Empty<byte>();
    public string ContentType { get; set; } = "application/octet-stream";
}

public class JobInfo
{
    public Guid JobId { get; set; }
    public string User { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Repository { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string CowPath { get; set; } = string.Empty;
    public int QueuePosition { get; set; }
}

public class RepositoryInfo
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public long Size { get; set; }
    public DateTime LastModified { get; set; }
    
    // Git-specific properties
    public string? GitUrl { get; set; }
    public string? Description { get; set; }
    public DateTime? RegisteredAt { get; set; }
    public DateTime? LastPull { get; set; }
    public string? LastPullStatus { get; set; }
    public string? RemoteUrl { get; set; }
    public string? CurrentBranch { get; set; }
    public string? CommitHash { get; set; }
    public string? CommitMessage { get; set; }
    public string? CommitAuthor { get; set; }
    public DateTime? CommitDate { get; set; }
    public bool? HasUncommittedChanges { get; set; }
}

public class JobFile
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public long Size { get; set; }
    public DateTime Modified { get; set; }
}