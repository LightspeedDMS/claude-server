using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;
using ClaudeBatchServer.Core.DTOs;

namespace ClaudeServerCLI.IntegrationTests.Commands;

[Collection("TestServer")]
public class RepositoryFileCommandsE2ETests : E2ETestBase, IAsyncLifetime
{
    private readonly string _testRepoName = "test-repo-e2e-files";
    private readonly string _testRepoUrl = "https://github.com/octocat/Hello-World.git";

    public RepositoryFileCommandsE2ETests(ITestOutputHelper output, TestServerHarness testServer)
        : base(output, testServer)
    {
    }

    public async Task InitializeAsync()
    {
        // Login through CLI using base class method
        var loginSuccess = await LoginAsync();
        if (!loginSuccess)
        {
            throw new InvalidOperationException("Failed to login for E2E tests");
        }

        // Create test repository through CLI (disable CIDX to speed up tests)
        var createRepoResult = await CliHelper.ExecuteCommandAsync($"repos create --name {_testRepoName} --git-url {_testRepoUrl} --description \"E2E test repository for file commands\" --cidx-aware false");
        
        // Repository might already exist, which is fine
        if (createRepoResult.ExitCode != 0 && !createRepoResult.StandardOutput.Contains("already exists"))
        {
            throw new InvalidOperationException($"Failed to create test repository: {createRepoResult.StandardOutput}");
        }

        // Wait for repository to be cloned
        await Task.Delay(5000);
    }

    public async Task DisposeAsync()
    {
        // Cleanup test repository through CLI
        try
        {
            await CliHelper.ExecuteCommandAsync($"repos delete {_testRepoName} --force");
        }
        catch
        {
            // Ignore cleanup errors
        }
        
        // Call base class dispose
        Dispose();
    }

    #region Repository Files List E2E Tests

    [Fact]
    public async Task RepositoryFilesListCommand_E2E_WithValidRepository_ShouldReturnFiles()
    {
        // Act
        var result = await CliHelper.ExecuteCommandAsync($"repos files list {_testRepoName}");

        // Assert
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("README.md", result.StandardOutput);
        Assert.DoesNotContain("Error:", result.StandardOutput);
    }

    [Fact]
    public async Task RepositoryFilesListCommand_E2E_WithJsonFormat_ShouldReturnValidJson()
    {
        // Act
        var result = await CliHelper.ExecuteCommandAsync($"repos files list {_testRepoName} --format json");

        // Assert
        Assert.Equal(0, result.ExitCode);
        
        // Verify it's valid JSON
        var jsonDoc = JsonDocument.Parse(result.StandardOutput);
        Assert.True(jsonDoc.RootElement.ValueKind == JsonValueKind.Array);
    }

    [Fact]
    public async Task RepositoryFilesListCommand_E2E_WithSpecificPath_ShouldReturnPathContents()
    {
        // Act
        var result = await CliHelper.ExecuteCommandAsync($"repos files list {_testRepoName} --path .");

        // Assert
        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain("Error:", result.StandardOutput);
    }

    [Fact]
    public async Task RepositoryFilesListCommand_E2E_WithNonExistentRepository_ShouldReturnError()
    {
        // Act
        var result = await CliHelper.ExecuteCommandAsync("repos files list nonexistent-repo-e2e");

        // Assert
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("not found", result.StandardOutput.ToLowerInvariant());
    }

    #endregion

    #region Repository Files Show E2E Tests

    [Fact]
    public async Task RepositoryFilesShowCommand_E2E_WithValidFile_ShouldReturnContent()
    {
        // Act
        var result = await CliHelper.ExecuteCommandAsync($"repos files show {_testRepoName} README.md");

        // Assert
        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain("Error:", result.StandardOutput);
        // README.md should contain some content
        Assert.True(result.StandardOutput.Length > 10);
    }

    [Fact]
    public async Task RepositoryFilesShowCommand_E2E_WithNonExistentFile_ShouldReturnError()
    {
        // Act
        var result = await CliHelper.ExecuteCommandAsync($"repos files show {_testRepoName} nonexistent-file-e2e.txt");

        // Assert
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("not found", result.StandardOutput.ToLowerInvariant());
    }

    [Fact]
    public async Task RepositoryFilesShowCommand_E2E_WithDirectory_ShouldReturnError()
    {
        // Act
        var result = await CliHelper.ExecuteCommandAsync($"repos files show {_testRepoName} .");

        // Assert
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("directory", result.StandardOutput.ToLowerInvariant());
    }

    #endregion

    #region Repository Files Download E2E Tests

    [Fact]
    public async Task RepositoryFilesDownloadCommand_E2E_WithValidFile_ShouldDownloadFile()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();

