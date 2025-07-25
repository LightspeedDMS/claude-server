using FluentAssertions;
using ClaudeBatchServer.Core.Models;

namespace ClaudeBatchServer.Tests.Models;

public class JobTests
{
    [Fact]
    public void Job_DefaultValues_ShouldBeSetCorrectly()
    {
        var job = new Job();

        job.Id.Should().NotBeEmpty();
        job.Username.Should().BeEmpty();
        job.Prompt.Should().BeEmpty();
        job.Repository.Should().BeEmpty();
        job.UploadedFiles.Should().NotBeNull().And.BeEmpty();
        job.Status.Should().Be(JobStatus.Created);
        job.Output.Should().BeEmpty();
        job.ExitCode.Should().BeNull();
        job.CowPath.Should().BeEmpty();
        job.QueuePosition.Should().Be(0);
        job.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
        job.StartedAt.Should().BeNull();
        job.CompletedAt.Should().BeNull();
        job.Options.Should().NotBeNull();
    }

    [Fact]
    public void Job_SetProperties_ShouldUpdateCorrectly()
    {
        var job = new Job
        {
            Username = "testuser",
            Prompt = "Test prompt",
            Repository = "test-repo",
            Status = JobStatus.Running,
            Output = "Test output",
            ExitCode = 0,
            CowPath = "/workspace/test",
            QueuePosition = 1
        };

        job.Username.Should().Be("testuser");
        job.Prompt.Should().Be("Test prompt");
        job.Repository.Should().Be("test-repo");
        job.Status.Should().Be(JobStatus.Running);
        job.Output.Should().Be("Test output");
        job.ExitCode.Should().Be(0);
        job.CowPath.Should().Be("/workspace/test");
        job.QueuePosition.Should().Be(1);
    }

    [Fact]
    public void JobOptions_DefaultValues_ShouldBeSetCorrectly()
    {
        var options = new JobOptions();

        options.TimeoutSeconds.Should().Be(300);
        options.AutoCleanup.Should().BeTrue();
        options.Environment.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void JobOptions_SetProperties_ShouldUpdateCorrectly()
    {
        var options = new JobOptions
        {
            TimeoutSeconds = 600,
            AutoCleanup = false,
            Environment = new Dictionary<string, string> { { "TEST", "value" } }
        };

        options.TimeoutSeconds.Should().Be(600);
        options.AutoCleanup.Should().BeFalse();
        options.Environment.Should().Contain("TEST", "value");
    }

    [Theory]
    [InlineData(JobStatus.Created)]
    [InlineData(JobStatus.Queued)]
    [InlineData(JobStatus.Running)]
    [InlineData(JobStatus.Completed)]
    [InlineData(JobStatus.Failed)]
    [InlineData(JobStatus.Timeout)]
    [InlineData(JobStatus.Terminated)]
    public void JobStatus_AllValues_ShouldBeValid(JobStatus status)
    {
        var job = new Job { Status = status };

        job.Status.Should().Be(status);
    }

    [Fact]
    public void Job_UploadedFiles_ShouldBeModifiable()
    {
        var job = new Job();

        job.UploadedFiles.Add("document.pdf");
        job.UploadedFiles.Add("script.py");

        job.UploadedFiles.Should().HaveCount(2);
        job.UploadedFiles.Should().Contain("document.pdf");
        job.UploadedFiles.Should().Contain("script.py");
    }

    [Fact]
    public void Job_TimeStamps_ShouldBeSettable()
    {
        var now = DateTime.UtcNow;
        var job = new Job
        {
            StartedAt = now,
            CompletedAt = now.AddMinutes(5)
        };

        job.StartedAt.Should().Be(now);
        job.CompletedAt.Should().Be(now.AddMinutes(5));
    }
}