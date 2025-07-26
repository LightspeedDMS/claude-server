using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ClaudeServerCLI.Models;
using ClaudeServerCLI.Serialization;

namespace ClaudeServerCLI.Services;

public class ConfigService : IConfigService
{
    private readonly ILogger<ConfigService> _logger;
    private readonly AuthenticationOptions _authOptions;
    private readonly JsonSerializerOptions _jsonOptions;
    private CliConfiguration? _cachedConfig;
    private string? _cachedConfigPath;

    public ConfigService(IOptions<AuthenticationOptions> authOptions, ILogger<ConfigService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _authOptions = authOptions.Value ?? throw new ArgumentNullException(nameof(authOptions));
        
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };
    }

    public async Task<CliConfiguration> LoadConfigAsync(string? configPath = null, CancellationToken cancellationToken = default)
    {
        configPath ??= _authOptions.ConfigPath;
        
        // Return cached config if same path and already loaded
        if (_cachedConfig != null && _cachedConfigPath == configPath)
        {
            return _cachedConfig;
        }

        try
        {
            if (!File.Exists(configPath))
            {
                _logger.LogDebug("Configuration file not found at {ConfigPath}, creating default config", configPath);
                var defaultConfig = CreateDefaultConfig();
                await SaveConfigAsync(defaultConfig, configPath, cancellationToken);
                return defaultConfig;
            }

            var json = await File.ReadAllTextAsync(configPath, cancellationToken);
            var config = JsonSerializer.Deserialize(json, CliJsonSerializerContext.Default.CliConfiguration);
            
            if (config == null)
            {
                _logger.LogWarning("Configuration file is empty or invalid at {ConfigPath}, creating default config", configPath);
                var defaultConfig = CreateDefaultConfig();
                await SaveConfigAsync(defaultConfig, configPath, cancellationToken);
                return defaultConfig;
            }

            // Ensure default profile exists
            if (!config.Profiles.ContainsKey(config.DefaultProfile))
            {
                config.Profiles[config.DefaultProfile] = new ProfileConfiguration();
                _logger.LogInformation("Added missing default profile: {DefaultProfile}", config.DefaultProfile);
            }

            _cachedConfig = config;
            _cachedConfigPath = configPath;
            
            _logger.LogDebug("Configuration loaded from {ConfigPath}", configPath);
            return config;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse configuration file at {ConfigPath}", configPath);
            throw new InvalidOperationException($"Configuration file at '{configPath}' is corrupted: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load configuration from {ConfigPath}", configPath);
            throw;
        }
    }

    public async Task SaveConfigAsync(CliConfiguration config, string? configPath = null, CancellationToken cancellationToken = default)
    {
        configPath ??= _authOptions.ConfigPath;

        try
        {
            await EnsureConfigDirectoryAsync(configPath);
            
            var json = JsonSerializer.Serialize(config, CliJsonSerializerContext.Default.CliConfiguration);
            await File.WriteAllTextAsync(configPath, json, cancellationToken);
            
            // Set restrictive file permissions (600 - owner read/write only)
            SetFilePermissions(configPath);
            
            // Update cache
            _cachedConfig = config;
            _cachedConfigPath = configPath;
            
            _logger.LogDebug("Configuration saved to {ConfigPath}", configPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save configuration to {ConfigPath}", configPath);
            throw;
        }
    }

    public async Task<ProfileConfiguration> GetProfileAsync(string profileName = "default", CancellationToken cancellationToken = default)
    {
        var config = await LoadConfigAsync(null, cancellationToken);
        
        if (config.Profiles.TryGetValue(profileName, out var profile))
        {
            return profile;
        }
        
        // Create new profile if it doesn't exist
        var newProfile = new ProfileConfiguration();
        config.Profiles[profileName] = newProfile;
        await SaveConfigAsync(config, null, cancellationToken);
        
        _logger.LogInformation("Created new profile: {ProfileName}", profileName);
        return newProfile;
    }

    public async Task SetProfileAsync(string profileName, ProfileConfiguration profile, CancellationToken cancellationToken = default)
    {
        var config = await LoadConfigAsync(null, cancellationToken);
        config.Profiles[profileName] = profile ?? throw new ArgumentNullException(nameof(profile));
        await SaveConfigAsync(config, null, cancellationToken);
        
        _logger.LogDebug("Profile updated: {ProfileName}", profileName);
    }

    public async Task<IEnumerable<string>> GetProfileNamesAsync(CancellationToken cancellationToken = default)
    {
        var config = await LoadConfigAsync(null, cancellationToken);
        return config.Profiles.Keys.ToList();
    }

    public async Task DeleteProfileAsync(string profileName, CancellationToken cancellationToken = default)
    {
        if (profileName == "default")
        {
            throw new InvalidOperationException("Cannot delete the default profile");
        }

        var config = await LoadConfigAsync(null, cancellationToken);
        
        if (config.Profiles.Remove(profileName))
        {
            // If we deleted the current default profile, reset to "default"
            if (config.DefaultProfile == profileName)
            {
                config.DefaultProfile = "default";
            }
            
            await SaveConfigAsync(config, null, cancellationToken);
            _logger.LogInformation("Profile deleted: {ProfileName}", profileName);
        }
        else
        {
            _logger.LogWarning("Attempted to delete non-existent profile: {ProfileName}", profileName);
        }
    }

    public string GetDefaultConfigPath()
    {
        return _authOptions.ConfigPath;
    }

    public async Task EnsureConfigDirectoryAsync(string configPath)
    {
        var directory = Path.GetDirectoryName(configPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
            _logger.LogDebug("Created configuration directory: {Directory}", directory);
        }
        
        await Task.CompletedTask;
    }

    private static CliConfiguration CreateDefaultConfig()
    {
        return new CliConfiguration
        {
            DefaultProfile = "default",
            Profiles = new Dictionary<string, ProfileConfiguration>
            {
                ["default"] = new ProfileConfiguration
                {
                    ServerUrl = "https://localhost:8443",
                    Timeout = 300,
                    AutoRefreshInterval = 2000
                }
            }
        };
    }

    private static void SetFilePermissions(string filePath)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                // Set file permissions to 600 (owner read/write only)
                var fileInfo = new FileInfo(filePath);
                if (fileInfo.Exists)
                {
                    // Use chmod to set permissions on Unix-like systems
                    var process = new System.Diagnostics.Process
                    {
                        StartInfo = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = "chmod",
                            Arguments = $"600 \"{filePath}\"",
                            UseShellExecute = false,
                            CreateNoWindow = true
                        }
                    };
                    process.Start();
                    process.WaitForExit();
                }
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // On Windows, we can't set Unix-style permissions, but we can limit access
                var fileInfo = new FileInfo(filePath);
                if (fileInfo.Exists)
                {
                    // Remove access for everyone except the current user
                    var fileSecurity = fileInfo.GetAccessControl();
                    fileSecurity.SetAccessRuleProtection(true, false); // Remove inheritance
                    
                    // Allow full control for the current user only
                    var currentUser = System.Security.Principal.WindowsIdentity.GetCurrent();
                    var accessRule = new System.Security.AccessControl.FileSystemAccessRule(
                        currentUser.User!, 
                        System.Security.AccessControl.FileSystemRights.FullControl, 
                        System.Security.AccessControl.AccessControlType.Allow);
                    fileSecurity.SetAccessRule(accessRule);
                    
                    fileInfo.SetAccessControl(fileSecurity);
                }
            }
        }
        catch (Exception)
        {
            // Silently ignore permission setting errors - the file is still encrypted
        }
    }
}