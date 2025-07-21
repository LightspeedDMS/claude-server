namespace ClaudeBatchServer.Core.DTOs;

public class RepositoryResponse
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public class FileInfoResponse
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public long Size { get; set; }
    public DateTime Modified { get; set; }
}

public class FileContentResponse
{
    public string Content { get; set; } = string.Empty;
    public string Encoding { get; set; } = "utf8";
}