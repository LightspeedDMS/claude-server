namespace ClaudeBatchServer.Core.Models;

public class Repository
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;
    public RepositorySettings Settings { get; set; } = new();
}

public class RepositorySettings
{
    public List<string> PreCommands { get; set; } = new();
    public Dictionary<string, string> ClaudeConfig { get; set; } = new();
    public bool AllowDirectAccess { get; set; } = true;
}