using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text.Json;
using Spectre.Console;
using ClaudeBatchServer.Core.DTOs;
using ClaudeServerCLI.Services;
using ClaudeServerCLI.Models;
using YamlDotNet.Serialization;

namespace ClaudeServerCLI.Commands;

public class ReposCommand : Command
{
    public ReposCommand() : base("repos", "Repository management commands")
    {
        AddCommand(new ReposListCommand());
        AddCommand(new ReposCreateCommand());
        AddCommand(new ReposShowCommand());
        AddCommand(new ReposDeleteCommand());
    }
}

public class ReposListCommand : AuthenticatedCommand
{
    private readonly Option<string> _formatOption;
    private readonly Option<bool> _watchOption;

    public ReposListCommand() : base("list", "List all repositories")
    {
        _formatOption = new Option<string>(
            aliases: ["--format", "-f"],
            description: "Output format (table, json, yaml)",
            getDefaultValue: () => "table"
        );

        _watchOption = new Option<bool>(
            aliases: ["--watch", "-w"],
            description: "Watch for changes and update display in real-time",
            getDefaultValue: () => false
        );

        AddOption(_formatOption);
        AddOption(_watchOption);
    }

    protected override async Task<int> ExecuteAuthenticatedAsync(InvocationContext context, string profile)
    {
        var format = context.ParseResult.GetValueForOption(_formatOption) ?? "table";
        var watch = context.ParseResult.GetValueForOption(_watchOption);
        var cancellationToken = context.GetCancellationToken();
        var apiClient = GetRequiredService<IApiClient>(context);

        if (watch)
        {
            return await WatchRepositoriesAsync(apiClient, format, cancellationToken);
        }
        else
        {
            return await ListRepositoriesOnceAsync(apiClient, format, cancellationToken);
        }
    }

    private async Task<int> ListRepositoriesOnceAsync(IApiClient apiClient, string format, CancellationToken cancellationToken)
    {
        try
        {
            var repositories = await apiClient.GetRepositoriesAsync(cancellationToken);
            DisplayRepositories(repositories, format);
            return 0;
        }
        catch (Exception ex)
        {
            WriteError($"Failed to get repositories: {ex.Message}");
            return 1;
        }
    }

    private async Task<int> WatchRepositoriesAsync(IApiClient apiClient, string format, CancellationToken cancellationToken)
    {
        WriteInfo("Watching repositories... Press Ctrl+C to exit");
        
        try
        {
            var lastCount = 0;
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var repositories = await apiClient.GetRepositoriesAsync(cancellationToken);
                    var repoList = repositories.ToList();
                    
                    // Clear console and redisplay if using table format
                    if (format == "table")
                    {
                        Console.Clear();
                        AnsiConsole.MarkupLine("[bold blue]Repositories (Live Updates - Press Ctrl+C to exit)[/]");
                        AnsiConsole.WriteLine();
                    }
                    
                    DisplayRepositories(repoList, format);
                    
                    if (format == "table")
                    {
                        AnsiConsole.WriteLine();
                        AnsiConsole.MarkupLine($"[grey]🔄 Refreshing every 2 seconds... Found {repoList.Count} repositories[/]");
                        AnsiConsole.MarkupLine("[grey]Press Ctrl+C to exit[/]");
                    }
                    
                    if (repoList.Count != lastCount)
                    {
                        lastCount = repoList.Count;
                        if (format != "table")
                        {
                            WriteInfo($"Repository count changed: {lastCount}");
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (format == "table")
                    {
                        AnsiConsole.MarkupLine($"[red]Error: {ex.Message}[/]");
                    }
                    else
                    {
                        WriteError($"Watch error: {ex.Message}");
                    }
                }

                try
                {
                    await Task.Delay(2000, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            WriteInfo("Watch mode stopped");
            return 0;
        }
        catch (OperationCanceledException)
        {
            WriteInfo("Watch mode cancelled");
            return 0;
        }
    }

    private static void DisplayRepositories(IEnumerable<RepositoryInfo> repositories, string format)
    {
        UI.ModernDisplay.DisplayRepositories(repositories, format);
    }

    private static string GetRepositoryTypeDisplay(string type)
    {
        return type.ToLowerInvariant() switch
        {
            "git" => "[green]📁 Git[/]",
            "folder" => "[blue]📂 Folder[/]",
            _ => "[grey]❓ Unknown[/]"
        };
    }

    private static string GetRepositoryStatusDisplay(RepositoryInfo repo)
    {
        if (repo.Type == "git")
        {
            var status = repo.LastPullStatus switch
            {
                "success" => "[green]✅ Ready[/]",
                "failed" => "[red]❌ Failed[/]",
                "cloning" => "[yellow]⚡ Cloning[/]",
                "never" => "[grey]⏸️ Never Pulled[/]",
                _ => "[grey]❓ Unknown[/]"
            };

            if (repo.HasUncommittedChanges == true)
            {
                status += " [yellow]⚠️[/]";
            }

            return status;
        }

        return "[blue]📂 Folder[/]";
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes == 0) return "0 B";
        
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        int order = 0;
        double size = bytes;
        
        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }
        
        return $"{size:0.##} {sizes[order]}";
    }
}

public class ReposCreateCommand : AuthenticatedCommand
{
    private readonly Option<string> _nameOption;
    private readonly Option<string> _gitUrlOption;
    private readonly Option<string> _pathOption;
    private readonly Option<string> _descriptionOption;
    private readonly Option<bool> _watchOption;
    private readonly Option<bool> _cidxAwareOption;

