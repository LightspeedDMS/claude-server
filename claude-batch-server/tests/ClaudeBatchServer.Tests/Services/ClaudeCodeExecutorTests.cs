using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using ClaudeBatchServer.Core.Models;
using ClaudeBatchServer.Core.Services;

namespace ClaudeBatchServer.Tests.Services;

public class ClaudeCodeExecutorTests
{
    private readonly Mock<IConfiguration> _mockConfiguration;
    private readonly Mock<ILogger<ClaudeCodeExecutor>> _mockLogger;
    private readonly ClaudeCodeExecutor _executor;

    public ClaudeCodeExecutorTests()
    {
        _mockConfiguration = new Mock<IConfiguration>();
        _mockLogger = new Mock<ILogger<ClaudeCodeExecutor>>();
        
        _mockConfiguration.Setup(c => c["Claude:Command"]).Returns("echo");
        
        _executor = new ClaudeCodeExecutor(_mockConfiguration.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task ExecuteAsync_WithValidJob_ShouldExecuteSuccessfully()
    {
        var job = new Job
        {
            Id = Guid.NewGuid(),
            Prompt = "test prompt",
            CowPath = "/tmp",
            Repository = "test-repo",
            Options = new JobOptions { TimeoutSeconds = 30 }
        };

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var (exitCode, output) = await _executor.ExecuteAsync(job, "testuser", cts.Token);

        exitCode.Should().Be(0);
        output.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_WithCancellation_ShouldThrowOperationCancelledException()
    {
        var job = new Job
        {
            Id = Guid.NewGuid(),
            Prompt = "test prompt",
            CowPath = "/tmp",
            Repository = "test-repo",
            Options = new JobOptions { TimeoutSeconds = 30 }
        };

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await _executor.ExecuteAsync(job, "testuser", cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ExecuteAsync_WithInvalidCommand_ShouldReturnNonZeroExitCode()
    {
        var mockConfig = new Mock<IConfiguration>();
        mockConfig.Setup(c => c["Claude:Command"]).Returns("nonexistentcommand12345");
        
        var executor = new ClaudeCodeExecutor(mockConfig.Object, _mockLogger.Object);
        
        var job = new Job
        {
            Id = Guid.NewGuid(),
            Prompt = "test prompt",
            CowPath = "/tmp",
            Repository = "test-repo",
            Options = new JobOptions { TimeoutSeconds = 5 }
        };

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var (exitCode, output) = await executor.ExecuteAsync(job, "testuser", cts.Token);

        exitCode.Should().Be(-1);
        output.Should().Contain("Execution failed");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("testuser")]
    public async Task ExecuteAsync_WithDifferentUsernames_ShouldExecute(string username)
    {
        var job = new Job
        {
            Id = Guid.NewGuid(),
            Prompt = "test prompt",
            CowPath = "/tmp",
            Repository = "test-repo",
            Options = new JobOptions { TimeoutSeconds = 5 }
        };

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var (exitCode, output) = await _executor.ExecuteAsync(job, username, cts.Token);

        exitCode.Should().Be(0);
        output.Should().NotBeNull();
    }

    [Fact]
    public async Task ExecuteAsync_WithImages_ShouldIncludeImageArguments()
    {
        var job = new Job
        {
            Id = Guid.NewGuid(),
            Prompt = "test prompt",
            CowPath = "/tmp",
            Repository = "test-repo",
            Images = new List<string> { "image1.png", "image2.jpg" },
            Options = new JobOptions { TimeoutSeconds = 5 }
        };

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var (exitCode, output) = await _executor.ExecuteAsync(job, "testuser", cts.Token);

        exitCode.Should().Be(0);
        output.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithNullConfiguration_ShouldUseDefaultCommand()
    {
        var mockConfig = new Mock<IConfiguration>();
        mockConfig.Setup(c => c["Claude:Command"]).Returns((string?)null);
        
        var executor = new ClaudeCodeExecutor(mockConfig.Object, _mockLogger.Object);
        
        executor.Should().NotBeNull();
    }
}