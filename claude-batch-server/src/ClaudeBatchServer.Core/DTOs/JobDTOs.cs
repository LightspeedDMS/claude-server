namespace ClaudeBatchServer.Core.DTOs;

public class CreateJobRequest
{
    public string Prompt { get; set; } = string.Empty;
    public string Repository { get; set; } = string.Empty;
    public List<string> Images { get; set; } = new();
    public JobOptionsDto Options { get; set; } = new();
}

public class CreateJobResponse
{
    public Guid JobId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string User { get; set; } = string.Empty;
    public string CowPath { get; set; } = string.Empty;
}

public class StartJobResponse
{
    public Guid JobId { get; set; }
    public string Status { get; set; } = string.Empty;
    public int QueuePosition { get; set; }
}

public class JobStatusResponse
{
    public Guid JobId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Output { get; set; } = string.Empty;
    public int? ExitCode { get; set; }
    public string CowPath { get; set; } = string.Empty;
    public int QueuePosition { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

public class JobListResponse
{
    public Guid JobId { get; set; }
    public string User { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime Started { get; set; }
    public string Repository { get; set; } = string.Empty;
}

public class DeleteJobResponse
{
    public bool Success { get; set; } = true;
    public bool Terminated { get; set; }
    public bool CowRemoved { get; set; }
}

public class JobOptionsDto
{
    public int Timeout { get; set; } = 300;
}

public class ImageUploadResponse
{
    public string Filename { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
}