    public ReposCreateCommand() : base("create", "Register a new repository")
    {
        _nameOption = new Option<string>(
            aliases: ["--name", "-n"],
            description: "Repository name (required)"
        ) { IsRequired = true };

        _gitUrlOption = new Option<string>(
            aliases: ["--clone", "--git-url", "-g"],
            description: "Git repository URL to clone"
        );

        _pathOption = new Option<string>(
            aliases: ["--path"],
            description: "Local path to existing repository"
        );

        _descriptionOption = new Option<string>(
            aliases: ["--description", "--desc", "-d"],
            description: "Repository description",
            getDefaultValue: () => ""
        );

        _watchOption = new Option<bool>(
            aliases: ["--watch", "-w"],
            description: "Watch the registration progress",
            getDefaultValue: () => false
        );

        _cidxAwareOption = new Option<bool>(
            aliases: ["--cidx-aware"],
            description: "Enable semantic indexing with cidx (default: true)",
            getDefaultValue: () => true
        );

        AddOption(_nameOption);
        AddOption(_gitUrlOption);
        AddOption(_pathOption);
        AddOption(_descriptionOption);
        AddOption(_watchOption);
        AddOption(_cidxAwareOption);

        // Add validation
        this.SetHandler(async (context) =>
        {
            var gitUrl = context.ParseResult.GetValueForOption(_gitUrlOption);
            var path = context.ParseResult.GetValueForOption(_pathOption);

            if (string.IsNullOrEmpty(gitUrl) && string.IsNullOrEmpty(path))
            {
                WriteError("Either --clone (Git URL) or --path (local path) must be specified");
                context.ExitCode = 1;
                return;
            }

            if (!string.IsNullOrEmpty(gitUrl) && !string.IsNullOrEmpty(path))
            {
                WriteError("Cannot specify both --clone and --path options");
                context.ExitCode = 1;
                return;
            }

            await base.Handler!.InvokeAsync(context);
        });
    }

