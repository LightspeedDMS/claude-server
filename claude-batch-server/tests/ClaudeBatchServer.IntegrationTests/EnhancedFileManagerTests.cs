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

namespace ClaudeBatchServer.IntegrationTests;

public class EnhancedFileManagerTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly string _testRepoPath;
    private readonly string _testJobsPath;
    private Guid _testJobId;

    public EnhancedFileManagerTests(WebApplicationFactory<Program> factory)
    {
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
            new System.Net.Http.Headers.AuthenticationHeaderValue("Test");
    }

    [Fact]
    public async Task GetFiles_WithFileMaskFilter_ShouldReturnOnlyMatchingFiles()
    {
        // Arrange
        await SetupTestJobWithFiles();
        
        // Act
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
        var response = await _client.GetAsync($"/jobs/{_testJobId}/files?mask=*.js,*.ts,*.json");
        
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

        // Create a test workspace directory directly
        var jobWorkspace = Path.Combine(_testJobsPath, "test-job-workspace");
        Directory.CreateDirectory(jobWorkspace);
        
        // Create test files structure directly in job workspace
        await File.WriteAllTextAsync(Path.Combine(jobWorkspace, "README.md"), "# Test Repository");
        await File.WriteAllTextAsync(Path.Combine(jobWorkspace, "package.json"), """
            {
              "name": "test-repo",
              "version": "1.0.0",
              "main": "index.js"
            }
            """);
        
        // Create src directory with files
        var srcDir = Path.Combine(jobWorkspace, "src");
        Directory.CreateDirectory(srcDir);
        await File.WriteAllTextAsync(Path.Combine(srcDir, "index.js"), "console.log('Hello World');");
        await File.WriteAllTextAsync(Path.Combine(srcDir, "utils.ts"), "export function helper() { return true; }");
        await File.WriteAllTextAsync(Path.Combine(srcDir, "config.json"), """{"debug": true}""");
        
        // Create nested directory structure
        var componentsDir = Path.Combine(srcDir, "components");
        Directory.CreateDirectory(componentsDir);
        await File.WriteAllTextAsync(Path.Combine(componentsDir, "Button.js"), "export const Button = () => null;");
        
        // Create tests directory
        var testsDir = Path.Combine(jobWorkspace, "tests");
        Directory.CreateDirectory(testsDir);
        await File.WriteAllTextAsync(Path.Combine(testsDir, "index.test.js"), "test('basic', () => {});");

        // Create a test job directly using a fake job ID
        _testJobId = Guid.NewGuid();
        
        // We'll mock this job's existence by creating the workspace files
        // In a real scenario, this setup would be done by the job service
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