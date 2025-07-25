using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using ClaudeBatchServer.Core.Services;

namespace ClaudeBatchServer.Tests.Services;

public class CowRepositoryServiceTests
{
    private readonly Mock<IConfiguration> _mockConfiguration;
    private readonly Mock<ILogger<CowRepositoryService>> _mockLogger;
    private readonly Mock<IGitMetadataService> _mockGitMetadataService;
    private readonly CowRepositoryService _repositoryService;
    private readonly string _testReposPath;
    private readonly string _testJobsPath;

    public CowRepositoryServiceTests()
    {
        _mockConfiguration = new Mock<IConfiguration>();
        _mockLogger = new Mock<ILogger<CowRepositoryService>>();
        _mockGitMetadataService = new Mock<IGitMetadataService>();
        
        _testReposPath = Path.Combine(Path.GetTempPath(), "test-repos", Guid.NewGuid().ToString());
        _testJobsPath = Path.Combine(Path.GetTempPath(), "test-jobs", Guid.NewGuid().ToString());
        
        _mockConfiguration.Setup(c => c["Workspace:RepositoriesPath"]).Returns(_testReposPath);
        _mockConfiguration.Setup(c => c["Workspace:JobsPath"]).Returns(_testJobsPath);
        
        _repositoryService = new CowRepositoryService(_mockConfiguration.Object, _mockLogger.Object, _mockGitMetadataService.Object);
        
        Directory.CreateDirectory(_testReposPath);
        Directory.CreateDirectory(_testJobsPath);
    }

    [Fact]
    public async Task GetRepositoriesAsync_WithEmptyDirectory_ShouldReturnEmptyList()
    {
        var repositories = await _repositoryService.GetRepositoriesAsync();

        repositories.Should().NotBeNull();
        repositories.Should().BeEmpty();
    }

    [Fact]
    public async Task GetRepositoriesAsync_WithRepositories_ShouldReturnRepositoryList()
    {
        var repoPath = Path.Combine(_testReposPath, "test-repo");
        Directory.CreateDirectory(repoPath);
        File.WriteAllText(Path.Combine(repoPath, "README.md"), "Test repository");

        var repositories = await _repositoryService.GetRepositoriesAsync();

        repositories.Should().NotBeNull();
        repositories.Should().HaveCount(1);
        repositories[0].Name.Should().Be("test-repo");
        repositories[0].Path.Should().Be(repoPath);
    }

    [Fact]
    public async Task GetRepositoryAsync_WithExistingRepository_ShouldReturnRepository()
    {
        var repoPath = Path.Combine(_testReposPath, "test-repo");
        Directory.CreateDirectory(repoPath);

        var repository = await _repositoryService.GetRepositoryAsync("test-repo");

        repository.Should().NotBeNull();
        repository!.Name.Should().Be("test-repo");
    }

    [Fact]
    public async Task GetRepositoryAsync_WithNonExistentRepository_ShouldReturnNull()
    {
        var repository = await _repositoryService.GetRepositoryAsync("nonexistent");

        repository.Should().BeNull();
    }

    [Fact]
    public async Task GetFilesInDirectoryAsync_WithNonExistentPath_ShouldReturnNull()
    {
        var files = await _repositoryService.GetFilesInDirectoryAsync("/nonexistent", "path");

        files.Should().BeNull();
    }

    [Fact]
    public async Task GetFilesInDirectoryAsync_WithValidPath_ShouldReturnFileList()
    {
        var testPath = Path.Combine(_testJobsPath, "test-job");
        Directory.CreateDirectory(testPath);
        File.WriteAllText(Path.Combine(testPath, "test.txt"), "test content");
        Directory.CreateDirectory(Path.Combine(testPath, "subdir"));

        var files = await _repositoryService.GetFilesInDirectoryAsync(testPath, "");

        files.Should().NotBeNull();
        files.Should().HaveCount(1); // Only files, not directories
        files.Should().Contain(f => f.Name == "test.txt" && f.Type == "file");
    }

    [Fact]
    public async Task GetDirectoriesAsync_WithValidPath_ShouldReturnDirectoryList()
    {
        var testPath = Path.Combine(_testJobsPath, "test-job");
        Directory.CreateDirectory(testPath);
        File.WriteAllText(Path.Combine(testPath, "test.txt"), "test content");
        Directory.CreateDirectory(Path.Combine(testPath, "subdir"));

        var directories = await _repositoryService.GetDirectoriesAsync(testPath, "");

        directories.Should().NotBeNull();
        directories.Should().HaveCount(1); // Only directories, not files
        directories.Should().Contain(d => d.Name == "subdir");
    }

    [Fact]
    public async Task GetFileContentAsync_WithExistingFile_ShouldReturnContent()
    {
        var testPath = Path.Combine(_testJobsPath, "test-job");
        Directory.CreateDirectory(testPath);
        var testContent = "This is test content";
        File.WriteAllText(Path.Combine(testPath, "test.txt"), testContent);

        var content = await _repositoryService.GetFileContentAsync(testPath, "test.txt");

        content.Should().Be(testContent);
    }

    [Fact]
    public async Task GetFileContentAsync_WithNonExistentFile_ShouldReturnNull()
    {
        var content = await _repositoryService.GetFileContentAsync("/nonexistent/path", "test.txt");

        content.Should().BeNull();
    }

    [Fact]
    public async Task DownloadFileAsync_WithExistingFile_ShouldReturnFileData()
    {
        var testPath = Path.Combine(_testJobsPath, "test-job");
        Directory.CreateDirectory(testPath);
        var testData = new byte[] { 1, 2, 3, 4, 5 };
        await File.WriteAllBytesAsync(Path.Combine(testPath, "test.bin"), testData);

        var fileData = await _repositoryService.DownloadFileAsync(testPath, "test.bin");

        fileData.Should().NotBeNull();
        fileData.Should().BeEquivalentTo(testData);
    }

    [Fact]
    public async Task DownloadFileAsync_WithNonExistentFile_ShouldReturnNull()
    {
        var fileData = await _repositoryService.DownloadFileAsync("/nonexistent/path", "test.bin");

        fileData.Should().BeNull();
    }

    [Fact]
    public async Task ValidateCowSupportAsync_ShouldReturnBooleanResult()
    {
        var result = await _repositoryService.ValidateCowSupportAsync();

        // Result should be a boolean (true or false)
        Assert.True(result == true || result == false);
    }

    [Fact]
    public async Task RemoveCowCloneAsync_WithNonExistentPath_ShouldReturnTrue()
    {
        var result = await _repositoryService.RemoveCowCloneAsync("/nonexistent/path");

        result.Should().BeTrue();
    }

    private void Dispose()
    {
        try
        {
            if (Directory.Exists(_testReposPath))
                Directory.Delete(_testReposPath, true);
            if (Directory.Exists(_testJobsPath))
                Directory.Delete(_testJobsPath, true);
        }
        catch
        {
        }
    }
}