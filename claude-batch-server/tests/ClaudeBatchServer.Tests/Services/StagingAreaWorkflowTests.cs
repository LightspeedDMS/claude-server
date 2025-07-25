using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using System.Text;
using ClaudeBatchServer.Core.Models;
using ClaudeBatchServer.Core.Services;

namespace ClaudeBatchServer.Tests.Services;

/// <summary>
/// Tests for the staging area workflow that fixes the file upload architecture flaw
/// Verifies that files are uploaded to staging area before CoW clone completion
/// </summary>
public class StagingAreaWorkflowTests : IDisposable
{
    private readonly string _testWorkspacePath;
    private readonly JobPersistenceService _jobPersistenceService;
    private readonly Mock<ILogger<JobPersistenceService>> _mockLogger;
    private readonly Mock<IConfiguration> _mockConfiguration;

    public StagingAreaWorkflowTests()
    {
        _testWorkspacePath = Path.Combine(Path.GetTempPath(), $"staging_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testWorkspacePath);
        
        _mockLogger = new Mock<ILogger<JobPersistenceService>>();
        _mockConfiguration = new Mock<IConfiguration>();
        
        // Configure retention policy
        _mockConfiguration.Setup(c => c["Jobs:RetentionDays"]).Returns("30");
        
        _jobPersistenceService = new JobPersistenceService(_testWorkspacePath, _mockConfiguration.Object, _mockLogger.Object);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testWorkspacePath))
        {
            Directory.Delete(_testWorkspacePath, recursive: true);
        }
    }

    [Fact]
    public void GetJobStagingPath_ReturnsCorrectStagingDirectory()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        var expectedPath = Path.Combine(_testWorkspacePath, "jobs", jobId.ToString(), "staging");

        // Act
        var stagingPath = _jobPersistenceService.GetJobStagingPath(jobId);

        // Assert
        Assert.Equal(expectedPath, stagingPath);
    }

    [Fact]
    public void GetStagedFiles_EmptyWhenNoStagingDirectory()
    {
        // Arrange
        var jobId = Guid.NewGuid();

        // Act
        var stagedFiles = _jobPersistenceService.GetStagedFiles(jobId);

        // Assert
        Assert.Empty(stagedFiles);
    }

    [Fact]
    public void GetStagedFiles_ReturnsUploadedFiles()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        var stagingPath = _jobPersistenceService.GetJobStagingPath(jobId);
        Directory.CreateDirectory(stagingPath);

        // Create test files in staging area
        var testFile1 = Path.Combine(stagingPath, "test1.txt");
        var testFile2 = Path.Combine(stagingPath, "subdir", "test2.js");
        
        Directory.CreateDirectory(Path.GetDirectoryName(testFile2)!);
        File.WriteAllText(testFile1, "Test content 1");
        File.WriteAllText(testFile2, "Test content 2");

        // Act
        var stagedFiles = _jobPersistenceService.GetStagedFiles(jobId);

        // Assert
        Assert.Equal(2, stagedFiles.Count);
        Assert.Contains("test1.txt", stagedFiles);
        Assert.Contains(Path.Combine("subdir", "test2.js"), stagedFiles);
    }

    [Fact]
    public async Task CopyStagedFilesToCowWorkspaceAsync_CopiesAllFiles()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        var stagingPath = _jobPersistenceService.GetJobStagingPath(jobId);
        var cowPath = Path.Combine(Path.GetTempPath(), $"cow_test_{Guid.NewGuid():N}");
        
        try
        {
            Directory.CreateDirectory(stagingPath);
            Directory.CreateDirectory(cowPath);

            // Create test files in staging area
            var testFile1 = Path.Combine(stagingPath, "uploaded1.txt");
            var testFile2 = Path.Combine(stagingPath, "docs", "uploaded2.md");
            
            Directory.CreateDirectory(Path.GetDirectoryName(testFile2)!);
            File.WriteAllText(testFile1, "Uploaded content 1");
            File.WriteAllText(testFile2, "# Uploaded markdown content");

            // Act
            var copiedCount = await _jobPersistenceService.CopyStagedFilesToCowWorkspaceAsync(jobId, cowPath);

            // Assert
            Assert.Equal(2, copiedCount);

            // Verify files were copied to CoW workspace files directory
            var cowFilesPath = Path.Combine(cowPath, "files");
            Assert.True(Directory.Exists(cowFilesPath));
            
            var copiedFile1 = Path.Combine(cowFilesPath, "uploaded1.txt");
            var copiedFile2 = Path.Combine(cowFilesPath, "docs", "uploaded2.md");
            
            Assert.True(File.Exists(copiedFile1));
            Assert.True(File.Exists(copiedFile2));
            
            Assert.Equal("Uploaded content 1", File.ReadAllText(copiedFile1));
            Assert.Equal("# Uploaded markdown content", File.ReadAllText(copiedFile2));
        }
        finally
        {
            if (Directory.Exists(cowPath))
            {
                Directory.Delete(cowPath, recursive: true);
            }
        }
    }

    [Fact]
    public async Task CopyStagedFilesToCowWorkspaceAsync_ReturnsZeroWhenNoStagingDirectory()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        var cowPath = Path.Combine(Path.GetTempPath(), $"cow_test_{Guid.NewGuid():N}");
        
        try
        {
            Directory.CreateDirectory(cowPath);

            // Act
            var copiedCount = await _jobPersistenceService.CopyStagedFilesToCowWorkspaceAsync(jobId, cowPath);

            // Assert
            Assert.Equal(0, copiedCount);
        }
        finally
        {
            if (Directory.Exists(cowPath))
            {
                Directory.Delete(cowPath, recursive: true);
            }
        }
    }

    [Fact]
    public void CleanupStagingDirectory_RemovesStagingAfterSuccessfulCopy()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        var stagingPath = _jobPersistenceService.GetJobStagingPath(jobId);
        Directory.CreateDirectory(stagingPath);
        
        var testFile = Path.Combine(stagingPath, "cleanup_test.txt");
        File.WriteAllText(testFile, "Test cleanup");
        
        Assert.True(Directory.Exists(stagingPath));
        Assert.True(File.Exists(testFile));

        // Act
        _jobPersistenceService.CleanupStagingDirectory(jobId);

        // Assert
        Assert.False(Directory.Exists(stagingPath));
    }

    [Fact]
    public void CleanupStagingDirectory_DoesNotFailWhenStagingDirectoryDoesNotExist()
    {
        // Arrange
        var jobId = Guid.NewGuid();

        // Act & Assert - should not throw
        _jobPersistenceService.CleanupStagingDirectory(jobId);
    }

    /// <summary>
    /// Integration test simulating the complete file upload workflow:
    /// 1. Job created (no CoW clone yet)
    /// 2. Files uploaded to staging area 
    /// 3. Job execution starts, CoW clone created
    /// 4. Staged files copied to CoW workspace
    /// 5. Staging area cleaned up
    /// </summary>
    [Fact]
    public async Task CompleteFileUploadWorkflow_SuccessfullyHandlesAsynchronousCoWCloning()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        var cowPath = Path.Combine(Path.GetTempPath(), $"integration_cow_{Guid.NewGuid():N}");
        
        try
        {
            // STEP 1: Job created (no CoW clone yet - simulating CreateJobAsync)
            var stagingPath = _jobPersistenceService.GetJobStagingPath(jobId);
            
            // STEP 2: Files uploaded to staging area (simulating UploadFileAsync)
            Directory.CreateDirectory(stagingPath);
            
            var uploadedFile1 = Path.Combine(stagingPath, "requirements.txt");
            var uploadedFile2 = Path.Combine(stagingPath, "src", "helper.py");
            
            Directory.CreateDirectory(Path.GetDirectoryName(uploadedFile2)!);
            File.WriteAllText(uploadedFile1, "pytest>=7.0.0\nrequests>=2.28.0");
            File.WriteAllText(uploadedFile2, "def helper_function():\n    return 'uploaded helper'");

            // Verify files are in staging
            var stagedFiles = _jobPersistenceService.GetStagedFiles(jobId);
            Assert.Equal(2, stagedFiles.Count);
            
            // STEP 3: Job execution starts, CoW clone created (simulating ProcessJobAsync)
            Directory.CreateDirectory(cowPath);
            
            // STEP 4: Staged files copied to CoW workspace
            var copiedCount = await _jobPersistenceService.CopyStagedFilesToCowWorkspaceAsync(jobId, cowPath);
            Assert.Equal(2, copiedCount);
            
            // Verify files are now in CoW workspace
            var cowFilesPath = Path.Combine(cowPath, "files");
            Assert.True(Directory.Exists(cowFilesPath));
            
            var finalFile1 = Path.Combine(cowFilesPath, "requirements.txt");
            var finalFile2 = Path.Combine(cowFilesPath, "src", "helper.py");
            
            Assert.True(File.Exists(finalFile1));
            Assert.True(File.Exists(finalFile2));
            Assert.Equal("pytest>=7.0.0\nrequests>=2.28.0", File.ReadAllText(finalFile1));
            Assert.Equal("def helper_function():\n    return 'uploaded helper'", File.ReadAllText(finalFile2));
            
            // STEP 5: Staging area cleaned up
            _jobPersistenceService.CleanupStagingDirectory(jobId);
            Assert.False(Directory.Exists(stagingPath));
            
            // Final verification: uploaded files are available for CIDX indexing and Claude Code execution
            Assert.True(File.Exists(finalFile1));
            Assert.True(File.Exists(finalFile2));
        }
        finally
        {
            if (Directory.Exists(cowPath))
            {
                Directory.Delete(cowPath, recursive: true);
            }
        }
    }
}