    protected override async Task<int> ExecuteAuthenticatedAsync(InvocationContext context, string profile)
    {
        var name = context.ParseResult.GetValueForOption(_nameOption)!;
        var gitUrl = context.ParseResult.GetValueForOption(_gitUrlOption);
        var path = context.ParseResult.GetValueForOption(_pathOption);
        var description = context.ParseResult.GetValueForOption(_descriptionOption) ?? "";
        var watch = context.ParseResult.GetValueForOption(_watchOption);
        var cidxAware = context.ParseResult.GetValueForOption(_cidxAwareOption);
        var cancellationToken = context.GetCancellationToken();
        var apiClient = GetRequiredService<IApiClient>(context);

        // Validate local path if provided
        if (!string.IsNullOrEmpty(path))
        {
            WriteError("Local path registration not yet implemented. Please use --clone with a Git URL.");
            return 1;
        }

        // Validate Git URL
        if (!string.IsNullOrEmpty(gitUrl) && !IsValidGitUrl(gitUrl))
        {
            WriteError("Invalid Git URL format");
            return 1;
        }

        try
        {
            WriteInfo($"Registering repository '{name}'...");
            
            var request = new RegisterRepositoryRequest
            {
                Name = name,
                GitUrl = gitUrl ?? "",
                Description = description,
                CidxAware = cidxAware
            };

            var response = await apiClient.CreateRepositoryAsync(request, cancellationToken);
            
            WriteSuccess($"Repository '{response.Name}' registered successfully");
            WriteInfo($"Clone status: {response.CloneStatus}");
            WriteInfo($"Path: {response.Path}");
            
            if (watch && response.CloneStatus == "cloning")
            {
                WriteInfo("Watching clone progress...");
                return await WatchRepositoryRegistrationAsync(apiClient, name, cancellationToken);
            }

            return 0;
        }
        catch (Exception ex)
        {
            WriteError($"Failed to register repository: {ex.Message}");
            return 1;
        }
    }

    private async Task<int> WatchRepositoryRegistrationAsync(IApiClient apiClient, string name, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var repo = await apiClient.GetRepositoryAsync(name, cancellationToken);
                    var status = repo.CloneStatus ?? "cloning";
                    
                    WriteInfo($"Repository status: {GetStatusDisplayText(status)}");
                    
                    if (status == "completed")
                    {
                        WriteSuccess("Repository registration completed successfully!");
                        WriteInfo($"Branch: {repo.CurrentBranch}");
                        WriteInfo($"Latest commit: {repo.CommitHash?[..8]} - {repo.CommitMessage}");
                        return 0;
                    }
                    else if (status == "failed" || status == "cidx_failed")
                    {
                        WriteError($"Repository registration failed with status: {status}");
                        return 1;
                    }
                    
                    await Task.Delay(2000, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    WriteError($"Error checking status: {ex.Message}");
                    break;
                }
            }
            
            WriteInfo("Watch cancelled");
            return 0;
        }
        catch (OperationCanceledException)
        {
            WriteInfo("Watch cancelled");
            return 0;
        }
    }

    private static string GetStatusDisplayText(string status)
    {
        return status switch
        {
            "cloning" => "Cloning repository...",
            "cidx_indexing" => "Building semantic index with cidx...",
            "completed" => "Ready",
            "failed" => "Clone failed",
            "cidx_failed" => "Cidx indexing failed",
            _ => status
        };
    }

    private static bool IsValidGitUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        // Basic validation for common Git URL patterns
        return url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
               url.StartsWith("git@", StringComparison.OrdinalIgnoreCase) ||
               url.StartsWith("ssh://", StringComparison.OrdinalIgnoreCase);
    }
}

public class ReposShowCommand : AuthenticatedCommand
{
    private readonly Argument<string> _nameArgument;
    private readonly Option<string> _formatOption;
    private readonly Option<bool> _watchOption;

    public ReposShowCommand() : base("show", "Show detailed repository information")
    {
        _nameArgument = new Argument<string>(
            name: "name",
            description: "Repository name"
        );

        _formatOption = new Option<string>(
            aliases: ["--format", "-f"],
            description: "Output format (table, json, yaml)",
            getDefaultValue: () => "table"
        );

        _watchOption = new Option<bool>(
            aliases: ["--watch", "-w"],
            description: "Watch for changes and update display in real-time",
            getDefaultValue: () => false
        );

        AddArgument(_nameArgument);
        AddOption(_formatOption);
        AddOption(_watchOption);
    }

    protected override async Task<int> ExecuteAuthenticatedAsync(InvocationContext context, string profile)
    {
        var name = context.ParseResult.GetValueForArgument(_nameArgument);
        var format = context.ParseResult.GetValueForOption(_formatOption) ?? "table";
        var watch = context.ParseResult.GetValueForOption(_watchOption);
        var cancellationToken = context.GetCancellationToken();
        var apiClient = GetRequiredService<IApiClient>(context);

        if (watch)
        {
            return await WatchRepositoryAsync(apiClient, name, format, cancellationToken);
        }
        else
        {
            return await ShowRepositoryOnceAsync(apiClient, name, format, cancellationToken);
        }
    }

