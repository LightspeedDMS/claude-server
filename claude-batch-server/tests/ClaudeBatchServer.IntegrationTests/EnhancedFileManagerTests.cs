using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Authentication;
using ClaudeBatchServer.Api;
using ClaudeBatchServer.Core.DTOs;
using DotNetEnv;
using Xunit.Abstractions;

namespace ClaudeBatchServer.IntegrationTests;

public class EnhancedFileManagerTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly string _testRepoPath;
    private readonly string _testJobsPath;
    private readonly ITestOutputHelper _output;
    private Guid _testJobId;

    public EnhancedFileManagerTests(WebApplicationFactory<Program> factory, ITestOutputHelper output)
    {
        _output = output;
        
        // Load environment variables from .env file in the project root
        var envPath = "/home/jsbattig/Dev/claude-server/claude-batch-server/.env";
        if (File.Exists(envPath))
        {
            Env.Load(envPath);
        }
        
        _testRepoPath = Path.Combine(Path.GetTempPath(), "enhanced-file-mgr-repos", Guid.NewGuid().ToString());
        _testJobsPath = Path.Combine(Path.GetTempPath(), "enhanced-file-mgr-jobs", Guid.NewGuid().ToString());
        
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((context, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Jwt:Key"] = "EnhancedFileManagerTestKeyThatIsLongEnough",
                    ["Jwt:ExpiryHours"] = "1",
                    ["Workspace:RepositoriesPath"] = _testRepoPath,
                    ["Workspace:JobsPath"] = _testJobsPath,
                    ["Jobs:MaxConcurrent"] = "1",
                    ["Jobs:TimeoutHours"] = "1",
                    ["Auth:ShadowFilePath"] = "/home/jsbattig/Dev/claude-server/claude-batch-server/test-shadow",
                    ["Auth:PasswdFilePath"] = "/home/jsbattig/Dev/claude-server/claude-batch-server/test-passwd",
                    ["Claude:Command"] = "claude --dangerously-skip-permissions --print"
                });
            });
            builder.ConfigureServices(services =>
            {
                services.AddAuthentication("Test")
                    .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>("Test", options => { });
            });
        });

        _client = _factory.CreateClient();
        _client.DefaultRequestHeaders.Authorization = 
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "test-valid-token-for-enhanced-file-manager-tests");
    }

    [Fact]
    public async Task GetFiles_WithFileMaskFilter_ShouldReturnOnlyMatchingFiles()
    {
        // Arrange
        await SetupTestJobWithFiles();
        
        // Act - first let's see what files are returned without a mask
        var allFilesResponse = await _client.GetAsync($"/jobs/{_testJobId}/files");
        allFilesResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var allFiles = await allFilesResponse.Content.ReadFromJsonAsync<List<FileInfoResponse>>();
        
        // Debug: Log all files to understand what we have
        _output.WriteLine($"Total files returned: {allFiles?.Count ?? 0}");
        if (allFiles != null)
        {
            foreach (var file in allFiles)
            {
                _output.WriteLine($"File: {file.Name}, Type: {file.Type}, Path: {file.Path}");
            }
        }
        
        // Debug: Check job status to see the CowPath
        var jobStatusResponse = await _client.GetAsync($"/jobs/{_testJobId}");
        jobStatusResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var jobStatus = await jobStatusResponse.Content.ReadFromJsonAsync<JobStatusResponse>();
        _output.WriteLine($"Job CowPath: {jobStatus?.CowPath}");
        
        // Debug: Check files in src subdirectory
        var srcFilesResponse = await _client.GetAsync($"/jobs/{_testJobId}/files?path=src");
        if (srcFilesResponse.StatusCode == HttpStatusCode.OK)
        {
            var srcFiles = await srcFilesResponse.Content.ReadFromJsonAsync<List<FileInfoResponse>>();
            _output.WriteLine($"Files in src directory: {srcFiles?.Count ?? 0}");
            if (srcFiles != null)
            {
                foreach (var file in srcFiles)
                {
                    _output.WriteLine($"  Src file: {file.Name}, Type: {file.Type}, Path: {file.Path}");
                }
            }
        }
        
        var response = await _client.GetAsync($"/jobs/{_testJobId}/files?mask=*.js");
        
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var files = await response.Content.ReadFromJsonAsync<List<FileInfoResponse>>();
        files.Should().NotBeNull();
        files!.Should().OnlyContain(f => f.Name.EndsWith(".js"));
    }

    [Fact]
    public async Task GetFiles_WithMultipleFileMasks_ShouldReturnMatchingFiles()
    {
        // Arrange
        await SetupTestJobWithFiles();
        
        // Act
        var response = await _client.GetAsync($"/jobs/{_testJobId}/files?mask=*.js,*.ts,*.json&type=files");
        
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var files = await response.Content.ReadFromJsonAsync<List<FileInfoResponse>>();
        files.Should().NotBeNull();
        files!.Should().OnlyContain(f => 
            f.Name.EndsWith(".js") || 
            f.Name.EndsWith(".ts") || 
            f.Name.EndsWith(".json"));
    }

    [Fact]
    public async Task GetFiles_WithDirectoriesOnlyFilter_ShouldReturnOnlyDirectories()
    {
        // Arrange
        await SetupTestJobWithFiles();
        
        // Act
        var response = await _client.GetAsync($"/jobs/{_testJobId}/files?type=directories");
        
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var items = await response.Content.ReadFromJsonAsync<List<FileInfoResponse>>();
        items.Should().NotBeNull();
        items!.Should().OnlyContain(f => f.Type == "directory");
    }

    [Fact]
    public async Task GetFiles_WithFilesOnlyFilter_ShouldReturnOnlyFiles()
    {
        // Arrange
        await SetupTestJobWithFiles();
        
        // Act
        var response = await _client.GetAsync($"/jobs/{_testJobId}/files?type=files");
        
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var items = await response.Content.ReadFromJsonAsync<List<FileInfoResponse>>();
        items.Should().NotBeNull();
        items!.Should().OnlyContain(f => f.Type == "file");
    }

    [Fact]
    public async Task GetFiles_WithDepthLimiting_ShouldRespectDepthFilter()
    {
        // Arrange
        await SetupTestJobWithFiles();
        
        // Act - Get only first level items
        var response = await _client.GetAsync($"/jobs/{_testJobId}/files?depth=1");
        
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var items = await response.Content.ReadFromJsonAsync<List<FileInfoResponse>>();
        items.Should().NotBeNull();
        
        // All returned paths should be at the root level (no subdirectory separators)
        foreach (var item in items!)
        {
            var relativePath = item.Path.Replace("\\", "/");
            relativePath.Split('/').Length.Should().BeLessOrEqualTo(2); // Root + 1 level max
        }
    }

    [Fact]
    public async Task GetFiles_WithCombinedFilters_ShouldApplyAllFilters()
    {
        // Arrange
        await SetupTestJobWithFiles();
        
        // Act - Get only JS files that are files (not dirs) at depth 1
        var response = await _client.GetAsync($"/jobs/{_testJobId}/files?mask=*.js&type=files&depth=1");
        
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var items = await response.Content.ReadFromJsonAsync<List<FileInfoResponse>>();
        items.Should().NotBeNull();
        items!.Should().OnlyContain(f => 
            f.Name.EndsWith(".js") && 
            f.Type == "file");
    }

    [Fact]
    public async Task GetFiles_WithSubdirectoryPath_ShouldReturnSubdirectoryContents()
    {
        // Arrange
        await SetupTestJobWithFiles();
        
        // Act
        var response = await _client.GetAsync($"/jobs/{_testJobId}/files?path=src");
        
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var items = await response.Content.ReadFromJsonAsync<List<FileInfoResponse>>();
        items.Should().NotBeNull();
        items!.Should().NotBeEmpty();
        
        // All items should be within the src subdirectory
        items!.Should().OnlyContain(f => f.Path.Contains("src"));
    }

    [Fact]
    public async Task GetFiles_WithInvalidMask_ShouldReturnBadRequest()
    {
        // Arrange
        await SetupTestJobWithFiles();
        
        // Act
        var response = await _client.GetAsync($"/jobs/{_testJobId}/files?mask=../../../etc/passwd");
        
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetFiles_WithNonExistentPath_ShouldReturnNotFound()
    {
        // Arrange
        await SetupTestJobWithFiles();
        
        // Act
        var response = await _client.GetAsync($"/jobs/{_testJobId}/files?path=nonexistent-folder");
        
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DownloadFile_WithValidFile_ShouldReturnFileContent()
    {
        // Arrange
        await SetupTestJobWithFiles();
        
        // Act
        var response = await _client.GetAsync($"/jobs/{_testJobId}/files/download?path=package.json");
        
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
        
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("\"name\":");
    }

    [Fact]
    public async Task DownloadFile_WithSubdirectoryFile_ShouldReturnFileContent()
    {
        // Arrange
        await SetupTestJobWithFiles();
        
        // Act
        var response = await _client.GetAsync($"/jobs/{_testJobId}/files/download?path=src/index.js");
        
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/javascript");
        
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("console.log");
    }

    private async Task SetupTestJobWithFiles()
    {
        // Ensure directories exist
        Directory.CreateDirectory(_testRepoPath);
        Directory.CreateDirectory(_testJobsPath);

        // Create a test repository first
        var testRepo = Path.Combine(_testRepoPath, "enhanced-test-repo");
        Directory.CreateDirectory(testRepo);
        
        // Create test files structure in the repository
        await File.WriteAllTextAsync(Path.Combine(testRepo, "README.md"), "# Test Repository");
        await File.WriteAllTextAsync(Path.Combine(testRepo, "package.json"), """
            {
              "name": "test-repo",
              "version": "1.0.0",
              "main": "index.js"
            }
            """);
        // Add root-level files for the mask tests
        await File.WriteAllTextAsync(Path.Combine(testRepo, "app.js"), "console.log('Root level JS file');");
        await File.WriteAllTextAsync(Path.Combine(testRepo, "types.ts"), "export interface TestType {}");
        await File.WriteAllTextAsync(Path.Combine(testRepo, "config.json"), """{"debug": true}""");
        
        // Create src directory with files
        var srcDir = Path.Combine(testRepo, "src");
        Directory.CreateDirectory(srcDir);
        await File.WriteAllTextAsync(Path.Combine(srcDir, "index.js"), "console.log('Hello World');");
        await File.WriteAllTextAsync(Path.Combine(srcDir, "utils.ts"), "export function helper() { return true; }");
        await File.WriteAllTextAsync(Path.Combine(srcDir, "config.json"), """{"debug": true}""");
        
        // Create nested directory structure
        var componentsDir = Path.Combine(srcDir, "components");
        Directory.CreateDirectory(componentsDir);
        await File.WriteAllTextAsync(Path.Combine(componentsDir, "Button.js"), "export const Button = () => null;");
        
        // Create tests directory
        var testsDir = Path.Combine(testRepo, "tests");
        Directory.CreateDirectory(testsDir);
        await File.WriteAllTextAsync(Path.Combine(testsDir, "index.test.js"), "test('basic', () => {});");

        // Create repository settings file (required by CowRepositoryService)
        var repoSettingsPath = Path.Combine(testRepo, ".claude-batch-settings.json");
        var settings = new
        {
            Name = "enhanced-test-repo",
            GitUrl = "file://" + testRepo,
            Description = "Test repository for enhanced file manager tests",
            CidxAware = false,
            CloneStatus = "completed",
            RegisteredAt = DateTime.UtcNow,
            LastModified = DateTime.UtcNow
        };
        await File.WriteAllTextAsync(repoSettingsPath, System.Text.Json.JsonSerializer.Serialize(settings, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

        // Create an actual job through the API
        var createJobRequest = new CreateJobRequest
        {
            Prompt = "Test prompt for enhanced file manager tests",
            Repository = "enhanced-test-repo",
            Options = new JobOptionsDto { Timeout = 30, CidxAware = false }
        };

        var createResponse = await _client.PostAsJsonAsync("/jobs", createJobRequest);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created, "Job creation should succeed");
        
        var jobResponse = await createResponse.Content.ReadFromJsonAsync<CreateJobResponse>();
        jobResponse.Should().NotBeNull();
        _testJobId = jobResponse!.JobId;

        // Start the job to create the CoW workspace (required for file access)
        var startResponse = await _client.PostAsync($"/jobs/{_testJobId}/start", null);
        startResponse.StatusCode.Should().Be(HttpStatusCode.OK, "Job start should succeed");

        // Poll until the CoW workspace is created (check for non-empty CowPath)
        for (int i = 0; i < 20; i++) // Up to 10 seconds (20 * 500ms)
        {
            await Task.Delay(500);
            
            var statusResponse = await _client.GetAsync($"/jobs/{_testJobId}");
            if (statusResponse.StatusCode == HttpStatusCode.OK)
            {
                var status = await statusResponse.Content.ReadFromJsonAsync<JobStatusResponse>();
                if (!string.IsNullOrEmpty(status?.CowPath))
                {
                    _output.WriteLine($"CoW workspace ready at: {status.CowPath}");
                    break;
                }
            }
            
            if (i == 19)
            {
                throw new TimeoutException("CoW workspace was not created within 10 seconds");
            }
        }
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testRepoPath))
                Directory.Delete(_testRepoPath, true);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Could not clean up test repo path {_testRepoPath}: {ex.Message}");
        }

        try
        {
            if (Directory.Exists(_testJobsPath))
                Directory.Delete(_testJobsPath, true);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Could not clean up test jobs path {_testJobsPath}: {ex.Message}");
        }

        _client.Dispose();
        _factory.Dispose();
    }
}