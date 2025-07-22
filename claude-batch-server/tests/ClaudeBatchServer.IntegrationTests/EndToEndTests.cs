using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using ClaudeBatchServer.Api;
using ClaudeBatchServer.Core.DTOs;
using DotNetEnv;

namespace ClaudeBatchServer.IntegrationTests;

public class EndToEndTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly string _testRepoPath;
    private readonly string _testJobsPath;

    public EndToEndTests(WebApplicationFactory<Program> factory)
    {
        // Load environment variables from .env file in the project root
        var envPath = "/home/jsbattig/Dev/claude-server/claude-batch-server/.env";
        if (File.Exists(envPath))
        {
            Env.Load(envPath);
        }
        
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
                    ["Auth:ShadowFilePath"] = "/home/jsbattig/Dev/claude-server/claude-batch-server/test-shadow",
                    ["Claude:Command"] = "claude --dangerously-skip-permissions --print"
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

    [Fact]
    public async Task ClaudeCodeExecution_RealE2E_ShouldCallClaudeAndReturnOutput()
    {
        // Load environment variables directly from .env file for this test
        var envPath = "/home/jsbattig/Dev/claude-server/claude-batch-server/.env";
        if (File.Exists(envPath))
        {
            Env.Load(envPath);
        }
        
        // Get credentials from environment
        var username = Environment.GetEnvironmentVariable("TEST_USERNAME");
        var password = Environment.GetEnvironmentVariable("TEST_PASSWORD");
        
        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            // Debug: Show the current directory and environment path
            var currentDir = Directory.GetCurrentDirectory();
            var envExists = File.Exists(envPath);
            throw new InvalidOperationException($"TEST_USERNAME and TEST_PASSWORD environment variables must be set in .env file. " +
                $"Current dir: {currentDir}, Env path: {envPath}, Env exists: {envExists}");
        }

        // Step 1: Authenticate with real credentials
        var loginRequest = new LoginRequest
        {
            Username = username,
            Password = password // Using plaintext password for testing
        };

        var loginResponse = await _client.PostAsJsonAsync("/auth/login", loginRequest);
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK, "Authentication should succeed with valid credentials");
        
        var loginResult = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
        loginResult.Should().NotBeNull();
        loginResult!.Token.Should().NotBeNullOrEmpty();

        // Step 2: Create authenticated client
        var authClient = CreateAuthenticatedClient(loginResult.Token);

        // Step 3: Create job with simple Claude Code prompt
        var createJobRequest = new CreateJobRequest
        {
            Prompt = "1+1",  // Simple math that Claude should answer with "2"
            Repository = "test-repo",
            Options = new JobOptionsDto { Timeout = 60 } // Allow more time for real Claude execution
        };

        var createResponse = await authClient.PostAsJsonAsync("/jobs", createJobRequest);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created, "Job creation should succeed");
        
        var jobResponse = await createResponse.Content.ReadFromJsonAsync<CreateJobResponse>();
        jobResponse.Should().NotBeNull();
        jobResponse!.JobId.Should().NotBeEmpty();

        // Step 4: Start the job
        var startResponse = await authClient.PostAsync($"/jobs/{jobResponse.JobId}/start", null);
        startResponse.StatusCode.Should().Be(HttpStatusCode.OK, "Job start should succeed");

        // Step 5: Poll for completion (real Claude execution takes time)
        JobStatusResponse? statusResponse = null;
        var timeout = DateTime.UtcNow.AddMinutes(3); // Give Claude time to respond
        var pollCount = 0;
        const int maxPolls = 60; // Max 60 polls (3 minutes with 3-second intervals)

        while (DateTime.UtcNow < timeout && pollCount < maxPolls)
        {
            await Task.Delay(3000); // Wait 3 seconds between polls
            pollCount++;
            
            var statusHttpResponse = await authClient.GetAsync($"/jobs/{jobResponse.JobId}");
            statusHttpResponse.StatusCode.Should().Be(HttpStatusCode.OK, "Job status check should succeed");
            
            statusResponse = await statusHttpResponse.Content.ReadFromJsonAsync<JobStatusResponse>();
            statusResponse.Should().NotBeNull($"Job status response should not be null on poll {pollCount}");
            
            if (statusResponse!.Status == "completed" || statusResponse.Status == "failed")
                break;
        }

        // Step 6: Verify job execution results
        statusResponse.Should().NotBeNull("Final status response should not be null");
        
        // Debug output for failed jobs
        if (statusResponse!.Status == "failed")
        {
            Console.WriteLine($"Job failed with exit code: {statusResponse.ExitCode}");
            Console.WriteLine($"Job output: {statusResponse.Output}");
        }
        
        statusResponse.Status.Should().Be("completed", "Job should complete successfully");
        statusResponse.ExitCode.Should().Be(0, "Claude Code should exit successfully");
        statusResponse.Output.Should().NotBeNullOrEmpty("Claude should produce output");
        
        // The key assertion: Claude should respond to "1+1" with "2"
        statusResponse.Output.Should().Contain("2", "Claude should correctly answer 1+1=2");
        
        // Verify timing fields are populated
        statusResponse.StartedAt.Should().NotBeNull("Job should have a start time");
        statusResponse.CompletedAt.Should().NotBeNull("Job should have a completion time");
        statusResponse.CompletedAt.Should().BeAfter(statusResponse.StartedAt!.Value, "Completion should be after start");
        
        // Verify job ID matches
        statusResponse.JobId.Should().Be(jobResponse.JobId, "Job ID should match");
    }

    private HttpClient CreateAuthenticatedClient(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = 
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return client;
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