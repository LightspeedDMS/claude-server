using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;
using ClaudeBatchServer.Core.DTOs;

namespace ClaudeServerCLI.IntegrationTests.Commands;

[Collection("TestServer")]
public class JobFileCommandsE2ETests : E2ETestBase, IAsyncLifetime
{
    private readonly string _testRepoName = "test-repo-e2e-job-files";
    private readonly string _testRepoUrl = "https://github.com/octocat/Hello-World.git";
    private string? _testJobId;

    public JobFileCommandsE2ETests(ITestOutputHelper output, TestServerHarness testServer)
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
        var createRepoResult = await CliHelper.ExecuteCommandAsync($"repos create --name {_testRepoName} --git-url {_testRepoUrl} --description \"E2E test repository for job file commands\" --cidx-aware false");
        
        // Repository might already exist, which is fine
        if (createRepoResult.ExitCode != 0 && !createRepoResult.StandardOutput.Contains("already exists"))
        {
            throw new InvalidOperationException($"Failed to create test repository: {createRepoResult.StandardOutput}");
        }

        // Wait for repository to be cloned and ready
        await Task.Delay(5000);
        
        // Retry job creation if repository is still being prepared
        CliExecutionResult createJobResult;
        var maxRetries = 5;
        var retryCount = 0;
        
        do
        {
            createJobResult = await CliHelper.ExecuteCommandAsync($"jobs create --repository {_testRepoName} --prompt \"List files in this repository\"");
            
            if (createJobResult.ExitCode == 0)
            {
                break; // Success
            }
            
            if (createJobResult.StandardOutput.Contains("still being prepared") && retryCount < maxRetries)
            {
                retryCount++;
                Output.WriteLine($"Repository still being prepared, retry {retryCount}/{maxRetries}...");
                await Task.Delay(3000); // Wait 3 seconds before retry
            }
            else
            {
                break; // Either succeeded or failed for a different reason
            }
        } while (retryCount < maxRetries);
        
        if (createJobResult.ExitCode != 0)
        {
            throw new InvalidOperationException($"Failed to create test job: {createJobResult.StandardOutput}");
        }

        // Extract job ID from output (assuming format like "Job created successfully: abc123")
        var output = createJobResult.StandardOutput;
        var jobIdIndex = output.IndexOf("Job created successfully:");
        if (jobIdIndex >= 0)
        {
            var jobIdSection = output.Substring(jobIdIndex).Split('\n')[0];
            var parts = jobIdSection.Split(':');
            if (parts.Length > 1)
            {
                _testJobId = parts[1].Trim();
            }
        }

        if (string.IsNullOrEmpty(_testJobId))
        {
            throw new InvalidOperationException($"Could not extract job ID from output: {output}");
        }

        // Upload some test files to the job
        var testFile1 = Path.GetTempFileName();
        var testFile2 = Path.GetTempFileName();
        
        await File.WriteAllTextAsync(testFile1, "This is test input file 1 for E2E testing");
        await File.WriteAllTextAsync(testFile2, "{\"config\": \"e2e-test\", \"enabled\": true}");

