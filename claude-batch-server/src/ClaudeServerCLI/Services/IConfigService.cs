using ClaudeServerCLI.Models;

namespace ClaudeServerCLI.Services;

public interface IConfigService
{
    /// <summary>
    /// Loads the CLI configuration from file
    /// </summary>
    /// <param name="configPath">Path to configuration file</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>CLI configuration</returns>
    Task<CliConfiguration> LoadConfigAsync(string? configPath = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves the CLI configuration to file
    /// </summary>
    /// <param name="config">Configuration to save</param>
    /// <param name="configPath">Path to configuration file</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task SaveConfigAsync(CliConfiguration config, string? configPath = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the profile configuration
    /// </summary>
    /// <param name="profileName">Profile name</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Profile configuration</returns>
    Task<ProfileConfiguration> GetProfileAsync(string profileName = "default", CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the profile configuration
    /// </summary>
    /// <param name="profileName">Profile name</param>
    /// <param name="profile">Profile configuration</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task SetProfileAsync(string profileName, ProfileConfiguration profile, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all available profile names
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of profile names</returns>
    Task<IEnumerable<string>> GetProfileNamesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a profile
    /// </summary>
    /// <param name="profileName">Profile name to delete</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task DeleteProfileAsync(string profileName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the default configuration path
    /// </summary>
    /// <returns>Default configuration file path</returns>
    string GetDefaultConfigPath();

    /// <summary>
    /// Ensures the configuration directory exists
    /// </summary>
    /// <param name="configPath">Configuration file path</param>
    Task EnsureConfigDirectoryAsync(string configPath);
}