using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using ClaudeBatchServer.Core.DTOs;
using ClaudeBatchServer.Core.Models;
using ClaudeBatchServer.Core.Services;

namespace ClaudeBatchServer.Tests.Services;

public class JobServiceTests : IDisposable
{
    private readonly Mock<IRepositoryService> _mockRepositoryService;
    private readonly Mock<IClaudeCodeExecutor> _mockClaudeExecutor;
    private readonly Mock<IConfiguration> _mockConfiguration;
    private readonly Mock<ILogger<JobService>> _mockLogger;
    private readonly JobService _jobService;
    private readonly string _testWorkspaceRoot;

    public JobServiceTests()
    {
        _mockRepositoryService = new Mock<IRepositoryService>();
        _mockClaudeExecutor = new Mock<IClaudeCodeExecutor>();
        _mockConfiguration = new Mock<IConfiguration>();
        _mockLogger = new Mock<ILogger<JobService>>();

        // Create a unique test workspace for this test run
        _testWorkspaceRoot = Path.Combine(Path.GetTempPath(), "job-service-tests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testWorkspaceRoot);

        _mockConfiguration.Setup(c => c["Jobs:MaxConcurrent"]).Returns("5");
        _mockConfiguration.Setup(c => c["Jobs:TimeoutHours"]).Returns("24");

        // Add mock for job persistence service
        var mockJobPersistenceService = new Mock<IJobPersistenceService>();
        
        _jobService = new JobService(
            _mockRepositoryService.Object,
            _mockClaudeExecutor.Object,
            mockJobPersistenceService.Object,
            _mockConfiguration.Object,
            _mockLogger.Object);
    }

    [Fact]
    public async Task CreateJobAsync_WithValidRequest_ShouldCreateJob()
    {
        var repository = new Repository { Name = "test-repo", Path = "/test/path" };
        var request = new CreateJobRequest
        {
            Prompt = "Test prompt",
            Repository = "test-repo",
            Images = new List<string> { "image1.png" },
            Options = new JobOptionsDto { Timeout = 300, CidxAware = false }
        };

        _mockRepositoryService
            .Setup(r => r.GetRepositoryAsync("test-repo"))
            .ReturnsAsync(repository);
        
        var testJobPath = Path.Combine(_testWorkspaceRoot, Guid.NewGuid().ToString());
        _mockRepositoryService
            .Setup(r => r.CreateCowCloneAsync("test-repo", It.IsAny<Guid>()))
            .ReturnsAsync(testJobPath);
        
        _mockClaudeExecutor
            .Setup(c => c.GenerateJobTitleAsync("Test prompt", It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Generated Test Title");

        var result = await _jobService.CreateJobAsync(request, "testuser");

        result.Should().NotBeNull();
        result.Status.Should().Be("created");
        result.User.Should().Be("testuser");
        result.CowPath.Should().Be(testJobPath);
        result.Title.Should().Be("Generated Test Title");
    }

    [Fact]
    public async Task CreateJobAsync_WithNonExistentRepository_ShouldThrowException()
    {
        var request = new CreateJobRequest
        {
            Repository = "nonexistent-repo",
            Prompt = "Test prompt",
            Options = new JobOptionsDto { CidxAware = false }
        };

        _mockRepositoryService
            .Setup(r => r.GetRepositoryAsync("nonexistent-repo"))
            .ReturnsAsync((Repository?)null);

        var act = async () => await _jobService.CreateJobAsync(request, "testuser");

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("Repository 'nonexistent-repo' not found");
    }

    [Fact]
    public async Task StartJobAsync_WithValidJob_ShouldQueueJob()
    {
        var repository = new Repository { Name = "test-repo", Path = "/test/path" };
        var createRequest = new CreateJobRequest
        {
            Prompt = "Test prompt",
            Repository = "test-repo",
            Options = new JobOptionsDto { CidxAware = false }
        };

        _mockRepositoryService
            .Setup(r => r.GetRepositoryAsync("test-repo"))
            .ReturnsAsync(repository);
        
        var testJobPath = Path.Combine(_testWorkspaceRoot, Guid.NewGuid().ToString());
        _mockRepositoryService
            .Setup(r => r.CreateCowCloneAsync("test-repo", It.IsAny<Guid>()))
            .ReturnsAsync(testJobPath);
        
        _mockClaudeExecutor
            .Setup(c => c.GenerateJobTitleAsync("Test prompt", It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Test Job Title");

        var createResult = await _jobService.CreateJobAsync(createRequest, "testuser");
        var startResult = await _jobService.StartJobAsync(createResult.JobId, "testuser");

        startResult.Should().NotBeNull();
        startResult.Status.Should().Be("queued");
        startResult.QueuePosition.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task StartJobAsync_WithNonExistentJob_ShouldThrowException()
    {
        var jobId = Guid.NewGuid();

        var act = async () => await _jobService.StartJobAsync(jobId, "testuser");

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("Job not found");
    }

    [Fact]
    public async Task StartJobAsync_WithDifferentUser_ShouldThrowUnauthorizedException()
    {
        var repository = new Repository { Name = "test-repo", Path = "/test/path" };
        var createRequest = new CreateJobRequest
        {
            Repository = "test-repo",
            Prompt = "Test prompt",
            Options = new JobOptionsDto { CidxAware = false }
        };

        _mockRepositoryService
            .Setup(r => r.GetRepositoryAsync("test-repo"))
            .ReturnsAsync(repository);
        
        var testJobPath = Path.Combine(_testWorkspaceRoot, Guid.NewGuid().ToString());
        _mockRepositoryService
            .Setup(r => r.CreateCowCloneAsync("test-repo", It.IsAny<Guid>()))
            .ReturnsAsync(testJobPath);
        
        _mockClaudeExecutor
            .Setup(c => c.GenerateJobTitleAsync("Test prompt", It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Test Job Title");

        var createResult = await _jobService.CreateJobAsync(createRequest, "testuser");

        var act = async () => await _jobService.StartJobAsync(createResult.JobId, "differentuser");

        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("Access denied");
    }

    [Fact]
    public async Task GetJobStatusAsync_WithValidJob_ShouldReturnStatus()
    {
        var repository = new Repository { Name = "test-repo", Path = "/test/path" };
        var createRequest = new CreateJobRequest
        {
            Repository = "test-repo",
            Prompt = "Test prompt",
            Options = new JobOptionsDto { CidxAware = false }
        };

        _mockRepositoryService
            .Setup(r => r.GetRepositoryAsync("test-repo"))
            .ReturnsAsync(repository);
        
        var testJobPath = Path.Combine(_testWorkspaceRoot, Guid.NewGuid().ToString());
        _mockRepositoryService
            .Setup(r => r.CreateCowCloneAsync("test-repo", It.IsAny<Guid>()))
            .ReturnsAsync(testJobPath);
        
        _mockClaudeExecutor
            .Setup(c => c.GenerateJobTitleAsync("Test prompt", It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Test Job Title");

        var createResult = await _jobService.CreateJobAsync(createRequest, "testuser");
        var statusResult = await _jobService.GetJobStatusAsync(createResult.JobId, "testuser");

        statusResult.Should().NotBeNull();
        statusResult!.JobId.Should().Be(createResult.JobId);
        statusResult.Status.Should().Be("created");
        statusResult.Title.Should().Be("Test Job Title");
    }

    [Fact]
    public async Task DeleteJobAsync_WithValidJob_ShouldDeleteJob()
    {
        var repository = new Repository { Name = "test-repo", Path = "/test/path" };
        var createRequest = new CreateJobRequest
        {
            Repository = "test-repo",
            Prompt = "Test prompt",
            Options = new JobOptionsDto { CidxAware = false }
        };

        _mockRepositoryService
            .Setup(r => r.GetRepositoryAsync("test-repo"))
            .ReturnsAsync(repository);
        
        var testJobPath = Path.Combine(_testWorkspaceRoot, Guid.NewGuid().ToString());
        _mockRepositoryService
            .Setup(r => r.CreateCowCloneAsync("test-repo", It.IsAny<Guid>()))
            .ReturnsAsync(testJobPath);
        
        _mockClaudeExecutor
            .Setup(c => c.GenerateJobTitleAsync("Test prompt", It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Test Job Title");

        _mockRepositoryService
            .Setup(r => r.RemoveCowCloneAsync(It.IsAny<string>()))
            .ReturnsAsync(true);

        var createResult = await _jobService.CreateJobAsync(createRequest, "testuser");
        var deleteResult = await _jobService.DeleteJobAsync(createResult.JobId, "testuser");

        deleteResult.Should().NotBeNull();
        deleteResult.Success.Should().BeTrue();
        deleteResult.CowRemoved.Should().BeTrue();
    }

    [Fact]
    public async Task UploadImageAsync_WithValidJob_ShouldUploadImage()
    {
        var repository = new Repository { Name = "test-repo", Path = "/test/path" };
        var createRequest = new CreateJobRequest
        {
            Repository = "test-repo",
            Prompt = "Test prompt",
            Options = new JobOptionsDto { CidxAware = false }
        };

        // Create a test job directory that actually exists
        var testJobPath = Path.Combine(_testWorkspaceRoot, "test-job-id");
        Directory.CreateDirectory(testJobPath);

        _mockRepositoryService
            .Setup(r => r.GetRepositoryAsync("test-repo"))
            .ReturnsAsync(repository);
        
        _mockRepositoryService
            .Setup(r => r.CreateCowCloneAsync("test-repo", It.IsAny<Guid>()))
            .ReturnsAsync(testJobPath);
        
        _mockClaudeExecutor
            .Setup(c => c.GenerateJobTitleAsync("Test prompt", It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Test Job Title");

        var createResult = await _jobService.CreateJobAsync(createRequest, "testuser");
        
        using var imageStream = new MemoryStream(new byte[] { 1, 2, 3, 4 });
        var uploadResult = await _jobService.UploadImageAsync(createResult.JobId, "testuser", "test.png", imageStream);

        uploadResult.Should().NotBeNull();
        uploadResult.Filename.Should().NotBeEmpty();
        uploadResult.Path.Should().Contain(createResult.JobId.ToString());
        
        // Verify the image file was actually created
        var imagesPath = Path.Combine(testJobPath, "images");
        Directory.Exists(imagesPath).Should().BeTrue();
        Directory.GetFiles(imagesPath).Should().HaveCount(1);
    }

    [Fact]
    public async Task GetUserJobsAsync_WithValidUser_ShouldReturnUserJobs()
    {
        var repository = new Repository { Name = "test-repo", Path = "/test/path" };
        var createRequest = new CreateJobRequest
        {
            Repository = "test-repo",
            Prompt = "Test prompt",
            Options = new JobOptionsDto { CidxAware = false }
        };

        _mockRepositoryService
            .Setup(r => r.GetRepositoryAsync("test-repo"))
            .ReturnsAsync(repository);
        
        var testJobPath = Path.Combine(_testWorkspaceRoot, Guid.NewGuid().ToString());
        _mockRepositoryService
            .Setup(r => r.CreateCowCloneAsync("test-repo", It.IsAny<Guid>()))
            .ReturnsAsync(testJobPath);
        
        _mockClaudeExecutor
            .Setup(c => c.GenerateJobTitleAsync("Test prompt", It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Test Job Title");

        await _jobService.CreateJobAsync(createRequest, "testuser");
        var userJobs = await _jobService.GetUserJobsAsync("testuser");

        userJobs.Should().NotBeNull();
        userJobs.Should().HaveCount(1);
        userJobs[0].User.Should().Be("testuser");
        userJobs[0].Title.Should().Be("Test Job Title");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testWorkspaceRoot))
                Directory.Delete(_testWorkspaceRoot, true);
        }
        catch
        {
            // Ignore cleanup errors in tests
        }
    }
}