    private async Task<int> ShowRepositoryOnceAsync(IApiClient apiClient, string name, string format, CancellationToken cancellationToken)
    {
        try
        {
            var repository = await apiClient.GetRepositoryAsync(name, cancellationToken);
            DisplayRepository(repository, format);
            return 0;
        }
        catch (InvalidOperationException)
        {
            WriteError($"Repository '{name}' not found");
            return 1;
        }
        catch (Exception ex)
        {
            WriteError($"Failed to get repository: {ex.Message}");
            return 1;
        }
    }

    private async Task<int> WatchRepositoryAsync(IApiClient apiClient, string name, string format, CancellationToken cancellationToken)
    {
        WriteInfo($"Watching repository '{name}'... Press Ctrl+C to exit");
        
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var repository = await apiClient.GetRepositoryAsync(name, cancellationToken);
                    
                    // Clear console and redisplay if using table format
                    if (format == "table")
                    {
                        Console.Clear();
                        AnsiConsole.MarkupLine($"[bold blue]Repository: {name} (Live Updates - Press Ctrl+C to exit)[/]");
                        AnsiConsole.WriteLine();
                    }
                    
                    DisplayRepository(repository, format);
                    
                    if (format == "table")
                    {
                        AnsiConsole.WriteLine();
                        AnsiConsole.MarkupLine("[grey]🔄 Refreshing every 2 seconds... Press Ctrl+C to exit[/]");
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (format == "table")
                    {
                        AnsiConsole.MarkupLine($"[red]Error: {ex.Message}[/]");
                    }
                    else
                    {
                        WriteError($"Watch error: {ex.Message}");
                    }
                }

                try
                {
                    await Task.Delay(2000, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            WriteInfo("Watch mode stopped");
            return 0;
        }
        catch (OperationCanceledException)
        {
            WriteInfo("Watch mode cancelled");
            return 0;
        }
    }

    private static void DisplayRepository(RepositoryInfo repository, string format)
    {
        switch (format.ToLowerInvariant())
        {
            case "json":
                var json = JsonSerializer.Serialize(repository, new JsonSerializerOptions 
                { 
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });
                Console.WriteLine(json);
                break;
                
            case "yaml":
                var serializer = new SerializerBuilder().Build();
                var yaml = serializer.Serialize(repository);
                Console.WriteLine(yaml);
                break;
                
            case "table":
            default:
                var table = new Table();
                table.Border = TableBorder.None;
                table.AddColumn("Property").Width(20);
                table.AddColumn("Value");

                table.AddRow("📁 Name", repository.Name);
                table.AddRow("📂 Type", GetRepositoryTypeDisplay(repository.Type));
                table.AddRow("📍 Path", repository.Path);
                table.AddRow("📏 Size", FormatFileSize(repository.Size));
                table.AddRow("📅 Last Modified", repository.LastModified.ToString("yyyy-MM-dd HH:mm:ss"));

                if (repository.Type == "git")
                {
                    table.AddRow("", ""); // Separator
                    table.AddRow("[bold]Git Information[/]", "");
                    
                    if (!string.IsNullOrEmpty(repository.GitUrl))
                        table.AddRow("🔗 Git URL", repository.GitUrl);
                    
                    if (!string.IsNullOrEmpty(repository.RemoteUrl))
                        table.AddRow("🌐 Remote URL", repository.RemoteUrl);
                    
                    if (!string.IsNullOrEmpty(repository.CurrentBranch))
                        table.AddRow("🌿 Branch", repository.CurrentBranch);
                    
                    if (!string.IsNullOrEmpty(repository.CommitHash))
                    {
                        table.AddRow("📝 Commit", $"{repository.CommitHash[..8]} - {repository.CommitMessage}");
                        if (!string.IsNullOrEmpty(repository.CommitAuthor))
                            table.AddRow("👤 Author", $"{repository.CommitAuthor}");
                        if (repository.CommitDate.HasValue)
                            table.AddRow("📅 Commit Date", repository.CommitDate.Value.ToString("yyyy-MM-dd HH:mm:ss"));
                    }
                    
                    var status = repository.LastPullStatus switch
                    {
                        "success" => "[green]✅ Ready[/]",
                        "failed" => "[red]❌ Failed[/]",
                        "cloning" => "[yellow]⚡ Cloning[/]",
                        "never" => "[grey]⏸️ Never Pulled[/]",
                        _ => "[grey]❓ Unknown[/]"
                    };
                    table.AddRow("🔄 Status", status);
                    
                    if (repository.HasUncommittedChanges.HasValue)
                    {
                        var uncommitted = repository.HasUncommittedChanges.Value 
                            ? "[yellow]⚠️ Yes[/]" 
                            : "[green]✅ No[/]";
                        table.AddRow("⚠️ Uncommitted Changes", uncommitted);
                    }
                    
                    if (repository.LastPull.HasValue)
                        table.AddRow("🕐 Last Pull", repository.LastPull.Value.ToString("yyyy-MM-dd HH:mm:ss"));
                    
                    if (repository.RegisteredAt.HasValue)
                        table.AddRow("📅 Registered", repository.RegisteredAt.Value.ToString("yyyy-MM-dd HH:mm:ss"));
                    
                    if (repository.CidxAware.HasValue)
                    {
                        var cidxAwareDisplay = repository.CidxAware.Value 
                            ? "[green]✅ Yes[/]" 
                            : "[grey]❌ No[/]";
                        table.AddRow("🧠 Cidx Aware", cidxAwareDisplay);
                    }
                }

                if (!string.IsNullOrEmpty(repository.Description))
                {
                    table.AddRow("", ""); // Separator
                    table.AddRow("📋 Description", repository.Description);
                }

                AnsiConsole.Write(table);
                break;
        }
    }

    private static string GetRepositoryTypeDisplay(string type)
    {
        return type.ToLowerInvariant() switch
        {
            "git" => "[green]📁 Git Repository[/]",
            "folder" => "[blue]📂 Local Folder[/]",
            _ => "[grey]❓ Unknown[/]"
        };
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes == 0) return "0 B";
        
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        int order = 0;
        double size = bytes;
        
        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }
        
