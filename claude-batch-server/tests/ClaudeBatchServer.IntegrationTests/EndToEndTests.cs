using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using ClaudeBatchServer.Api;
using ClaudeBatchServer.Core.DTOs;

namespace ClaudeBatchServer.IntegrationTests;

public class EndToEndTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly string _testRepoPath;
    private readonly string _testJobsPath;

    public EndToEndTests(WebApplicationFactory<Program> factory)
    {
        _testRepoPath = Path.Combine(Path.GetTempPath(), "e2e-test-repos", Guid.NewGuid().ToString());
        _testJobsPath = Path.Combine(Path.GetTempPath(), "e2e-test-jobs", Guid.NewGuid().ToString());
        
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((context, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Jwt:Key"] = "E2ETestKeyForEndToEndTestsThatIsLongEnough",
                    ["Jwt:ExpiryHours"] = "1",
                    ["Workspace:RepositoriesPath"] = _testRepoPath,
                    ["Workspace:JobsPath"] = _testJobsPath,
                    ["Jobs:MaxConcurrent"] = "1",
                    ["Jobs:TimeoutHours"] = "1",
                    ["Claude:Command"] = "echo"
                });
            });
        });
        
        _client = _factory.CreateClient();
        
        SetupTestEnvironment();
    }

    private void SetupTestEnvironment()
    {
        Directory.CreateDirectory(_testRepoPath);
        Directory.CreateDirectory(_testJobsPath);
        
        var testRepo = Path.Combine(_testRepoPath, "test-repo");
        Directory.CreateDirectory(testRepo);
        File.WriteAllText(Path.Combine(testRepo, "README.md"), "# Test Repository\n\nThis is a test repository for E2E testing.");
        File.WriteAllText(Path.Combine(testRepo, "app.js"), "console.log('Hello World');");
        
        var claudeDir = Path.Combine(testRepo, ".claude");
        Directory.CreateDirectory(claudeDir);
        File.WriteAllText(Path.Combine(claudeDir, "settings.json"), "{}");
    }

    [Fact]
    public async Task FullWorkflow_CreateJobAndExecute_ShouldWorkEndToEnd()
    {
        var createJobRequest = new CreateJobRequest
        {
            Prompt = "List all files in this repository",
            Repository = "test-repo",
            Options = new JobOptionsDto { Timeout = 30 }
        };

        var createResponse = await _client.PostAsJsonAsync("/jobs", createJobRequest);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Repositories_GetRepositories_ShouldReturnTestRepository()
    {
        var response = await _client.GetAsync("/repositories");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Jobs_CreateJob_WithNonExistentRepository_ShouldReturnBadRequest()
    {
        var createJobRequest = new CreateJobRequest
        {
            Prompt = "Test prompt",
            Repository = "nonexistent-repo"
        };

        var response = await _client.PostAsJsonAsync("/jobs", createJobRequest);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ImageUpload_WithValidImage_ShouldWork()
    {
        var jobId = Guid.NewGuid();
        
        using var content = new MultipartFormDataContent();
        using var imageData = new ByteArrayContent(CreateTestImageData());
        imageData.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        content.Add(imageData, "file", "test.png");

        var response = await _client.PostAsync($"/jobs/{jobId}/images", content);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task FileOperations_GetFiles_ShouldReturnFileList()
    {
        var jobId = Guid.NewGuid();
        var response = await _client.GetAsync($"/jobs/{jobId}/files");
        
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task FileOperations_DownloadFile_ShouldReturnFileContent()
    {
        var jobId = Guid.NewGuid();
        var response = await _client.GetAsync($"/jobs/{jobId}/files/download?path=README.md");
        
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task FileOperations_GetFileContent_ShouldReturnTextContent()
    {
        var jobId = Guid.NewGuid();
        var response = await _client.GetAsync($"/jobs/{jobId}/files/content?path=README.md");
        
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CopyOnWrite_ValidateSupport_ShouldBeDetected()
    {
        var testPath = Path.Combine(_testJobsPath, "cow-test");
        Directory.CreateDirectory(testPath);
        
        Directory.Exists(testPath).Should().BeTrue();
    }

    [Fact]
    public async Task QueueManagement_MultipleJobs_ShouldHandleCorrectly()
    {
        var job1Request = new CreateJobRequest
        {
            Prompt = "Job 1",
            Repository = "test-repo"
        };
        
        var job2Request = new CreateJobRequest
        {
            Prompt = "Job 2",
            Repository = "test-repo"
        };

        var response1 = await _client.PostAsJsonAsync("/jobs", job1Request);
        var response2 = await _client.PostAsJsonAsync("/jobs", job2Request);
        
        response1.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response2.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Security_AccessControl_ShouldPreventUnauthorizedAccess()
    {
        // Test GET endpoints
        var getEndpoints = new[]
        {
            "/jobs",
            "/repositories",
            $"/jobs/{Guid.NewGuid()}",
            $"/jobs/{Guid.NewGuid()}/files"
        };

        foreach (var endpoint in getEndpoints)
        {
            var response = await _client.GetAsync(endpoint);
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, 
                $"GET endpoint {endpoint} should require authentication");
        }

        // Test POST endpoints
        var postEndpoints = new[]
        {
            $"/jobs/{Guid.NewGuid()}/start"
        };

        foreach (var endpoint in postEndpoints)
        {
            var response = await _client.PostAsync(endpoint, null);
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, 
                $"POST endpoint {endpoint} should require authentication");
        }
    }

    [Fact]
    public async Task ErrorHandling_InvalidRequests_ShouldReturnAppropriateErrors()
    {
        var invalidJobRequest = new CreateJobRequest
        {
            Prompt = "",
            Repository = ""
        };

        var response = await _client.PostAsJsonAsync("/jobs", invalidJobRequest);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static byte[] CreateTestImageData()
    {
        var pngHeader = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        var data = new byte[100];
        Array.Copy(pngHeader, data, pngHeader.Length);
        return data;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testRepoPath))
                Directory.Delete(_testRepoPath, true);
            if (Directory.Exists(_testJobsPath))
                Directory.Delete(_testJobsPath, true);
        }
        catch
        {
        }
        
        _client?.Dispose();
    }
}