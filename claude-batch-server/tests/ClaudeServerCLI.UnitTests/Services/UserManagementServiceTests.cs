using System.Text;
using ClaudeServerCLI.Models;
using ClaudeServerCLI.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace ClaudeServerCLI.UnitTests.Services;

public class UserManagementServiceTests : IDisposable
{
    private readonly Mock<ILogger<UserManagementService>> _mockLogger;
    private readonly UserManagementService _service;
    private readonly string _testDirectory;

    public UserManagementServiceTests()
    {
        _mockLogger = new Mock<ILogger<UserManagementService>>();
        
        // Create a temporary test directory
        _testDirectory = Path.Combine(Path.GetTempPath(), $"claude-user-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDirectory);
        
        // Set working directory to test directory so service can find auth files
        Environment.CurrentDirectory = _testDirectory;
        
        _service = new UserManagementService(_mockLogger.Object);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, true);
        }
    }

    [Fact]
    public void IsValidUsername_WithValidUsernames_ReturnsTrue()
    {
        // Arrange & Act & Assert
        Assert.True(_service.IsValidUsername("alice"));
        Assert.True(_service.IsValidUsername("bob123"));
        Assert.True(_service.IsValidUsername("user_name"));
        Assert.True(_service.IsValidUsername("test-user"));
        Assert.True(_service.IsValidUsername("a23456789012345678901234567890ab")); // 32 chars
    }

    [Fact]
    public void IsValidUsername_WithInvalidUsernames_ReturnsFalse()
    {
        // Arrange & Act & Assert
        Assert.False(_service.IsValidUsername(""));
        Assert.False(_service.IsValidUsername("ab")); // Too short
        Assert.False(_service.IsValidUsername("123user")); // Starts with number
        Assert.False(_service.IsValidUsername("user@name")); // Invalid character
        Assert.False(_service.IsValidUsername("user name")); // Space
        Assert.False(_service.IsValidUsername("a234567890123456789012345678901234")); // Too long (33 chars)
    }

    [Fact]
    public async Task UserExistsAsync_WithNoPasswdFile_ReturnsFalse()
    {
        // Arrange
        var username = "testuser";

        // Act
        var result = await _service.UserExistsAsync(username);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task UserExistsAsync_WithExistingUser_ReturnsTrue()
    {
        // Arrange
        var username = "testuser";
        var passwdContent = $"{username}:x:1000:1000:Test User:/home/{username}:/bin/bash\n";
        await File.WriteAllTextAsync(_service.GetPasswdFilePath(), passwdContent);

        // Act
        var result = await _service.UserExistsAsync(username);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task UserExistsAsync_WithNonExistingUser_ReturnsFalse()
    {
        // Arrange
        var passwdContent = "otheruser:x:1000:1000:Other User:/home/otheruser:/bin/bash\n";
        await File.WriteAllTextAsync(_service.GetPasswdFilePath(), passwdContent);

        // Act
        var result = await _service.UserExistsAsync("testuser");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task AddUserAsync_WithValidInput_CreatesUserSuccessfully()
    {
        // Arrange
        var username = "testuser";
        var password = "testpassword123";

        // Act
        var result = await _service.AddUserAsync(username, password);

        // Assert
        Assert.True(result.Success, $"Expected success but got: {result.Message} | Details: {result.ErrorDetails}");
        Assert.Contains("successfully added", result.Message);
        
        // Verify passwd file
        var passwdContent = await File.ReadAllTextAsync(_service.GetPasswdFilePath());
        Assert.Contains($"{username}:x:1000:1000:", passwdContent);
        
        // Verify shadow file
        var shadowContent = await File.ReadAllTextAsync(_service.GetShadowFilePath());
        Assert.Contains($"{username}:", shadowContent);
        Assert.Contains("$6$", shadowContent); // SHA-512 hash format
    }

    [Fact]
    public async Task AddUserAsync_WithCustomParameters_CreatesUserWithCustomValues()
    {
        // Arrange
        var username = "customuser";
        var password = "password123";
        var uid = 1001;
        var gid = 1001;
        var homeDir = "/custom/home";
        var shell = "/bin/zsh";

        // Act
        var result = await _service.AddUserAsync(username, password, uid, gid, homeDir, shell);

        // Assert
        Assert.True(result.Success);
        
        var passwdContent = await File.ReadAllTextAsync(_service.GetPasswdFilePath());
        Assert.Contains($"{username}:x:{uid}:{gid}:", passwdContent);
        Assert.Contains($":{homeDir}:{shell}", passwdContent);
    }

    [Fact]
    public async Task AddUserAsync_WithInvalidUsername_ReturnsError()
    {
        // Arrange
        var invalidUsername = "123invalid";
        var password = "password123";

        // Act
        var result = await _service.AddUserAsync(invalidUsername, password);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Invalid username format", result.Message);
    }

    [Fact]
    public async Task AddUserAsync_WithExistingUser_ReturnsError()
    {
        // Arrange
        var username = "testuser";
        var password = "password123";
        
        // Create existing user
        await _service.AddUserAsync(username, password);

        // Act - Try to add same user again
        var result = await _service.AddUserAsync(username, "newpassword");

        // Assert
        Assert.False(result.Success);
        Assert.Contains("already exists", result.Message);
    }

    [Fact]
    public async Task RemoveUserAsync_WithExistingUser_RemovesUserSuccessfully()
    {
        // Arrange
        var username = "testuser";
        var password = "password123";
        
        // Create user first
        await _service.AddUserAsync(username, password);
        Assert.True(await _service.UserExistsAsync(username));

        // Act
        var result = await _service.RemoveUserAsync(username);

        // Assert
        Assert.True(result.Success);
        Assert.Contains("successfully removed", result.Message);
        Assert.False(await _service.UserExistsAsync(username));
        
        // Verify backup was created
        Assert.Contains("Backups created", result.BackupFile);
    }

    [Fact]
    public async Task RemoveUserAsync_WithNonExistingUser_ReturnsError()
    {
        // Arrange
        var username = "nonexistent";

        // Act
        var result = await _service.RemoveUserAsync(username);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("does not exist", result.Message);
    }

    [Fact]
    public async Task RemoveUserAsync_WithMissingFiles_ReturnsError()
    {
        // Arrange
        var username = "testuser";

        // Act (no files exist)
        var result = await _service.RemoveUserAsync(username);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("not found", result.Message);
    }

    [Fact]
    public async Task UpdateUserPasswordAsync_WithExistingUser_UpdatesPasswordSuccessfully()
    {
        // Arrange
        var username = "testuser";
        var oldPassword = "oldpassword123";
        var newPassword = "newpassword456";
        
        // Create user first
        await _service.AddUserAsync(username, oldPassword);
        var originalShadowContent = await File.ReadAllTextAsync(_service.GetShadowFilePath());

        // Act
        var result = await _service.UpdateUserPasswordAsync(username, newPassword);

        // Assert
        Assert.True(result.Success);
        Assert.Contains("successfully updated", result.Message);
        
        // Verify shadow file was updated
        var updatedShadowContent = await File.ReadAllTextAsync(_service.GetShadowFilePath());
        Assert.NotEqual(originalShadowContent, updatedShadowContent);
        Assert.Contains($"{username}:", updatedShadowContent);
        Assert.Contains("$6$", updatedShadowContent); // New hash format
        
        // Verify backup was created  
        Assert.Contains("Backup created", result.BackupFile);
    }

    [Fact]
    public async Task UpdateUserPasswordAsync_WithNonExistingUser_ReturnsError()
    {
        // Arrange
        var username = "nonexistent";
        var password = "password123";

        // Act
        var result = await _service.UpdateUserPasswordAsync(username, password);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("does not exist", result.Message);
    }

    [Fact]
    public async Task ListUsersAsync_WithNoUsers_ReturnsEmptyList()
    {
        // Act
        var users = await _service.ListUsersAsync();

        // Assert
        Assert.Empty(users);
    }

    [Fact]
    public async Task ListUsersAsync_WithMultipleUsers_ReturnsAllUsers()
    {
        // Arrange
        await _service.AddUserAsync("alice", "password1", 1001, 1001, "/home/alice", "/bin/bash");
        await _service.AddUserAsync("bob", "password2", 1002, 1002, "/home/bob", "/bin/zsh");
        await _service.AddUserAsync("charlie", "password3", 1003, 1003, "/home/charlie", "/bin/fish");

        // Act
        var users = await _service.ListUsersAsync();

        // Assert
        var userList = users.ToList();
        Assert.Equal(3, userList.Count);
        
        var alice = userList.First(u => u.Username == "alice");
        Assert.Equal(1001, alice.Uid);
        Assert.Equal(1001, alice.Gid);
        Assert.Equal("/home/alice", alice.HomeDirectory);
        Assert.Equal("/bin/bash", alice.Shell);
        Assert.Equal(UserStatus.Active, alice.Status);
        Assert.True(alice.HasPassword);
        Assert.NotNull(alice.LastPasswordChange);
        
        var bob = userList.First(u => u.Username == "bob");
        Assert.Equal("bob", bob.Username);
        Assert.Equal(1002, bob.Uid);
        
        var charlie = userList.First(u => u.Username == "charlie");
        Assert.Equal("charlie", charlie.Username);
        Assert.Equal(1003, charlie.Uid);
    }

    [Fact]
    public async Task ListUsersAsync_WithMissingShadowFile_ReturnsUsersWithNoShadowStatus()
    {
        // Arrange - Create passwd file but not shadow file
        var passwdContent = "testuser:x:1000:1000:Test User:/home/testuser:/bin/bash\n";
        await File.WriteAllTextAsync(_service.GetPasswdFilePath(), passwdContent);

        // Act
        var users = await _service.ListUsersAsync();

        // Assert
        var userList = users.ToList();
        Assert.Single(userList);
        
        var user = userList.First();
        Assert.Equal("testuser", user.Username);
        Assert.Equal(UserStatus.NoShadowEntry, user.Status);
        Assert.False(user.HasPassword);
        Assert.Null(user.LastPasswordChange);
    }

    [Fact]
    public void GetPasswdFilePath_ReturnsCorrectPath()
    {
        // Act
        var path = _service.GetPasswdFilePath();

        // Assert
        Assert.EndsWith("claude-server-passwd", path);
        Assert.Contains(_testDirectory, path);
    }

    [Fact]
    public void GetShadowFilePath_ReturnsCorrectPath()
    {
        // Act
        var path = _service.GetShadowFilePath();

        // Assert
        Assert.EndsWith("claude-server-shadow", path);
        Assert.Contains(_testDirectory, path);
    }

    [Fact]
    public async Task AddUserAsync_CreatesBackupWhenFilesExist()
    {
        // Arrange
        var username1 = "user1";
        var username2 = "user2";
        var password = "password123";
        
        // Create first user to establish files
        await _service.AddUserAsync(username1, password);

        // Act - Add second user (should create backups)
        var result = await _service.AddUserAsync(username2, password);

        // Assert
        Assert.True(result.Success);
        Assert.Contains("Backups created", result.BackupFile);
        
        // Verify backup files exist
        var backupFiles = Directory.GetFiles(_testDirectory, "*.backup.*");
        Assert.True(backupFiles.Length >= 2); // passwd and shadow backups
    }

    [Fact]
    public async Task AddUserAsync_WithSpecialCharactersInPassword_HandlesCorrectly()
    {
        // Arrange
        var username = "testuser";
        var password = "p@ssw0rd!@#$%^&*()_+-={}[]|\\:;\"'<>?,./";

        // Act
        var result = await _service.AddUserAsync(username, password);

        // Assert
        Assert.True(result.Success);
        
        // Verify user was created
        Assert.True(await _service.UserExistsAsync(username));
        
        // Verify shadow file contains hash
        var shadowContent = await File.ReadAllTextAsync(_service.GetShadowFilePath());
        Assert.Contains($"{username}:", shadowContent);
        Assert.Contains("$6$", shadowContent);
    }

    [Fact]
    public async Task UpdateUserPasswordAsync_PreservesOtherShadowFields()
    {
        // Arrange
        var username = "testuser";
        var password = "password123";
        var newPassword = "newpassword456";
        
        // Create user
        await _service.AddUserAsync(username, password);
        
        // Get original shadow entry
        var originalShadowContent = await File.ReadAllTextAsync(_service.GetShadowFilePath());
        var originalLine = originalShadowContent.Split('\n').First(l => l.StartsWith($"{username}:"));
        var originalParts = originalLine.Split(':');

        // Act
        await _service.UpdateUserPasswordAsync(username, newPassword);

        // Assert
        var updatedShadowContent = await File.ReadAllTextAsync(_service.GetShadowFilePath());
        var updatedLine = updatedShadowContent.Split('\n').First(l => l.StartsWith($"{username}:"));
        var updatedParts = updatedLine.Split(':');
        
        // Password hash should be different
        Assert.NotEqual(originalParts[1], updatedParts[1]);
        
        // Last change date should be updated
        Assert.NotEqual(originalParts[2], updatedParts[2]);
        
        // Other fields should be preserved
        Assert.Equal(originalParts[3], updatedParts[3]); // min age
        Assert.Equal(originalParts[4], updatedParts[4]); // max age
        Assert.Equal(originalParts[5], updatedParts[5]); // warn period
    }

    [Fact]
    public async Task ListUsersAsync_ReturnsUsersInAlphabeticalOrder()
    {
        // Arrange
        await _service.AddUserAsync("zebra", "password1");
        await _service.AddUserAsync("alpha", "password2");
        await _service.AddUserAsync("beta", "password3");

        // Act
        var users = await _service.ListUsersAsync();

        // Assert
        var userList = users.ToList();
        Assert.Equal(3, userList.Count);
        Assert.Equal("alpha", userList[0].Username);
        Assert.Equal("beta", userList[1].Username);  
        Assert.Equal("zebra", userList[2].Username);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void IsValidUsername_WithNullOrEmpty_ReturnsFalse(string username)
    {
        // Act & Assert
        Assert.False(_service.IsValidUsername(username));
    }

    [Fact]
    public async Task RemoveUserAsync_RemovesFromBothFiles()
    {
        // Arrange
        var username = "testuser";
        var password = "password123";
        
        await _service.AddUserAsync(username, password);
        
        // Verify user exists in both files
        var passwdContent = await File.ReadAllTextAsync(_service.GetPasswdFilePath());
        var shadowContent = await File.ReadAllTextAsync(_service.GetShadowFilePath());
        Assert.Contains($"{username}:", passwdContent);
        Assert.Contains($"{username}:", shadowContent);

        // Act
        await _service.RemoveUserAsync(username);

        // Assert
        var updatedPasswdContent = await File.ReadAllTextAsync(_service.GetPasswdFilePath());
        var updatedShadowContent = await File.ReadAllTextAsync(_service.GetShadowFilePath());
        Assert.DoesNotContain($"{username}:", updatedPasswdContent);
        Assert.DoesNotContain($"{username}:", updatedShadowContent);
    }
}