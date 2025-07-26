using System.CommandLine;
using System.CommandLine.Invocation;
using Spectre.Console;
using ClaudeServerCLI.Services;

namespace ClaudeServerCLI.Commands;

public class AuthCommand : Command
{
    public AuthCommand() : base("auth", """
        Authentication and profile management commands
        
        Manage user authentication, login sessions, and profile configurations.
        Profiles store server URLs and authentication tokens for different environments.
        
        EXAMPLES:
          # Login with username and password
          claude-server auth login -u admin -p mypassword
          
          # Login to a specific profile
          claude-server auth login -u admin -p mypassword --profile production
          
          # Check current authentication status
          claude-server auth whoami
          
          # Logout from current profile
          claude-server auth logout
          
          # Logout from specific profile
          claude-server auth logout --profile production
        """)
    {
        AddCommand(new LoginCommand());
        AddCommand(new LogoutCommand());
        AddCommand(new WhoAmICommand());
    }
}

public class LoginCommand : BaseCommand
{
    private readonly Option<string> _usernameOption;
    private readonly Option<string> _passwordOption;
    private readonly Option<string> _profileOption;
    private readonly Option<bool> _hashedPasswordOption;

    public LoginCommand() : base("login", """
        Login to the Claude Batch Server with username and password
        
        Authenticates with the Claude Batch Server and stores the session token
        in the specified profile for subsequent commands.
        
        EXAMPLES:
          # Basic login
          claude-server auth login -u admin -p mypassword
          
          # Login to specific profile/environment
          claude-server auth login -u admin -p mypassword --profile staging
          
          # Login with pre-hashed password
          claude-server auth login -u admin -p $2b$12$hashedpassword --hashed
        """)
    {
        _usernameOption = new Option<string>(
            aliases: ["--username", "--usr", "-u"],
            description: "Username for authentication. Must match a user in the server's user database."
        ) { IsRequired = true };

        _passwordOption = new Option<string>(
            aliases: ["--password", "--pwd", "-p"],
            description: "Password for authentication. Use --hashed if providing a pre-hashed password."
        ) { IsRequired = true };

        _profileOption = new Option<string>(
            aliases: ["--profile", "-prof"],
            description: "Profile name to store credentials under. Allows multiple server configurations.",
            getDefaultValue: () => "default"
        );

        _hashedPasswordOption = new Option<bool>(
            aliases: ["--hashed", "-h"],
            description: "Indicates the password is already bcrypt hashed (for scripting/automation).",
            getDefaultValue: () => false
        );

        AddOption(_usernameOption);
        AddOption(_passwordOption);
        AddOption(_profileOption);
        AddOption(_hashedPasswordOption);
    }

    protected override async Task<int> ExecuteInternalAsync(InvocationContext context)
    {
        var username = context.ParseResult.GetValueForOption(_usernameOption)!;
        var password = context.ParseResult.GetValueForOption(_passwordOption)!;
        var profile = context.ParseResult.GetValueForOption(_profileOption) ?? "default";
        var isHashedPassword = context.ParseResult.GetValueForOption(_hashedPasswordOption);

        var authService = GetRequiredService<IAuthService>(context);

        // Check server health first
        if (!await CheckServerHealthAsync(context))
        {
            return 1;
        }

        WriteInfo($"Logging in as '{username}' using profile '{profile}'...");

        var progressTask = AnsiConsole.Progress()
            .AutoClear(false)
            .HideCompleted(false)
            .Start(ctx =>
            {
                var task = ctx.AddTask("[green]Authenticating...[/]", autoStart: true);
                
                var loginTask = authService.LoginAsync(username, password, isHashedPassword, profile, context.GetCancellationToken());
                loginTask.Wait();
                
                task.StopTask();
                return loginTask.Result;
            });

        if (progressTask)
        {
            WriteSuccess($"Successfully logged in as '{username}' using profile '{profile}'");
            
            // Show current user info
            var currentUser = await authService.GetCurrentUserAsync(profile, context.GetCancellationToken());
            if (!string.IsNullOrEmpty(currentUser))
            {
                WriteInfo($"Authenticated as: {currentUser}");
            }
            
            return 0;
        }
        else
        {
            WriteError("Login failed. Please check your credentials and try again.");
            return 1;
        }
    }
}

public class LogoutCommand : BaseCommand
{
    private readonly Option<string> _profileOption;

    public LogoutCommand() : base("logout", "Logout from the Claude Batch Server")
    {
        _profileOption = new Option<string>(
            aliases: ["--profile", "-p"],
            description: "Profile to logout from",
            getDefaultValue: () => "default"
        );

        AddOption(_profileOption);
    }

    protected override async Task<int> ExecuteInternalAsync(InvocationContext context)
    {
        var profile = context.ParseResult.GetValueForOption(_profileOption) ?? "default";
        var authService = GetRequiredService<IAuthService>(context);

        // Check if already logged out
        if (!await authService.IsAuthenticatedAsync(profile, context.GetCancellationToken()))
        {
            WriteWarning($"Not currently authenticated for profile '{profile}'");
            return 0;
        }

        WriteInfo($"Logging out from profile '{profile}'...");

        var success = await authService.LogoutAsync(profile, context.GetCancellationToken());

        if (success)
        {
            WriteSuccess($"Successfully logged out from profile '{profile}'");
            return 0;
        }
        else
        {
            WriteError("Logout failed");
            return 1;
        }
    }
}

public class WhoAmICommand : BaseCommand
{
    private readonly Option<string> _profileOption;

    public WhoAmICommand() : base("whoami", "Show current authentication status")
    {
        _profileOption = new Option<string>(
            aliases: ["--profile", "-p"],
            description: "Profile to check authentication status for",
            getDefaultValue: () => "default"
        );

        AddOption(_profileOption);
    }

    protected override async Task<int> ExecuteInternalAsync(InvocationContext context)
    {
        var profile = context.ParseResult.GetValueForOption(_profileOption) ?? "default";
        var authService = GetRequiredService<IAuthService>(context);
        var configService = GetRequiredService<IConfigService>(context);

        // Create a table to show authentication status
        var table = new Table();
        table.AddColumn("Property");
        table.AddColumn("Value");

        // Profile information
        table.AddRow("Profile", profile);

        // Authentication status
        var isAuthenticated = await authService.IsAuthenticatedAsync(profile, context.GetCancellationToken());
        table.AddRow(
            "Authenticated", 
            isAuthenticated 
                ? "[green]✓ Yes[/]" 
                : "[red]✗ No[/]"
        );

        if (isAuthenticated)
        {
            // Current user
            var currentUser = await authService.GetCurrentUserAsync(profile, context.GetCancellationToken());
            if (!string.IsNullOrEmpty(currentUser))
            {
                table.AddRow("Username", currentUser);
            }

            // Server URL
            try
            {
                var profileConfig = await configService.GetProfileAsync(profile, context.GetCancellationToken());
                table.AddRow("Server URL", profileConfig.ServerUrl);
            }
            catch (Exception ex)
            {
                table.AddRow("Server URL", $"[red]Error: {ex.Message}[/]");
            }
        }
        else
        {
            table.AddRow("Note", "[yellow]Run 'claude-server login' to authenticate[/]");
        }

        // Show available profiles
        try
        {
            var profiles = await configService.GetProfileNamesAsync(context.GetCancellationToken());
            table.AddRow("Available Profiles", string.Join(", ", profiles));
        }
        catch (Exception ex)
        {
            table.AddRow("Available Profiles", $"[red]Error: {ex.Message}[/]");
        }

        AnsiConsole.Write(table);
        return 0;
    }
}