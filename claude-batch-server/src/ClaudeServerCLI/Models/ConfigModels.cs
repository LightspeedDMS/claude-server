namespace ClaudeServerCLI.Models;

public class CliConfiguration
{
    public Dictionary<string, ProfileConfiguration> Profiles { get; set; } = new();
    public string DefaultProfile { get; set; } = "default";
}

public class ProfileConfiguration
{
    public string ServerUrl { get; set; } = "https://localhost:8443";
    public int Timeout { get; set; } = 300;
    public int AutoRefreshInterval { get; set; } = 2000;
    public string? EncryptedToken { get; set; }
}

public class ApiClientOptions
{
    public string BaseUrl { get; set; } = "https://localhost:8443";
    public int TimeoutSeconds { get; set; } = 30;
    public int RetryCount { get; set; } = 3;
    public int RetryDelayMs { get; set; } = 1000;
    public bool EnableLogging { get; set; } = false;
}

public class AuthenticationOptions
{
    public string? Profile { get; set; } = "default";
    public string ConfigPath { get; set; } = GetDefaultConfigPath();
    public string TokenEnvironmentVariable { get; set; } = "CLAUDE_SERVER_TOKEN";
    
    private static string GetDefaultConfigPath()
    {
        var userPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(userPath, 
            Environment.OSVersion.Platform == PlatformID.Win32NT 
                ? @"AppData\Roaming\claude-server-cli\config.json"
                : ".config/claude-server-cli/config.json");
    }
}