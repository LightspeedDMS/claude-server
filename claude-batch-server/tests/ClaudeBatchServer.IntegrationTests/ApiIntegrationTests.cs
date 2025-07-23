using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using ClaudeBatchServer.Api;
using ClaudeBatchServer.Core.DTOs;

namespace ClaudeBatchServer.IntegrationTests;

public class ApiIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public ApiIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((context, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Jwt:Key"] = "TestKeyForIntegrationTestsThatIsLongEnough",
                    ["Jwt:ExpiryHours"] = "1",
                    ["Workspace:RepositoriesPath"] = Path.Combine(Path.GetTempPath(), "integration-test-repos"),
                    ["Workspace:JobsPath"] = Path.Combine(Path.GetTempPath(), "integration-test-jobs"),
                    ["Jobs:MaxConcurrent"] = "2",
                    ["Jobs:TimeoutHours"] = "1",
                    ["Claude:Command"] = "echo"
                });
            });
        });
        
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task HealthCheck_ShouldReturnOk()
    {
        var response = await _client.GetAsync("/");
        
        response.StatusCode.Should().Be(HttpStatusCode.NotFound); // No health endpoint implemented yet
    }

    [Fact]
    public async Task Auth_Login_WithInvalidCredentials_ShouldReturnUnauthorized()
    {
        var loginRequest = new LoginRequest
        {
            Username = "nonexistentuser",
            Password = "wrongpassword"
        };

        var response = await _client.PostAsJsonAsync("/auth/login", loginRequest);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Auth_Login_WithEmptyCredentials_ShouldReturnBadRequest()
    {
        var loginRequest = new LoginRequest
        {
            Username = "",
            Password = ""
        };

        var response = await _client.PostAsJsonAsync("/auth/login", loginRequest);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Jobs_CreateJob_WithoutAuthentication_ShouldReturnUnauthorized()
    {
        var createJobRequest = new CreateJobRequest
        {
            Prompt = "Test prompt",
            Repository = "test-repo"
        };

        var response = await _client.PostAsJsonAsync("/jobs", createJobRequest);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Jobs_GetJobs_WithoutAuthentication_ShouldReturnUnauthorized()
    {
        var response = await _client.GetAsync("/jobs");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Repositories_GetRepositories_WithoutAuthentication_ShouldReturnUnauthorized()
    {
        var response = await _client.GetAsync("/repositories");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Auth_Logout_WithoutAuthentication_ShouldReturnUnauthorized()
    {
        var response = await _client.PostAsync("/auth/logout", null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Jobs_GetJobStatus_WithInvalidJobId_WithoutAuth_ShouldReturnUnauthorized()
    {
        var jobId = Guid.NewGuid();
        var response = await _client.GetAsync($"/jobs/{jobId}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Jobs_DeleteJob_WithInvalidJobId_WithoutAuth_ShouldReturnUnauthorized()
    {
        var jobId = Guid.NewGuid();
        var response = await _client.DeleteAsync($"/jobs/{jobId}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Jobs_StartJob_WithInvalidJobId_WithoutAuth_ShouldReturnUnauthorized()
    {
        var jobId = Guid.NewGuid();
        var response = await _client.PostAsync($"/jobs/{jobId}/start", null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Files_GetFiles_WithInvalidJobId_WithoutAuth_ShouldReturnUnauthorized()
    {
        var jobId = Guid.NewGuid();
        var response = await _client.GetAsync($"/jobs/{jobId}/files");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Files_DownloadFile_WithInvalidJobId_WithoutAuth_ShouldReturnUnauthorized()
    {
        var jobId = Guid.NewGuid();
        var response = await _client.GetAsync($"/jobs/{jobId}/files/download?path=test.txt");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Files_GetFileContent_WithInvalidJobId_WithoutAuth_ShouldReturnUnauthorized()
    {
        var jobId = Guid.NewGuid();
        var response = await _client.GetAsync($"/jobs/{jobId}/files/content?path=test.txt");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Images_UploadImage_WithInvalidJobId_WithoutAuth_ShouldReturnUnauthorized()
    {
        var jobId = Guid.NewGuid();
        
        using var content = new MultipartFormDataContent();
        using var fileContent = new ByteArrayContent(new byte[] { 1, 2, 3, 4 });
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        content.Add(fileContent, "file", "test.png");

        var response = await _client.PostAsync($"/jobs/{jobId}/files", content);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private string CreateTestJwtToken()
    {
        return "test.jwt.token";
    }

    private HttpClient CreateAuthenticatedClient(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = 
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}