using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ClaudeServerCLI.Services;

namespace ClaudeServerCLI.IntegrationTests;

[Collection("TestServer")]
public class CliIntegrationTests : IClassFixture<TestServerHarness>, IDisposable
{
    private readonly TestServerHarness _serverHarness;
    private readonly CLITestHelper _cliHelper;
    private readonly IServiceProvider _serviceProvider;

    public CliIntegrationTests(TestServerHarness serverHarness)
    {
        _serverHarness = serverHarness;
        _cliHelper = new CLITestHelper(_serverHarness);

        // Create service collection for testing
        var services = new ServiceCollection();
        
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ApiClient:BaseUrl"] = _serverHarness.ServerUrl,
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
        var apiClient = _cliHelper.CreateApiClient();

        // Act
        var isHealthy = await apiClient.IsServerHealthyAsync();

        // Assert
        isHealthy.Should().BeTrue("The test server should be healthy and responding");
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

        // Auth service should indicate not authenticated initially
        var isAuthenticated = await authService.IsAuthenticatedAsync("default");
        isAuthenticated.Should().BeFalse("Should not be authenticated initially");
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
    public void ApiClient_Configuration_ShouldRespectSettings()
    {
        // Arrange & Act
        var apiClient = _serviceProvider.GetRequiredService<IApiClient>();

        // Assert - Should not throw when configuring
        apiClient.SetTimeout(TimeSpan.FromMinutes(2));
        apiClient.SetBaseUrl("https://custom.example.com");
        apiClient.SetAuthToken("test-token");
        apiClient.ClearAuthToken();

        // Should be able to change configuration without errors
        apiClient.SetBaseUrl(_serverHarness.ServerUrl);
    }

    public void Dispose()
    {
        _serviceProvider?.GetService<IServiceProvider>()?.GetService<IDisposable>()?.Dispose();
    }
}

