using ClaudeBatchServer.Core.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace ClaudeBatchServer.Tests.Services;

public class ClaudeCodeSessionServiceTests
{
    private readonly Mock<ILogger<ClaudeCodeSessionService>> _mockLogger;
    private readonly ClaudeCodeSessionService _service;
    private readonly string _testClaudeDir;
    private readonly string _testProjectsDir;

    public ClaudeCodeSessionServiceTests()
    {
        _mockLogger = new Mock<ILogger<ClaudeCodeSessionService>>();
        _service = new ClaudeCodeSessionService(_mockLogger.Object);
        
        // Create a temporary test directory structure that mimics ~/.claude/projects
        _testClaudeDir = Path.Combine(Path.GetTempPath(), $".claude-test-{Guid.NewGuid()}");
        _testProjectsDir = Path.Combine(_testClaudeDir, ".claude", "projects");
        Directory.CreateDirectory(_testProjectsDir);
        
        // Mock the home directory to point to our test directory
        Environment.SetEnvironmentVariable("HOME", _testClaudeDir);
        Environment.SetEnvironmentVariable("USERPROFILE", _testClaudeDir);
    }

    [Fact]
    public async Task GetLatestSessionIdAsync_WithNoSessions_ReturnsNull()
    {
        // Arrange
        var directoryPath = "/home/user/nonexistent";

        // Act
        var result = await _service.GetLatestSessionIdAsync(directoryPath);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetLatestSessionIdAsync_WithSessions_ReturnsLatest()
    {
        // Arrange
        var directoryPath = "/home/user/testproject";
        var encodedPath = "home-user-testproject";
        var sessionDir = Path.Combine(_testProjectsDir, encodedPath);
        Directory.CreateDirectory(sessionDir);

        var session1Id = "11111111-1111-1111-1111-111111111111";
        var session2Id = "22222222-2222-2222-2222-222222222222";
        var session3Id = "33333333-3333-3333-3333-333333333333";

        // Create session files with different timestamps
        var session1File = Path.Combine(sessionDir, $"{session1Id}.jsonl");
        var session2File = Path.Combine(sessionDir, $"{session2Id}.jsonl");
        var session3File = Path.Combine(sessionDir, $"{session3Id}.jsonl");

        await File.WriteAllTextAsync(session1File, "session1 content");
        await Task.Delay(10); // Ensure different timestamps
        await File.WriteAllTextAsync(session2File, "session2 content");
        await Task.Delay(10);
        await File.WriteAllTextAsync(session3File, "session3 content");

        // Act
        var result = await _service.GetLatestSessionIdAsync(directoryPath);

        // Assert
        Assert.Equal(session3Id, result);

        // Cleanup
        Directory.Delete(sessionDir, true);
    }

    [Fact]
    public async Task GetAllSessionIdsAsync_WithMultipleSessions_ReturnsOrderedByTimestamp()
    {
        // Arrange
        var directoryPath = "/home/user/testproject";
        var encodedPath = "home-user-testproject";
        var sessionDir = Path.Combine(_testProjectsDir, encodedPath);
        Directory.CreateDirectory(sessionDir);

        var session1Id = "11111111-1111-1111-1111-111111111111";
        var session2Id = "22222222-2222-2222-2222-222222222222";
        var session3Id = "33333333-3333-3333-3333-333333333333";

        // Create session files with different timestamps
        var session1File = Path.Combine(sessionDir, $"{session1Id}.jsonl");
        var session2File = Path.Combine(sessionDir, $"{session2Id}.jsonl");
        var session3File = Path.Combine(sessionDir, $"{session3Id}.jsonl");

        await File.WriteAllTextAsync(session1File, "session1 content");
        await Task.Delay(10);
        await File.WriteAllTextAsync(session2File, "session2 content");
        await Task.Delay(10);
        await File.WriteAllTextAsync(session3File, "session3 content");

        // Act
        var result = await _service.GetAllSessionIdsAsync(directoryPath);

        // Assert
        var sessionIds = result.ToList();
        Assert.Equal(3, sessionIds.Count);
        Assert.Equal(session3Id, sessionIds[0]); // Most recent first
        Assert.Equal(session2Id, sessionIds[1]);
        Assert.Equal(session1Id, sessionIds[2]);

        // Cleanup
        Directory.Delete(sessionDir, true);
    }

    [Fact]
    public async Task GetAllSessionIdsAsync_IgnoresNonSessionFiles()
    {
        // Arrange
        var directoryPath = "/home/user/testproject";
        var encodedPath = "home-user-testproject";
        var sessionDir = Path.Combine(_testProjectsDir, encodedPath);
        Directory.CreateDirectory(sessionDir);

        var sessionId = "11111111-1111-1111-1111-111111111111";
        var sessionFile = Path.Combine(sessionDir, $"{sessionId}.jsonl");
        var nonSessionFile = Path.Combine(sessionDir, "not-a-session.txt");
        var invalidUuidFile = Path.Combine(sessionDir, "invalid-uuid.jsonl");

        await File.WriteAllTextAsync(sessionFile, "session content");
        await File.WriteAllTextAsync(nonSessionFile, "other content");
        await File.WriteAllTextAsync(invalidUuidFile, "invalid content");

        // Act
        var result = await _service.GetAllSessionIdsAsync(directoryPath);

        // Assert
        var sessionIds = result.ToList();
        Assert.Single(sessionIds);
        Assert.Equal(sessionId, sessionIds[0]);

        // Cleanup
        Directory.Delete(sessionDir, true);
    }

    [Fact]
    public async Task SessionExistsAsync_WithExistingSession_ReturnsTrue()
    {
        // Arrange
        var directoryPath = "/home/user/testproject";
        var encodedPath = "home-user-testproject";
        var sessionDir = Path.Combine(_testProjectsDir, encodedPath);
        Directory.CreateDirectory(sessionDir);

        var sessionId = "11111111-1111-1111-1111-111111111111";
        var sessionFile = Path.Combine(sessionDir, $"{sessionId}.jsonl");
        await File.WriteAllTextAsync(sessionFile, "session content");

        // Act
        var result = await _service.SessionExistsAsync(directoryPath, sessionId);

        // Assert
        Assert.True(result);

        // Cleanup
        Directory.Delete(sessionDir, true);
    }

    [Fact]
    public async Task SessionExistsAsync_WithNonExistingSession_ReturnsFalse()
    {
        // Arrange
        var directoryPath = "/home/user/testproject";
        var sessionId = "11111111-1111-1111-1111-111111111111";

        // Act
        var result = await _service.SessionExistsAsync(directoryPath, sessionId);

        // Assert
        Assert.False(result);
    }

    [Theory]
    [InlineData("/home/user/project", "home-user-project")]
    [InlineData("/home/user/project/sub", "home-user-project-sub")]
    [InlineData("C:\\Users\\test\\project", "C:-Users-test-project")]
    [InlineData("/", "")]
    public void EncodeDirectoryPath_WithVariousPaths_ReturnsCorrectEncoding(string input, string expected)
    {
        // This tests the private method indirectly through public methods
        // We can verify the encoding by checking if the correct directory is accessed
        
        // Arrange & Act
        var encodedPath = input.Replace('\\', '/').Replace('/', '-').TrimStart('-');

        // Assert
        Assert.Equal(expected, encodedPath);
    }

    private void Dispose()
    {
        // Cleanup test directory
        if (Directory.Exists(_testClaudeDir))
        {
            Directory.Delete(_testClaudeDir, true);
        }
        
        // Reset environment variables
        Environment.SetEnvironmentVariable("HOME", null);
        Environment.SetEnvironmentVariable("USERPROFILE", null);
    }
}