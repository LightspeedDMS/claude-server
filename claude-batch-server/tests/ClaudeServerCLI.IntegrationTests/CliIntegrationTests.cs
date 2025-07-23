using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ClaudeServerCLI.Services;

namespace ClaudeServerCLI.IntegrationTests;

public class CliIntegrationTests : IClassFixture<ApiServerFixture>
{
    private readonly ApiServerFixture _serverFixture;
    private readonly IServiceProvider _serviceProvider;

    public CliIntegrationTests(ApiServerFixture serverFixture)
    {
        _serverFixture = serverFixture;

        // Create service collection for testing
        var services = new ServiceCollection();
        
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ApiClient:BaseUrl"] = _serverFixture.ServerUrl,
                ["ApiClient:TimeoutSeconds"] = "10",
                ["ApiClient:RetryCount"] = "2",
                ["ApiClient:RetryDelayMs"] = "100",
                ["Authentication:ConfigPath"] = Path.GetTempFileName() + ".json"
            })
            .Build();

        services.ConfigureServices(configuration);
        
        _serviceProvider = services.BuildServiceProvider();
    }

    [Fact]
    public async Task ApiClient_ServerHealthCheck_ShouldReturnTrue()
    {
        // Arrange
        var apiClient = _serviceProvider.GetRequiredService<IApiClient>();

        // Act
        var isHealthy = await apiClient.IsServerHealthyAsync();

        // Assert
        isHealthy.Should().BeTrue();
    }

    [Fact]
    public async Task AuthService_ConfigService_Integration_ShouldWorkTogether()
    {
        // Arrange
        var configService = _serviceProvider.GetRequiredService<IConfigService>();
        var authService = _serviceProvider.GetRequiredService<IAuthService>();

        // Act & Assert - Should not throw and should return valid configuration
        var profiles = await configService.GetProfileNamesAsync();
        profiles.Should().Contain("default");

        var profile = await configService.GetProfileAsync("default");
        profile.Should().NotBeNull();
        profile.ServerUrl.Should().NotBeNullOrEmpty();

        // Auth service should indicate not authenticated
        var isAuthenticated = await authService.IsAuthenticatedAsync("default");
        isAuthenticated.Should().BeFalse();
    }

    [Fact]
    public async Task ConfigService_MultiProfile_ShouldWorkCorrectly()
    {
        // Arrange
        var configService = _serviceProvider.GetRequiredService<IConfigService>();

        // Act - Create and manage multiple profiles
        var testProfile = new ClaudeServerCLI.Models.ProfileConfiguration
        {
            ServerUrl = "https://test.example.com",
            Timeout = 120,
            AutoRefreshInterval = 3000
        };

        await configService.SetProfileAsync("test", testProfile);

        // Assert
        var profiles = await configService.GetProfileNamesAsync();
        profiles.Should().Contain("test");
        profiles.Should().Contain("default");

        var retrievedProfile = await configService.GetProfileAsync("test");
        retrievedProfile.ServerUrl.Should().Be("https://test.example.com");
        retrievedProfile.Timeout.Should().Be(120);
        retrievedProfile.AutoRefreshInterval.Should().Be(3000);

        // Clean up
        await configService.DeleteProfileAsync("test");
        var profilesAfterDelete = await configService.GetProfileNamesAsync();
        profilesAfterDelete.Should().NotContain("test");
    }

    [Fact]
    public async Task ApiClient_Configuration_ShouldRespectSettings()
    {
        // Arrange & Act
        var apiClient = _serviceProvider.GetRequiredService<IApiClient>();

        // Assert - Should not throw when configuring
        apiClient.SetTimeout(TimeSpan.FromMinutes(2));
        apiClient.SetBaseUrl("https://custom.example.com");
        apiClient.SetAuthToken("test-token");
        apiClient.ClearAuthToken();

        // Should be able to change configuration without errors
        apiClient.SetBaseUrl(_serverFixture.ServerUrl);
    }

    public void Dispose()
    {
        _serviceProvider?.GetService<IServiceProvider>()?.GetService<IDisposable>()?.Dispose();
    }
}

/// <summary>
/// Test fixture that represents an API server for integration testing.
/// In a real scenario, this would start/stop the actual API server.
/// For now, it provides configuration for connecting to an existing server.
/// </summary>
public class ApiServerFixture : IDisposable
{
    public string ServerUrl { get; } = "https://localhost:8443";

    public ApiServerFixture()
    {
        // In a real integration test, you might:
        // 1. Start the API server process
        // 2. Wait for it to be ready
        // 3. Configure test data
        
        // For now, we assume the server is running externally
    }

    public void Dispose()
    {
        // In a real integration test, you would:
        // 1. Stop the API server process
        // 2. Clean up test data
        // 3. Remove temporary files
    }
}