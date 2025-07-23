using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;
using ClaudeServerCLI.Models;
using ClaudeServerCLI.Services;

namespace ClaudeServerCLI.UnitTests.Services;

public class ConfigServiceTests : IDisposable
{
    private readonly ConfigService _configService;
    private readonly string _testConfigPath;
    private readonly ILogger<ConfigService> _logger;

    public ConfigServiceTests()
    {
        _logger = new TestLogger<ConfigService>();
        var options = Options.Create(new AuthenticationOptions
        {
            ConfigPath = Path.GetTempFileName() + ".json"
        });
        
        _testConfigPath = options.Value.ConfigPath;
        _configService = new ConfigService(options, _logger);
        
        // Clean up any existing test file
        if (File.Exists(_testConfigPath))
        {
            File.Delete(_testConfigPath);
        }
    }

    [Fact]
    public async Task LoadConfigAsync_WhenFileDoesNotExist_ShouldCreateDefaultConfig()
    {
        // Act
        var config = await _configService.LoadConfigAsync(_testConfigPath);

        // Assert
        config.Should().NotBeNull();
        config.DefaultProfile.Should().Be("default");
        config.Profiles.Should().ContainKey("default");
        config.Profiles["default"].ServerUrl.Should().Be("https://localhost:8443");
        config.Profiles["default"].Timeout.Should().Be(300);
        
        // File should be created
        File.Exists(_testConfigPath).Should().BeTrue();
    }

    [Fact]
    public async Task LoadConfigAsync_WhenFileExists_ShouldLoadCorrectly()
    {
        // Arrange
        var expectedConfig = new CliConfiguration
        {
            DefaultProfile = "test",
            Profiles = new Dictionary<string, ProfileConfiguration>
            {
                ["test"] = new ProfileConfiguration
                {
                    ServerUrl = "https://test.example.com",
                    Timeout = 600,
                    AutoRefreshInterval = 5000
                }
            }
        };
        
        await _configService.SaveConfigAsync(expectedConfig, _testConfigPath);

        // Act
        var config = await _configService.LoadConfigAsync(_testConfigPath);

        // Assert
        config.Should().NotBeNull();
        config.DefaultProfile.Should().Be("test");
        config.Profiles.Should().ContainKey("test");
        config.Profiles["test"].ServerUrl.Should().Be("https://test.example.com");
        config.Profiles["test"].Timeout.Should().Be(600);
        config.Profiles["test"].AutoRefreshInterval.Should().Be(5000);
    }

    [Fact]
    public async Task SaveConfigAsync_ShouldCreateDirectoryAndFile()
    {
        // Arrange
        var config = new CliConfiguration
        {
            DefaultProfile = "default",
            Profiles = new Dictionary<string, ProfileConfiguration>
            {
                ["default"] = new ProfileConfiguration()
            }
        };

        // Act
        await _configService.SaveConfigAsync(config, _testConfigPath);

        // Assert
        File.Exists(_testConfigPath).Should().BeTrue();
        var savedContent = await File.ReadAllTextAsync(_testConfigPath);
        savedContent.Should().Contain("default");
        savedContent.Should().Contain("https://localhost:8443");
    }

    [Fact]
    public async Task GetProfileAsync_WhenProfileExists_ShouldReturnProfile()
    {
        // Arrange
        var config = new CliConfiguration
        {
            Profiles = new Dictionary<string, ProfileConfiguration>
            {
                ["test"] = new ProfileConfiguration
                {
                    ServerUrl = "https://test.example.com",
                    Timeout = 120
                }
            }
        };
        await _configService.SaveConfigAsync(config, _testConfigPath);

        // Act
        var profile = await _configService.GetProfileAsync("test");

        // Assert
        profile.Should().NotBeNull();
        profile.ServerUrl.Should().Be("https://test.example.com");
        profile.Timeout.Should().Be(120);
    }

    [Fact]
    public async Task GetProfileAsync_WhenProfileDoesNotExist_ShouldCreateAndReturnNewProfile()
    {
        // Arrange
        await _configService.LoadConfigAsync(_testConfigPath); // Create default config

        // Act
        var profile = await _configService.GetProfileAsync("newprofile");

        // Assert
        profile.Should().NotBeNull();
        profile.ServerUrl.Should().Be("https://localhost:8443");
        profile.Timeout.Should().Be(300);
        
        // Should be persisted
        var config = await _configService.LoadConfigAsync(_testConfigPath);
        config.Profiles.Should().ContainKey("newprofile");
    }

    [Fact]
    public async Task SetProfileAsync_ShouldUpdateAndPersistProfile()
    {
        // Arrange
        await _configService.LoadConfigAsync(_testConfigPath); // Create default config
        var newProfile = new ProfileConfiguration
        {
            ServerUrl = "https://custom.example.com",
            Timeout = 900,
            AutoRefreshInterval = 1000,
            EncryptedToken = "encrypted_token_here"
        };

        // Act
        await _configService.SetProfileAsync("custom", newProfile);

        // Assert
        var retrievedProfile = await _configService.GetProfileAsync("custom");
        retrievedProfile.ServerUrl.Should().Be("https://custom.example.com");
        retrievedProfile.Timeout.Should().Be(900);
        retrievedProfile.AutoRefreshInterval.Should().Be(1000);
        retrievedProfile.EncryptedToken.Should().Be("encrypted_token_here");
    }

    [Fact]
    public async Task GetProfileNamesAsync_ShouldReturnAllProfiles()
    {
        // Arrange
        var config = new CliConfiguration
        {
            Profiles = new Dictionary<string, ProfileConfiguration>
            {
                ["default"] = new ProfileConfiguration(),
                ["prod"] = new ProfileConfiguration(),
                ["dev"] = new ProfileConfiguration()
            }
        };
        await _configService.SaveConfigAsync(config, _testConfigPath);

        // Act
        var profileNames = await _configService.GetProfileNamesAsync();

        // Assert
        profileNames.Should().Contain("default");
        profileNames.Should().Contain("prod");
        profileNames.Should().Contain("dev");
        profileNames.Should().HaveCount(3);
    }

    [Fact]
    public async Task DeleteProfileAsync_ShouldRemoveProfileFromConfig()
    {
        // Arrange
        var config = new CliConfiguration
        {
            DefaultProfile = "default",
            Profiles = new Dictionary<string, ProfileConfiguration>
            {
                ["default"] = new ProfileConfiguration(),
                ["tobedeleted"] = new ProfileConfiguration()
            }
        };
        await _configService.SaveConfigAsync(config, _testConfigPath);

        // Act
        await _configService.DeleteProfileAsync("tobedeleted");

        // Assert
        var profileNames = await _configService.GetProfileNamesAsync();
        profileNames.Should().NotContain("tobedeleted");
        profileNames.Should().Contain("default");
    }

    [Fact]
    public async Task DeleteProfileAsync_WhenDeletingDefault_ShouldThrowException()
    {
        // Arrange
        await _configService.LoadConfigAsync(_testConfigPath);

        // Act & Assert
        await _configService.Invoking(x => x.DeleteProfileAsync("default"))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Cannot delete the default profile");
    }

    [Fact]
    public void GetDefaultConfigPath_ShouldReturnValidPath()
    {
        // Act
        var path = _configService.GetDefaultConfigPath();

        // Assert
        path.Should().NotBeNullOrEmpty();
        path.Should().EndWith(".json");
    }

    public void Dispose()
    {
        if (File.Exists(_testConfigPath))
        {
            File.Delete(_testConfigPath);
        }
    }
}

// Test logger implementation
public class TestLogger<T> : ILogger<T>
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
}