using System.CommandLine;
using System.CommandLine.Invocation;
using ClaudeServerCLI.Models;
using ClaudeServerCLI.Services;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;

namespace ClaudeServerCLI.Commands;

/// <summary>
/// Main command group for user management operations
/// </summary>
public class UserCommand : Command
{
    public UserCommand() : base("user", "Manage Claude Server authentication users")
    {
        AddCommand(new AddUserCommand());
        AddCommand(new RemoveUserCommand());
        AddCommand(new ListUsersCommand());
        AddCommand(new UpdateUserCommand());
    }
}

/// <summary>
/// Command to add a new user to the authentication system
/// </summary>
public class AddUserCommand : BaseCommand
{
    private readonly Argument<string> _usernameArgument;
    private readonly Argument<string> _passwordArgument;
    private readonly Option<int> _uidOption;
    private readonly Option<int> _gidOption;
    private readonly Option<string> _homeDirOption;
    private readonly Option<string> _shellOption;

    public AddUserCommand() : base("add", "Add a new user to Claude Server authentication")
    {
        _usernameArgument = new Argument<string>("username", "Username for the new user");
        _passwordArgument = new Argument<string>("password", "Password for the new user");
        
        _uidOption = new Option<int>(
            aliases: ["--uid", "-u"],
            description: "User ID",
            getDefaultValue: () => 1000
        );
        
        _gidOption = new Option<int>(
            aliases: ["--gid", "-g"],
            description: "Group ID",
            getDefaultValue: () => 1000
        );
        
        _homeDirOption = new Option<string>(
            aliases: ["--home", "-h"],
            description: "Home directory (defaults to /home/{username})"
        );
        
        _shellOption = new Option<string>(
            aliases: ["--shell", "-s"],
            description: "Shell",
            getDefaultValue: () => "/bin/bash"
        );

        AddArgument(_usernameArgument);
        AddArgument(_passwordArgument);
        AddOption(_uidOption);
        AddOption(_gidOption);
        AddOption(_homeDirOption);
        AddOption(_shellOption);
    }

    protected override async Task<int> ExecuteInternalAsync(InvocationContext context)
    {
        var userService = GetRequiredService<IUserManagementService>(context);
        
        var username = context.ParseResult.GetValueForArgument(_usernameArgument);
        var password = context.ParseResult.GetValueForArgument(_passwordArgument);
        var uid = context.ParseResult.GetValueForOption(_uidOption);
        var gid = context.ParseResult.GetValueForOption(_gidOption);
        var homeDir = context.ParseResult.GetValueForOption(_homeDirOption);
        var shell = context.ParseResult.GetValueForOption(_shellOption);

        // Display user details being created
        AnsiConsole.MarkupLine("[cyan]👤 Adding user to Claude Server authentication[/]");
        
        var table = new Table()
            .RoundedBorder()
            .AddColumn("[blue]Property[/]")
            .AddColumn("[yellow]Value[/]");
        
        table.AddRow("Username", username);
        table.AddRow("UID", uid.ToString());
        table.AddRow("GID", gid.ToString());
        table.AddRow("Home", homeDir ?? $"/home/{username}");
        table.AddRow("Shell", shell ?? "/bin/bash");
        
        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();

        // Generate password hash with progress
        UserOperationResult result = null!;
        await ShowProgressBarAsync("🔐 Generating password hash and creating user...", async task =>
        {
            task.MaxValue = 100;
            task.Value = 20;
            
            result = await userService.AddUserAsync(username, password, uid, gid, homeDir, shell);
            
            task.Value = 100;
            task.StopTask();
        });

        if (result.Success)
        {
            WriteSuccess(result.Message!);
            
            if (!string.IsNullOrEmpty(result.BackupFile))
            {
                WriteInfo($"📋 {result.BackupFile}");
            }
            
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[cyan]📋 Next Steps:[/]");
            AnsiConsole.MarkupLine("   1. Restart Claude Server API: [purple]claude-server restart[/] (if available)");
            AnsiConsole.MarkupLine($"   2. Test login: [purple]claude-server auth login --username {username}[/]");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[blue]🔧 Management Commands:[/]");
            AnsiConsole.MarkupLine("   List users: [purple]claude-server user list[/]");
            AnsiConsole.MarkupLine($"   Remove user: [purple]claude-server user remove {username}[/]");
            AnsiConsole.MarkupLine($"   Update password: [purple]claude-server user update {username} <new_password>[/]");
            
            return 0;
        }
        else
        {
            WriteError(result.Message!);
            if (!string.IsNullOrEmpty(result.ErrorDetails))
            {
                AnsiConsole.MarkupLine("[dim]Details: {0}[/]", result.ErrorDetails);
            }
            return 1;
        }
    }
}

/// <summary>
/// Command to remove a user from the authentication system
/// </summary>
public class RemoveUserCommand : BaseCommand
{
    private readonly Argument<string> _usernameArgument;
    private readonly Option<bool> _forceOption;

