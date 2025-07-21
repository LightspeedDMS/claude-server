namespace ClaudeBatchServer.Core.Models;

public class Job
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Username { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty;
    public string Repository { get; set; } = string.Empty;
    public List<string> Images { get; set; } = new();
    public JobStatus Status { get; set; } = JobStatus.Created;
    public string Output { get; set; } = string.Empty;
    public int? ExitCode { get; set; }
    public string CowPath { get; set; } = string.Empty;
    public int QueuePosition { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public JobOptions Options { get; set; } = new();
}

public enum JobStatus
{
    Created,
    Queued,
    Running,
    Completed,
    Failed,
    Timeout,
    Terminated
}

public class JobOptions
{
    public int TimeoutSeconds { get; set; } = 300;
    public bool AutoCleanup { get; set; } = true;
    public Dictionary<string, string> Environment { get; set; } = new();
}