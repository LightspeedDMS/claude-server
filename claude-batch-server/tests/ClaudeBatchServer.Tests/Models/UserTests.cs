using FluentAssertions;
using ClaudeBatchServer.Core.Models;

namespace ClaudeBatchServer.Tests.Models;

public class UserTests
{
    [Fact]
    public void User_DefaultValues_ShouldBeSetCorrectly()
    {
        // Act
        var user = new User();

        // Assert
        user.Username.Should().Be(string.Empty);
        user.PasswordHash.Should().Be(string.Empty);
        user.LastLogin.Should().Be(default(DateTime));
        user.IsActive.Should().BeTrue(); // Default is true according to model
        user.Roles.Should().NotBeNull();
        user.Roles.Should().BeEmpty();
    }

    [Fact]
    public void User_SetProperties_ShouldUpdateCorrectly()
    {
        // Arrange
        var username = "testuser";
        var passwordHash = "hash123";
        var lastLogin = DateTime.UtcNow;
        var roles = new List<string> { "admin", "user" };

        // Act
        var user = new User
        {
            Username = username,
            PasswordHash = passwordHash,
            LastLogin = lastLogin,
            IsActive = false,
            Roles = roles
        };

        // Assert
        user.Username.Should().Be(username);
        user.PasswordHash.Should().Be(passwordHash);
        user.LastLogin.Should().Be(lastLogin);
        user.IsActive.Should().BeFalse();
        user.Roles.Should().BeEquivalentTo(roles);
    }

    [Fact]
    public void User_Username_ShouldAcceptValidUsernames()
    {
        // Arrange
        var validUsernames = new[] { "user", "user123", "user_name", "user-name", "user.name" };

        foreach (var username in validUsernames)
        {
            // Act
            var user = new User { Username = username };

            // Assert
            user.Username.Should().Be(username);
        }
    }

    [Fact]
    public void User_LastLogin_ShouldAcceptValidDates()
    {
        // Arrange
        var dates = new[]
        {
            DateTime.MinValue,
            DateTime.MaxValue,
            DateTime.UtcNow,
            new DateTime(2023, 1, 1),
            new DateTime(2024, 12, 31, 23, 59, 59)
        };

        foreach (var date in dates)
        {
            // Act
            var user = new User { LastLogin = date };

            // Assert
            user.LastLogin.Should().Be(date);
        }
    }

    [Fact]
    public void User_IsActive_ShouldToggleCorrectly()
    {
        // Arrange
        var user = new User();

        // Act & Assert - Initially true by default
        user.IsActive.Should().BeTrue();

        // Act & Assert - Can be set to false
        user.IsActive = false;
        user.IsActive.Should().BeFalse();

        // Act & Assert - Can be set back to true
        user.IsActive = true;
        user.IsActive.Should().BeTrue();
    }

    [Fact]
    public void User_PasswordHash_ShouldAcceptHashedPasswords()
    {
        // Arrange
        var hashes = new[]
        {
            "$6$rounds=656000$YJGyxIBLSRqyqyYj$4dCMhXkRhTNNLNPLX4MDbVKGBGK1iXG2TgKmX8GkNJRGvONVYEpS7Hm.g/gYFw2TJHJwJ8wD7c9.r5K2v7sWWq.",
            "$2b$12$LUfnPfqLbIDM9K8a4xKE1O1Vf0u1L8vXcLdGKs3K1XdK7cTjP9K8a",
            "sha256:64chars1234567890123456789012345678901234567890123456789012"
        };

        foreach (var hash in hashes)
        {
            // Act
            var user = new User { PasswordHash = hash };

            // Assert
            user.PasswordHash.Should().Be(hash);
        }
    }

    [Fact]
    public void User_Roles_ShouldAcceptMultipleRoles()
    {
        // Arrange
        var roles = new List<string> { "admin", "user", "moderator" };

        // Act
        var user = new User { Roles = roles };

        // Assert
        user.Roles.Should().BeEquivalentTo(roles);
        user.Roles.Should().HaveCount(3);
        user.Roles.Should().Contain("admin");
        user.Roles.Should().Contain("user");
        user.Roles.Should().Contain("moderator");
    }

    [Fact]
    public void User_AllPropertiesSet_ShouldMaintainValues()
    {
        // Arrange
        var username = "fulluser";
        var passwordHash = "$6$test$hash";
        var lastLogin = new DateTime(2024, 7, 25, 16, 45, 30);
        var roles = new List<string> { "admin", "user" };

        // Act
        var user = new User
        {
            Username = username,
            PasswordHash = passwordHash,
            LastLogin = lastLogin,
            IsActive = false,
            Roles = roles
        };

        // Assert - All properties should maintain their values
        user.Username.Should().Be(username);
        user.PasswordHash.Should().Be(passwordHash);
        user.LastLogin.Should().Be(lastLogin);
        user.IsActive.Should().BeFalse();
        user.Roles.Should().BeEquivalentTo(roles);
    }
}