namespace ClaudeBatchServer.Core.DTOs;

public class RepositoryResponse
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty; // "git" or "folder"
    public long Size { get; set; }
    public DateTime LastModified { get; set; }
    
    // Git-specific properties (null for regular folders)
    public string? GitUrl { get; set; }
    public string? Description { get; set; }
    public DateTime? RegisteredAt { get; set; }
    public DateTime? LastPull { get; set; }
    public string? LastPullStatus { get; set; } // "success", "failed", "never"
    public string? RemoteUrl { get; set; }
    public string? CurrentBranch { get; set; }
    public string? CommitHash { get; set; }
    public string? CommitMessage { get; set; }
    public string? CommitAuthor { get; set; }
    public DateTime? CommitDate { get; set; }
    public bool? HasUncommittedChanges { get; set; }
    public AheadBehindStatus? AheadBehind { get; set; }
    public string? CloneStatus { get; set; } = "unknown";
    public bool? CidxAware { get; set; }  // Whether repository is configured for cidx indexing
}

public class AheadBehindStatus
{
    public int Ahead { get; set; }
    public int Behind { get; set; }
}

public class RegisterRepositoryRequest
{
    public string Name { get; set; } = string.Empty;
    public string GitUrl { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool CidxAware { get; set; } = true; // Enable cidx indexing during registration
}

public class RegisterRepositoryResponse
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string GitUrl { get; set; } = string.Empty;
    public DateTime RegisteredAt { get; set; }
    public string CloneStatus { get; set; } = "cloning";
    public bool CidxAware { get; set; }
}

public class UnregisterRepositoryResponse
{
    public bool Success { get; set; } = true;
    public bool Removed { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class FileInfoResponse
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public long Size { get; set; }
    public DateTime Modified { get; set; }
    public bool IsDirectory { get; set; }
}

public class FileContentResponse
{
    public string FileName { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string Encoding { get; set; } = "utf8";
    public long Size { get; set; }
    public string? MimeType { get; set; }
}

public class DirectoryInfoResponse
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public DateTime Modified { get; set; }
    public bool HasSubdirectories { get; set; }
    public int FileCount { get; set; }
}