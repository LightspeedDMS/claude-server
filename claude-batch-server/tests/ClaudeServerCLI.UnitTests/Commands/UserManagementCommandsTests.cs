using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Invocation;
using System.CommandLine.IO;
using ClaudeServerCLI.Commands;
using ClaudeServerCLI.Models;
using ClaudeServerCLI.Services;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace ClaudeServerCLI.UnitTests.Commands;

public class UserManagementCommandsTests
{
    private readonly Mock<IUserManagementService> _mockUserService;
    private readonly IServiceProvider _serviceProvider;

    public UserManagementCommandsTests()
    {
        _mockUserService = new Mock<IUserManagementService>();
        
        var services = new ServiceCollection();
        services.AddSingleton(_mockUserService.Object);
        _serviceProvider = services.BuildServiceProvider();
    }

    [Fact]
    public async Task AddUserCommand_WithValidArguments_CallsAddUserAsync()
    {
        // Arrange
        var command = new AddUserCommand();
        var result = UserOperationResult.SuccessResult("User successfully added");
        
        _mockUserService
            .Setup(x => x.AddUserAsync("testuser", "password123", 1000, 1000, null, "/bin/bash"))
            .ReturnsAsync(result);

        var console = new TestConsole();
        var context = CreateInvocationContext(command, ["testuser", "password123"], console);

        // Act
        var exitCode = await command.Handler!.InvokeAsync(context);

        // Assert
        Assert.Equal(0, exitCode);
        _mockUserService.Verify(x => x.AddUserAsync("testuser", "password123", 1000, 1000, null, "/bin/bash"), Times.Once);
        
        var output = console.OutText;
        Assert.Contains("Adding user to Claude Server authentication", output);
        Assert.Contains("successfully added", output);
    }

    [Fact]
    public async Task AddUserCommand_WithCustomOptions_PassesCorrectParameters()
    {
        // Arrange
        var command = new AddUserCommand();
        var result = UserOperationResult.SuccessResult("User successfully added");
        
        _mockUserService
            .Setup(x => x.AddUserAsync("customuser", "password123", 1001, 1002, "/custom/home", "/bin/zsh"))
            .ReturnsAsync(result);

        var console = new TestConsole();
        var context = CreateInvocationContext(command, 
            ["customuser", "password123", "--uid", "1001", "--gid", "1002", "--home", "/custom/home", "--shell", "/bin/zsh"], 
            console);

        // Act
        var exitCode = await command.Handler!.InvokeAsync(context);

        // Assert
        Assert.Equal(0, exitCode);
        _mockUserService.Verify(x => x.AddUserAsync("customuser", "password123", 1001, 1002, "/custom/home", "/bin/zsh"), Times.Once);
    }

