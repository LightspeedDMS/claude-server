using FluentAssertions;
using ClaudeBatchServer.Core.Services;
using Xunit;

namespace ClaudeBatchServer.UnitTests.Services;

public class SecurityUtilsTests
{
    [Fact]
    public void IsValidRepositoryName_ValidNames_ShouldReturnTrue()
    {
        // Arrange & Act & Assert
        SecurityUtils.IsValidRepositoryName("valid-repo").Should().BeTrue();
        SecurityUtils.IsValidRepositoryName("my_project").Should().BeTrue();
        SecurityUtils.IsValidRepositoryName("repo123").Should().BeTrue();
        SecurityUtils.IsValidRepositoryName("test.repo").Should().BeTrue();
        SecurityUtils.IsValidRepositoryName("a").Should().BeTrue();
    }

    [Fact]
    public void IsValidRepositoryName_InvalidNames_ShouldReturnFalse()
    {
        // Arrange & Act & Assert
        SecurityUtils.IsValidRepositoryName(null).Should().BeFalse();
        SecurityUtils.IsValidRepositoryName("").Should().BeFalse();
        SecurityUtils.IsValidRepositoryName("  ").Should().BeFalse();
        
        // Test injection attempts
        SecurityUtils.IsValidRepositoryName("repo; rm -rf /").Should().BeFalse();
        SecurityUtils.IsValidRepositoryName("repo && malicious").Should().BeFalse();
        SecurityUtils.IsValidRepositoryName("repo|evil").Should().BeFalse();
        SecurityUtils.IsValidRepositoryName("repo`command`").Should().BeFalse();
        SecurityUtils.IsValidRepositoryName("repo$malicious").Should().BeFalse();
        SecurityUtils.IsValidRepositoryName("repo(evil)").Should().BeFalse();
        SecurityUtils.IsValidRepositoryName("repo<script>").Should().BeFalse();
        SecurityUtils.IsValidRepositoryName("repo'injection'").Should().BeFalse();
        SecurityUtils.IsValidRepositoryName("repo\"injection\"").Should().BeFalse();
        SecurityUtils.IsValidRepositoryName("repo\ninjection").Should().BeFalse();
        SecurityUtils.IsValidRepositoryName("repo\rinjection").Should().BeFalse();
        
        // Test length limit (over 100 characters)
        SecurityUtils.IsValidRepositoryName(new string('a', 101)).Should().BeFalse();
    }

    [Fact]
    public void IsValidGitUrl_ValidUrls_ShouldReturnTrue()
    {
        // Arrange & Act & Assert
        SecurityUtils.IsValidGitUrl("https://github.com/user/repo.git").Should().BeTrue();
        SecurityUtils.IsValidGitUrl("http://gitlab.com/user/repo.git").Should().BeTrue();
        SecurityUtils.IsValidGitUrl("git@github.com:user/repo.git").Should().BeTrue();
        SecurityUtils.IsValidGitUrl("https://bitbucket.org/team/project.git").Should().BeTrue();
    }

    [Fact]
    public void IsValidGitUrl_InvalidUrls_ShouldReturnFalse()
    {
        // Arrange & Act & Assert
        SecurityUtils.IsValidGitUrl(null).Should().BeFalse();
        SecurityUtils.IsValidGitUrl("").Should().BeFalse();
        SecurityUtils.IsValidGitUrl("  ").Should().BeFalse();
        
        // Test injection attempts
        SecurityUtils.IsValidGitUrl("https://github.com/user/repo.git; rm -rf /").Should().BeFalse();
        SecurityUtils.IsValidGitUrl("https://github.com/user/repo.git && malicious").Should().BeFalse();
        SecurityUtils.IsValidGitUrl("https://github.com/user/repo.git|evil").Should().BeFalse();
        SecurityUtils.IsValidGitUrl("https://github.com/user/repo.git`command`").Should().BeFalse();
        SecurityUtils.IsValidGitUrl("https://github.com/user/repo.git$malicious").Should().BeFalse();
        SecurityUtils.IsValidGitUrl("https://github.com/user/repo.git'injection'").Should().BeFalse();
        SecurityUtils.IsValidGitUrl("https://github.com/user/repo.git\"injection\"").Should().BeFalse();
        SecurityUtils.IsValidGitUrl("https://github.com/user/repo.git\ninjection").Should().BeFalse();
        
        // Test length limit (over 500 characters)
        SecurityUtils.IsValidGitUrl("https://github.com/user/" + new string('a', 500) + ".git").Should().BeFalse();
        
        // Invalid formats
        SecurityUtils.IsValidGitUrl("not-a-url").Should().BeFalse();
        SecurityUtils.IsValidGitUrl("ftp://example.com/repo.git").Should().BeFalse();
    }

    [Fact]
    public void IsValidPath_ValidPaths_ShouldReturnTrue()
    {
        // Arrange & Act & Assert
        SecurityUtils.IsValidPath("valid/path").Should().BeTrue();
        SecurityUtils.IsValidPath("file.txt").Should().BeTrue();
        SecurityUtils.IsValidPath("folder/subfolder/file.ext").Should().BeTrue();
        SecurityUtils.IsValidPath("path with spaces/file.txt").Should().BeTrue();
    }

