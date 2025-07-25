namespace ClaudeBatchServer.Core.Models;

public class DirectoryMetadata
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public DateTime Modified { get; set; }
    public bool HasSubdirectories { get; set; }
    public int FileCount { get; set; }
}