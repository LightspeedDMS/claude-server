using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
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
            
            // Build the root command with a custom command line builder
            var rootCommand = BuildRootCommand(host.Services);
            
            var builder = new CommandLineBuilder(rootCommand);
            builder.UseDefaults();
            
            // Add middleware to inject the service provider
            builder.AddMiddleware(async (context, next) =>
            {
                context.BindingContext.AddService<IServiceProvider>(_ => host.Services);
                await next(context);
            });
            
            var parser = builder.Build();
            
            // Execute the command
            return await parser.InvokeAsync(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Fatal error: {ex.Message}");
            return 1;
        }
    }

    private static RootCommand BuildRootCommand(IServiceProvider serviceProvider)
    {
        var rootCommand = new RootCommand("""
            Claude Batch Server CLI - Advanced command-line interface for managing Claude Code batch jobs
            
            Manage repositories, create and monitor AI-powered batch jobs, handle authentication,
            and control your Claude Code workflow from the command line.
            
            QUICK START:
              claude-server auth login -u username -p password
              claude-server repos create my-repo https://github.com/user/repo.git
              claude-server jobs create --repo my-repo --prompt "Analyze this codebase"
              claude-server jobs list --watch
            
            AUTHENTICATION:
              Most commands require authentication. Use 'claude-server auth login' first.
              Credentials are stored in profiles (default: 'default').
            
            EXAMPLES:
              # Authentication
              claude-server auth login -u admin -p mypassword
              claude-server auth whoami
              
              # Repository management
              claude-server repos list
              claude-server repos create myrepo https://github.com/user/repo.git --cidx
              claude-server repos show myrepo
              
              # Job management  
              claude-server jobs create --repo myrepo --prompt "Add unit tests"
              claude-server jobs list --status running --watch
              claude-server jobs show abc123 --follow
              
              # Output formats
              claude-server repos list --format json
              claude-server jobs list --format yaml
            
            For detailed help on any command, use: claude-server <command> --help
            """)
        {
            Name = "claude-server"
        };

        // Add command groups
        var authCommand = new AuthCommand();
        rootCommand.AddCommand(authCommand);
        
        var reposCommand = new ReposCommand();
        rootCommand.AddCommand(reposCommand);
        
        var jobsCommand = new JobsCommand();
        rootCommand.AddCommand(jobsCommand);
        
        var userCommand = new UserCommand();
        rootCommand.AddCommand(userCommand);
        
        // Add global options
        var verboseOption = new Option<bool>(
            aliases: ["--verbose", "-v"],
            description: "Enable verbose logging and detailed output for debugging"
        );
        
        var serverUrlOption = new Option<string>(
            aliases: ["--server-url", "--url"],
            description: "Claude Batch Server URL (e.g., https://localhost:8443). Overrides profile setting."
        );
        
        var timeoutOption = new Option<int>(
            aliases: ["--timeout", "-t"],
            description: "HTTP request timeout in seconds. Increase for long-running operations.",
            getDefaultValue: () => 30
        );

        rootCommand.AddGlobalOption(verboseOption);
        rootCommand.AddGlobalOption(serverUrlOption);
        rootCommand.AddGlobalOption(timeoutOption);

        return rootCommand;
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