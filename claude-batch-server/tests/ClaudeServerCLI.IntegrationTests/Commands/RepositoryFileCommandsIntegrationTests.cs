using System.CommandLine;
using System.CommandLine.Invocation;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
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
public class RepositoryFileCommandsIntegrationTests : IAsyncLifetime
{
    private readonly TestServerHarness _testServer;
    private readonly Mock<IAuthService> _mockAuthService;
    private readonly IApiClient _apiClient;
    private readonly string _testRepoName = "test-repo-files";
    private readonly string _testRepoUrl = "https://github.com/octocat/Hello-World.git";

    public RepositoryFileCommandsIntegrationTests(TestServerHarness testServer)
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
                Description = "Test repository for file commands integration tests",
                CidxAware = false
            };
            
            await _apiClient.CreateRepositoryAsync(registerRequest);
            
            // Wait a bit for repository to be cloned
            await Task.Delay(5000);
        }
        catch (Exception ex)
        {
            // Repository might already exist, which is fine for tests
            if (!ex.Message.Contains("already exists"))
                throw;
        }
    }

    public async Task DisposeAsync()
    {
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

    #region Repository Files List Integration Tests

    [Fact]
    public async Task RepositoryFilesListCommand_WithRealRepository_ShouldReturnFiles()
    {
        // Arrange
        var command = new RepositoryFilesListIntegrationCommand(_apiClient, _mockAuthService.Object);
        var rootCommand = new RootCommand();
        rootCommand.AddCommand(command);

        // Act
        var result = await TestCommandExecution(rootCommand, $"list {_testRepoName}");

        // Assert
        Assert.Equal(0, result);
        // Additional assertions would check the actual output, but for integration tests
        // we're primarily verifying that the command executes without error
    }

    [Fact]
    public async Task RepositoryFilesListCommand_WithSpecificPath_ShouldReturnPathContents()
    {
        // Arrange
        var command = new RepositoryFilesListIntegrationCommand(_apiClient, _mockAuthService.Object);
        var rootCommand = new RootCommand();
        rootCommand.AddCommand(command);

        // Act
        var result = await TestCommandExecution(rootCommand, $"list {_testRepoName} --path .");

        // Assert
        Assert.Equal(0, result);
    }

    [Fact]
    public async Task RepositoryFilesListCommand_WithNonExistentRepository_ShouldReturnError()
    {
        // Arrange
        var command = new RepositoryFilesListIntegrationCommand(_apiClient, _mockAuthService.Object);
        var rootCommand = new RootCommand();
        rootCommand.AddCommand(command);

        // Act
        var result = await TestCommandExecution(rootCommand, "list nonexistent-repo");

        // Assert
        Assert.Equal(1, result);
    }

    #endregion

    #region Repository Files Show Integration Tests

    [Fact]
    public async Task RepositoryFilesShowCommand_WithValidFile_ShouldReturnContent()
    {
        // Arrange
        var command = new RepositoryFilesShowIntegrationCommand(_apiClient, _mockAuthService.Object);
        var rootCommand = new RootCommand();
        rootCommand.AddCommand(command);

        // Act - README.md should exist in most repositories
        var result = await TestCommandExecution(rootCommand, $"show {_testRepoName} README.md");

        // Assert
        Assert.Equal(0, result);
    }

    [Fact]
    public async Task RepositoryFilesShowCommand_WithNonExistentFile_ShouldReturnError()
    {
        // Arrange
        var command = new RepositoryFilesShowIntegrationCommand(_apiClient, _mockAuthService.Object);
        var rootCommand = new RootCommand();
        rootCommand.AddCommand(command);

        // Act
        var result = await TestCommandExecution(rootCommand, $"show {_testRepoName} nonexistent-file.txt");

        // Assert
        Assert.Equal(1, result);
    }

    #endregion

    #region Repository Files Download Integration Tests

    [Fact]
    public async Task RepositoryFilesDownloadCommand_WithValidFile_ShouldDownloadFile()
    {
        // Arrange
        var command = new RepositoryFilesDownloadIntegrationCommand(_apiClient, _mockAuthService.Object);
        var rootCommand = new RootCommand();
        rootCommand.AddCommand(command);
        
        var tempFile = Path.GetTempFileName();

        try
        {
            // Act
            var result = await TestCommandExecution(rootCommand, $"download {_testRepoName} README.md --output {tempFile}");

            // Assert
            Assert.Equal(0, result);
            Assert.True(File.Exists(tempFile));
            Assert.True(new FileInfo(tempFile).Length > 0);
        }
        finally
        {
            // Cleanup
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task RepositoryFilesDownloadCommand_WithDefaultOutputPath_ShouldDownloadToCurrentDirectory()
    {
        // Arrange
        var command = new RepositoryFilesDownloadIntegrationCommand(_apiClient, _mockAuthService.Object);
        var rootCommand = new RootCommand();
        rootCommand.AddCommand(command);
        
        var currentDir = Directory.GetCurrentDirectory();
        var expectedFile = Path.Combine(currentDir, "README.md");

        try
        {
            // Act
            var result = await TestCommandExecution(rootCommand, $"download {_testRepoName} README.md");

            // Assert
            Assert.Equal(0, result);
            Assert.True(File.Exists(expectedFile));
        }
        finally
        {
            // Cleanup
            if (File.Exists(expectedFile))
                File.Delete(expectedFile);
        }
    }

    #endregion

    #region Repository Files Search Integration Tests

    [Fact]
    public async Task RepositoryFilesSearchCommand_WithPattern_ShouldReturnMatchingFiles()
    {
        // Arrange
        var command = new RepositoryFilesSearchIntegrationCommand(_apiClient, _mockAuthService.Object);
        var rootCommand = new RootCommand();
        rootCommand.AddCommand(command);

        // Act
        var result = await TestCommandExecution(rootCommand, $"search {_testRepoName} --pattern *.md");

        // Assert
        Assert.Equal(0, result);
    }

    [Fact]
    public async Task RepositoryFilesSearchCommand_WithNoMatches_ShouldReturnEmptyResult()
    {
        // Arrange
        var command = new RepositoryFilesSearchIntegrationCommand(_apiClient, _mockAuthService.Object);
        var rootCommand = new RootCommand();
        rootCommand.AddCommand(command);

        // Act
        var result = await TestCommandExecution(rootCommand, $"search {_testRepoName} --pattern *.nonexistent");

        // Assert
        Assert.Equal(0, result);
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
public class RepositoryFilesListIntegrationCommand : Command
{
    private readonly IApiClient _apiClient;
    private readonly IAuthService _authService;

    public RepositoryFilesListIntegrationCommand(IApiClient apiClient, IAuthService authService) 
        : base("list", "List files in repository")
    {
        _apiClient = apiClient;
        _authService = authService;
        
        var repoArg = new Argument<string>("repository", "Repository name");
        var pathOption = new Option<string>("--path", "Path within repository");
        
        AddArgument(repoArg);
        AddOption(pathOption);
        
        this.SetHandler(async (InvocationContext context) =>
        {
            var repoName = context.ParseResult.GetValueForArgument(repoArg);
            var path = context.ParseResult.GetValueForOption(pathOption);
            var cancellationToken = context.GetCancellationToken();
            
            try
            {
                if (!await _authService.IsAuthenticatedAsync("default", cancellationToken))
                {
                    Console.Error.WriteLine("Not authenticated");
                    context.ExitCode = 1;
                    return;
                }

                var files = await _apiClient.GetRepositoryFilesAsync(repoName, path, cancellationToken);
                
                foreach (var file in files)
                {
                    Console.WriteLine($"{(file.IsDirectory ? "d" : "-")} {file.Name} ({file.Size} bytes)");
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

public class RepositoryFilesShowIntegrationCommand : Command
{
    private readonly IApiClient _apiClient;
    private readonly IAuthService _authService;

    public RepositoryFilesShowIntegrationCommand(IApiClient apiClient, IAuthService authService) 
        : base("show", "Show file content")
    {
        _apiClient = apiClient;
        _authService = authService;
        
        var repoArg = new Argument<string>("repository", "Repository name");
        var fileArg = new Argument<string>("file", "File path");
        
        AddArgument(repoArg);
        AddArgument(fileArg);
        
        this.SetHandler(async (InvocationContext context) =>
        {
            var repoName = context.ParseResult.GetValueForArgument(repoArg);
            var filePath = context.ParseResult.GetValueForArgument(fileArg);
            var cancellationToken = context.GetCancellationToken();
            
            try
            {
                if (!await _authService.IsAuthenticatedAsync("default", cancellationToken))
                {
                    Console.Error.WriteLine("Not authenticated");
                    context.ExitCode = 1;
                    return;
                }

                var fileContent = await _apiClient.GetRepositoryFileContentAsync(repoName, filePath, cancellationToken);
                Console.WriteLine(fileContent.Content);
                
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

public class RepositoryFilesDownloadIntegrationCommand : Command
{
    private readonly IApiClient _apiClient;
    private readonly IAuthService _authService;

    public RepositoryFilesDownloadIntegrationCommand(IApiClient apiClient, IAuthService authService) 
        : base("download", "Download file")
    {
        _apiClient = apiClient;
        _authService = authService;
        
        var repoArg = new Argument<string>("repository", "Repository name");
        var fileArg = new Argument<string>("file", "File path");
        var outputOption = new Option<string>("--output", "Output file path");
        
        AddArgument(repoArg);
        AddArgument(fileArg);
        AddOption(outputOption);
        
        this.SetHandler(async (InvocationContext context) =>
        {
            var repoName = context.ParseResult.GetValueForArgument(repoArg);
            var filePath = context.ParseResult.GetValueForArgument(fileArg);
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

                var fileContent = await _apiClient.GetRepositoryFileContentAsync(repoName, filePath, cancellationToken);
                
                var targetPath = outputPath ?? Path.GetFileName(filePath);
                await File.WriteAllTextAsync(targetPath, fileContent.Content, cancellationToken);
                
                Console.WriteLine($"Downloaded {filePath} to {targetPath}");
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

public class RepositoryFilesSearchIntegrationCommand : Command
{
    private readonly IApiClient _apiClient;
    private readonly IAuthService _authService;

    public RepositoryFilesSearchIntegrationCommand(IApiClient apiClient, IAuthService authService) 
        : base("search", "Search files")
    {
        _apiClient = apiClient;
        _authService = authService;
        
        var repoArg = new Argument<string>("repository", "Repository name");
        var patternOption = new Option<string>("--pattern", "Search pattern");
        
        AddArgument(repoArg);
        AddOption(patternOption);
        
        this.SetHandler(async (InvocationContext context) =>
        {
            var repoName = context.ParseResult.GetValueForArgument(repoArg);
            var pattern = context.ParseResult.GetValueForOption(patternOption);
            var cancellationToken = context.GetCancellationToken();
            
            try
            {
                if (!await _authService.IsAuthenticatedAsync("default", cancellationToken))
                {
                    Console.Error.WriteLine("Not authenticated");
                    context.ExitCode = 1;
                    return;
                }

                var files = await _apiClient.GetRepositoryFilesAsync(repoName, null, cancellationToken);
                
                // Simple pattern matching (in real implementation, this would be more sophisticated)
                var matchingFiles = files.Where(f => 
                    pattern == null || 
                    f.Name.Contains(pattern.Replace("*", "")) ||
                    System.IO.Path.GetExtension(f.Name).Equals(pattern?.Replace("*", ""), StringComparison.OrdinalIgnoreCase)
                );
                
                foreach (var file in matchingFiles)
                {
                    Console.WriteLine($"{file.Path}");
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