    [Fact]
    public void IsValidPath_InvalidPaths_ShouldReturnFalse()
    {
        // Arrange & Act & Assert
        SecurityUtils.IsValidPath(null).Should().BeFalse();
        SecurityUtils.IsValidPath("").Should().BeFalse();
        SecurityUtils.IsValidPath("  ").Should().BeFalse();
        
        // Test path traversal attempts
        SecurityUtils.IsValidPath("../../../etc/passwd").Should().BeFalse();
        SecurityUtils.IsValidPath("..\\..\\windows\\system32").Should().BeFalse();
        SecurityUtils.IsValidPath("path/../../../sensitive").Should().BeFalse();
        SecurityUtils.IsValidPath("path/..\\..\\sensitive").Should().BeFalse();
        
        // Test injection attempts
        SecurityUtils.IsValidPath("path; rm -rf /").Should().BeFalse();
        SecurityUtils.IsValidPath("path && malicious").Should().BeFalse();
        SecurityUtils.IsValidPath("path|evil").Should().BeFalse();
        SecurityUtils.IsValidPath("path`command`").Should().BeFalse();
        SecurityUtils.IsValidPath("path$malicious").Should().BeFalse();
        SecurityUtils.IsValidPath("path'injection'").Should().BeFalse();
        SecurityUtils.IsValidPath("path\"injection\"").Should().BeFalse();
    }

    [Fact]
    public void SanitizeForShell_RemovesDangerousCharacters()
    {
        // Arrange & Act & Assert
        SecurityUtils.SanitizeForShell("clean text").Should().Be("clean text");
        SecurityUtils.SanitizeForShell("text; rm -rf /").Should().Be("text rm -rf ");
        SecurityUtils.SanitizeForShell("text && malicious").Should().Be("text  malicious");
        SecurityUtils.SanitizeForShell("text|evil").Should().Be("textevil");
        SecurityUtils.SanitizeForShell("text`command`").Should().Be("textcommand");
        SecurityUtils.SanitizeForShell("text$malicious").Should().Be("textmalicious");
        SecurityUtils.SanitizeForShell("text(evil)").Should().Be("textevil");
        SecurityUtils.SanitizeForShell("text<script>").Should().Be("textscript");
        SecurityUtils.SanitizeForShell("text'injection'").Should().Be("textinjection");
        SecurityUtils.SanitizeForShell("text\"injection\"").Should().Be("textinjection");
        SecurityUtils.SanitizeForShell("text\ninjection").Should().Be("textinjection");
        SecurityUtils.SanitizeForShell("text\rinjection").Should().Be("textinjection");
        
        // Test empty/null input
        SecurityUtils.SanitizeForShell("").Should().Be("");
        SecurityUtils.SanitizeForShell(null).Should().Be("");
    }

    [Fact]
    public void CreateSafeProcess_CreatesSecureProcessStartInfo()
    {
        // Arrange & Act
        var processInfo = SecurityUtils.CreateSafeProcess("git", "clone", "https://github.com/user/repo.git", "/path/to/destination");

        // Assert
        processInfo.FileName.Should().Be("git");
        processInfo.UseShellExecute.Should().BeFalse();
        processInfo.CreateNoWindow.Should().BeTrue();
        processInfo.RedirectStandardOutput.Should().BeTrue();
        processInfo.RedirectStandardError.Should().BeTrue();
        processInfo.ArgumentList.Should().HaveCount(3);
        processInfo.ArgumentList[0].Should().Be("clone");
        processInfo.ArgumentList[1].Should().Be("https://github.com/user/repo.git");
        processInfo.ArgumentList[2].Should().Be("/path/to/destination");
    }

    [Fact]
    public void CreateSafeProcess_IgnoresEmptyArguments()
    {
        // Arrange & Act
        var processInfo = SecurityUtils.CreateSafeProcess("command", "arg1", "", "arg3", "  ", "arg5");

        // Assert
        processInfo.ArgumentList.Should().HaveCount(3);
        processInfo.ArgumentList[0].Should().Be("arg1");
        processInfo.ArgumentList[1].Should().Be("arg3");
        processInfo.ArgumentList[2].Should().Be("arg5");
    }

    [Theory]
    [InlineData("repo; rm -rf /")]
    [InlineData("repo && echo 'hacked'")]
    [InlineData("repo | cat /etc/passwd")]
    [InlineData("repo `whoami`")]
    [InlineData("repo $(malicious)")]
    [InlineData("repo$PATH")]
    [InlineData("repo'evil'")]
    [InlineData("repo\"evil\"")]
    [InlineData("repo\necho evil")]
    [InlineData("repo\recho evil")]
    public void IsValidRepositoryName_DetectsCommonInjectionPatterns(string maliciousName)
    {
        // Arrange & Act & Assert
        SecurityUtils.IsValidRepositoryName(maliciousName).Should().BeFalse($"'{maliciousName}' should be detected as malicious");
    }

    [Theory]
    [InlineData("https://github.com/user/repo.git; rm -rf /")]
    [InlineData("https://github.com/user/repo.git && echo 'hacked'")]
    [InlineData("https://github.com/user/repo.git | cat /etc/passwd")]
    [InlineData("https://github.com/user/repo.git `whoami`")]
    [InlineData("https://github.com/user/repo.git$PATH")]
    [InlineData("https://github.com/user/repo.git'evil'")]
    [InlineData("https://github.com/user/repo.git\"evil\"")]
    [InlineData("https://github.com/user/repo.git\necho evil")]
    public void IsValidGitUrl_DetectsCommonInjectionPatterns(string maliciousUrl)
    {
        // Arrange & Act & Assert
        SecurityUtils.IsValidGitUrl(maliciousUrl).Should().BeFalse($"'{maliciousUrl}' should be detected as malicious");
    }
}