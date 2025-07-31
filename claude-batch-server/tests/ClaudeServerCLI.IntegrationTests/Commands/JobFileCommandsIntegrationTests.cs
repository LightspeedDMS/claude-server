using System.CommandLine;
using System.CommandLine.Invocation;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
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
    private string? _authToken;

    public JobFileCommandsIntegrationTests(TestServerHarness testServer)
    {
        _testServer = testServer;
        _mockAuthService = new Mock<IAuthService>();
        
        // Create real API client pointing to test server with SSL certificate bypass
        var handler = new HttpClientHandler()
        {
            ClientCertificateOptions = ClientCertificateOption.Manual,
            ServerCertificateCustomValidationCallback = (httpRequestMessage, cert, cetChain, policyErrors) => true
        };
        var httpClient = new HttpClient(handler);
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
            .ReturnsAsync(() => _authToken ?? "");
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
        _authToken = loginResponse.Token;
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
            Console.WriteLine($"[DEBUG] Created test job with ID: {_testJobId}");
            
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
        var services = CreateServiceProvider();
        var rootCommand = CreateRootCommandWithServices(services);
        var jobFilesCommand = new JobFilesCommand();
        rootCommand.AddCommand(jobFilesCommand);

        // Act
        var result = await TestCommandExecution(rootCommand, $"files list {_testJobId}");

        // Assert
        Assert.Equal(0, result);
    }

    [Fact]
    public async Task JobFilesListCommand_WithNonExistentJob_ShouldReturnError()
    {
        // Arrange
        var services = CreateServiceProvider();
        var rootCommand = CreateRootCommandWithServices(services);
        var jobFilesCommand = new JobFilesCommand();
        rootCommand.AddCommand(jobFilesCommand);

        // Act
        var result = await TestCommandExecution(rootCommand, "files list nonexistent-job-id");

        // Assert
        Assert.Equal(1, result);
    }

    #endregion

    #region Job Files Download Integration Tests

    [Fact]
    public async Task JobFilesDownloadCommand_WithValidFile_ShouldDownloadFile()
    {
        // Arrange
        var services = CreateServiceProvider();
        var rootCommand = CreateRootCommandWithServices(services);
        var jobFilesCommand = new JobFilesCommand();
        rootCommand.AddCommand(jobFilesCommand);
        
        var tempFile = Path.GetTempFileName();

        try
        {
            // Act
            var result = await TestCommandExecution(rootCommand, $"files download {_testJobId} test-input.txt --output {tempFile}");

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
        var services = CreateServiceProvider();
        var rootCommand = CreateRootCommandWithServices(services);
        var jobFilesCommand = new JobFilesCommand();
        rootCommand.AddCommand(jobFilesCommand);
        
        var currentDir = Directory.GetCurrentDirectory();
        var expectedFile = Path.Combine(currentDir, "config.json");

        try
        {
            // Act
            var result = await TestCommandExecution(rootCommand, $"files download {_testJobId} config.json");

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
        var services = CreateServiceProvider();
        var rootCommand = CreateRootCommandWithServices(services);
        var jobFilesCommand = new JobFilesCommand();
        rootCommand.AddCommand(jobFilesCommand);

        // Act
        var result = await TestCommandExecution(rootCommand, $"files download {_testJobId} nonexistent-file.txt");

        // Assert
        Assert.Equal(1, result);
    }

    #endregion

    #region Job Files Upload Integration Tests

    [Fact]
    public async Task JobFilesUploadCommand_WithValidFile_ShouldUploadFile()
    {
        // Arrange
        var services = CreateServiceProvider();
        var rootCommand = CreateRootCommandWithServices(services);
        var jobFilesCommand = new JobFilesCommand();
        rootCommand.AddCommand(jobFilesCommand);
        
        var tempFile = Path.GetTempFileName();
        await File.WriteAllTextAsync(tempFile, "New test file content for upload");

        try
        {
            // Act
            var result = await TestCommandExecution(rootCommand, $"files upload {_testJobId} {tempFile}");

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
        var services = CreateServiceProvider();
        var rootCommand = CreateRootCommandWithServices(services);
        var jobFilesCommand = new JobFilesCommand();
        rootCommand.AddCommand(jobFilesCommand);
        
        var tempFile = Path.GetTempFileName();
        await File.WriteAllTextAsync(tempFile, "Overwritten content");

        try
        {
            // First upload
            await TestCommandExecution(rootCommand, $"files upload {_testJobId} {tempFile}");
            
            // Update file content
            await File.WriteAllTextAsync(tempFile, "New overwritten content");

            // Act - Upload with overwrite
            var result = await TestCommandExecution(rootCommand, $"files upload {_testJobId} {tempFile} --overwrite");

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
        var services = CreateServiceProvider();
        var rootCommand = CreateRootCommandWithServices(services);
        var jobFilesCommand = new JobFilesCommand();
        rootCommand.AddCommand(jobFilesCommand);

        // Act
        var result = await TestCommandExecution(rootCommand, $"files upload {_testJobId} /tmp/nonexistent-file.txt");

        // Assert
        Assert.Equal(1, result);
    }

    #endregion

    #region Helper Methods

    private async Task<int> TestCommandExecution(RootCommand rootCommand, string commandLine)
    {
        var args = commandLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        
        // Capture output for debugging
        var originalOut = Console.Out;
        var originalError = Console.Error;
        
        using var outWriter = new StringWriter();
        using var errorWriter = new StringWriter();
        
        Console.SetOut(outWriter);
        Console.SetError(errorWriter);
        
        try
        {
            var result = await rootCommand.InvokeAsync(args);
            
            var output = outWriter.ToString();
            var error = errorWriter.ToString();
            
            // Always write debug info to original console so it shows in test output
            originalOut.WriteLine($"[DEBUG] Command: '{commandLine}' returned exit code {result}");
            if (!string.IsNullOrEmpty(output))
            {
                originalOut.WriteLine($"[DEBUG] Output: {output}");
            }
            if (!string.IsNullOrEmpty(error))
            {
                originalOut.WriteLine($"[DEBUG] Error: {error}");
            }
            
            return result;
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }

    private IServiceProvider CreateServiceProvider()
    {
        var services = new ServiceCollection();
        
        // Add logging
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        
        // Add API client
        services.AddSingleton(_apiClient);
        
        // Add auth service mock that returns authenticated state
        services.AddSingleton<IAuthService>(_mockAuthService.Object);
        
        return services.BuildServiceProvider();
    }

    private RootCommand CreateRootCommandWithServices(IServiceProvider serviceProvider)
    {
        var rootCommand = new RootCommand();
        
        // Configure service provider for the command tree
        rootCommand.SetHandler((context) =>
        {
            context.BindingContext.AddService<IServiceProvider>(_ => serviceProvider);
        });
        
        return rootCommand;
    }

    #endregion
}