        try
        {
            await CliHelper.ExecuteCommandAsync($"jobs files upload {_testJobId} {testFile1} --name test-input-e2e.txt");
            await CliHelper.ExecuteCommandAsync($"jobs files upload {_testJobId} {testFile2} --name config-e2e.json");
        }
        finally
        {
            File.Delete(testFile1);
            File.Delete(testFile2);
        }
    }

    public async Task DisposeAsync()
    {
        // Cleanup test job through CLI
        if (!string.IsNullOrEmpty(_testJobId))
        {
            try
            {
                await CliHelper.ExecuteCommandAsync($"jobs delete {_testJobId} --force");
            }
            catch
            {
                // Ignore cleanup errors
            }
        }

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

    #region Job Files List E2E Tests

    [Fact]
    public async Task JobFilesListCommand_E2E_WithValidJob_ShouldReturnFiles()
    {
        // Act
        var result = await CliHelper.ExecuteCommandAsync($"jobs files list {_testJobId}");

        // Assert
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("test-input-e2e.txt", result.StandardOutput);
        Assert.Contains("config-e2e.json", result.StandardOutput);
        Assert.DoesNotContain("Error:", result.StandardOutput);
    }

    [Fact]
    public async Task JobFilesListCommand_E2E_WithJsonFormat_ShouldReturnValidJson()
    {
        // Act
        var result = await CliHelper.ExecuteCommandAsync($"jobs files list {_testJobId} --format json");

        // Assert
        Assert.Equal(0, result.ExitCode);
        
        // Verify it's valid JSON
        var jsonDoc = JsonDocument.Parse(result.StandardOutput);
        Assert.True(jsonDoc.RootElement.ValueKind == JsonValueKind.Array);
    }

    [Fact]
    public async Task JobFilesListCommand_E2E_WithNonExistentJob_ShouldReturnError()
    {
        // Act
        var result = await CliHelper.ExecuteCommandAsync("jobs files list nonexistent-job-e2e-123");

        // Assert
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("not found", result.StandardOutput.ToLowerInvariant());
    }

    #endregion

    #region Job Files Download E2E Tests

    [Fact]
    public async Task JobFilesDownloadCommand_E2E_WithValidFile_ShouldDownloadFile()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();

        try
        {
            // Act
            var result = await CliHelper.ExecuteCommandAsync($"jobs files download {_testJobId} test-input-e2e.txt --output {tempFile}");

            // Assert
            Assert.Equal(0, result.ExitCode);
            Assert.Contains("Downloaded", result.StandardOutput);
            Assert.True(File.Exists(tempFile));
            Assert.True(new FileInfo(tempFile).Length > 0);
            
            var content = await File.ReadAllTextAsync(tempFile);
            Assert.Contains("This is test input file 1", content);
        }
        finally
        {
            // Cleanup
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task JobFilesDownloadCommand_E2E_WithDefaultOutputPath_ShouldDownloadToCurrentDirectory()
    {
        // Arrange
        var currentDir = Directory.GetCurrentDirectory();
        var expectedFile = Path.Combine(currentDir, "config-e2e.json");

        try
        {
            // Act
            var result = await CliHelper.ExecuteCommandAsync($"jobs files download {_testJobId} config-e2e.json");

            // Assert
            Assert.Equal(0, result.ExitCode);
            Assert.Contains("Downloaded", result.StandardOutput);
            Assert.True(File.Exists(expectedFile));
            
            var content = await File.ReadAllTextAsync(expectedFile);
            Assert.Contains("e2e-test", content);
        }
        finally
        {
            // Cleanup
            if (File.Exists(expectedFile))
                File.Delete(expectedFile);
        }
    }

    [Fact]
    public async Task JobFilesDownloadCommand_E2E_WithNonExistentFile_ShouldReturnError()
    {
        // Act
        var result = await CliHelper.ExecuteCommandAsync($"jobs files download {_testJobId} nonexistent-file-e2e.txt");

        // Assert
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("not found", result.StandardOutput.ToLowerInvariant());
    }

    #endregion

    #region Job Files Upload E2E Tests

    [Fact]
    public async Task JobFilesUploadCommand_E2E_WithValidFile_ShouldUploadFile()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        await File.WriteAllTextAsync(tempFile, "New E2E test file content for upload testing");

        try
        {
            // Act
            var result = await CliHelper.ExecuteCommandAsync($"jobs files upload {_testJobId} {tempFile}");

            // Assert
            Assert.Equal(0, result.ExitCode);
            Assert.Contains("Uploaded", result.StandardOutput);
            
            // Verify file was uploaded by listing job files
            var listResult = await CliHelper.ExecuteCommandAsync($"jobs files list {_testJobId}");
            Assert.Equal(0, listResult.ExitCode);
            Assert.Contains(Path.GetFileName(tempFile), listResult.StandardOutput);
        }
        finally
        {
            // Cleanup
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task JobFilesUploadCommand_E2E_WithCustomFileName_ShouldUploadWithName()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        var customName = "custom-e2e-filename.txt";
        await File.WriteAllTextAsync(tempFile, "Content with custom filename");

        try
        {
            // Act
            var result = await CliHelper.ExecuteCommandAsync($"jobs files upload {_testJobId} {tempFile} --name {customName}");

            // Assert
            Assert.Equal(0, result.ExitCode);
            Assert.Contains("Uploaded", result.StandardOutput);
            
            // Verify file was uploaded with custom name
            var listResult = await CliHelper.ExecuteCommandAsync($"jobs files list {_testJobId}");
            Assert.Equal(0, listResult.ExitCode);
            Assert.Contains(customName, listResult.StandardOutput);
        }
        finally
        {
            // Cleanup
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task JobFilesUploadCommand_E2E_WithOverwriteFlag_ShouldUploadWithOverwrite()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        var fileName = "overwrite-test-e2e.txt";
        await File.WriteAllTextAsync(tempFile, "Original content");

        try
        {
            // First upload
            var firstResult = await CliHelper.ExecuteCommandAsync($"jobs files upload {_testJobId} {tempFile} --name {fileName}");
            Assert.Equal(0, firstResult.ExitCode);
            
            // Update file content
            await File.WriteAllTextAsync(tempFile, "Overwritten content");

            // Act - Upload with overwrite
            var result = await CliHelper.ExecuteCommandAsync($"jobs files upload {_testJobId} {tempFile} --name {fileName} --overwrite");

            // Assert
            Assert.Equal(0, result.ExitCode);
            Assert.Contains("Uploaded", result.StandardOutput);
            
            // Download and verify content was overwritten
            var downloadFile = Path.GetTempFileName();
            try
            {
                var downloadResult = await CliHelper.ExecuteCommandAsync($"jobs files download {_testJobId} {fileName} --output {downloadFile}");
                Assert.Equal(0, downloadResult.ExitCode);
                
                var content = await File.ReadAllTextAsync(downloadFile);
                Assert.Contains("Overwritten content", content);
            }
            finally
            {
                if (File.Exists(downloadFile))
                    File.Delete(downloadFile);
            }
        }
        finally
        {
            // Cleanup
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task JobFilesUploadCommand_E2E_WithNonExistentFile_ShouldReturnError()
    {
        // Act
        var result = await CliHelper.ExecuteCommandAsync($"jobs files upload {_testJobId} /tmp/nonexistent-file-e2e.txt");

        // Assert
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("not found", result.StandardOutput.ToLowerInvariant());
    }

    #endregion

    #region Job Files Delete E2E Tests

    [Fact]
    public async Task JobFilesDeleteCommand_E2E_WithValidFileAndForce_ShouldDeleteFile()
    {
        // Arrange - Upload a file first
        var tempFile = Path.GetTempFileName();
        var fileName = "delete-test-e2e.txt";
        await File.WriteAllTextAsync(tempFile, "File to be deleted");

        try
        {
            var uploadResult = await CliHelper.ExecuteCommandAsync($"jobs files upload {_testJobId} {tempFile} --name {fileName}");
            Assert.Equal(0, uploadResult.ExitCode);

            // Act
            var result = await CliHelper.ExecuteCommandAsync($"jobs files delete {_testJobId} {fileName} --force");

            // Assert
            Assert.Equal(0, result.ExitCode);
            Assert.Contains("Deleted", result.StandardOutput);
            
            // Verify file was deleted by listing job files
            var listResult = await CliHelper.ExecuteCommandAsync($"jobs files list {_testJobId}");
            Assert.Equal(0, listResult.ExitCode);
            Assert.DoesNotContain(fileName, listResult.StandardOutput);
        }
        finally
        {
            // Cleanup
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task JobFilesDeleteCommand_E2E_WithNonExistentFile_ShouldReturnError()
    {
        // Act
        var result = await CliHelper.ExecuteCommandAsync($"jobs files delete {_testJobId} nonexistent-file-e2e.txt --force");

        // Assert
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("not found", result.StandardOutput.ToLowerInvariant());
    }

    #endregion

    #region Combined Workflow E2E Tests

    [Fact]
    public async Task JobFilesWorkflow_E2E_UploadListDownloadDelete_ShouldWorkTogether()
    {
        var tempUploadFile = Path.GetTempFileName();
        var tempDownloadFile = Path.GetTempFileName();
        var fileName = "workflow-test-e2e.txt";
        var testContent = "Complete E2E workflow test content";

        try
        {
            // Step 1: Upload file
            await File.WriteAllTextAsync(tempUploadFile, testContent);
            var uploadResult = await CliHelper.ExecuteCommandAsync($"jobs files upload {_testJobId} {tempUploadFile} --name {fileName}");
            Assert.Equal(0, uploadResult.ExitCode);
            Assert.Contains("Uploaded", uploadResult.StandardOutput);

            // Step 2: List files to verify upload
            var listResult = await CliHelper.ExecuteCommandAsync($"jobs files list {_testJobId}");
            Assert.Equal(0, listResult.ExitCode);
            Assert.Contains(fileName, listResult.StandardOutput);

            // Step 3: Download file
            var downloadResult = await CliHelper.ExecuteCommandAsync($"jobs files download {_testJobId} {fileName} --output {tempDownloadFile}");
            Assert.Equal(0, downloadResult.ExitCode);
            Assert.Contains("Downloaded", downloadResult.StandardOutput);
            
            // Verify downloaded content
            var downloadedContent = await File.ReadAllTextAsync(tempDownloadFile);
            Assert.Equal(testContent, downloadedContent);

            // Step 4: Delete file
            var deleteResult = await CliHelper.ExecuteCommandAsync($"jobs files delete {_testJobId} {fileName} --force");
            Assert.Equal(0, deleteResult.ExitCode);
            Assert.Contains("Deleted", deleteResult.StandardOutput);

            // Step 5: Verify deletion by listing again
            var finalListResult = await CliHelper.ExecuteCommandAsync($"jobs files list {_testJobId}");
            Assert.Equal(0, finalListResult.ExitCode);
            Assert.DoesNotContain(fileName, finalListResult.StandardOutput);
        }
        finally
        {
            if (File.Exists(tempUploadFile))
                File.Delete(tempUploadFile);
            if (File.Exists(tempDownloadFile))
                File.Delete(tempDownloadFile);
        }
    }

    [Fact]
    public async Task JobFilesWorkflow_E2E_UploadOverwriteDownload_ShouldWorkTogether()
    {
        var tempFile = Path.GetTempFileName();
        var downloadFile = Path.GetTempFileName();
        var fileName = "overwrite-workflow-e2e.txt";
        var originalContent = "Original workflow content";
        var newContent = "Updated workflow content";

        try
        {
            // Step 1: Upload original file
            await File.WriteAllTextAsync(tempFile, originalContent);
            var uploadResult = await CliHelper.ExecuteCommandAsync($"jobs files upload {_testJobId} {tempFile} --name {fileName}");
            Assert.Equal(0, uploadResult.ExitCode);

            // Step 2: Update and overwrite file
            await File.WriteAllTextAsync(tempFile, newContent);
            var overwriteResult = await CliHelper.ExecuteCommandAsync($"jobs files upload {_testJobId} {tempFile} --name {fileName} --overwrite");
            Assert.Equal(0, overwriteResult.ExitCode);

            // Step 3: Download and verify new content
            var downloadResult = await CliHelper.ExecuteCommandAsync($"jobs files download {_testJobId} {fileName} --output {downloadFile}");
            Assert.Equal(0, downloadResult.ExitCode);
            
            var downloadedContent = await File.ReadAllTextAsync(downloadFile);
            Assert.Equal(newContent, downloadedContent);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
            if (File.Exists(downloadFile))
                File.Delete(downloadFile);
        }
    }

    #endregion

    #region Authentication E2E Tests

    [Fact]
    public async Task JobFilesCommand_E2E_WithoutAuthentication_ShouldReturnError()
    {
        // Arrange - Logout first
        await CliHelper.ExecuteCommandAsync("auth logout");

        try
        {
            // Act
            var result = await CliHelper.ExecuteCommandAsync($"jobs files list {_testJobId}");

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
    public async Task JobFilesCommand_E2E_WithInvalidArguments_ShouldReturnError()
    {
        // Act - Missing job ID argument
        var result = await CliHelper.ExecuteCommandAsync("jobs files list");

        // Assert
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("required", result.StandardOutput.ToLowerInvariant());
    }

    [Fact]
    public async Task JobFilesCommand_E2E_WithHelpFlag_ShouldShowHelp()
    {
        // Act
        var result = await CliHelper.ExecuteCommandAsync("jobs files list --help");

        // Assert
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("job", result.StandardOutput.ToLowerInvariant());
        Assert.Contains("usage", result.StandardOutput.ToLowerInvariant());
    }

    #endregion
}