        try
        {
            // Act
            var result = await CliHelper.ExecuteCommandAsync($"repos files download {_testRepoName} README.md --output {tempFile}");

            // Assert
            Assert.Equal(0, result.ExitCode);
            Assert.Contains("Downloaded", result.StandardOutput);
            Assert.True(File.Exists(tempFile));
            Assert.True(new FileInfo(tempFile).Length > 0);
            
            var content = await File.ReadAllTextAsync(tempFile);
            Assert.NotEmpty(content);
        }
        finally
        {
            // Cleanup
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task RepositoryFilesDownloadCommand_E2E_WithDefaultOutputPath_ShouldDownloadToCurrentDirectory()
    {
        // Arrange
        var currentDir = Directory.GetCurrentDirectory();
        var expectedFile = Path.Combine(currentDir, "README.md");

        try
        {
            // Act
            var result = await CliHelper.ExecuteCommandAsync($"repos files download {_testRepoName} README.md");

            // Assert
            Assert.Equal(0, result.ExitCode);
            Assert.Contains("Downloaded", result.StandardOutput);
            Assert.True(File.Exists(expectedFile));
        }
        finally
        {
            // Cleanup
            if (File.Exists(expectedFile))
                File.Delete(expectedFile);
        }
    }

    [Fact]
    public async Task RepositoryFilesDownloadCommand_E2E_WithNonExistentFile_ShouldReturnError()
    {
        // Act
        var result = await CliHelper.ExecuteCommandAsync($"repos files download {_testRepoName} nonexistent-file-e2e.txt");

        // Assert
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("not found", result.StandardOutput.ToLowerInvariant());
    }

    #endregion

    #region Repository Files Search E2E Tests

    [Fact]
    public async Task RepositoryFilesSearchCommand_E2E_WithPattern_ShouldReturnMatchingFiles()
    {
        // Act
        var result = await CliHelper.ExecuteCommandAsync($"repos files search {_testRepoName} --pattern *.md");

        // Assert
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("README.md", result.StandardOutput);
    }

    [Fact]
    public async Task RepositoryFilesSearchCommand_E2E_WithJsonFormat_ShouldReturnValidJson()
    {
        // Act
        var result = await CliHelper.ExecuteCommandAsync($"repos files search {_testRepoName} --pattern *.md --format json");

        // Assert
        Assert.Equal(0, result.ExitCode);
        
        // Verify it's valid JSON
        var jsonDoc = JsonDocument.Parse(result.StandardOutput);
        Assert.True(jsonDoc.RootElement.ValueKind == JsonValueKind.Array);
    }

    [Fact]
    public async Task RepositoryFilesSearchCommand_E2E_WithNoMatches_ShouldReturnEmptyResult()
    {
        // Act
        var result = await CliHelper.ExecuteCommandAsync($"repos files search {_testRepoName} --pattern *.nonexistent-extension-e2e");

        // Assert
        Assert.Equal(0, result.ExitCode);
        // Should not contain any file paths or show empty result
        Assert.DoesNotContain(".nonexistent-extension-e2e", result.StandardOutput);
    }

    [Fact]
    public async Task RepositoryFilesSearchCommand_E2E_WithCaseSensitivePattern_ShouldWork()
    {
        // Act
        var result = await CliHelper.ExecuteCommandAsync($"repos files search {_testRepoName} --pattern README*");

        // Assert
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("README", result.StandardOutput);
    }

    #endregion

    #region Combined Workflow E2E Tests

    [Fact]
    public async Task RepositoryFilesWorkflow_E2E_ListShowDownload_ShouldWorkTogether()
    {
        var tempFile = Path.GetTempFileName();

        try
        {
            // Step 1: List files
            var listResult = await CliHelper.ExecuteCommandAsync($"repos files list {_testRepoName}");
            Assert.Equal(0, listResult.ExitCode);
            Assert.Contains("README.md", listResult.StandardOutput);

            // Step 2: Show file content
            var showResult = await CliHelper.ExecuteCommandAsync($"repos files show {_testRepoName} README.md");
            Assert.Equal(0, showResult.ExitCode);
            Assert.True(showResult.StandardOutput.Length > 10);

            // Step 3: Download file
            var downloadResult = await CliHelper.ExecuteCommandAsync($"repos files download {_testRepoName} README.md --output {tempFile}");
            Assert.Equal(0, downloadResult.ExitCode);
            Assert.True(File.Exists(tempFile));

            // Verify downloaded content matches show content
            var downloadedContent = await File.ReadAllTextAsync(tempFile);
            Assert.Equal(showResult.StandardOutput.Trim(), downloadedContent.Trim());
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task RepositoryFilesWorkflow_E2E_SearchAndShow_ShouldWorkTogether()
    {
        // Step 1: Search for markdown files
        var searchResult = await CliHelper.ExecuteCommandAsync($"repos files search {_testRepoName} --pattern *.md");
        Assert.Equal(0, searchResult.ExitCode);
        Assert.Contains("README.md", searchResult.StandardOutput);

        // Step 2: Show the found file
        var showResult = await CliHelper.ExecuteCommandAsync($"repos files show {_testRepoName} README.md");
        Assert.Equal(0, showResult.ExitCode);
        Assert.True(showResult.StandardOutput.Length > 10);
    }

    #endregion

    #region Authentication E2E Tests

    [Fact]
    public async Task RepositoryFilesCommand_E2E_WithoutAuthentication_ShouldReturnError()
    {
        // Arrange - Logout first
        await CliHelper.ExecuteCommandAsync("auth logout");

        try
        {
            // Act
            var result = await CliHelper.ExecuteCommandAsync($"repos files list {_testRepoName}");

            // Assert
            Assert.Equal(1, result.ExitCode);
            Assert.Contains("authentication", result.StandardOutput.ToLowerInvariant());
        }
        finally
        {
            // Restore authentication
            await CliHelper.ExecuteCommandAsync($"auth login --username {ServerHarness.TestUser} --password {ServerHarness.TestPassword}");
        }
    }

    #endregion

    #region Error Handling E2E Tests

    [Fact]
    public async Task RepositoryFilesCommand_E2E_WithInvalidArguments_ShouldReturnError()
    {
        // Act - Missing repository argument
        var result = await CliHelper.ExecuteCommandAsync("repos files list");

        // Assert
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("required", result.StandardOutput.ToLowerInvariant());
    }

    [Fact]
    public async Task RepositoryFilesCommand_E2E_WithHelpFlag_ShouldShowHelp()
    {
        // Act
        var result = await CliHelper.ExecuteCommandAsync("repos files list --help");

        // Assert
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("repository", result.StandardOutput.ToLowerInvariant());
        Assert.Contains("usage", result.StandardOutput.ToLowerInvariant());
    }

    #endregion
}