    [Fact]
    public async Task AddUserCommand_WithServiceFailure_ReturnsErrorCode()
    {
        // Arrange
        var command = new AddUserCommand();
        var result = UserOperationResult.ErrorResult("Username already exists");
        
        _mockUserService
            .Setup(x => x.AddUserAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(result);

        var console = new TestConsole();
        var context = CreateInvocationContext(command, ["testuser", "password123"], console);

        // Act
        var exitCode = await command.Handler!.InvokeAsync(context);

        // Assert
        Assert.Equal(1, exitCode);
        
        var output = console.OutText;
        Assert.Contains("Username already exists", output);
    }

    [Fact]
    public async Task RemoveUserCommand_WithExistingUser_CallsRemoveUserAsync()
    {
        // Arrange
        var command = new RemoveUserCommand();
        var result = UserOperationResult.SuccessResult("User successfully removed");
        
        _mockUserService
            .Setup(x => x.UserExistsAsync("testuser"))
            .ReturnsAsync(true);
        
        _mockUserService
            .Setup(x => x.RemoveUserAsync("testuser"))
            .ReturnsAsync(result);

        var console = new TestConsole();
        var context = CreateInvocationContext(command, ["testuser", "--force"], console);

        // Act
        var exitCode = await command.Handler!.InvokeAsync(context);

        // Assert
        Assert.Equal(0, exitCode);
        _mockUserService.Verify(x => x.UserExistsAsync("testuser"), Times.Once);
        _mockUserService.Verify(x => x.RemoveUserAsync("testuser"), Times.Once);
        
        var output = console.OutText;
        Assert.Contains("successfully removed", output);
    }

    [Fact]
    public async Task RemoveUserCommand_WithNonExistingUser_ReturnsErrorCode()
    {
        // Arrange
        var command = new RemoveUserCommand();
        
        _mockUserService
            .Setup(x => x.UserExistsAsync("nonexistent"))
            .ReturnsAsync(false);

        var console = new TestConsole();
        var context = CreateInvocationContext(command, ["nonexistent"], console);

        // Act
        var exitCode = await command.Handler!.InvokeAsync(context);

        // Assert
        Assert.Equal(1, exitCode);
        _mockUserService.Verify(x => x.UserExistsAsync("nonexistent"), Times.Once);
        _mockUserService.Verify(x => x.RemoveUserAsync(It.IsAny<string>()), Times.Never);
        
        var output = console.OutText;
        Assert.Contains("does not exist", output);
    }

    [Fact]
    public async Task ListUsersCommand_WithUsers_DisplaysUserList()
    {
        // Arrange
        var command = new ListUsersCommand();
        var users = new List<UserInfo>
        {
            new UserInfo
            {
                Username = "alice",
                Uid = 1001,
                Gid = 1001,
                HomeDirectory = "/home/alice",
                Shell = "/bin/bash",
                Status = UserStatus.Active,
                HasPassword = true,
                LastPasswordChange = DateTime.Now.AddDays(-5)
            },
            new UserInfo
            {
                Username = "bob",
                Uid = 1002,
                Gid = 1002,
                HomeDirectory = "/home/bob",
                Shell = "/bin/zsh",
                Status = UserStatus.NoPassword,
                HasPassword = false,
                LastPasswordChange = null
            }
        };
        
        _mockUserService
            .Setup(x => x.ListUsersAsync())
            .ReturnsAsync(users);

        var console = new TestConsole();
        var context = CreateInvocationContext(command, [], console);

        // Act
        var exitCode = await command.Handler!.InvokeAsync(context);

        // Assert
        Assert.Equal(0, exitCode);
        _mockUserService.Verify(x => x.ListUsersAsync(), Times.Once);
        
        var output = console.OutText;
        Assert.Contains("Claude Server Authentication Users", output);
        Assert.Contains("Total Users: 2", output);
        Assert.Contains("alice", output);
        Assert.Contains("bob", output);
    }

    [Fact]
    public async Task ListUsersCommand_WithDetailedOption_ShowsDetailedInformation()
    {
        // Arrange
        var command = new ListUsersCommand();
        var users = new List<UserInfo>
        {
            new UserInfo
            {
                Username = "testuser",
                Uid = 1000,
                Gid = 1000,
                HomeDirectory = "/home/testuser",
                Shell = "/bin/bash",
                Status = UserStatus.Active,
                HasPassword = true,
                LastPasswordChange = DateTime.Now.AddDays(-3)
            }
        };
        
        _mockUserService
            .Setup(x => x.ListUsersAsync())
            .ReturnsAsync(users);

        var console = new TestConsole();
        var context = CreateInvocationContext(command, ["--detailed"], console);

        // Act
        var exitCode = await command.Handler!.InvokeAsync(context);

        // Assert
        Assert.Equal(0, exitCode);
        
        var output = console.OutText;
        Assert.Contains("Home", output); // Should show detailed columns
        Assert.Contains("Shell", output);
        Assert.Contains("/home/testuser", output);
        Assert.Contains("/bin/bash", output);
    }

    [Fact]
    public async Task ListUsersCommand_WithNoUsers_ShowsEmptyMessage()
    {
        // Arrange
        var command = new ListUsersCommand();
        
        _mockUserService
            .Setup(x => x.ListUsersAsync())
            .ReturnsAsync(new List<UserInfo>());

        var console = new TestConsole();
        var context = CreateInvocationContext(command, [], console);

        // Act
        var exitCode = await command.Handler!.InvokeAsync(context);

        // Assert
        Assert.Equal(0, exitCode);
        
        var output = console.OutText;
        Assert.Contains("No users found", output);
        Assert.Contains("Add a user:", output);
    }

    [Fact]
    public async Task UpdateUserCommand_WithValidArguments_CallsUpdateUserPasswordAsync()
    {
        // Arrange
        var command = new UpdateUserCommand();
        var result = UserOperationResult.SuccessResult("Password successfully updated");
        
        _mockUserService
            .Setup(x => x.UpdateUserPasswordAsync("testuser", "newpassword"))
            .ReturnsAsync(result);

        var console = new TestConsole();
        var context = CreateInvocationContext(command, ["testuser", "newpassword"], console);

        // Act
        var exitCode = await command.Handler!.InvokeAsync(context);

        // Assert
        Assert.Equal(0, exitCode);
        _mockUserService.Verify(x => x.UpdateUserPasswordAsync("testuser", "newpassword"), Times.Once);
        
        var output = console.OutText;
        Assert.Contains("Updating password for user", output);
        Assert.Contains("successfully updated", output);
    }

    [Fact]
    public async Task UpdateUserCommand_WithServiceFailure_ReturnsErrorCode()
    {
        // Arrange
        var command = new UpdateUserCommand();
        var result = UserOperationResult.ErrorResult("User does not exist", "Detailed error info");
        
        _mockUserService
            .Setup(x => x.UpdateUserPasswordAsync("nonexistent", "password"))
            .ReturnsAsync(result);

        var console = new TestConsole();
        var context = CreateInvocationContext(command, ["nonexistent", "password"], console);

        // Act
        var exitCode = await command.Handler!.InvokeAsync(context);

        // Assert
        Assert.Equal(1, exitCode);
        
        var output = console.OutText;
        Assert.Contains("User does not exist", output);
        Assert.Contains("Detailed error info", output);
    }

    [Fact]
    public void UserCommand_HasCorrectSubcommands()
    {
        // Arrange
        var command = new UserCommand();

        // Act & Assert
        Assert.Equal("user", command.Name);
        Assert.Equal("Manage Claude Server authentication users", command.Description);
        
        var subcommands = command.Subcommands.Select(c => c.Name).ToList();
        Assert.Contains("add", subcommands);
        Assert.Contains("remove", subcommands);
        Assert.Contains("list", subcommands);
        Assert.Contains("update", subcommands);
        Assert.Equal(4, subcommands.Count);
    }

    [Fact]
    public void AddUserCommand_HasCorrectArgumentsAndOptions()
    {
        // Arrange
        var command = new AddUserCommand();

        // Act & Assert
        Assert.Equal("add", command.Name);
        Assert.Equal("Add a new user to Claude Server authentication", command.Description);
        
        var arguments = command.Arguments.Select(a => a.Name).ToList();
        Assert.Contains("username", arguments);
        Assert.Contains("password", arguments);
        Assert.Equal(2, arguments.Count);
        
        var options = command.Options.Select(o => o.Name).ToList();
        Assert.Contains("uid", options);
        Assert.Contains("gid", options);
        Assert.Contains("home", options);
        Assert.Contains("shell", options);
    }

    [Fact]
    public void RemoveUserCommand_HasCorrectArgumentsAndOptions()
    {
        // Arrange
        var command = new RemoveUserCommand();

        // Act & Assert
        Assert.Equal("remove", command.Name);
        Assert.Contains("Remove a user", command.Description);
        
        var arguments = command.Arguments.Select(a => a.Name).ToList();
        Assert.Contains("username", arguments);
        Assert.Single(arguments);
        
        var options = command.Options.Select(o => o.Name).ToList();
        Assert.Contains("force", options);
    }

    [Fact]
    public void ListUsersCommand_HasCorrectOptions()
    {
        // Arrange
        var command = new ListUsersCommand();

        // Act & Assert
        Assert.Equal("list", command.Name);
        Assert.Contains("List all users", command.Description);
        
        var options = command.Options.Select(o => o.Name).ToList();
        Assert.Contains("detailed", options);
    }

    [Fact]
    public void UpdateUserCommand_HasCorrectArguments()
    {
        // Arrange
        var command = new UpdateUserCommand();

        // Act & Assert
        Assert.Equal("update", command.Name);
        Assert.Contains("Update a user's password", command.Description);
        
        var arguments = command.Arguments.Select(a => a.Name).ToList();
        Assert.Contains("username", arguments);
        Assert.Contains("new-password", arguments);
        Assert.Equal(2, arguments.Count);
    }

    private InvocationContext CreateInvocationContext(Command command, string[] args, IConsole console)
    {
        var parser = new CommandLineBuilder(command)
            .UseDefaults()
            .Build();
        
        var parseResult = parser.Parse(args);
        var context = new InvocationContext(parseResult, console);
        
        // Add the service provider to the binding context
        context.BindingContext.AddService<IServiceProvider>(_ => _serviceProvider);
        
        return context;
    }
}

/// <summary>
/// Test console implementation for capturing output
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