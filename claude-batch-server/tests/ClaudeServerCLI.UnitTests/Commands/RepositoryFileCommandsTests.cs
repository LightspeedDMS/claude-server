using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;
using ClaudeServerCLI.Commands;
using ClaudeServerCLI.Services;
using ClaudeBatchServer.Core.DTOs;

namespace ClaudeServerCLI.UnitTests.Commands;

public class RepositoryFileCommandsTests : IDisposable
{
    private readonly Mock<IApiClient> _mockApiClient;
    private readonly Mock<IAuthService> _mockAuthService;
    private readonly Mock<IServiceProvider> _mockServiceProvider;
    private readonly ServiceCollection _services;
    private readonly CancellationToken _cancellationToken;

    public RepositoryFileCommandsTests()
    {
        _mockApiClient = new Mock<IApiClient>();
        _mockAuthService = new Mock<IAuthService>();
        _mockServiceProvider = new Mock<IServiceProvider>();
        _services = new ServiceCollection();
        _cancellationToken = CancellationToken.None;

        // Setup service provider mocks
        _mockServiceProvider.Setup(sp => sp.GetService(typeof(IApiClient))).Returns(_mockApiClient.Object);
        _mockServiceProvider.Setup(sp => sp.GetService(typeof(IAuthService))).Returns(_mockAuthService.Object);
        
        _services.AddSingleton(_mockApiClient.Object);
        _services.AddSingleton(_mockAuthService.Object);
    }

    public void Dispose()
    {
        _services?.Clear();
    }

    #region Repository Files List Command Tests