    public RemoveUserCommand() : base("remove", "Remove a user from Claude Server authentication")
    {
        _usernameArgument = new Argument<string>("username", "Username to remove");
        _forceOption = new Option<bool>(
            aliases: ["--force", "-f"],
            description: "Remove user without confirmation prompt"
        );

        AddArgument(_usernameArgument);
        AddOption(_forceOption);
    }

    protected override async Task<int> ExecuteInternalAsync(InvocationContext context)
    {
        var userService = GetRequiredService<IUserManagementService>(context);
        var username = context.ParseResult.GetValueForArgument(_usernameArgument);
        var force = context.ParseResult.GetValueForOption(_forceOption);

        // Check if user exists first
        if (!await userService.UserExistsAsync(username))
        {
            WriteWarning($"User '{username}' does not exist in Claude Server authentication");
            return 1;
        }

        AnsiConsole.MarkupLine("[cyan]🗑️ Removing user from Claude Server authentication[/]");
        AnsiConsole.MarkupLine("[blue]📊 User: [yellow]{0}[/][/]", username);
        AnsiConsole.WriteLine();

        // Confirmation prompt unless --force is used
        if (!force)
        {
            var confirm = AnsiConsole.Confirm($"Are you sure you want to remove user '[yellow]{username}[/]'?");
            if (!confirm)
            {
                WriteInfo("Operation cancelled");
                return 0;
            }
        }

        // Remove user with progress
        UserOperationResult result = null!;
        await ShowProgressBarAsync("🗑️ Removing user and creating backups...", async task =>
        {
            task.MaxValue = 100;
            task.Value = 20;
            
            result = await userService.RemoveUserAsync(username);
            
            task.Value = 100;
            task.StopTask();
        });

        if (result.Success)
        {
            WriteSuccess(result.Message!);
            
            if (!string.IsNullOrEmpty(result.BackupFile))
            {
                WriteInfo($"📋 {result.BackupFile}");
            }
            
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[cyan]📋 Next Steps:[/]");
            AnsiConsole.MarkupLine("   1. Restart Claude Server API: [purple]claude-server restart[/] (if available)");
            AnsiConsole.MarkupLine("   2. Verify removal: [purple]claude-server user list[/]");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[blue]🔧 Management Commands:[/]");
            AnsiConsole.MarkupLine("   List users: [purple]claude-server user list[/]");
            AnsiConsole.MarkupLine("   Add user: [purple]claude-server user add <username> <password>[/]");
            
            return 0;
        }
        else
        {
            WriteError(result.Message!);
            if (!string.IsNullOrEmpty(result.ErrorDetails))
            {
                AnsiConsole.MarkupLine("[dim]Details: {0}[/]", result.ErrorDetails);
            }
            return 1;
        }
    }
}

/// <summary>
/// Command to list all users in the authentication system
/// </summary>
public class ListUsersCommand : BaseCommand
{
    private readonly Option<bool> _detailedOption;

    public ListUsersCommand() : base("list", "List all users in Claude Server authentication")
    {
        _detailedOption = new Option<bool>(
            aliases: ["--detailed", "-d"],
            description: "Show detailed user information"
        );

        AddOption(_detailedOption);
    }

