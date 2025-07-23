using System.CommandLine;
using System.CommandLine.Invocation;
using Microsoft.Extensions.DependencyInjection;
using ClaudeServerCLI.Commands;
using ClaudeServerCLI.Services;

namespace ClaudeServerCLI;

class Program
{
    static async Task<int> Main(string[] args)
    {
        try
        {
            // Create the host with dependency injection
            using var host = ServiceConfiguration.CreateHost(args);
            
            // Build the root command
            var rootCommand = BuildRootCommand(host.Services);
            
            // Execute the command
            return await rootCommand.InvokeAsync(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Fatal error: {ex.Message}");
            return 1;
        }
    }

    private static RootCommand BuildRootCommand(IServiceProvider serviceProvider)
    {
        var rootCommand = new RootCommand("Claude Batch Server CLI - Command-line interface for managing Claude Code batch jobs")
        {
            Name = "claude-server"
        };

        // Add command groups
        var authCommand = new AuthCommand();
        ConfigureServiceProvider(authCommand, serviceProvider);
        rootCommand.AddCommand(authCommand);
        
        var reposCommand = new ReposCommand();
        ConfigureServiceProvider(reposCommand, serviceProvider);
        rootCommand.AddCommand(reposCommand);
        
        var jobsCommand = new JobsCommand();
        ConfigureServiceProvider(jobsCommand, serviceProvider);
        rootCommand.AddCommand(jobsCommand);
        
        // Add global options
        var verboseOption = new Option<bool>(
            aliases: ["--verbose", "-v"],
            description: "Enable verbose logging"
        );
        
        var serverUrlOption = new Option<string>(
            aliases: ["--server-url", "--url"],
            description: "Server URL to connect to (overrides profile setting)"
        );
        
        var timeoutOption = new Option<int>(
            aliases: ["--timeout", "-t"],
            description: "Request timeout in seconds",
            getDefaultValue: () => 30
        );

        rootCommand.AddGlobalOption(verboseOption);
        rootCommand.AddGlobalOption(serverUrlOption);
        rootCommand.AddGlobalOption(timeoutOption);

        return rootCommand;
    }

    private static void ConfigureServiceProvider(Command command, IServiceProvider serviceProvider)
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

// Extension methods for InvocationContext
public static class InvocationContextExtensions
{
    public static T GetService<T>(this InvocationContext context) where T : notnull
    {
        var serviceProvider = context.BindingContext.GetService<IServiceProvider>()
            ?? throw new InvalidOperationException("Service provider not available");
        
        return serviceProvider.GetRequiredService<T>();
    }

    public static CancellationToken GetCancellationToken(this InvocationContext context)
    {
        return context.GetCancellationToken();
    }
}