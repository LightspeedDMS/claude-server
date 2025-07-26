using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Invocation;
using System.CommandLine.IO;
using ClaudeServerCLI.Commands;
using ClaudeServerCLI.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace ClaudeServerCLI.IntegrationTests.Commands;

public class UserManagementIntegrationTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly IServiceProvider _serviceProvider;

    public UserManagementIntegrationTests()
    {
        // Create a temporary test directory
        _testDirectory = Path.Combine(Path.GetTempPath(), $"claude-user-integration-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDirectory);
        
        // Set working directory to test directory
        Environment.CurrentDirectory = _testDirectory;
        
        // Set up services
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        services.AddScoped<IUserManagementService, UserManagementService>();
        _serviceProvider = services.BuildServiceProvider();
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, true);
        }
    }

    [Fact]
    public async Task AddUserCommand_EndToEnd_CreatesUserFiles()
    {
        // Arrange
        var console = new TestConsole();
        var rootCommand = CreateRootCommand();

        // Act
        var exitCode = await rootCommand.InvokeAsync("user add testuser password123", console);

        // Assert
        Assert.Equal(0, exitCode);
        
        // Verify files were created
        var passwdFile = Path.Combine(_testDirectory, "claude-server-passwd");
        var shadowFile = Path.Combine(_testDirectory, "claude-server-shadow");
        
        Assert.True(File.Exists(passwdFile));
        Assert.True(File.Exists(shadowFile));
        
        // Verify content
        var passwdContent = await File.ReadAllTextAsync(passwdFile);
        var shadowContent = await File.ReadAllTextAsync(shadowFile);
        
        Assert.Contains("testuser:x:1000:1000:", passwdContent);
        Assert.Contains("testuser:", shadowContent);
        Assert.Contains("$6$", shadowContent); // SHA-512 hash
        
        // Verify console output
        var output = console.OutText;
        Assert.Contains("Adding user to Claude Server authentication", output);
        Assert.Contains("successfully added", output);
    }

    [Fact]
    public async Task AddUserCommand_WithInvalidUsername_ShowsError()
    {
        // Arrange
        var console = new TestConsole();
        var rootCommand = CreateRootCommand();

        // Act
        var exitCode = await rootCommand.InvokeAsync("user add 123invalid password123", console);

        // Assert
        Assert.Equal(1, exitCode);
        
        var output = console.OutText;
        Assert.Contains("Invalid username format", output);
    }

    [Fact]
    public async Task ListUsersCommand_WithExistingUsers_ShowsUserList()
    {
        // Arrange - First create some users
        var console1 = new TestConsole();
        var rootCommand = CreateRootCommand();
        
        // Add first user
        await rootCommand.InvokeAsync("user add alice password1", console1);
        
        // Add second user  
        var console2 = new TestConsole();
        await rootCommand.InvokeAsync("user add bob password2 --uid 1002", console2);

        // Act - List users
        var listConsole = new TestConsole();
        var exitCode = await rootCommand.InvokeAsync("user list", listConsole);

        // Assert
        Assert.Equal(0, exitCode);
        
        var output = listConsole.OutText;
        Assert.Contains("Total Users: 2", output);
        Assert.Contains("alice", output);
        Assert.Contains("bob", output);
        Assert.Contains("Active", output);
    }

    [Fact]
    public async Task ListUsersCommand_WithDetailedFlag_ShowsDetailedInformation()
    {
        // Arrange - Create a user first
        var console1 = new TestConsole();
        var rootCommand = CreateRootCommand();
        await rootCommand.InvokeAsync("user add detailed-user password123 --home /custom/home", console1);

        // Act - List users with detailed flag
        var listConsole = new TestConsole();
        var exitCode = await rootCommand.InvokeAsync("user list --detailed", listConsole);

        // Assert
        Assert.Equal(0, exitCode);
        
        var output = listConsole.OutText;
        Assert.Contains("detailed-user", output);
        Assert.Contains("/custom/home", output);
        Assert.Contains("bash", output);
        Assert.Contains("Home", output); // Should show detailed headers
        Assert.Contains("Shell", output);
    }

    [Fact]
    public async Task RemoveUserCommand_WithExistingUser_RemovesUser()
    {
        // Arrange - First create a user
        var console1 = new TestConsole();
        var rootCommand = CreateRootCommand();
        await rootCommand.InvokeAsync("user add removeme password123", console1);
        
        // Verify user was created
        var passwdFile = Path.Combine(_testDirectory, "claude-server-passwd");
        var passwdContent = await File.ReadAllTextAsync(passwdFile);
        Assert.Contains("removeme:", passwdContent);

        // Act - Remove the user
        var removeConsole = new TestConsole();
        var exitCode = await rootCommand.InvokeAsync("user remove removeme --force", removeConsole);

        // Assert
        Assert.Equal(0, exitCode);
        
        // Verify user was removed
        var updatedPasswdContent = await File.ReadAllTextAsync(passwdFile);
        Assert.DoesNotContain("removeme:", updatedPasswdContent);
        
        var output = removeConsole.OutText;
        Assert.Contains("successfully removed", output);
        Assert.Contains("Backups created", output);
    }

    [Fact]
    public async Task UpdateUserCommand_WithExistingUser_UpdatesPassword()
    {
        // Arrange - First create a user
        var console1 = new TestConsole();
        var rootCommand = CreateRootCommand();
        await rootCommand.InvokeAsync("user add updateme oldpassword", console1);
        
        // Get original shadow content
        var shadowFile = Path.Combine(_testDirectory, "claude-server-shadow");
        var originalShadowContent = await File.ReadAllTextAsync(shadowFile);

        // Act - Update the password
        var updateConsole = new TestConsole();
        var exitCode = await rootCommand.InvokeAsync("user update updateme newpassword", updateConsole);

        // Assert
        Assert.Equal(0, exitCode);
        
        // Verify password was updated (shadow file should be different)
        var updatedShadowContent = await File.ReadAllTextAsync(shadowFile);
        Assert.NotEqual(originalShadowContent, updatedShadowContent);
        Assert.Contains("updateme:", updatedShadowContent);
        
        var output = updateConsole.OutText;
        Assert.Contains("successfully updated", output);
        Assert.Contains("Backup created", output);
    }

    [Fact]
    public async Task UpdateUserCommand_WithNonExistingUser_ShowsError()
    {
        // Act
        var console = new TestConsole();
        var rootCommand = CreateRootCommand();
        var exitCode = await rootCommand.InvokeAsync("user update nonexistent newpassword", console);

        // Assert
        Assert.Equal(1, exitCode);
        
        var output = console.OutText;
        Assert.Contains("does not exist", output);
    }

    [Fact]
    public async Task RemoveUserCommand_WithNonExistingUser_ShowsError()
    {
        // Act
        var console = new TestConsole();
        var rootCommand = CreateRootCommand();
        var exitCode = await rootCommand.InvokeAsync("user remove nonexistent", console);

        // Assert
        Assert.Equal(1, exitCode);
        
        var output = console.OutText;
        Assert.Contains("does not exist", output);
    }

    [Fact]
    public async Task AddUserCommand_WithDuplicateUser_ShowsError()
    {
        // Arrange - First create a user
        var console1 = new TestConsole();
        var rootCommand = CreateRootCommand();
        await rootCommand.InvokeAsync("user add duplicate password1", console1);

        // Act - Try to add same user again
        var console2 = new TestConsole();
        var exitCode = await rootCommand.InvokeAsync("user add duplicate password2", console2);

        // Assert
        Assert.Equal(1, exitCode);
        
        var output = console2.OutText;
        Assert.Contains("already exists", output);
    }

    [Fact]
    public async Task CompleteWorkflow_AddListUpdateRemove_WorksCorrectly()
    {
        var rootCommand = CreateRootCommand();

        // 1. Add user
        var addConsole = new TestConsole();
        var addResult = await rootCommand.InvokeAsync("user add workflow-user password123 --uid 1005", addConsole);
        Assert.Equal(0, addResult);

        // 2. List users to verify creation
        var listConsole = new TestConsole();
        var listResult = await rootCommand.InvokeAsync("user list", listConsole);
        Assert.Equal(0, listResult);
        Assert.Contains("workflow-user", listConsole.OutText);
        Assert.Contains("Total Users: 1", listConsole.OutText);

        // 3. Update password
        var updateConsole = new TestConsole();
        var updateResult = await rootCommand.InvokeAsync("user update workflow-user newpassword456", updateConsole);
        Assert.Equal(0, updateResult);

        // 4. Remove user
        var removeConsole = new TestConsole();
        var removeResult = await rootCommand.InvokeAsync("user remove workflow-user --force", removeConsole);
        Assert.Equal(0, removeResult);

        // 5. List users to verify removal
        var finalListConsole = new TestConsole();
        var finalListResult = await rootCommand.InvokeAsync("user list", finalListConsole);
        Assert.Equal(0, finalListResult);
        Assert.Contains("No users found", finalListConsole.OutText);
    }

    [Fact]
    public async Task AddUserCommand_CreatesBackupFiles_WhenFilesAlreadyExist()
    {
        // Arrange - Create first user
        var console1 = new TestConsole();
        var rootCommand = CreateRootCommand();
        await rootCommand.InvokeAsync("user add user1 password1", console1);

        // Act - Add second user (should create backups)
        var console2 = new TestConsole();
        var exitCode = await rootCommand.InvokeAsync("user add user2 password2", console2);

        // Assert
        Assert.Equal(0, exitCode);
        
        // Verify backup files were created
        var backupFiles = Directory.GetFiles(_testDirectory, "*.backup.*");
        Assert.True(backupFiles.Length >= 2); // At least passwd and shadow backups
        
        var output = console2.OutText;
        Assert.Contains("Backups created", output);
    }

    private RootCommand CreateRootCommand()
    {
        var rootCommand = new RootCommand("Test CLI");
        var userCommand = new UserCommand();
        
        // Configure service provider for all subcommands
        ConfigureServiceProvider(userCommand, _serviceProvider);
        rootCommand.AddCommand(userCommand);
        
        return rootCommand;
    }

    private void ConfigureServiceProvider(Command command, IServiceProvider serviceProvider)
    {
        // Configure the service provider for all command handlers
        foreach (var subCommand in command.Subcommands)
        {
            ConfigureServiceProvider(subCommand, serviceProvider);
        }

        // Set the service provider in the command's handler but don't replace handlers that are already set
        if (command.Handler == null)
        {
            command.SetHandler((context) =>
            {
                context.BindingContext.AddService<IServiceProvider>(_ => serviceProvider);
            });
        }
        else
        {
            // For commands that already have handlers (like our AuthenticatedCommand),
            // just inject the service provider into the binding context
            var originalHandler = command.Handler;
            command.SetHandler(async (context) =>
            {
                context.BindingContext.AddService<IServiceProvider>(_ => serviceProvider);
                
                if (originalHandler != null)
                {
                    await originalHandler.InvokeAsync(context);
                }
            });
        }
    }
}

/// <summary>
/// Test console implementation for integration tests
/// </summary>
public class TestConsole : IConsole
{
    private readonly StringWriter _out = new();
    private readonly StringWriter _error = new();

    public IStandardStreamWriter Out => StandardStreamWriter.Create(_out);
    public bool IsInputRedirected => false;
    public bool IsOutputRedirected => false;
    public bool IsErrorRedirected => false;
    public IStandardStreamWriter Error => StandardStreamWriter.Create(_error);
    
    public string OutText => _out.ToString();
    public string ErrorText => _error.ToString();
}