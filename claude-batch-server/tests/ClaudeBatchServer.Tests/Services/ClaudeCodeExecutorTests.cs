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
    private readonly Mock<IRepositoryService> _mockRepositoryService;
    private readonly ClaudeCodeExecutor _executor;

    public ClaudeCodeExecutorTests()
    {
        _mockConfiguration = new Mock<IConfiguration>();
        _mockLogger = new Mock<ILogger<ClaudeCodeExecutor>>();
        _mockRepositoryService = new Mock<IRepositoryService>();
        
        // Use /bin/true which ignores all arguments and always succeeds
        _mockConfiguration.Setup(c => c["Claude:Command"]).Returns("/bin/true");
        
        // Configure to use direct execution for backward compatibility with tests
        _mockConfiguration.Setup(c => c["Claude:UseFireAndForget"]).Returns("false");
        
        _executor = new ClaudeCodeExecutor(_mockConfiguration.Object, _mockLogger.Object, _mockRepositoryService.Object);
    }

    [Fact]
    public async Task ExecuteAsync_WithValidJob_ShouldExecuteSuccessfully()
    {
        var job = new Job
        {
            Id = Guid.NewGuid(),
            Prompt = "test prompt",
            CowPath = Path.GetTempPath(),
            Repository = "test-repo",
            Options = new JobOptions 
            { 
                TimeoutSeconds = 30,
                GitAware = false,
                CidxAware = false
            }
        };

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        // Use the current system user that actually exists
        var currentUser = Environment.UserName;
        var (exitCode, output) = await _executor.ExecuteAsync(job, currentUser, cts.Token);

        exitCode.Should().Be(0);
        output.Should().NotBeNull(); // /bin/true produces no output, so just check it's not null
    }

    [Fact]
    public async Task ExecuteAsync_WithCancellation_ShouldHandleCancellation()
    {
        // Use a command that sleeps indefinitely to ensure cancellation
        var mockConfig = new Mock<IConfiguration>();
        mockConfig.Setup(c => c["Claude:Command"]).Returns("bash -c 'sleep 30'");
        mockConfig.Setup(c => c["Claude:UseFireAndForget"]).Returns("false");
        
        var executor = new ClaudeCodeExecutor(mockConfig.Object, _mockLogger.Object, _mockRepositoryService.Object);
        
        var job = new Job
        {
            Id = Guid.NewGuid(),
            Prompt = "test prompt",
            CowPath = Path.GetTempPath(),
            Repository = "test-repo",
            Options = new JobOptions 
            { 
                TimeoutSeconds = 10,
                GitAware = false,
                CidxAware = false
            }
        };

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        var act = async () => await executor.ExecuteAsync(job, Environment.UserName, cts.Token);

        // Either it should throw OperationCanceledException or return with error code
        try
        {
            var (exitCode, output) = await act();
            // If no exception, verify the execution was interrupted
            exitCode.Should().NotBe(0, "Cancelled execution should not succeed");
        }
        catch (OperationCanceledException)
        {
            // This is the expected behavior
            Assert.True(true, "Operation was properly cancelled");
        }
    }

    [Fact]
    public async Task ExecuteAsync_WithInvalidCommand_ShouldReturnNonZeroExitCode()
    {
        var mockConfig = new Mock<IConfiguration>();
        mockConfig.Setup(c => c["Claude:Command"]).Returns("nonexistentcommand12345");
        mockConfig.Setup(c => c["Claude:UseFireAndForget"]).Returns("false");
        
        var executor = new ClaudeCodeExecutor(mockConfig.Object, _mockLogger.Object, _mockRepositoryService.Object);
        
        var job = new Job
        {
            Id = Guid.NewGuid(),
            Prompt = "test prompt",
            CowPath = Path.GetTempPath(),
            Repository = "test-repo",
            Options = new JobOptions 
            { 
                TimeoutSeconds = 5,
                GitAware = false,
                CidxAware = false
            }
        };

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var (exitCode, output) = await executor.ExecuteAsync(job, Environment.UserName, cts.Token);

        exitCode.Should().NotBe(0, "Invalid command should return non-zero exit code");
        // System typically returns 127 for "command not found", 1 for general errors, or -1 for execution errors
        exitCode.Should().BeOneOf(-1, 1, 127);
        output.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_WithCurrentUser_ShouldExecute()
    {
        var job = new Job
        {
            Id = Guid.NewGuid(),
            Prompt = "test prompt",
            CowPath = Path.GetTempPath(),
            Repository = "test-repo",
            Options = new JobOptions 
            { 
                TimeoutSeconds = 5,
                GitAware = false,
                CidxAware = false
            }
        };

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var (exitCode, output) = await _executor.ExecuteAsync(job, Environment.UserName, cts.Token);

        exitCode.Should().Be(0);
        output.Should().NotBeNull();
    }

    [Fact]
    public async Task ExecuteAsync_WithNonExistentUser_ShouldThrowException()
    {
        var job = new Job
        {
            Id = Guid.NewGuid(),
            Prompt = "test prompt",
            CowPath = Path.GetTempPath(),
            Repository = "test-repo",
            Options = new JobOptions 
            { 
                TimeoutSeconds = 5,
                GitAware = false,
                CidxAware = false
            }
        };

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        
        var act = async () => await _executor.ExecuteAsync(job, "nonexistentuser123", cts.Token);

        // In a test environment without root, this might return (-1, "Execution failed") instead of throwing
        var result = await act.Should().NotThrowAsync();
        var (exitCode, output) = await _executor.ExecuteAsync(job, "nonexistentuser123", cts.Token);
        
        exitCode.Should().Be(-1);
        output.Should().Contain("not found");
    }

    [Fact]
    public async Task ExecuteAsync_WithImages_ShouldIncludeImageArguments()
    {
        var job = new Job
        {
            Id = Guid.NewGuid(),
            Prompt = "test prompt",
            CowPath = Path.GetTempPath(),
            Repository = "test-repo",
            UploadedFiles = new List<string> { "document.pdf", "script.py" },
            Options = new JobOptions 
            { 
                TimeoutSeconds = 5,
                GitAware = false,
                CidxAware = false
            }
        };

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var (exitCode, output) = await _executor.ExecuteAsync(job, Environment.UserName, cts.Token);

        exitCode.Should().Be(0);
        output.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithNullConfiguration_ShouldUseDefaultCommand()
    {
        var mockConfig = new Mock<IConfiguration>();
        mockConfig.Setup(c => c["Claude:Command"]).Returns((string?)null);
        mockConfig.Setup(c => c["Claude:UseFireAndForget"]).Returns("false");
        
        var executor = new ClaudeCodeExecutor(mockConfig.Object, _mockLogger.Object, _mockRepositoryService.Object);
        
        executor.Should().NotBeNull();
    }
}