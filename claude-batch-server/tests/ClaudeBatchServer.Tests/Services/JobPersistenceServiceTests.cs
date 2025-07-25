using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using ClaudeBatchServer.Core.Models;
using ClaudeBatchServer.Core.Services;

namespace ClaudeBatchServer.Tests.Services;

/// <summary>
/// TDD tests for JobPersistenceService - file-based job persistence with configurable retention
/// </summary>
public class JobPersistenceServiceTests : IDisposable
{
    private readonly string _testWorkspacePath;
    private readonly string _testJobsPath;
    private readonly Mock<ILogger<JobPersistenceService>> _mockLogger;
    private readonly Mock<IConfiguration> _mockConfiguration;
    private readonly JobPersistenceService _service;

    public JobPersistenceServiceTests()
    {
        // Create isolated test workspace
        _testWorkspacePath = Path.Combine(Path.GetTempPath(), "claude-test-workspace", Guid.NewGuid().ToString());
        _testJobsPath = Path.Combine(_testWorkspacePath, "jobs");
        Directory.CreateDirectory(_testJobsPath);

        _mockLogger = new Mock<ILogger<JobPersistenceService>>();
        _mockConfiguration = new Mock<IConfiguration>();
        
        // Configure default retention days
        _mockConfiguration.Setup(c => c["Jobs:RetentionDays"]).Returns("30");
        
        _service = new JobPersistenceService(_testWorkspacePath, _mockConfiguration.Object, _mockLogger.Object);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testWorkspacePath))
        {
            Directory.Delete(_testWorkspacePath, true);
        }
    }

    #region SaveJobAsync Tests

    [Fact]
    public async Task SaveJobAsync_WithValidJob_ShouldCreateJobFile()
    {
        // Arrange
        var job = CreateTestJob();

        // Act
        await _service.SaveJobAsync(job);

        // Assert
        var expectedFilePath = Path.Combine(_testJobsPath, $"{job.Id}.job.json");
        File.Exists(expectedFilePath).Should().BeTrue();
        
        var jsonContent = await File.ReadAllTextAsync(expectedFilePath);
        var savedJob = JsonSerializer.Deserialize<Job>(jsonContent);
        
        savedJob.Should().NotBeNull();
        savedJob!.Id.Should().Be(job.Id);
        savedJob.Username.Should().Be(job.Username);
        savedJob.Title.Should().Be(job.Title);
        savedJob.Status.Should().Be(job.Status);
    }

    [Fact]
    public async Task SaveJobAsync_WithUpdatedJob_ShouldOverwriteExistingFile()
    {
        // Arrange
        var job = CreateTestJob();
        await _service.SaveJobAsync(job);

        // Act - Update job and save again
        job.Status = JobStatus.Completed;
        job.Output = "Job completed successfully";
        job.CompletedAt = DateTime.UtcNow;
        await _service.SaveJobAsync(job);

        // Assert
        var expectedFilePath = Path.Combine(_testJobsPath, $"{job.Id}.job.json");
        var jsonContent = await File.ReadAllTextAsync(expectedFilePath);
        var savedJob = JsonSerializer.Deserialize<Job>(jsonContent);
        
        savedJob!.Status.Should().Be(JobStatus.Completed);
        savedJob.Output.Should().Be("Job completed successfully");
        savedJob.CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task SaveJobAsync_WithNullJob_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        await FluentActions.Invoking(() => _service.SaveJobAsync(null!))
            .Should().ThrowAsync<ArgumentNullException>();
    }

    #endregion

    #region LoadJobAsync Tests

    [Fact]
    public async Task LoadJobAsync_WithExistingJob_ShouldReturnJob()
    {
        // Arrange
        var job = CreateTestJob();
        await _service.SaveJobAsync(job);

        // Act
        var loadedJob = await _service.LoadJobAsync(job.Id);

        // Assert
        loadedJob.Should().NotBeNull();
        loadedJob!.Id.Should().Be(job.Id);
        loadedJob.Username.Should().Be(job.Username);
        loadedJob.Title.Should().Be(job.Title);
        loadedJob.Status.Should().Be(job.Status);
        loadedJob.Options.CidxAware.Should().Be(job.Options.CidxAware);
    }

    [Fact]
    public async Task LoadJobAsync_WithNonExistentJob_ShouldReturnNull()
    {
        // Arrange
        var nonExistentJobId = Guid.NewGuid();

        // Act
        var loadedJob = await _service.LoadJobAsync(nonExistentJobId);

        // Assert
        loadedJob.Should().BeNull();
    }

    [Fact]
    public async Task LoadJobAsync_WithCorruptedFile_ShouldReturnNullAndLogError()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        var filePath = Path.Combine(_testJobsPath, $"{jobId}.job.json");
        await File.WriteAllTextAsync(filePath, "{ invalid json content");

        // Act
        var loadedJob = await _service.LoadJobAsync(jobId);

        // Assert
        loadedJob.Should().BeNull();
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Failed to load job")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion

    #region LoadAllJobsAsync Tests

    [Fact]
    public async Task LoadAllJobsAsync_WithMultipleJobs_ShouldReturnAllJobs()
    {
        // Arrange
        var job1 = CreateTestJob("user1", "Job 1");
        var job2 = CreateTestJob("user2", "Job 2");
        var job3 = CreateTestJob("user1", "Job 3");

        await _service.SaveJobAsync(job1);
        await _service.SaveJobAsync(job2);
        await _service.SaveJobAsync(job3);

        // Act
        var allJobs = await _service.LoadAllJobsAsync();

        // Assert
        allJobs.Should().HaveCount(3);
        allJobs.Should().Contain(j => j.Id == job1.Id);
        allJobs.Should().Contain(j => j.Id == job2.Id);
        allJobs.Should().Contain(j => j.Id == job3.Id);
    }

    [Fact]
    public async Task LoadAllJobsAsync_WithNoJobs_ShouldReturnEmptyList()
    {
        // Act
        var allJobs = await _service.LoadAllJobsAsync();

        // Assert
        allJobs.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadAllJobsAsync_WithSomeCorruptedFiles_ShouldReturnOnlyValidJobs()
    {
        // Arrange
        var validJob = CreateTestJob();
        await _service.SaveJobAsync(validJob);

        // Create corrupted file
        var corruptedFilePath = Path.Combine(_testJobsPath, $"{Guid.NewGuid()}.job.json");
        await File.WriteAllTextAsync(corruptedFilePath, "{ corrupted json");

        // Act
        var allJobs = await _service.LoadAllJobsAsync();

        // Assert
        allJobs.Should().HaveCount(1);
        allJobs[0].Id.Should().Be(validJob.Id);
    }

    #endregion

    #region LoadJobsForUserAsync Tests

    [Fact]
    public async Task LoadJobsForUserAsync_WithUserJobs_ShouldReturnOnlyUserJobs()
    {
        // Arrange
        var user1Job1 = CreateTestJob("user1", "User1 Job1");
        var user1Job2 = CreateTestJob("user1", "User1 Job2");
        var user2Job1 = CreateTestJob("user2", "User2 Job1");

        await _service.SaveJobAsync(user1Job1);
        await _service.SaveJobAsync(user1Job2);
        await _service.SaveJobAsync(user2Job1);

        // Act
        var user1Jobs = await _service.LoadJobsForUserAsync("user1");

        // Assert
        user1Jobs.Should().HaveCount(2);
        user1Jobs.Should().OnlyContain(j => j.Username == "user1");
        user1Jobs.Should().Contain(j => j.Id == user1Job1.Id);
        user1Jobs.Should().Contain(j => j.Id == user1Job2.Id);
    }

    [Fact]
    public async Task LoadJobsForUserAsync_WithNonExistentUser_ShouldReturnEmptyList()
    {
        // Arrange
        var job = CreateTestJob("existingUser");
        await _service.SaveJobAsync(job);

        // Act
        var userJobs = await _service.LoadJobsForUserAsync("nonExistentUser");

        // Assert
        userJobs.Should().BeEmpty();
    }

    #endregion

    #region DeleteJobAsync Tests

    [Fact]
    public async Task DeleteJobAsync_WithExistingJob_ShouldDeleteFile()
    {
        // Arrange
        var job = CreateTestJob();
        await _service.SaveJobAsync(job);
        var filePath = Path.Combine(_testJobsPath, $"{job.Id}.job.json");
        File.Exists(filePath).Should().BeTrue();

        // Act
        await _service.DeleteJobAsync(job.Id);

        // Assert
        File.Exists(filePath).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteJobAsync_WithNonExistentJob_ShouldNotThrow()
    {
        // Arrange
        var nonExistentJobId = Guid.NewGuid();

        // Act & Assert
        await FluentActions.Invoking(() => _service.DeleteJobAsync(nonExistentJobId))
            .Should().NotThrowAsync();
    }

    #endregion

    #region CleanupOldJobsAsync Tests

    [Fact]
    public async Task CleanupOldJobsAsync_WithOldCompletedJobs_ShouldDeleteOldJobs()
    {
        // Arrange
        _mockConfiguration.Setup(c => c["Jobs:RetentionDays"]).Returns("7"); // 7 days retention
        
        var oldJob = CreateTestJob("user1", "Old Job");
        oldJob.Status = JobStatus.Completed;
        oldJob.CompletedAt = DateTime.UtcNow.AddDays(-10); // 10 days old
        
        var recentJob = CreateTestJob("user1", "Recent Job");
        recentJob.Status = JobStatus.Completed;
        recentJob.CompletedAt = DateTime.UtcNow.AddDays(-3); // 3 days old
        
        var runningJob = CreateTestJob("user1", "Running Job");
        runningJob.Status = JobStatus.Running;
        runningJob.CreatedAt = DateTime.UtcNow.AddDays(-10); // Old but still running

        await _service.SaveJobAsync(oldJob);
        await _service.SaveJobAsync(recentJob);
        await _service.SaveJobAsync(runningJob);

        // Act
        await _service.CleanupOldJobsAsync();

        // Assert
        var remainingJobs = await _service.LoadAllJobsAsync();
        remainingJobs.Should().HaveCount(2);
        remainingJobs.Should().NotContain(j => j.Id == oldJob.Id);
        remainingJobs.Should().Contain(j => j.Id == recentJob.Id);
        remainingJobs.Should().Contain(j => j.Id == runningJob.Id);
    }

    [Fact]
    public async Task CleanupOldJobsAsync_WithDefaultRetention_ShouldUse30Days()
    {
        // Arrange
        _mockConfiguration.Setup(c => c["Jobs:RetentionDays"]).Returns((string?)null); // Use default
        
        var oldJob = CreateTestJob();
        oldJob.Status = JobStatus.Completed;
        oldJob.CompletedAt = DateTime.UtcNow.AddDays(-35); // 35 days old (beyond default 30)
        
        await _service.SaveJobAsync(oldJob);

        // Act
        await _service.CleanupOldJobsAsync();

        // Assert
        var remainingJobs = await _service.LoadAllJobsAsync();
        remainingJobs.Should().BeEmpty();
    }

    [Theory]
    [InlineData(JobStatus.Created)]
    [InlineData(JobStatus.Queued)]
    [InlineData(JobStatus.Running)]
    [InlineData(JobStatus.GitPulling)]
    [InlineData(JobStatus.CidxIndexing)]
    public async Task CleanupOldJobsAsync_WithNonFinishedJobs_ShouldNotDeleteThem(JobStatus nonFinishedStatus)
    {
        // Arrange
        _mockConfiguration.Setup(c => c["Jobs:RetentionDays"]).Returns("7");
        
        var oldNonFinishedJob = CreateTestJob();
        oldNonFinishedJob.Status = nonFinishedStatus;
        oldNonFinishedJob.CreatedAt = DateTime.UtcNow.AddDays(-10);
        
        await _service.SaveJobAsync(oldNonFinishedJob);

        // Act
        await _service.CleanupOldJobsAsync();

        // Assert
        var remainingJobs = await _service.LoadAllJobsAsync();
        remainingJobs.Should().HaveCount(1);
        remainingJobs[0].Id.Should().Be(oldNonFinishedJob.Id);
    }

    #endregion

    #region Helper Methods

    private Job CreateTestJob(string username = "testuser", string title = "Test Job")
    {
        return new Job
        {
            Id = Guid.NewGuid(),
            Username = username,
            Title = title,
            Prompt = "Test prompt for the job",
            Repository = "test-repo",
            Status = JobStatus.Created,
            CowPath = Path.Combine(_testWorkspacePath, "jobs", Guid.NewGuid().ToString()),
            CreatedAt = DateTime.UtcNow,
            Options = new JobOptions
            {
                CidxAware = true,
                GitAware = true,
                TimeoutSeconds = 300
            },
            UploadedFiles = new List<string> { "document.pdf", "script.py" }
        };
    }

    #endregion
}