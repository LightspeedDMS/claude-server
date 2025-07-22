using System.IO;
using Xunit;
using Microsoft.Extensions.Logging;
using Moq;
using FluentAssertions;
using ClaudeBatchServer.Core.Services;

namespace ClaudeBatchServer.UnitTests.Services;

public class GitMetadataServiceTests : IDisposable
{
    private readonly GitMetadataService _gitMetadataService;
    private readonly Mock<ILogger<GitMetadataService>> _loggerMock;
    private readonly string _testDirectory;
    private readonly string _gitTestRepo;
    private readonly string _nonGitFolder;

    public GitMetadataServiceTests()
    {
        _loggerMock = new Mock<ILogger<GitMetadataService>>();
        _gitMetadataService = new GitMetadataService(_loggerMock.Object);
        
        _testDirectory = Path.Combine(Path.GetTempPath(), $"git-metadata-tests-{Guid.NewGuid()}");
        _gitTestRepo = Path.Combine(_testDirectory, "test-git-repo");
        _nonGitFolder = Path.Combine(_testDirectory, "non-git-folder");
        
        Directory.CreateDirectory(_testDirectory);
        Directory.CreateDirectory(_gitTestRepo);
        Directory.CreateDirectory(_nonGitFolder);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, true);
        }
    }

    [Fact]
    public async Task IsGitRepositoryAsync_WithGitDirectory_ReturnsTrue()
    {
        // Arrange
        var gitDir = Path.Combine(_gitTestRepo, ".git");
        Directory.CreateDirectory(gitDir);

        // Act
        var result = await _gitMetadataService.IsGitRepositoryAsync(_gitTestRepo);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsGitRepositoryAsync_WithGitFile_ReturnsTrue()
    {
        // Arrange
        var gitFile = Path.Combine(_gitTestRepo, ".git");
        await File.WriteAllTextAsync(gitFile, "gitdir: /some/path/.git");

        // Act
        var result = await _gitMetadataService.IsGitRepositoryAsync(_gitTestRepo);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsGitRepositoryAsync_WithoutGitDirectory_ReturnsFalse()
    {
        // Act
        var result = await _gitMetadataService.IsGitRepositoryAsync(_nonGitFolder);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsGitRepositoryAsync_WithNonExistentPath_ReturnsFalse()
    {
        // Arrange
        var nonExistentPath = Path.Combine(_testDirectory, "does-not-exist");

        // Act
        var result = await _gitMetadataService.IsGitRepositoryAsync(nonExistentPath);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task GetGitMetadataAsync_WithNonGitRepository_ReturnsNull()
    {
        // Act
        var result = await _gitMetadataService.GetGitMetadataAsync(_nonGitFolder);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task CalculateFolderSizeAsync_WithFiles_ReturnsCorrectSize()
    {
        // Arrange
        var testFile1 = Path.Combine(_nonGitFolder, "test1.txt");
        var testFile2 = Path.Combine(_nonGitFolder, "test2.txt");
        var subDir = Path.Combine(_nonGitFolder, "subdir");
        Directory.CreateDirectory(subDir);
        var testFile3 = Path.Combine(subDir, "test3.txt");

        var content1 = "Hello World"; // 11 bytes
        var content2 = "Test Content"; // 12 bytes  
        var content3 = "Sub Directory File"; // 18 bytes

        await File.WriteAllTextAsync(testFile1, content1);
        await File.WriteAllTextAsync(testFile2, content2);
        await File.WriteAllTextAsync(testFile3, content3);

        // Act
        var result = await _gitMetadataService.CalculateFolderSizeAsync(_nonGitFolder);

        // Assert
        result.Should().Be(41); // 11 + 12 + 18 = 41 bytes
    }

    [Fact]
    public async Task CalculateFolderSizeAsync_WithEmptyFolder_ReturnsZero()
    {
        // Act
        var result = await _gitMetadataService.CalculateFolderSizeAsync(_nonGitFolder);

        // Assert
        result.Should().Be(0);
    }

    [Fact]
    public async Task CalculateFolderSizeAsync_WithNonExistentPath_ReturnsZero()
    {
        // Arrange
        var nonExistentPath = Path.Combine(_testDirectory, "does-not-exist");

        // Act
        var result = await _gitMetadataService.CalculateFolderSizeAsync(nonExistentPath);

        // Assert
        result.Should().Be(0);
    }

    [Fact]
    public async Task CalculateFolderSizeAsync_WithHiddenFiles_IncludesHiddenFiles()
    {
        // Arrange
        var visibleFile = Path.Combine(_nonGitFolder, "visible.txt");
        var hiddenFile = Path.Combine(_nonGitFolder, ".hidden");
        
        var visibleContent = "Visible"; // 7 bytes
        var hiddenContent = "Hidden"; // 6 bytes

        await File.WriteAllTextAsync(visibleFile, visibleContent);
        await File.WriteAllTextAsync(hiddenFile, hiddenContent);

        // Act
        var result = await _gitMetadataService.CalculateFolderSizeAsync(_nonGitFolder);

        // Assert
        result.Should().Be(13); // 7 + 6 = 13 bytes
    }

    [Fact]
    public async Task CalculateFolderSizeAsync_WithGitRepository_IncludesGitFolder()
    {
        // Arrange
        var gitDir = Path.Combine(_gitTestRepo, ".git");
        Directory.CreateDirectory(gitDir);
        
        var gitConfigFile = Path.Combine(gitDir, "config");
        var regularFile = Path.Combine(_gitTestRepo, "README.md");
        
        var gitContent = "[core]\nrepositoryformatversion = 0";
        var readmeContent = "# Test Repository";

        await File.WriteAllTextAsync(gitConfigFile, gitContent);
        await File.WriteAllTextAsync(regularFile, readmeContent);

        // Act
        var result = await _gitMetadataService.CalculateFolderSizeAsync(_gitTestRepo);

        // Assert
        result.Should().BeGreaterThan(40); // Should include both .git and regular files
        result.Should().BeLessThan(100); // Reasonable upper bound
    }
}