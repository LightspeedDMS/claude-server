using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Authentication;
using Xunit;
using ClaudeBatchServer.Core.DTOs;
using DotNetEnv;

namespace ClaudeBatchServer.IntegrationTests;

public class RepositoriesEnhancedE2ETests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly string _testWorkspaceRoot;
    private readonly string _testReposPath;
    private readonly string _testJobsPath;

    public RepositoriesEnhancedE2ETests(WebApplicationFactory<Program> factory)
    {
        // Load environment variables from .env file
        var envPath = "/home/jsbattig/Dev/claude-server/claude-batch-server/.env";
        if (File.Exists(envPath))
        {
            Env.Load(envPath);
        }
        
        _testWorkspaceRoot = Path.Combine(Path.GetTempPath(), $"repos-enhanced-e2e-{Guid.NewGuid()}");
        _testReposPath = Path.Combine(_testWorkspaceRoot, "repos");
        _testJobsPath = Path.Combine(_testWorkspaceRoot, "jobs");
        Directory.CreateDirectory(_testReposPath);
        Directory.CreateDirectory(_testJobsPath);
        
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((context, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Jwt:Key"] = "ReposE2ETestKeyForRepositoriesTestsThatIsLongEnough",
                    ["Jwt:ExpiryHours"] = "1",
                    ["Workspace:RepositoriesPath"] = _testReposPath,
                    ["Workspace:JobsPath"] = _testJobsPath,
                    ["Jobs:MaxConcurrent"] = "1",
                    ["Jobs:TimeoutHours"] = "1",
                    ["Auth:ShadowFilePath"] = "/home/jsbattig/Dev/claude-server/claude-batch-server/test-shadow",
                    ["Auth:PasswdFilePath"] = "/home/jsbattig/Dev/claude-server/claude-batch-server/test-passwd",
                    ["Claude:Command"] = "claude --dangerously-skip-permissions --print"
                });
            });
            
            // FIXED: Use simplified test authentication like SecurityE2ETests
            builder.ConfigureServices(services =>
            {
                // For integration tests, bypass complex JWT validation temporarily
                // Production JWT authentication improvements are in Program.cs
                services.AddAuthentication("Test")
                    .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, TestAuthenticationHandler>("Test", options => { });
            });
        });
        
        _client = _factory.CreateClient();
    }

    public void Dispose()
    {
        if (Directory.Exists(_testWorkspaceRoot))
        {
            Directory.Delete(_testWorkspaceRoot, true);
        }
    }

    [Fact]
    public async Task GetRepositories_WithMixedContent_ReturnsEnhancedMetadata()
    {
        // Arrange
        await SetupTestAuthentication();
        await CreateTestRepositoriesAndFolders();

        // Act
        var response = await _client.GetAsync("/repositories");
        var content = await response.Content.ReadAsStringAsync();
        var repositories = JsonSerializer.Deserialize<List<RepositoryResponse>>(content, new JsonSerializerOptions 
        { 
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase 
        });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        repositories.Should().NotBeNull();
        repositories.Should().HaveCount(3);

        // Verify test-git-repo (registered Git repository)
        var gitRepo = repositories!.FirstOrDefault(r => r.Name == "test-git-repo");
        gitRepo.Should().NotBeNull();
        gitRepo!.Type.Should().Be("git");
        gitRepo.GitUrl.Should().Be("https://github.com/test/repo.git");
        gitRepo.Description.Should().Be("Test Git Repository");
        gitRepo.CloneStatus.Should().Be("completed");
        gitRepo.RegisteredAt.Should().NotBeNull();
        gitRepo.Size.Should().BeGreaterThan(0);
        gitRepo.LastModified.Should().BeCloseTo(DateTime.Now, TimeSpan.FromMinutes(5));

        // Verify unmanaged-git (unregistered Git repository)
        var unmanagedGit = repositories!.FirstOrDefault(r => r.Name == "unmanaged-git");
        unmanagedGit.Should().NotBeNull();
        unmanagedGit!.Type.Should().Be("git");
        unmanagedGit.GitUrl.Should().BeNull(); // No settings file
        unmanagedGit.Description.Should().BeNull();
        unmanagedGit.CloneStatus.Should().Be("unknown"); // Default for unregistered repos
        unmanagedGit.RegisteredAt.Should().BeNull();
        unmanagedGit.Size.Should().BeGreaterThan(0);

        // Verify regular-folder (non-Git folder)
        var regularFolder = repositories!.FirstOrDefault(r => r.Name == "regular-folder");
        regularFolder.Should().NotBeNull();
        regularFolder!.Type.Should().Be("folder");
        regularFolder.GitUrl.Should().BeNull();
        regularFolder.RemoteUrl.Should().BeNull();
        regularFolder.CurrentBranch.Should().BeNull();
        regularFolder.CommitHash.Should().BeNull();
        regularFolder.Size.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task GetRepositories_WithGitMetadata_ReturnsGitInformation()
    {
        // Arrange
        await SetupTestAuthentication();
        await CreateGitRepositoryWithCommits();

        // Act
        var response = await _client.GetAsync("/repositories");
        var content = await response.Content.ReadAsStringAsync();
        var repositories = JsonSerializer.Deserialize<List<RepositoryResponse>>(content, new JsonSerializerOptions 
        { 
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase 
        });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        repositories.Should().NotBeNull();
        
        var gitRepo = repositories!.FirstOrDefault(r => r.Name == "git-with-commits");
        gitRepo.Should().NotBeNull();
        gitRepo!.Type.Should().Be("git");
        gitRepo.CurrentBranch.Should().Be("main");
        gitRepo.CommitHash.Should().NotBeNullOrEmpty();
        gitRepo.CommitMessage.Should().Be("Initial commit");
        gitRepo.CommitAuthor.Should().Be("Test Author");
        gitRepo.CommitDate.Should().NotBeNull();
        gitRepo.HasUncommittedChanges.Should().BeFalse();
    }

    [Fact]
    public async Task GetRepositories_WithUncommittedChanges_DetectsUncommittedChanges()
    {
        // Arrange
        await SetupTestAuthentication();
        await CreateGitRepositoryWithUncommittedChanges();

        // Act
        var response = await _client.GetAsync("/repositories");
        var content = await response.Content.ReadAsStringAsync();
        var repositories = JsonSerializer.Deserialize<List<RepositoryResponse>>(content, new JsonSerializerOptions 
        { 
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase 
        });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        repositories.Should().NotBeNull();
        
        var gitRepo = repositories!.FirstOrDefault(r => r.Name == "git-uncommitted");
        gitRepo.Should().NotBeNull();
        gitRepo!.Type.Should().Be("git");
        gitRepo.HasUncommittedChanges.Should().BeTrue();
    }

    [Fact]
    public async Task GetRepositories_CalculatesSizeAccurately()
    {
        // Arrange
        await SetupTestAuthentication();
        await CreateFolderWithKnownSize();

        // Act
        var response = await _client.GetAsync("/repositories");
        var content = await response.Content.ReadAsStringAsync();
        var repositories = JsonSerializer.Deserialize<List<RepositoryResponse>>(content, new JsonSerializerOptions 
        { 
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase 
        });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        repositories.Should().NotBeNull();
        
        var folder = repositories!.FirstOrDefault(r => r.Name == "known-size-folder");
        folder.Should().NotBeNull();
        folder!.Type.Should().Be("folder");
        folder.Size.Should().Be(50); // 3 files with 10 + 15 + 25 = 50 bytes
    }

    [Fact]
    public async Task GetRepositories_EmptyRepositoriesFolder_ReturnsEmptyList()
    {
        // Arrange
        await SetupTestAuthentication();
        // No test repositories created

        // Act
        var response = await _client.GetAsync("/repositories");
        var content = await response.Content.ReadAsStringAsync();
        var repositories = JsonSerializer.Deserialize<List<RepositoryResponse>>(content, new JsonSerializerOptions 
        { 
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase 
        });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        repositories.Should().NotBeNull();
        repositories.Should().BeEmpty();
    }

    private async Task SetupTestAuthentication()
    {
        // FIXED: Since we're using TestAuthenticationHandler, we can use any valid token
        // The TestAuthenticationHandler accepts any non-empty token that isn't "expired.token.here"
        var testToken = "test-valid-token-for-repositories-enhanced-e2e-tests";
        
        // Set the authorization header for subsequent requests
        _client.DefaultRequestHeaders.Authorization = null;
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", testToken);
        
        await Task.CompletedTask; // Make it async to match the signature
    }

    private async Task CreateTestRepositoriesAndFolders()
    {
        // Create test-git-repo (registered Git repository)
        var testGitRepo = Path.Combine(_testReposPath, "test-git-repo");
        Directory.CreateDirectory(testGitRepo);
        
        var gitDir = Path.Combine(testGitRepo, ".git");
        Directory.CreateDirectory(gitDir);
        await File.WriteAllTextAsync(Path.Combine(gitDir, "config"), "[core]\nrepositoryformatversion = 0");
        await File.WriteAllTextAsync(Path.Combine(testGitRepo, "README.md"), "# Test Repository");
        
        var settingsPath = Path.Combine(testGitRepo, ".claude-batch-settings.json");
        var settings = new
        {
            Name = "test-git-repo",
            Description = "Test Git Repository",
            GitUrl = "https://github.com/test/repo.git",
            RegisteredAt = DateTime.UtcNow,
            CloneStatus = "completed"
        };
        await File.WriteAllTextAsync(settingsPath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));

        // Create unmanaged-git (unregistered Git repository)
        var unmanagedGit = Path.Combine(_testReposPath, "unmanaged-git");
        Directory.CreateDirectory(unmanagedGit);
        var unmanagedGitDir = Path.Combine(unmanagedGit, ".git");
        Directory.CreateDirectory(unmanagedGitDir);
        await File.WriteAllTextAsync(Path.Combine(unmanagedGitDir, "config"), "[core]\nrepositoryformatversion = 0");
        await File.WriteAllTextAsync(Path.Combine(unmanagedGit, "source.js"), "console.log('Hello World');");

        // Create regular-folder (non-Git folder)
        var regularFolder = Path.Combine(_testReposPath, "regular-folder");
        Directory.CreateDirectory(regularFolder);
        await File.WriteAllTextAsync(Path.Combine(regularFolder, "data.txt"), "Some data content");
        await File.WriteAllTextAsync(Path.Combine(regularFolder, "notes.md"), "# Notes\nSome notes here");
    }

    private async Task CreateGitRepositoryWithCommits()
    {
        var repoPath = Path.Combine(_testReposPath, "git-with-commits");
        Directory.CreateDirectory(repoPath);

        // Initialize git repository
        await ExecuteGitCommand(repoPath, "init");
        await ExecuteGitCommand(repoPath, "config user.name \"Test Author\"");
        await ExecuteGitCommand(repoPath, "config user.email \"test@example.com\"");
        
        // Create and commit a file
        await File.WriteAllTextAsync(Path.Combine(repoPath, "test.txt"), "Initial content");
        await ExecuteGitCommand(repoPath, "add test.txt");
        await ExecuteGitCommand(repoPath, "commit -m \"Initial commit\"");
        
        // Ensure we're on main branch
        await ExecuteGitCommand(repoPath, "branch -M main");
    }

    private async Task CreateGitRepositoryWithUncommittedChanges()
    {
        var repoPath = Path.Combine(_testReposPath, "git-uncommitted");
        Directory.CreateDirectory(repoPath);

        // Initialize git repository
        await ExecuteGitCommand(repoPath, "init");
        await ExecuteGitCommand(repoPath, "config user.name \"Test Author\"");
        await ExecuteGitCommand(repoPath, "config user.email \"test@example.com\"");
        
        // Create and commit a file
        await File.WriteAllTextAsync(Path.Combine(repoPath, "committed.txt"), "Committed content");
        await ExecuteGitCommand(repoPath, "add committed.txt");
        await ExecuteGitCommand(repoPath, "commit -m \"Initial commit\"");
        
        // Create uncommitted changes
        await File.WriteAllTextAsync(Path.Combine(repoPath, "uncommitted.txt"), "Uncommitted content");
        await File.AppendAllTextAsync(Path.Combine(repoPath, "committed.txt"), "\nModified content");
    }

    private async Task CreateFolderWithKnownSize()
    {
        var folderPath = Path.Combine(_testReposPath, "known-size-folder");
        Directory.CreateDirectory(folderPath);

        // Create files with known sizes
        await File.WriteAllTextAsync(Path.Combine(folderPath, "file1.txt"), "1234567890"); // 10 bytes
        await File.WriteAllTextAsync(Path.Combine(folderPath, "file2.txt"), "123456789012345"); // 15 bytes
        await File.WriteAllTextAsync(Path.Combine(folderPath, "file3.txt"), "1234567890123456789012345"); // 25 bytes
        // Total: 50 bytes
    }

    private async Task ExecuteGitCommand(string workingDirectory, string arguments)
    {
        var processInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "git",
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = System.Diagnostics.Process.Start(processInfo);
        if (process != null)
        {
            await process.WaitForExitAsync();
            if (process.ExitCode != 0)
            {
                var error = await process.StandardError.ReadToEndAsync();
                throw new InvalidOperationException($"Git command failed: git {arguments}. Error: {error}");
            }
        }
    }
}