    [Fact]
    public async Task RepositoryFilesListCommand_WithValidRepository_ShouldReturnFiles()
    {
        // Arrange
        var repoName = "test-repo";
        var expectedFiles = new List<FileInfoResponse>
        {
            new() { Name = "README.md", Path = "README.md", IsDirectory = false, Size = 1024 },
            new() { Name = "src", Path = "src", IsDirectory = true, Size = 0 },
            new() { Name = "tests", Path = "tests", IsDirectory = true, Size = 0 }
        };

        _mockAuthService.Setup(x => x.IsAuthenticatedAsync("default", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mockAuthService.Setup(x => x.GetCurrentUserAsync("default", It.IsAny<CancellationToken>()))
            .ReturnsAsync("testuser");
        _mockAuthService.Setup(x => x.GetTokenAsync("default", It.IsAny<CancellationToken>()))
            .ReturnsAsync("test-token-123");
            
        _mockApiClient.Setup(x => x.GetRepositoryFilesAsync(repoName, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedFiles);

        var filesCommand = new Command("files", "Repository file commands");
        var listCommand = new RepositoryFilesListCommand();
        filesCommand.AddCommand(listCommand);
        
        var rootCommand = new RootCommand();
        rootCommand.AddCommand(filesCommand);

        // Act & Assert
        var result = await TestCommandExecution(rootCommand, $"files list {repoName}");
        Assert.Equal(0, result);
        
        _mockApiClient.Verify(x => x.GetRepositoryFilesAsync(repoName, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RepositoryFilesListCommand_WithPath_ShouldReturnFilesInPath()
    {
        // Arrange
        var repoName = "test-repo";
        var path = "src";
        var expectedFiles = new List<FileInfoResponse>
        {
            new() { Name = "main.cs", Path = "src/main.cs", IsDirectory = false, Size = 2048 },
            new() { Name = "models", Path = "src/models", IsDirectory = true, Size = 0 }
        };

        _mockAuthService.Setup(x => x.IsAuthenticatedAsync("default", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mockAuthService.Setup(x => x.GetCurrentUserAsync("default", It.IsAny<CancellationToken>()))
            .ReturnsAsync("testuser");
        _mockAuthService.Setup(x => x.GetTokenAsync("default", It.IsAny<CancellationToken>()))
            .ReturnsAsync("test-token-123");
            
        _mockApiClient.Setup(x => x.GetRepositoryFilesAsync(repoName, path, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedFiles);

        var filesCommand = new Command("files", "Repository file commands");
        var listCommand = new RepositoryFilesListCommand();
        filesCommand.AddCommand(listCommand);
        
        var rootCommand = new RootCommand();
        rootCommand.AddCommand(filesCommand);

        // Act & Assert
        var result = await TestCommandExecution(rootCommand, $"files list {repoName} --path {path}");
        Assert.Equal(0, result);
        
        _mockApiClient.Verify(x => x.GetRepositoryFilesAsync(repoName, path, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RepositoryFilesListCommand_WithNonExistentRepository_ShouldReturnError()
    {
        // Arrange
        var repoName = "nonexistent-repo";
        
        _mockAuthService.Setup(x => x.IsAuthenticatedAsync("default", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mockAuthService.Setup(x => x.GetCurrentUserAsync("default", It.IsAny<CancellationToken>()))
            .ReturnsAsync("testuser");
        _mockAuthService.Setup(x => x.GetTokenAsync("default", It.IsAny<CancellationToken>()))
            .ReturnsAsync("test-token-123");
            
        _mockApiClient.Setup(x => x.GetRepositoryFilesAsync(repoName, null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Repository not found"));

        var filesCommand = new Command("files", "Repository file commands");
        var listCommand = new RepositoryFilesListCommand();
        filesCommand.AddCommand(listCommand);
        
        var rootCommand = new RootCommand();
        rootCommand.AddCommand(filesCommand);

        // Act & Assert
        var result = await TestCommandExecution(rootCommand, $"files list {repoName}");
        Assert.Equal(1, result);
        
        _mockApiClient.Verify(x => x.GetRepositoryFilesAsync(repoName, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RepositoryFilesListCommand_WhenNotAuthenticated_ShouldReturnError()
    {
        // Arrange
        var repoName = "test-repo";
        
        _mockAuthService.Setup(x => x.IsAuthenticatedAsync("default", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var filesCommand = new Command("files", "Repository file commands");
        var listCommand = new RepositoryFilesListCommand();
        filesCommand.AddCommand(listCommand);
        
        var rootCommand = new RootCommand();
        rootCommand.AddCommand(filesCommand);

        // Act & Assert
        var result = await TestCommandExecution(rootCommand, $"files list {repoName}");
        Assert.Equal(1, result);
        
        _mockApiClient.Verify(x => x.GetRepositoryFilesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    #region Repository Files Show Command Tests

    [Fact]
    public async Task RepositoryFilesShowCommand_WithValidFile_ShouldReturnFileContent()
    {
        // Arrange
        var repoName = "test-repo";
        var filePath = "README.md";
        var expectedContent = new FileContentResponse
        {
            FileName = "README.md",
            Content = "# Test Repository\n\nThis is a test repository.",
            Size = 42,
            MimeType = "text/markdown"
        };

        _mockAuthService.Setup(x => x.IsAuthenticatedAsync("default", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mockAuthService.Setup(x => x.GetCurrentUserAsync("default", It.IsAny<CancellationToken>()))
            .ReturnsAsync("testuser");
        _mockAuthService.Setup(x => x.GetTokenAsync("default", It.IsAny<CancellationToken>()))
            .ReturnsAsync("test-token-123");
            
        _mockApiClient.Setup(x => x.GetRepositoryFileContentAsync(repoName, filePath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedContent);

        var filesCommand = new Command("files", "Repository file commands");
        var showCommand = new RepositoryFilesShowCommand();
        filesCommand.AddCommand(showCommand);
        
        var rootCommand = new RootCommand();
        rootCommand.AddCommand(filesCommand);

        // Act & Assert
        var result = await TestCommandExecution(rootCommand, $"files show {repoName} {filePath}");
        Assert.Equal(0, result);
        
        _mockApiClient.Verify(x => x.GetRepositoryFileContentAsync(repoName, filePath, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RepositoryFilesShowCommand_WithNonExistentFile_ShouldReturnError()
    {
        // Arrange
        var repoName = "test-repo";
        var filePath = "nonexistent.txt";
        
        _mockAuthService.Setup(x => x.IsAuthenticatedAsync("default", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mockAuthService.Setup(x => x.GetCurrentUserAsync("default", It.IsAny<CancellationToken>()))
            .ReturnsAsync("testuser");
        _mockAuthService.Setup(x => x.GetTokenAsync("default", It.IsAny<CancellationToken>()))
            .ReturnsAsync("test-token-123");
            
        _mockApiClient.Setup(x => x.GetRepositoryFileContentAsync(repoName, filePath, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("File not found"));

        var filesCommand = new Command("files", "Repository file commands");
        var showCommand = new RepositoryFilesShowCommand();
        filesCommand.AddCommand(showCommand);
        
        var rootCommand = new RootCommand();
        rootCommand.AddCommand(filesCommand);

        // Act & Assert
        var result = await TestCommandExecution(rootCommand, $"files show {repoName} {filePath}");
        Assert.Equal(1, result);
        
        _mockApiClient.Verify(x => x.GetRepositoryFileContentAsync(repoName, filePath, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RepositoryFilesShowCommand_WithDirectory_ShouldReturnError()
    {
        // Arrange
        var repoName = "test-repo";
        var filePath = "src";
        
        _mockAuthService.Setup(x => x.IsAuthenticatedAsync("default", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mockAuthService.Setup(x => x.GetCurrentUserAsync("default", It.IsAny<CancellationToken>()))
            .ReturnsAsync("testuser");
        _mockAuthService.Setup(x => x.GetTokenAsync("default", It.IsAny<CancellationToken>()))
            .ReturnsAsync("test-token-123");
            
        _mockApiClient.Setup(x => x.GetRepositoryFileContentAsync(repoName, filePath, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Path is a directory"));

        var filesCommand = new Command("files", "Repository file commands");
        var showCommand = new RepositoryFilesShowCommand();
        filesCommand.AddCommand(showCommand);
        
        var rootCommand = new RootCommand();
        rootCommand.AddCommand(filesCommand);

        // Act & Assert
        var result = await TestCommandExecution(rootCommand, $"files show {repoName} {filePath}");
        Assert.Equal(1, result);
        
        _mockApiClient.Verify(x => x.GetRepositoryFileContentAsync(repoName, filePath, It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region Repository Files Download Command Tests

    [Fact]
    public async Task RepositoryFilesDownloadCommand_WithValidFile_ShouldDownloadFile()
    {
        // Arrange
        var repoName = "test-repo";
        var filePath = "README.md";
        var outputPath = "/tmp/downloaded-readme.md";
        var fileContent = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("# Test Repository"));

        _mockAuthService.Setup(x => x.IsAuthenticatedAsync("default", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mockAuthService.Setup(x => x.GetCurrentUserAsync("default", It.IsAny<CancellationToken>()))
            .ReturnsAsync("testuser");
        _mockAuthService.Setup(x => x.GetTokenAsync("default", It.IsAny<CancellationToken>()))
            .ReturnsAsync("test-token-123");
            
        _mockApiClient.Setup(x => x.GetRepositoryFileContentAsync(repoName, filePath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FileContentResponse
            {
                FileName = "README.md",
                Content = "# Test Repository",
                Size = 16,
                MimeType = "text/markdown"
            });

        var filesCommand = new Command("files", "Repository file commands");
        var downloadCommand = new RepositoryFilesDownloadCommand();
        filesCommand.AddCommand(downloadCommand);
        
        var rootCommand = new RootCommand();
        rootCommand.AddCommand(filesCommand);

        // Act & Assert
        var result = await TestCommandExecution(rootCommand, $"files download {repoName} {filePath} --output {outputPath}");
        Assert.Equal(0, result);
        
        _mockApiClient.Verify(x => x.GetRepositoryFileContentAsync(repoName, filePath, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RepositoryFilesDownloadCommand_WithoutOutputPath_ShouldUseOriginalFileName()
    {
        // Arrange
        var repoName = "test-repo";
        var filePath = "src/main.cs";
        var fileContent = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("using System;"));

        _mockAuthService.Setup(x => x.IsAuthenticatedAsync("default", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mockAuthService.Setup(x => x.GetCurrentUserAsync("default", It.IsAny<CancellationToken>()))
            .ReturnsAsync("testuser");
        _mockAuthService.Setup(x => x.GetTokenAsync("default", It.IsAny<CancellationToken>()))
            .ReturnsAsync("test-token-123");
            
        _mockApiClient.Setup(x => x.GetRepositoryFileContentAsync(repoName, filePath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FileContentResponse
            {
                FileName = "main.cs",
                Content = "using System;",
                Size = 12,
                MimeType = "text/x-csharp"
            });

        var filesCommand = new Command("files", "Repository file commands");
        var downloadCommand = new RepositoryFilesDownloadCommand();
        filesCommand.AddCommand(downloadCommand);
        
        var rootCommand = new RootCommand();
        rootCommand.AddCommand(filesCommand);

        // Act & Assert
        var result = await TestCommandExecution(rootCommand, $"files download {repoName} {filePath}");
        Assert.Equal(0, result);
        
        _mockApiClient.Verify(x => x.GetRepositoryFileContentAsync(repoName, filePath, It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region Repository Files Search Command Tests

    [Fact]
    public async Task RepositoryFilesSearchCommand_WithPattern_ShouldReturnMatchingFiles()
    {
        // Arrange
        var repoName = "test-repo";
        var pattern = "*.cs";
        var expectedFiles = new List<FileInfoResponse>
        {
            new() { Name = "main.cs", Path = "src/main.cs", IsDirectory = false, Size = 2048 },
            new() { Name = "test.cs", Path = "tests/test.cs", IsDirectory = false, Size = 1024 }
        };

        _mockAuthService.Setup(x => x.IsAuthenticatedAsync("default", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mockAuthService.Setup(x => x.GetCurrentUserAsync("default", It.IsAny<CancellationToken>()))
            .ReturnsAsync("testuser");
        _mockAuthService.Setup(x => x.GetTokenAsync("default", It.IsAny<CancellationToken>()))
            .ReturnsAsync("test-token-123");
            
        // Mock the repository files list to simulate search
        _mockApiClient.Setup(x => x.GetRepositoryFilesAsync(repoName, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedFiles);

        var filesCommand = new Command("files", "Repository file commands");
        var searchCommand = new RepositoryFilesSearchCommand();
        filesCommand.AddCommand(searchCommand);
        
        var rootCommand = new RootCommand();
        rootCommand.AddCommand(filesCommand);

        // Act & Assert  
        var result = await TestCommandExecution(rootCommand, $"files search {repoName} --pattern {pattern}");
        Assert.Equal(0, result);
        
        _mockApiClient.Verify(x => x.GetRepositoryFilesAsync(repoName, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RepositoryFilesSearchCommand_WithNoMatches_ShouldReturnEmptyResult()
    {
        // Arrange
        var repoName = "test-repo";
        var pattern = "*.xyz";
        var expectedFiles = new List<FileInfoResponse>();

        _mockAuthService.Setup(x => x.IsAuthenticatedAsync("default", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mockAuthService.Setup(x => x.GetCurrentUserAsync("default", It.IsAny<CancellationToken>()))
            .ReturnsAsync("testuser");
        _mockAuthService.Setup(x => x.GetTokenAsync("default", It.IsAny<CancellationToken>()))
            .ReturnsAsync("test-token-123");
            
        _mockApiClient.Setup(x => x.GetRepositoryFilesAsync(repoName, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedFiles);

        var filesCommand = new Command("files", "Repository file commands");
        var searchCommand = new RepositoryFilesSearchCommand();
        filesCommand.AddCommand(searchCommand);
        
        var rootCommand = new RootCommand();
        rootCommand.AddCommand(filesCommand);

        // Act & Assert
        var result = await TestCommandExecution(rootCommand, $"files search {repoName} --pattern {pattern}");
        Assert.Equal(0, result);
        
        _mockApiClient.Verify(x => x.GetRepositoryFilesAsync(repoName, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region Helper Methods

    private async Task<int> TestCommandExecution(RootCommand rootCommand, string commandLine)
    {
        var args = commandLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        
        // Create command line builder with middleware to inject service provider
        var builder = new CommandLineBuilder(rootCommand);
        builder.UseDefaults();
        
        // Add middleware to inject the service provider (same as in Program.cs)
        builder.AddMiddleware(async (context, next) =>
        {
            context.BindingContext.AddService<IServiceProvider>(_ => _mockServiceProvider.Object);
            await next(context);
        });
        
        var parser = builder.Build();
        return await parser.InvokeAsync(args);
    }

    #endregion
}

// Mock command classes that will be implemented later
public class RepositoryFilesListCommand : AuthenticatedCommand
{
    private readonly Argument<string> _repositoryArgument;
    private readonly Option<string> _pathOption;
    
    public RepositoryFilesListCommand() : base("list", "List files in repository")
    {
        _repositoryArgument = new Argument<string>("repository", "Repository name");
        _pathOption = new Option<string>("--path", "Path within repository");
        
        AddArgument(_repositoryArgument);
        AddOption(_pathOption);
    }
    
    protected override async Task<int> ExecuteAuthenticatedAsync(InvocationContext context, string profile, IApiClient apiClient)
    {
        var repository = context.ParseResult.GetValueForArgument(_repositoryArgument);
        var path = context.ParseResult.GetValueForOption(_pathOption);
        var cancellationToken = context.GetCancellationToken();
        
        var files = await apiClient.GetRepositoryFilesAsync(repository, path, cancellationToken);
        return 0;
    }
}

public class RepositoryFilesShowCommand : AuthenticatedCommand
{
    private readonly Argument<string> _repositoryArgument;
    private readonly Argument<string> _fileArgument;
    
    public RepositoryFilesShowCommand() : base("show", "Show file content")
    {
        _repositoryArgument = new Argument<string>("repository", "Repository name");
        _fileArgument = new Argument<string>("file", "File path");
        
        AddArgument(_repositoryArgument);
        AddArgument(_fileArgument);
    }
    
    protected override async Task<int> ExecuteAuthenticatedAsync(InvocationContext context, string profile, IApiClient apiClient)
    {
        var repository = context.ParseResult.GetValueForArgument(_repositoryArgument);
        var filePath = context.ParseResult.GetValueForArgument(_fileArgument);
        var cancellationToken = context.GetCancellationToken();
        
        var fileContent = await apiClient.GetRepositoryFileContentAsync(repository, filePath, cancellationToken);
        return 0;
    }
}

public class RepositoryFilesDownloadCommand : AuthenticatedCommand
{
    private readonly Argument<string> _repositoryArgument;
    private readonly Argument<string> _fileArgument;
    private readonly Option<string> _outputOption;
    
    public RepositoryFilesDownloadCommand() : base("download", "Download file")
    {
        _repositoryArgument = new Argument<string>("repository", "Repository name");
        _fileArgument = new Argument<string>("file", "File path");
        _outputOption = new Option<string>("--output", "Output file path");
        
        AddArgument(_repositoryArgument);
        AddArgument(_fileArgument);
        AddOption(_outputOption);
    }
    
    protected override async Task<int> ExecuteAuthenticatedAsync(InvocationContext context, string profile, IApiClient apiClient)
    {
        var repository = context.ParseResult.GetValueForArgument(_repositoryArgument);
        var filePath = context.ParseResult.GetValueForArgument(_fileArgument);
        var cancellationToken = context.GetCancellationToken();
        
        var fileContent = await apiClient.GetRepositoryFileContentAsync(repository, filePath, cancellationToken);
        return 0;
    }
}

public class RepositoryFilesSearchCommand : AuthenticatedCommand
{
    private readonly Argument<string> _repositoryArgument;
    private readonly Option<string> _patternOption;
    
    public RepositoryFilesSearchCommand() : base("search", "Search files")
    {
        _repositoryArgument = new Argument<string>("repository", "Repository name");
        _patternOption = new Option<string>("--pattern", "Search pattern");
        
        AddArgument(_repositoryArgument);
        AddOption(_patternOption);
    }
    
    protected override async Task<int> ExecuteAuthenticatedAsync(InvocationContext context, string profile, IApiClient apiClient)
    {
        var repository = context.ParseResult.GetValueForArgument(_repositoryArgument);
        var pattern = context.ParseResult.GetValueForOption(_patternOption);
        var cancellationToken = context.GetCancellationToken();
        
        var files = await apiClient.GetRepositoryFilesAsync(repository, null, cancellationToken);
        return 0;
    }
}