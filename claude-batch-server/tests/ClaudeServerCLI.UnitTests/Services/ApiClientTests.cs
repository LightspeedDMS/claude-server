using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using Xunit;
using System.Net;
using System.Text;
using System.Text.Json;
using ClaudeBatchServer.Core.DTOs;
using ClaudeServerCLI.Models;
using ClaudeServerCLI.Services;

namespace ClaudeServerCLI.UnitTests.Services;

public class ApiClientTests : IDisposable
{
    private readonly Mock<HttpMessageHandler> _mockHandler;
    private readonly HttpClient _httpClient;
    private readonly Mock<ILogger<ApiClient>> _mockLogger;
    private readonly ApiClient _apiClient;
    private readonly JsonSerializerOptions _jsonOptions;

    public ApiClientTests()
    {
        _mockHandler = new Mock<HttpMessageHandler>();
        _httpClient = new HttpClient(_mockHandler.Object)
        {
            BaseAddress = new Uri("https://localhost:8443")
        };
        
        _mockLogger = new Mock<ILogger<ApiClient>>();
        
        var options = Options.Create(new ApiClientOptions
        {
            BaseUrl = "https://localhost:8443",
            TimeoutSeconds = 30,
            RetryCount = 2,
            RetryDelayMs = 100
        });

        _apiClient = new ApiClient(_httpClient, options, _mockLogger.Object);
        
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    [Fact]
    public async Task LoginAsync_WithValidCredentials_ShouldReturnLoginResponse()
    {
        // Arrange
        var request = new LoginRequest { Username = "testuser", Password = "testpass" };
        var expectedResponse = new LoginResponse
        {
            Token = "jwt_token_here",
            Username = "testuser",
            Expires = DateTime.UtcNow.AddHours(1)
        };

        SetupHttpResponse(HttpStatusCode.OK, expectedResponse);

        // Act
        var result = await _apiClient.LoginAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.Token.Should().Be(expectedResponse.Token);
        result.Username.Should().Be(expectedResponse.Username);
    }

    [Fact]
    public async Task LoginAsync_WithInvalidCredentials_ShouldThrowException()
    {
        // Arrange
        var request = new LoginRequest { Username = "testuser", Password = "wrongpass" };
        
        SetupHttpResponse(HttpStatusCode.Unauthorized, new { error = "Invalid credentials" });

        // Act & Assert
        await _apiClient.Invoking(x => x.LoginAsync(request))
            .Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("Authentication failed or token expired");
    }

    [Fact]
    public async Task LogoutAsync_ShouldReturnLogoutResponse()
    {
        // Arrange
        var expectedResponse = new LogoutResponse { Success = true };
        SetupHttpResponse(HttpStatusCode.OK, expectedResponse);

        // Act
        var result = await _apiClient.LogoutAsync();

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task GetRepositoriesAsync_ShouldReturnRepositoryList()
    {
        // Arrange
        _apiClient.SetAuthToken("test-token"); // Set auth token for authenticated endpoints
        
        var expectedRepos = new[]
        {
            new RepositoryResponse
            {
                Name = "repo1",
                Path = "/path/to/repo1",
                Type = "git",
                Size = 1024,
                LastModified = DateTime.UtcNow
            },
            new RepositoryResponse
            {
                Name = "repo2",
                Path = "/path/to/repo2",
                Type = "folder",
                Size = 2048,
                LastModified = DateTime.UtcNow.AddDays(-1)
            }
        };

        SetupHttpResponse(HttpStatusCode.OK, expectedRepos);

        // Act
        var result = await _apiClient.GetRepositoriesAsync();

        // Assert
        result.Should().HaveCount(2);
        result.First().Name.Should().Be("repo1");
        result.Last().Name.Should().Be("repo2");
    }

    [Fact]
    public async Task CreateJobAsync_ShouldReturnCreateJobResponse()
    {
        // Arrange
        _apiClient.SetAuthToken("test-token");
        
        var request = new CreateJobRequest
        {
            Prompt = "Test prompt",
            Repository = "test-repo",
            Images = new List<string>(),
            Options = new JobOptionsDto()
        };

        var expectedResponse = new CreateJobResponse
        {
            JobId = Guid.NewGuid(),
            Status = "created",
            User = "testuser",
            CowPath = "/path/to/cow"
        };

        SetupHttpResponse(HttpStatusCode.OK, expectedResponse);

        // Act
        var result = await _apiClient.CreateJobAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.JobId.Should().Be(expectedResponse.JobId);
        result.Status.Should().Be(expectedResponse.Status);
    }

    [Fact]
    public async Task GetJobAsync_WithValidJobId_ShouldReturnJobStatus()
    {
        // Arrange
        _apiClient.SetAuthToken("test-token");
        
        var jobId = Guid.NewGuid().ToString();
        var expectedResponse = new JobStatusResponse
        {
            JobId = Guid.Parse(jobId),
            Status = "running",
            Output = "Job output here",
            CowPath = "/path/to/cow",
            CreatedAt = DateTime.UtcNow,
            StartedAt = DateTime.UtcNow.AddMinutes(-5)
        };

        SetupHttpResponse(HttpStatusCode.OK, expectedResponse);

        // Act
        var result = await _apiClient.GetJobAsync(jobId);

        // Assert
        result.Should().NotBeNull();
        result.JobId.Should().Be(Guid.Parse(jobId));
        result.Status.Should().Be("running");
        result.Output.Should().Be("Job output here");
    }

    [Fact]
    public async Task GetJobAsync_WithInvalidJobId_ShouldThrowException()
    {
        // Arrange
        _apiClient.SetAuthToken("test-token");
        
        var jobId = Guid.NewGuid().ToString();
        SetupHttpResponse(HttpStatusCode.NotFound, new { error = "Job not found" });

        // Act & Assert
        await _apiClient.Invoking(x => x.GetJobAsync(jobId))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Resource not found");
    }

    [Fact]
    public async Task StartJobAsync_ShouldReturnStartJobResponse()
    {
        // Arrange
        _apiClient.SetAuthToken("test-token");
        
        var jobId = Guid.NewGuid().ToString();
        var expectedResponse = new StartJobResponse
        {
            JobId = Guid.Parse(jobId),
            Status = "queued",
            QueuePosition = 1
        };

        SetupHttpResponse(HttpStatusCode.OK, expectedResponse);

        // Act
        var result = await _apiClient.StartJobAsync(jobId);

        // Assert
        result.Should().NotBeNull();
        result.JobId.Should().Be(Guid.Parse(jobId));
        result.Status.Should().Be("queued");
        result.QueuePosition.Should().Be(1);
    }

    [Fact]
    public async Task CancelJobAsync_ShouldReturnCancelJobResponse()
    {
        // Arrange
        _apiClient.SetAuthToken("test-token");
        
        var jobId = Guid.NewGuid().ToString();
        var expectedResponse = new CancelJobResponse
        {
            JobId = Guid.Parse(jobId),
            Status = "cancelled",
            Success = true,
            Message = "Job cancelled successfully",
            CancelledAt = DateTime.UtcNow
        };

        SetupHttpResponse(HttpStatusCode.OK, expectedResponse);

        // Act
        var result = await _apiClient.CancelJobAsync(jobId);

        // Assert
        result.Should().NotBeNull();
        result.JobId.Should().Be(Guid.Parse(jobId));
        result.Status.Should().Be("cancelled");
        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task IsServerHealthyAsync_WhenServerHealthy_ShouldReturnTrue()
    {
        // Arrange
        SetupHttpResponse(HttpStatusCode.OK, new { status = "healthy" });

        // Act
        var result = await _apiClient.IsServerHealthyAsync();

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsServerHealthyAsync_WhenServerUnhealthy_ShouldReturnFalse()
    {
        // Arrange
        SetupHttpResponse(HttpStatusCode.InternalServerError, new { error = "Server error" });

        // Act
        var result = await _apiClient.IsServerHealthyAsync();

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void SetAuthToken_ShouldConfigureAuthorizationHeader()
    {
        // Arrange
        var token = "test_jwt_token";

        // Act
        _apiClient.SetAuthToken(token);

        // Assert
        _httpClient.DefaultRequestHeaders.Authorization?.Scheme.Should().Be("Bearer");
        _httpClient.DefaultRequestHeaders.Authorization?.Parameter.Should().Be(token);
    }

    [Fact]
    public void ClearAuthToken_ShouldRemoveAuthorizationHeader()
    {
        // Arrange
        _apiClient.SetAuthToken("test_token");

        // Act
        _apiClient.ClearAuthToken();

        // Assert
        _httpClient.DefaultRequestHeaders.Authorization.Should().BeNull();
    }

    [Fact]
    public void SetBaseUrl_ShouldUpdateHttpClientBaseAddress()
    {
        // Arrange
        var newUrl = "https://api.example.com";

        // Act
        _apiClient.SetBaseUrl(newUrl);

        // Assert
        _httpClient.BaseAddress.Should().Be(new Uri(newUrl));
    }

    [Fact]
    public void SetTimeout_ShouldUpdateHttpClientTimeout()
    {
        // Arrange
        var timeout = TimeSpan.FromMinutes(5);

        // Act
        _apiClient.SetTimeout(timeout);

        // Assert
        _httpClient.Timeout.Should().Be(timeout);
    }

    private void SetupHttpResponse<T>(HttpStatusCode statusCode, T content)
    {
        var json = JsonSerializer.Serialize(content, _jsonOptions);
        var response = new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        _mockHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);
    }

    public void Dispose()
    {
        _apiClient?.Dispose();
        _httpClient?.Dispose();
    }
}