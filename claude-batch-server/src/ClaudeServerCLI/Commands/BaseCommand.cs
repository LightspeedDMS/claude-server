using System.CommandLine;
using System.CommandLine.Invocation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using ClaudeServerCLI.Services;

namespace ClaudeServerCLI.Commands;

public abstract class BaseCommand : Command
{
    protected BaseCommand(string name, string description) : base(name, description)
    {
        this.SetHandler(ExecuteAsync);
    }

    protected abstract Task<int> ExecuteInternalAsync(InvocationContext context);

    private async Task<int> ExecuteAsync(InvocationContext context)
    {
        try
        {
            var result = await ExecuteInternalAsync(context);
            context.ExitCode = result;
            return result;
        }
        catch (UnauthorizedAccessException ex)
        {
            AnsiConsole.MarkupLine("[red]Authentication Error:[/] {0}", ex.Message);
            AnsiConsole.MarkupLine("[yellow]Try running 'claude-server login' to authenticate.[/]");
            context.ExitCode = 1;
            return 1;
        }
        catch (ArgumentException ex)
        {
            AnsiConsole.MarkupLine("[red]Invalid Arguments:[/] {0}", ex.Message);
            context.ExitCode = 1;
            return 1;
        }
        catch (InvalidOperationException ex)
        {
            AnsiConsole.MarkupLine("[red]Operation Error:[/] {0}", ex.Message);
            context.ExitCode = 1;
            return 1;
        }
        catch (HttpRequestException ex)
        {
            AnsiConsole.MarkupLine("[red]Network Error:[/] {0}", ex.Message);
            AnsiConsole.MarkupLine("[yellow]Check that the server is running and accessible.[/]");
            context.ExitCode = 1;
            return 1;
        }
        catch (TaskCanceledException)
        {
            AnsiConsole.MarkupLine("[red]Operation was cancelled or timed out.[/]");
            context.ExitCode = 1;
            return 1;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine("[red]Unexpected Error:[/] {0}", ex.Message);
            
            // Log full exception details for debugging
            var logger = context.GetService<ILogger<BaseCommand>>();
            logger?.LogError(ex, "Unexpected error in command execution");
            
            context.ExitCode = 1;
            return 1;
        }
    }

    protected static T GetRequiredService<T>(InvocationContext context) where T : notnull
    {
        var serviceProvider = context.BindingContext.GetService<IServiceProvider>()
            ?? throw new InvalidOperationException("Service provider not available");
        
        return serviceProvider.GetRequiredService<T>();
    }

    protected static void WriteSuccess(string message)
    {
        AnsiConsole.MarkupLine("[green]✓[/] {0}", message);
    }

    protected static void WriteError(string message)
    {
        AnsiConsole.MarkupLine("[red]✗[/] {0}", message);
    }

    protected static void WriteWarning(string message)
    {
        AnsiConsole.MarkupLine("[yellow]⚠[/] {0}", message);
    }

    protected static void WriteInfo(string message)
    {
        AnsiConsole.MarkupLine("[blue]ℹ[/] {0}", message);
    }

    protected static async Task<bool> EnsureAuthenticatedAsync(InvocationContext context, string profile = "default")
    {
        var authService = GetRequiredService<IAuthService>(context);
        
        if (await authService.IsAuthenticatedAsync(profile))
        {
            return true;
        }

        WriteError($"Not authenticated for profile '{profile}'");
        AnsiConsole.MarkupLine("[yellow]Run 'claude-server login' to authenticate.[/]");
        return false;
    }

    protected static async Task<bool> CheckServerHealthAsync(InvocationContext context)
    {
        var apiClient = GetRequiredService<IApiClient>(context);
        
        var isHealthy = await apiClient.IsServerHealthyAsync();
        if (!isHealthy)
        {
            WriteError("Server is not responding or not healthy");
            AnsiConsole.MarkupLine("[yellow]Check that the server is running and accessible.[/]");
        }
        
        return isHealthy;
    }

    protected static void ShowProgressBar(string message, Action<ProgressTask> work)
    {
        AnsiConsole.Progress()
            .Start(ctx =>
            {
                var task = ctx.AddTask(message);
                work(task);
            });
    }

    protected static async Task ShowProgressBarAsync(string message, Func<ProgressTask, Task> work)
    {
        await AnsiConsole.Progress()
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask(message);
                await work(task);
            });
    }
}

public abstract class AuthenticatedCommand : BaseCommand
{
    private readonly Option<string> _profileOption;

    protected AuthenticatedCommand(string name, string description) : base(name, description)
    {
        _profileOption = new Option<string>(
            aliases: ["--profile", "-p"],
            description: "Profile to use for authentication",
            getDefaultValue: () => "default"
        );
        
        AddOption(_profileOption);
    }

    protected override async Task<int> ExecuteInternalAsync(InvocationContext context)
    {
        var profile = context.ParseResult.GetValueForOption(_profileOption) ?? "default";
        
        if (!await EnsureAuthenticatedAsync(context, profile))
        {
            return 1;
        }

        return await ExecuteAuthenticatedAsync(context, profile);
    }

    protected abstract Task<int> ExecuteAuthenticatedAsync(InvocationContext context, string profile);
}