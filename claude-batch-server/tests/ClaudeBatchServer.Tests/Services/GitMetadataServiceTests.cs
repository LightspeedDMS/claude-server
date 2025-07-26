using System.IO;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using ClaudeBatchServer.Core.Services;

namespace ClaudeBatchServer.Tests.Services;

public class GitMetadataServiceTests : IDisposable
{
    private readonly Mock<ILogger<GitMetadataService>> _mockLogger;
    private readonly GitMetadataService _gitMetadataService;
    private readonly string _testRepoPath;

    public GitMetadataServiceTests()
    {
        _mockLogger = new Mock<ILogger<GitMetadataService>>();
        _gitMetadataService = new GitMetadataService(_mockLogger.Object);
        
        // Create a temporary directory for testing
        _testRepoPath = Path.Combine(Path.GetTempPath(), "git-metadata-test", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testRepoPath);
    }

    [Fact]
    public async Task IsGitRepositoryAsync_WithValidRepository_ShouldReturnTrue()
    {
        // Arrange - Create a basic git repository
        await CreateTestGitRepository();

        // Act
        var isGitRepo = await _gitMetadataService.IsGitRepositoryAsync(_testRepoPath);

        // Assert
        isGitRepo.Should().BeTrue();
    }

    [Fact]
    public async Task IsGitRepositoryAsync_WithNonExistentPath_ShouldReturnFalse()
    {
        // Act
        var isGitRepo = await _gitMetadataService.IsGitRepositoryAsync("/nonexistent/path");

        // Assert
        isGitRepo.Should().BeFalse();
    }

    [Fact]
    public async Task IsGitRepositoryAsync_WithNonGitDirectory_ShouldReturnFalse()
    {
        // Arrange - Use test directory without .git folder
        var nonGitPath = Path.Combine(Path.GetTempPath(), "non-git-test", Guid.NewGuid().ToString());
        Directory.CreateDirectory(nonGitPath);

        try
        {
            // Act
            var isGitRepo = await _gitMetadataService.IsGitRepositoryAsync(nonGitPath);

            // Assert
            isGitRepo.Should().BeFalse();
        }
        finally
        {
            Directory.Delete(nonGitPath, true);
        }
    }

    [Fact]
    public async Task GetGitMetadataAsync_WithValidRepository_ShouldReturnMetadata()
    {
        // Arrange
        await CreateTestGitRepository();
        await CreateTestCommit();

        // Act
        var metadata = await _gitMetadataService.GetGitMetadataAsync(_testRepoPath);

        // Assert
        metadata.Should().NotBeNull();
        metadata!.CurrentBranch.Should().NotBeNull();
        metadata.CommitHash.Should().NotBeNull();
        metadata.CommitMessage.Should().NotBeNull();
    }

    [Fact]
    public async Task GetGitMetadataAsync_WithInvalidPath_ShouldReturnNull()
    {
        // Act
        var metadata = await _gitMetadataService.GetGitMetadataAsync("/invalid/path");

        // Assert
        metadata.Should().BeNull();
    }

    [Fact]
    public async Task CalculateFolderSizeAsync_WithValidDirectory_ShouldReturnSize()
    {
        // Arrange - Create some test files
        var testFile = Path.Combine(_testRepoPath, "test.txt");
        await File.WriteAllTextAsync(testFile, "Test content for size calculation");

        // Act
        var size = await _gitMetadataService.CalculateFolderSizeAsync(_testRepoPath);

        // Assert
        size.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task CalculateFolderSizeAsync_WithNonExistentPath_ShouldReturnZero()
    {
        // Act
        var size = await _gitMetadataService.CalculateFolderSizeAsync("/nonexistent/path");

        // Assert
        size.Should().Be(0);
    }

    private async Task CreateTestGitRepository()
    {
        // Initialize git repository
        await ExecuteGitCommand("init");
        await ExecuteGitCommand("config user.email \"test@example.com\"");
        await ExecuteGitCommand("config user.name \"Test User\"");
    }

    private async Task CreateTestCommit()
    {
        // Create a test file and commit it
        var testFile = Path.Combine(_testRepoPath, "test.txt");
        await File.WriteAllTextAsync(testFile, "Test content");
        
        await ExecuteGitCommand("add .");
        await ExecuteGitCommand("commit -m \"Initial commit\"");
    }

    private async Task ExecuteGitCommand(string arguments)
    {
        var processInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "git",
            Arguments = arguments,
            WorkingDirectory = _testRepoPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new System.Diagnostics.Process { StartInfo = processInfo };
        process.Start();
        await process.WaitForExitAsync();
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testRepoPath))
                Directory.Delete(_testRepoPath, true);
        }
        catch
        {
            // Ignore cleanup errors in tests
        }
    }
}