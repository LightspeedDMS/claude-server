using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using ClaudeServerCLI.Commands;
using ClaudeServerCLI.Services;
using ClaudeServerCLI.Models;
using ClaudeBatchServer.Core.DTOs;

namespace ClaudeServerCLI.IntegrationTests.Commands;

[Collection("TestServer")]
public class JobFileCommandsIntegrationTests : IAsyncLifetime
{
    private readonly TestServerHarness _testServer;
    private readonly Mock<IAuthService> _mockAuthService;
    private readonly IApiClient _apiClient;
    private readonly string _testRepoName = "test-repo-job-files";
    private readonly string _testRepoUrl = "https://github.com/octocat/Hello-World.git";
    private string? _testJobId;

    public JobFileCommandsIntegrationTests(TestServerHarness testServer)
    {
        _testServer = testServer;
        _mockAuthService = new Mock<IAuthService>();
        
        // Create real API client pointing to test server
        var httpClient = new HttpClient();
        var mockLogger = new Mock<ILogger<ApiClient>>();
        var options = Options.Create(new ApiClientOptions
        {
            BaseUrl = _testServer.ServerUrl,
            TimeoutSeconds = 30,
            RetryCount = 2,
            RetryDelayMs = 100
        });
        
        _apiClient = new ApiClient(httpClient, options, mockLogger.Object);
        
        // Setup auth service to return test credentials
        _mockAuthService.Setup(x => x.IsAuthenticatedAsync("default", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mockAuthService.Setup(x => x.GetCurrentUserAsync("default", It.IsAny<CancellationToken>()))
            .ReturnsAsync("testuser");
        _mockAuthService.Setup(x => x.GetTokenAsync("default", It.IsAny<CancellationToken>()))
            .ReturnsAsync("test-token");
    }

    public async Task InitializeAsync()
    {
        // Login to test server
        var loginRequest = new LoginRequest
        {
            Username = _testServer.TestUser,
            Password = _testServer.TestPassword
        };
        
        var loginResponse = await _apiClient.LoginAsync(loginRequest);
        _apiClient.SetAuthToken(loginResponse.Token);
        
        // Create test repository
        try
        {
            var registerRequest = new RegisterRepositoryRequest
            {
                Name = _testRepoName,
                GitUrl = _testRepoUrl,
                Description = "Test repository for job file commands integration tests",
                CidxAware = false
            };
            
            await _apiClient.CreateRepositoryAsync(registerRequest);
            
            // Wait a bit for repository to be cloned
            await Task.Delay(3000);
        }
        catch (Exception ex)
        {
            // Repository might already exist, which is fine for tests
            if (!ex.Message.Contains("already exists"))
                throw;
        }

        // Create test job
        try
        {
            var createJobRequest = new CreateJobRequest
            {
                Repository = _testRepoName,
                Prompt = "List files in this repository",
                Options = new JobOptionsDto
                {
                    Timeout = 300,
                    GitAware = true,
                    CidxAware = false
                }
            };
            
            var jobResponse = await _apiClient.CreateJobAsync(createJobRequest);
            _testJobId = jobResponse.JobId.ToString();
            
            // Upload some test files to the job
            var testFile1 = new FileUpload
            {
                FileName = "test-input.txt",
                Content = Encoding.UTF8.GetBytes("This is a test input file")
            };
            
            var testFile2 = new FileUpload
            {
                FileName = "config.json",
                Content = Encoding.UTF8.GetBytes("{\"testConfig\": true}")
            };
            
            await _apiClient.UploadSingleFileAsync(_testJobId, testFile1);
            await _apiClient.UploadSingleFileAsync(_testJobId, testFile2);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to create test job: {ex.Message}", ex);
        }
    }

    public async Task DisposeAsync()
    {
        // Cleanup test job
        if (!string.IsNullOrEmpty(_testJobId))
        {
            try
            {
                await _apiClient.DeleteJobAsync(_testJobId);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }

        // Cleanup test repository
        try
        {
            await _apiClient.DeleteRepositoryAsync(_testRepoName);
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    #region Job Files List Integration Tests

    [Fact]
    public async Task JobFilesListCommand_WithValidJob_ShouldReturnFiles()
    {
        // Arrange
        var command = new JobFilesListIntegrationCommand(_apiClient, _mockAuthService.Object);
        var rootCommand = new RootCommand();
        rootCommand.AddCommand(command);

        // Act
        var result = await TestCommandExecution(rootCommand, $"list {_testJobId}");

        // Assert
        Assert.Equal(0, result);
    }

    [Fact]
    public async Task JobFilesListCommand_WithNonExistentJob_ShouldReturnError()
    {
        // Arrange
        var command = new JobFilesListIntegrationCommand(_apiClient, _mockAuthService.Object);
        var rootCommand = new RootCommand();
        rootCommand.AddCommand(command);

        // Act
        var result = await TestCommandExecution(rootCommand, "list nonexistent-job-id");

        // Assert
        Assert.Equal(1, result);
    }

    #endregion

    #region Job Files Download Integration Tests

    [Fact]
    public async Task JobFilesDownloadCommand_WithValidFile_ShouldDownloadFile()
    {
        // Arrange
        var command = new JobFilesDownloadIntegrationCommand(_apiClient, _mockAuthService.Object);
        var rootCommand = new RootCommand();
        rootCommand.AddCommand(command);
        
        var tempFile = Path.GetTempFileName();

        try
        {
            // Act
            var result = await TestCommandExecution(rootCommand, $"download {_testJobId} test-input.txt --output {tempFile}");

            // Assert
            Assert.Equal(0, result);
            Assert.True(File.Exists(tempFile));
            
            var content = await File.ReadAllTextAsync(tempFile);
            Assert.Contains("This is a test input file", content);
        }
        finally
        {
            // Cleanup
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task JobFilesDownloadCommand_WithDefaultOutputPath_ShouldDownloadToCurrentDirectory()
    {
        // Arrange
        var command = new JobFilesDownloadIntegrationCommand(_apiClient, _mockAuthService.Object);
        var rootCommand = new RootCommand();
        rootCommand.AddCommand(command);
        
        var currentDir = Directory.GetCurrentDirectory();
        var expectedFile = Path.Combine(currentDir, "config.json");

        try
        {
            // Act
            var result = await TestCommandExecution(rootCommand, $"download {_testJobId} config.json");

            // Assert
            Assert.Equal(0, result);
            Assert.True(File.Exists(expectedFile));
            
            var content = await File.ReadAllTextAsync(expectedFile);
            Assert.Contains("testConfig", content);
        }
        finally
        {
            // Cleanup
            if (File.Exists(expectedFile))
                File.Delete(expectedFile);
        }
    }

    [Fact]
    public async Task JobFilesDownloadCommand_WithNonExistentFile_ShouldReturnError()
    {
        // Arrange
        var command = new JobFilesDownloadIntegrationCommand(_apiClient, _mockAuthService.Object);
        var rootCommand = new RootCommand();
        rootCommand.AddCommand(command);

        // Act
        var result = await TestCommandExecution(rootCommand, $"download {_testJobId} nonexistent-file.txt");

        // Assert
        Assert.Equal(1, result);
    }

    #endregion

    #region Job Files Upload Integration Tests

    [Fact]
    public async Task JobFilesUploadCommand_WithValidFile_ShouldUploadFile()
    {
        // Arrange
        var command = new JobFilesUploadIntegrationCommand(_apiClient, _mockAuthService.Object);
        var rootCommand = new RootCommand();
        rootCommand.AddCommand(command);
        
        var tempFile = Path.GetTempFileName();
        await File.WriteAllTextAsync(tempFile, "New test file content for upload");

        try
        {
            // Act
            var result = await TestCommandExecution(rootCommand, $"upload {_testJobId} {tempFile}");

            // Assert
            Assert.Equal(0, result);
            
            // Verify file was uploaded by checking job files
            var jobFiles = await _apiClient.GetJobFilesAsync(_testJobId!);
            Assert.Contains(jobFiles, f => f.FileName == Path.GetFileName(tempFile));
        }
        finally
        {
            // Cleanup
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task JobFilesUploadCommand_WithOverwriteFlag_ShouldUploadWithOverwrite()
    {
        // Arrange
        var command = new JobFilesUploadIntegrationCommand(_apiClient, _mockAuthService.Object);
        var rootCommand = new RootCommand();
        rootCommand.AddCommand(command);
        
        var tempFile = Path.GetTempFileName();
        await File.WriteAllTextAsync(tempFile, "Overwritten content");

        try
        {
            // First upload
            await TestCommandExecution(rootCommand, $"upload {_testJobId} {tempFile}");
            
            // Update file content
            await File.WriteAllTextAsync(tempFile, "New overwritten content");

            // Act - Upload with overwrite
            var result = await TestCommandExecution(rootCommand, $"upload {_testJobId} {tempFile} --overwrite");

            // Assert
            Assert.Equal(0, result);
        }
        finally
        {
            // Cleanup
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task JobFilesUploadCommand_WithNonExistentFile_ShouldReturnError()
    {
        // Arrange
        var command = new JobFilesUploadIntegrationCommand(_apiClient, _mockAuthService.Object);
        var rootCommand = new RootCommand();
        rootCommand.AddCommand(command);

        // Act
        var result = await TestCommandExecution(rootCommand, $"upload {_testJobId} /tmp/nonexistent-file.txt");

        // Assert
        Assert.Equal(1, result);
    }

    #endregion

    #region Helper Methods

    private async Task<int> TestCommandExecution(RootCommand rootCommand, string commandLine)
    {
        var args = commandLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return await rootCommand.InvokeAsync(args);
    }

    #endregion
}

// Integration command implementations that use real API client
public class JobFilesListIntegrationCommand : Command
{
    private readonly IApiClient _apiClient;
    private readonly IAuthService _authService;

    public JobFilesListIntegrationCommand(IApiClient apiClient, IAuthService authService) 
        : base("list", "List files in job")
    {
        _apiClient = apiClient;
        _authService = authService;
        
        var jobIdArg = new Argument<string>("jobId", "Job ID");
        AddArgument(jobIdArg);
        
        this.SetHandler(async (InvocationContext context) =>
        {
            var jobId = context.ParseResult.GetValueForArgument(jobIdArg);
            var cancellationToken = context.GetCancellationToken();
            
            try
            {
                if (!await _authService.IsAuthenticatedAsync("default", cancellationToken))
                {
                    Console.Error.WriteLine("Not authenticated");
                    context.ExitCode = 1;
                    return;
                }

                var files = await _apiClient.GetJobFilesAsync(jobId, cancellationToken);
                
                foreach (var file in files)
                {
                    Console.WriteLine($"{file.FileName} ({file.Size} bytes) - {file.CreatedAt:yyyy-MM-dd HH:mm:ss}");
                }
                
                context.ExitCode = 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
                context.ExitCode = 1;
            }
        });
    }
}

public class JobFilesDownloadIntegrationCommand : Command
{
    private readonly IApiClient _apiClient;
    private readonly IAuthService _authService;

    public JobFilesDownloadIntegrationCommand(IApiClient apiClient, IAuthService authService) 
        : base("download", "Download job file")
    {
        _apiClient = apiClient;
        _authService = authService;
        
        var jobIdArg = new Argument<string>("jobId", "Job ID");
        var fileNameArg = new Argument<string>("fileName", "File name");
        var outputOption = new Option<string>("--output", "Output file path");
        
        AddArgument(jobIdArg);
        AddArgument(fileNameArg);
        AddOption(outputOption);
        
        this.SetHandler(async (InvocationContext context) =>
        {
            var jobId = context.ParseResult.GetValueForArgument(jobIdArg);
            var fileName = context.ParseResult.GetValueForArgument(fileNameArg);
            var outputPath = context.ParseResult.GetValueForOption(outputOption);
            var cancellationToken = context.GetCancellationToken();
            
            try
            {
                if (!await _authService.IsAuthenticatedAsync("default", cancellationToken))
                {
                    Console.Error.WriteLine("Not authenticated");
                    context.ExitCode = 1;
                    return;
                }

                var fileStream = await _apiClient.DownloadJobFileAsync(jobId, fileName, cancellationToken);
                
                var targetPath = outputPath ?? fileName;
                using (var outputStream = File.Create(targetPath))
                {
                    await fileStream.CopyToAsync(outputStream, cancellationToken);
                }
                
                Console.WriteLine($"Downloaded {fileName} to {targetPath}");
                context.ExitCode = 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
                context.ExitCode = 1;
            }
        });
    }
}

public class JobFilesUploadIntegrationCommand : Command
{
    private readonly IApiClient _apiClient;
    private readonly IAuthService _authService;

    public JobFilesUploadIntegrationCommand(IApiClient apiClient, IAuthService authService) 
        : base("upload", "Upload file to job")
    {
        _apiClient = apiClient;
        _authService = authService;
        
        var jobIdArg = new Argument<string>("jobId", "Job ID");
        var localFileArg = new Argument<string>("localFile", "Local file path");
        var overwriteOption = new Option<bool>("--overwrite", "Overwrite existing file");
        
        AddArgument(jobIdArg);
        AddArgument(localFileArg);
        AddOption(overwriteOption);
        
        this.SetHandler(async (InvocationContext context) =>
        {
            var jobId = context.ParseResult.GetValueForArgument(jobIdArg);
            var localFilePath = context.ParseResult.GetValueForArgument(localFileArg);
            var overwrite = context.ParseResult.GetValueForOption(overwriteOption);
            var cancellationToken = context.GetCancellationToken();
            
            try
            {
                if (!await _authService.IsAuthenticatedAsync("default", cancellationToken))
                {
                    Console.Error.WriteLine("Not authenticated");
                    context.ExitCode = 1;
                    return;
                }

                if (!File.Exists(localFilePath))
                {
                    Console.Error.WriteLine($"File not found: {localFilePath}");
                    context.ExitCode = 1;
                    return;
                }

                var fileContent = await File.ReadAllBytesAsync(localFilePath, cancellationToken);
                var fileUpload = new FileUpload
                {
                    FileName = Path.GetFileName(localFilePath),
                    Content = fileContent
                };

                var response = await _apiClient.UploadSingleFileAsync(jobId, fileUpload, overwrite, cancellationToken);
                
                // If no exception was thrown, upload was successful
                Console.WriteLine($"Uploaded {response.Filename} ({response.FileSize} bytes)");
                context.ExitCode = 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
                context.ExitCode = 1;
            }
        });
    }
}