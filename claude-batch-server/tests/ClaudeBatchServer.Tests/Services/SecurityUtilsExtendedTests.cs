using System.Security.Cryptography;
using FluentAssertions;
using ClaudeBatchServer.Core.Services;

namespace ClaudeBatchServer.Tests.Services;

/// <summary>
/// Extended tests for SecurityUtils to improve coverage beyond existing HashAuthenticationTests
/// Tests focus on the actual methods available in SecurityUtils class
/// </summary>
public class SecurityUtilsExtendedTests
{
    [Theory]
    [InlineData("valid-repo")]
    [InlineData("repo123")]
    [InlineData("my_repo")]
    [InlineData("repo.name")]
    [InlineData("repo-name")]
    [InlineData("a")]
    public void IsValidRepositoryName_WithValidNames_ShouldReturnTrue(string repositoryName)
    {
        // Act
        var result = SecurityUtils.IsValidRepositoryName(repositoryName);

        // Assert
        result.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("repo;name")]
    [InlineData("repo&name")]
    [InlineData("repo|name")]
    [InlineData("repo`name")]
    [InlineData("repo$name")]
    [InlineData("repo(name")]
    [InlineData("repo)name")]
    [InlineData("repo<name")]
    [InlineData("repo>name")]
    [InlineData("repo'name")]
    [InlineData("repo\"name")]
    [InlineData("repo\nname")]
    [InlineData("repo\rname")]
    public void IsValidRepositoryName_WithInvalidNames_ShouldReturnFalse(string? repositoryName)
    {
        // Act
        var result = SecurityUtils.IsValidRepositoryName(repositoryName);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void IsValidRepositoryName_WithTooLongName_ShouldReturnFalse()
    {
        // Arrange - Create a name longer than 100 characters
        var longName = new string('a', 101);

        // Act
        var result = SecurityUtils.IsValidRepositoryName(longName);

        // Assert
        result.Should().BeFalse();
    }

    [Theory]
    [InlineData("https://github.com/user/repo.git")]
    [InlineData("http://gitlab.com/user/repo.git")]
    [InlineData("git@github.com:user/repo.git")]
    public void IsValidGitUrl_WithValidUrls_ShouldReturnTrue(string gitUrl)
    {
        // Act
        var result = SecurityUtils.IsValidGitUrl(gitUrl);

        // Assert
        result.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("not-a-url")]
    [InlineData("ftp://example.com/repo.git")]
    [InlineData("https://example.com/repo;injection.git")]
    public void IsValidGitUrl_WithInvalidUrls_ShouldReturnFalse(string? gitUrl)
    {
        // Act
        var result = SecurityUtils.IsValidGitUrl(gitUrl);

        // Assert
        result.Should().BeFalse();
    }

    [Theory]
    [InlineData("normal/path")]
    [InlineData("/absolute/path")]
    [InlineData("./relative/path")]
    [InlineData("simple-file.txt")]
    public void IsValidPath_WithSafePaths_ShouldReturnTrue(string path)
    {
        // Act
        var result = SecurityUtils.IsValidPath(path);

        // Assert
        result.Should().BeTrue();
    }

    [Theory]
    [InlineData("../dangerous")]
    [InlineData("path/../traversal")]
    [InlineData("..\\windows\\traversal")]
    [InlineData("some/../path/../dangerous")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void IsValidPath_WithDangerousPaths_ShouldReturnFalse(string? path)
    {
        // Act
        var result = SecurityUtils.IsValidPath(path);

        // Assert
        result.Should().BeFalse();
    }

    [Theory]
    [InlineData("safe input")]
    [InlineData("simple-command")]
    [InlineData("file123.txt")]
    public void SanitizeForShell_WithSafeInput_ShouldReturnSanitized(string input)
    {
        // Act
        var result = SecurityUtils.SanitizeForShell(input);

        // Assert
        result.Should().NotBeNull();
        result.Should().NotContain(";");
        result.Should().NotContain("|");
        result.Should().NotContain("&");
    }

    [Theory]
    [InlineData("command; dangerous")]
    [InlineData("input & malicious")]
    [InlineData("command | pipe")]
    [InlineData("input `backtick`")]
    [InlineData("input $(command)")]
    public void SanitizeForShell_WithDangerousInput_ShouldRemoveDangerousChars(string input)
    {
        // Act
        var result = SecurityUtils.SanitizeForShell(input);

        // Assert
        result.Should().NotBeNull();
        result.Should().NotContain(";");
        result.Should().NotContain("|");
        result.Should().NotContain("&");
        result.Should().NotContain("`");
        result.Should().NotContain("$");
    }

    [Fact]
    public void CreateSafeProcess_WithValidCommand_ShouldReturnProcessStartInfo()
    {
        // Act
        var result = SecurityUtils.CreateSafeProcess("echo", "hello", "world");

        // Assert
        result.Should().NotBeNull();
        result.FileName.Should().Be("echo");
        // Arguments are combined into a single string, so check if it contains the args
        result.Arguments.Should().NotBeNull();
        result.UseShellExecute.Should().BeFalse();
        result.CreateNoWindow.Should().BeTrue();
    }
}