        return $"{size:0.##} {sizes[order]}";
    }
}

public class ReposDeleteCommand : AuthenticatedCommand
{
    private readonly Argument<string> _nameArgument;
    private readonly Option<bool> _forceOption;

    public ReposDeleteCommand() : base("delete", "Unregister a repository")
    {
        _nameArgument = new Argument<string>(
            name: "name",
            description: "Repository name to unregister"
        );

        _forceOption = new Option<bool>(
            aliases: ["--force", "-f"],
            description: "Force deletion without confirmation",
            getDefaultValue: () => false
        );

        AddArgument(_nameArgument);
        AddOption(_forceOption);
    }

    protected override async Task<int> ExecuteAuthenticatedAsync(InvocationContext context, string profile)
    {
        var name = context.ParseResult.GetValueForArgument(_nameArgument);
        var force = context.ParseResult.GetValueForOption(_forceOption);
        var cancellationToken = context.GetCancellationToken();
        var apiClient = GetRequiredService<IApiClient>(context);

        try
        {
            // Check if repository exists first
            try
            {
                var repo = await apiClient.GetRepositoryAsync(name, cancellationToken);
                
                if (!force)
                {
                    WriteWarning($"This will unregister repository '{name}' ({repo.Type})");
                    WriteWarning($"Path: {repo.Path}");
                    
                    if (!AnsiConsole.Confirm("Are you sure you want to continue?"))
                    {
                        WriteInfo("Operation cancelled");
                        return 0;
                    }
                }
            }
            catch (InvalidOperationException)
            {
                WriteError($"Repository '{name}' not found");
                return 1;
            }

            WriteInfo($"Unregistering repository '{name}'...");
            var response = await apiClient.DeleteRepositoryAsync(name, cancellationToken);
            
            if (response.Success)
            {
                WriteSuccess($"Repository '{name}' unregistered successfully");
                if (response.Removed)
                {
                    WriteInfo("Repository files were removed from the server");
                }
                if (!string.IsNullOrEmpty(response.Message))
                {
                    WriteInfo($"Note: {response.Message}");
                }
                return 0;
            }
            else
            {
                WriteError($"Failed to unregister repository: {response.Message}");
                return 1;
            }
        }
        catch (Exception ex)
        {
            WriteError($"Failed to unregister repository: {ex.Message}");
            return 1;
        }
    }
}