    protected override async Task<int> ExecuteInternalAsync(InvocationContext context)
    {
        var userService = GetRequiredService<IUserManagementService>(context);
        var detailed = context.ParseResult.GetValueForOption(_detailedOption);

        AnsiConsole.MarkupLine("[cyan]👥 Claude Server Authentication Users[/]");
        AnsiConsole.WriteLine();

        // Get users with progress
        IEnumerable<UserInfo> users = null!;
        await ShowProgressBarAsync("📊 Loading user information...", async task =>
        {
            task.MaxValue = 100;
            task.Value = 20;
            
            users = await userService.ListUsersAsync();
            
            task.Value = 100;
            task.StopTask();
        });

        var userList = users.ToList();
        
        if (!userList.Any())
        {
            WriteWarning("No users found in Claude Server authentication");
            AnsiConsole.MarkupLine("[blue]Add a user: [purple]claude-server user add <username> <password>[/][/]");
            return 0;
        }

        AnsiConsole.MarkupLine("[blue]📊 Total Users: [yellow]{0}[/][/]", userList.Count);
        AnsiConsole.WriteLine();

        if (detailed)
        {
            // Detailed view with full information
            var table = new Table()
                .RoundedBorder()
                .AddColumn("[blue]Username[/]")
                .AddColumn("[blue]UID[/]")
                .AddColumn("[blue]GID[/]")
                .AddColumn("[blue]Home[/]")
                .AddColumn("[blue]Shell[/]")
                .AddColumn("[blue]Last Change[/]")
                .AddColumn("[blue]Status[/]");

            foreach (var user in userList)
            {
                var statusColor = user.Status switch
                {
                    UserStatus.Active => "green",
                    UserStatus.NoPassword => "yellow",
                    UserStatus.NoShadowEntry => "red",
                    UserStatus.Locked => "red",
                    _ => "white"
                };

                var statusText = user.Status switch
                {
                    UserStatus.Active => "✅ Active",
                    UserStatus.NoPassword => "🔒 No Password",
                    UserStatus.NoShadowEntry => "❌ No Shadow",
                    UserStatus.Locked => "🚫 Locked",
                    _ => "❓ Unknown"
                };
                
                var homeDir = user.HomeDirectory.Length > 20 ? 
                    "..." + user.HomeDirectory[^17..] : user.HomeDirectory;
                var shell = Path.GetFileName(user.Shell);
                var lastChange = user.LastPasswordChange?.ToString("yyyy-MM-dd") ?? "Never";

                table.AddRow(
                    user.Username,
                    user.Uid.ToString(),
                    user.Gid.ToString(),
                    homeDir,
                    shell,
                    lastChange,
                    $"[{statusColor}]{statusText}[/]"
                );
            }

            AnsiConsole.Write(table);
        }
        else
        {
            // Simple view with just essential information
            var table = new Table()
                .RoundedBorder()
                .AddColumn("[blue]Username[/]")
                .AddColumn("[blue]UID[/]")
                .AddColumn("[blue]Status[/]")
                .AddColumn("[blue]Last Change[/]");

            foreach (var user in userList)
            {
                var statusColor = user.Status switch
                {
                    UserStatus.Active => "green",
                    UserStatus.NoPassword => "yellow",
                    UserStatus.NoShadowEntry => "red",
                    UserStatus.Locked => "red",
                    _ => "white"
                };

                var statusText = user.Status switch
                {
                    UserStatus.Active => "✅ Active",
                    UserStatus.NoPassword => "🔒 No Password",
                    UserStatus.NoShadowEntry => "❌ No Shadow",
                    UserStatus.Locked => "🚫 Locked",
                    _ => "❓ Unknown"
                };
                
                var lastChange = user.LastPasswordChange?.ToString("yyyy-MM-dd") ?? "Never";

                table.AddRow(
                    user.Username,
                    user.Uid.ToString(),
                    $"[{statusColor}]{statusText}[/]",
                    lastChange
                );
            }

            AnsiConsole.Write(table);
        }

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[blue]🔧 Management Commands:[/]");
        AnsiConsole.MarkupLine("   Add user: [purple]claude-server user add <username> <password>[/]");
        AnsiConsole.MarkupLine("   Remove user: [purple]claude-server user remove <username>[/]");
        AnsiConsole.MarkupLine("   Update password: [purple]claude-server user update <username> <new_password>[/]");
        AnsiConsole.MarkupLine("   Detailed view: [purple]claude-server user list --detailed[/]");
        
        return 0;
    }
}

/// <summary>
/// Command to update a user's password
/// </summary>
public class UpdateUserCommand : BaseCommand
{
    private readonly Argument<string> _usernameArgument;
    private readonly Argument<string> _newPasswordArgument;

    public UpdateUserCommand() : base("update", "Update a user's password in Claude Server authentication")
    {
        _usernameArgument = new Argument<string>("username", "Username to update");
        _newPasswordArgument = new Argument<string>("new-password", "New password for the user");

        AddArgument(_usernameArgument);
        AddArgument(_newPasswordArgument);
    }

    protected override async Task<int> ExecuteInternalAsync(InvocationContext context)
    {
        var userService = GetRequiredService<IUserManagementService>(context);
        var username = context.ParseResult.GetValueForArgument(_usernameArgument);
        var newPassword = context.ParseResult.GetValueForArgument(_newPasswordArgument);

        AnsiConsole.MarkupLine("[cyan]🔐 Updating password for user '[yellow]{0}[/]'[/]", username);
        AnsiConsole.WriteLine();

        // Update password with progress
        UserOperationResult result = null!;
        await ShowProgressBarAsync("🔐 Generating new password hash and updating...", async task =>
        {
            task.MaxValue = 100;
            task.Value = 20;
            
            result = await userService.UpdateUserPasswordAsync(username, newPassword);
            
            task.Value = 100;
            task.StopTask();
        });

        if (result.Success)
        {
            WriteSuccess(result.Message!);
            
            if (!string.IsNullOrEmpty(result.BackupFile))
            {
                WriteInfo($"📋 {result.BackupFile}");
            }
            
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[cyan]📋 Next Steps:[/]");
            AnsiConsole.MarkupLine("   1. Restart Claude Server API: [purple]claude-server restart[/] (if available)");
            AnsiConsole.MarkupLine($"   2. Test new password: [purple]claude-server auth login --username {username}[/]");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[blue]🔧 Management Commands:[/]");
            AnsiConsole.MarkupLine("   List users: [purple]claude-server user list[/]");
            AnsiConsole.MarkupLine("   Add user: [purple]claude-server user add <username> <password>[/]");
            AnsiConsole.MarkupLine($"   Remove user: [purple]claude-server user remove {username}[/]");
            
            return 0;
        }
        else
        {
            WriteError(result.Message!);
            if (!string.IsNullOrEmpty(result.ErrorDetails))
            {
                AnsiConsole.MarkupLine("[dim]Details: {0}[/]", result.ErrorDetails);
            }
            return 1;
        }
    }
}