namespace ClaudeBatchServer.Core.Models;

public class Job
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Username { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Repository { get; set; } = string.Empty;
    public List<string> UploadedFiles { get; set; } = new();
    public JobStatus Status { get; set; } = JobStatus.Created;
    public string Output { get; set; } = string.Empty;
    public int? ExitCode { get; set; }
    public string CowPath { get; set; } = string.Empty;
    public int QueuePosition { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string CancelReason { get; set; } = string.Empty;
    public JobOptions Options { get; set; } = new();
    public string GitPullStatus { get; set; } = "not_started"; // Status of git pull on source repository before CoW
    public string GitStatus { get; set; } = "not_checked"; // Status of git operations in CoW workspace
    public string CidxStatus { get; set; } = "not_started";
    public int? ClaudeProcessId { get; set; }
}

public enum JobStatus
{
    Created,
    Queued,
    GitPulling,        // Git pull on source repository
    GitFailed,         // Git pull failed on source repository
    CidxIndexing,      // CIDX indexing on source repository
    CidxReady,         // CIDX ready in CoW workspace
    Running,
    Completed,
    Failed,
    Timeout,
    Terminated,
    Cancelling,
    Cancelled
}

public class JobOptions
{
    public int TimeoutSeconds { get; set; } = 300;
    public bool AutoCleanup { get; set; } = true;
    public bool GitAware { get; set; } = true;
    public bool CidxAware { get; set; } = true;
    public Dictionary<string, string> Environment { get; set; } = new();
}