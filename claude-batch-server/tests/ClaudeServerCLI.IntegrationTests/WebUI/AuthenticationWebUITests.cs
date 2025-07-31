using FluentAssertions;
using System.Net;
using System.Text;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace ClaudeServerCLI.IntegrationTests.WebUI;

[Collection("TestServer")]
public class AuthenticationWebUITests
{
    private readonly HttpClient _client;
    private readonly ITestOutputHelper _output;
    private readonly TestServerHarness _serverHarness;

    public AuthenticationWebUITests(TestServerHarness serverHarness, ITestOutputHelper output)
    {
        _serverHarness = serverHarness;
        _output = output;
        
        // Create HTTP client with SSL bypass for testing
        var handler = new HttpClientHandler()
        {
            ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
        };
        _client = new HttpClient(handler)
        {
            BaseAddress = new Uri(serverHarness.ServerUrl)
        };
    }

    [Fact]
    public async Task AuthLogin_WithPlaintextPassword_ShouldSucceed()
    {
        // Arrange
        var loginRequest = new
        {
            username = _serverHarness.TestUser,
            password = _serverHarness.TestPassword
        };

        // Act
        var response = await _client.PostAsync("/auth/login", 
            new StringContent(JsonSerializer.Serialize(loginRequest), Encoding.UTF8, "application/json"));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<JsonElement>(content);
        result.GetProperty("token").GetString().Should().NotBeNullOrEmpty();
        result.GetProperty("username").GetString().Should().Be("testuser");
    }

    [Fact]
    public async Task AuthLogin_WithYescryptHash_ShouldSucceed()
    {
        // Arrange - using pre-computed hash for TestPass123!
        var loginRequest = new
        {
            username = _serverHarness.TestUser,
            password = _serverHarness.TestPasswordHash
        };

        // Act
        var response = await _client.PostAsync("/auth/login", 
            new StringContent(JsonSerializer.Serialize(loginRequest), Encoding.UTF8, "application/json"));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<JsonElement>(content);
        result.GetProperty("token").GetString().Should().NotBeNullOrEmpty();
        result.GetProperty("username").GetString().Should().Be("testuser");
    }

    [Fact]
    public async Task AuthLogin_WithInvalidCredentials_ShouldReturnDetailedError()
    {
        // Arrange
        var loginRequest = new
        {
            username = _serverHarness.TestUser,
            password = "wrongpassword"
        };

        // Act
        var response = await _client.PostAsync("/auth/login", 
            new StringContent(JsonSerializer.Serialize(loginRequest), Encoding.UTF8, "application/json"));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<JsonElement>(content);
        
        // Should provide specific error message, not generic "Login failed"
        result.GetProperty("error").GetString().Should().NotBeNullOrEmpty();
        result.GetProperty("errorType").GetString().Should().Be("InvalidCredentials");
    }

    [Fact]
    public async Task AuthLogin_WithMalformedHash_ShouldReturnSpecificError()
    {
        // Arrange
        var loginRequest = new
        {
            username = _serverHarness.TestUser,
            password = "$y$invalid$hash",
            authType = "hash"
        };

        // Act
        var response = await _client.PostAsync("/auth/login", 
            new StringContent(JsonSerializer.Serialize(loginRequest), Encoding.UTF8, "application/json"));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<JsonElement>(content);
        
        result.GetProperty("error").GetString().Should().Contain("Invalid username or password");
        result.GetProperty("errorType").GetString().Should().Be("InvalidCredentials");
    }

    [Fact]
    public async Task AuthLogin_WithEmptyCredentials_ShouldReturnValidationError()
    {
        // Arrange
        var loginRequest = new
        {
            username = "",
            password = ""
        };

        // Act
        var response = await _client.PostAsync("/auth/login", 
            new StringContent(JsonSerializer.Serialize(loginRequest), Encoding.UTF8, "application/json"));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<JsonElement>(content);
        
        result.GetProperty("error").GetString().Should().Contain("Username and password are required");
        result.GetProperty("errorType").GetString().Should().Be("ValidationError");
    }

    [Fact]
    public async Task AuthLogin_WithNonexistentUser_ShouldReturnUserNotFoundError()
    {
        // Arrange
        var loginRequest = new
        {
            username = "nonexistentuser",
            password = "anypassword"
        };

        // Act
        var response = await _client.PostAsync("/auth/login", 
            new StringContent(JsonSerializer.Serialize(loginRequest), Encoding.UTF8, "application/json"));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<JsonElement>(content);
        
        result.GetProperty("error").GetString().Should().Contain("User not found");
        result.GetProperty("errorType").GetString().Should().Be("UserNotFound");
    }
}