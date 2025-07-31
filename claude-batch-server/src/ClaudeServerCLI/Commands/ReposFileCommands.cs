using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text.Json;
using System.Text.RegularExpressions;
using Spectre.Console;
using ClaudeBatchServer.Core.DTOs;
using ClaudeServerCLI.Services;
using YamlDotNet.Serialization;

namespace ClaudeServerCLI.Commands;

public class RepositoryFilesCommand : Command
{
    public RepositoryFilesCommand() : base("files", """
        Repository file management commands
        
        Browse, view, search, and download files from registered repositories.
        Operations are performed on the original repository files, not job workspaces.
        
        EXAMPLES:
          # List files in repository root
          claude-server repos files list myproject
          
          # List files in specific directory
          claude-server repos files list myproject --path src
          
          # View file content
          claude-server repos files show myproject README.md
          
          # Download file
          claude-server repos files download myproject src/main.cs --output ./main.cs
          
          # Search for files by pattern
          claude-server repos files search myproject --pattern "*.cs"
          
          # Export file listings as JSON/YAML
          claude-server repos files list myproject --format json
        """)
    {
        AddCommand(new RepositoryFilesListCommand());
        AddCommand(new RepositoryFilesShowCommand());
        AddCommand(new RepositoryFilesDownloadCommand());
        AddCommand(new RepositoryFilesSearchCommand());
    }
}

public class RepositoryFilesListCommand : AuthenticatedCommand
{
    private readonly Argument<string> _repositoryArgument;
    private readonly Option<string> _pathOption;
    private readonly Option<string> _formatOption;
    private readonly Option<bool> _quietOption;

    public RepositoryFilesListCommand() : base("list", """
        List files and directories in a repository
        
        Browse the file structure of registered repositories. Can list files
        from the root or specific directories within the repository.
        
        EXAMPLES:
          # List all files in repository root
          claude-server repos files list myproject
          
          # List files in specific directory
          claude-server repos files list myproject --path src/models
          
          # Export as JSON for scripting
          claude-server repos files list myproject --format json
          
          # Export as YAML for configuration
          claude-server repos files list myproject --format yaml
        """)
    {
        _repositoryArgument = new Argument<string>(
            name: "repository",
            description: "Name of the repository to browse"
        );

        _pathOption = new Option<string>(
            aliases: ["--path"],
            description: "Path within the repository (default: root)"
        );

        _formatOption = new Option<string>(
            aliases: ["--format", "-f"],
            description: "Output format: 'table', 'json', 'yaml'",
            getDefaultValue: () => "table"
        );

        _quietOption = new Option<bool>(
            aliases: ["--quiet", "-q"],
            description: "Suppress progress messages and ANSI output (for testing/automation)",
            getDefaultValue: () => false
        );

        AddArgument(_repositoryArgument);
        AddOption(_pathOption);
        AddOption(_formatOption);
        AddOption(_quietOption);
    }

    protected override async Task<int> ExecuteAuthenticatedAsync(InvocationContext context, string profile, IApiClient apiClient)
    {
        var repository = context.ParseResult.GetValueForArgument(_repositoryArgument);
        var path = context.ParseResult.GetValueForOption(_pathOption);
        var format = context.ParseResult.GetValueForOption(_formatOption) ?? "table";
        var quiet = context.ParseResult.GetValueForOption(_quietOption);
        var cancellationToken = context.GetCancellationToken();

        try
        {
            // Only show progress info when not in quiet mode
            if (!quiet)
            {
                WriteInfo($"Listing files in repository '{repository}'{(path != null ? $" at path '{path}'" : "")}...");
            }
            
            var files = await apiClient.GetRepositoryFilesAsync(repository, path, cancellationToken);
            
            DisplayFiles(files, format, context);
            return 0;
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not found"))
        {
            WriteError($"Repository '{repository}' not found");
            WriteInfo("Use 'claude-server repos list' to see available repositories");
            return 1;
        }
        catch (Exception ex)
        {
            WriteError($"Failed to list repository files: {ex.Message}");
            return 1;
        }
    }

    private void DisplayFiles(IEnumerable<FileInfoResponse> files, string format, InvocationContext context)
    {
        switch (format.ToLowerInvariant())
        {
            case "json":
                // Simple direct JSON serialization without any Spectre.Console involvement
                var filesList = files.ToList(); // Ensure enumeration is materialized
                var json = JsonSerializer.Serialize(filesList, new JsonSerializerOptions { WriteIndented = true });
                
                // Write directly to context console only (for test capture)
                context.Console.WriteLine(json);
                break;
                
            case "yaml":
                var serializer = new SerializerBuilder().Build();
                var yaml = serializer.Serialize(files);
                // Use WriteOutput to properly handle both console and test capture without markup parsing
                WriteOutput(context, yaml);
                break;
                
            case "table":
            default:
                if (!files.Any())
                {
                    AnsiConsole.MarkupLine("[grey]No files found[/]");
                    return;
                }

                var table = new Table();
                table.AddColumn("Type");
                table.AddColumn("Name");
                table.AddColumn("Size");
                table.AddColumn("Path");

                foreach (var file in files.OrderBy(f => f.IsDirectory ? 0 : 1).ThenBy(f => f.Name))
                {
                    var type = file.IsDirectory ? "[blue]DIR[/]" : "[green]FILE[/]";
                    var size = file.IsDirectory ? "-" : FormatFileSize(file.Size);
                    
                    table.AddRow(type, file.Name, size, file.Path);
                }

                AnsiConsole.Write(table);
                break;
        }
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024:F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024 * 1024):F1} MB";
        return $"{bytes / (1024 * 1024 * 1024):F1} GB";
    }
}

public class RepositoryFilesShowCommand : AuthenticatedCommand
{
    private readonly Argument<string> _repositoryArgument;
    private readonly Argument<string> _filePathArgument;
    private readonly Option<bool> _noLineNumbersOption;
    private readonly Option<bool> _quietOption;

    public RepositoryFilesShowCommand() : base("show", """
        Display the content of a file from a repository
        
        View the full content of text files directly in the terminal.
        Binary files will show metadata instead of content.
        
        EXAMPLES:
          # View a README file
          claude-server repos files show myproject README.md
          
          # View source code
          claude-server repos files show myproject src/Program.cs
          
          # View without line numbers
          claude-server repos files show myproject config.json --no-line-numbers
        """)
    {
        _repositoryArgument = new Argument<string>(
            name: "repository",
            description: "Name of the repository"
        );

        _filePathArgument = new Argument<string>(
            name: "file",
            description: "Path to the file within the repository"
        );

        _noLineNumbersOption = new Option<bool>(
            aliases: ["--no-line-numbers", "--no-lines"],
            description: "Don't show line numbers",
            getDefaultValue: () => false
        );

        _quietOption = new Option<bool>(
            aliases: ["--quiet", "-q"],
            description: "Suppress progress messages and ANSI output (for testing/automation)",
            getDefaultValue: () => false
        );

        AddArgument(_repositoryArgument);
        AddArgument(_filePathArgument);
        AddOption(_noLineNumbersOption);
        AddOption(_quietOption);
    }

    protected override async Task<int> ExecuteAuthenticatedAsync(InvocationContext context, string profile, IApiClient apiClient)
    {
        var repository = context.ParseResult.GetValueForArgument(_repositoryArgument);
        var filePath = context.ParseResult.GetValueForArgument(_filePathArgument);
        var noLineNumbers = context.ParseResult.GetValueForOption(_noLineNumbersOption);
        var quiet = context.ParseResult.GetValueForOption(_quietOption);
        var cancellationToken = context.GetCancellationToken();

        try
        {
            if (!quiet)
            {
                WriteInfo($"Retrieving file '{filePath}' from repository '{repository}'...");
            }
            
            var fileContent = await apiClient.GetRepositoryFileContentAsync(repository, filePath, cancellationToken);
            
            DisplayFileContent(fileContent, noLineNumbers, quiet);
            return 0;
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not found"))
        {
            WriteError($"File '{filePath}' not found in repository '{repository}'");
            return 1;
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("directory"))
        {
            WriteError($"'{filePath}' is a directory, not a file");
            WriteInfo($"Use 'claude-server repos files list {repository} --path {filePath}' to list directory contents");
            return 1;
        }
        catch (Exception ex)
        {
            WriteError($"Failed to retrieve file content: {ex.Message}");
            return 1;
        }
    }

    private static void DisplayFileContent(FileContentResponse fileContent, bool noLineNumbers, bool quiet = false)
    {
        if (!quiet)
        {
            AnsiConsole.MarkupLine($"[bold blue]File:[/] {fileContent.FileName}");
            AnsiConsole.MarkupLine($"[bold blue]Size:[/] {FormatFileSize(fileContent.Size)}");
            if (!string.IsNullOrEmpty(fileContent.MimeType))
            {
                AnsiConsole.MarkupLine($"[bold blue]Type:[/] {fileContent.MimeType}");
            }
            AnsiConsole.WriteLine();
        }

        if (IsBinaryContent(fileContent.MimeType))
        {
            if (!quiet)
            {
                AnsiConsole.MarkupLine("[yellow]Binary file content not displayed[/]");
            }
            return;
        }

        if (noLineNumbers)
        {
            Console.WriteLine(fileContent.Content);
        }
        else
        {
            var lines = fileContent.Content.Split('\n');
            var lineNumberWidth = lines.Length.ToString().Length;
            
            for (int i = 0; i < lines.Length; i++)
            {
                var lineNumber = (i + 1).ToString().PadLeft(lineNumberWidth);
                AnsiConsole.MarkupLine($"[grey]{lineNumber}:[/] {lines[i].EscapeMarkup()}");
            }
        }
    }

    private static bool IsBinaryContent(string? mimeType)
    {
        if (string.IsNullOrEmpty(mimeType)) return false;
        
        return mimeType.StartsWith("image/") ||
               mimeType.StartsWith("video/") ||
               mimeType.StartsWith("audio/") ||
               mimeType.Contains("binary") ||
               mimeType.Contains("octet-stream");
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024:F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024 * 1024):F1} MB";
        return $"{bytes / (1024 * 1024 * 1024):F1} GB";
    }
}

public class RepositoryFilesDownloadCommand : AuthenticatedCommand
{
    private readonly Argument<string> _repositoryArgument;
    private readonly Argument<string> _filePathArgument;
    private readonly Option<string> _outputOption;
    private readonly Option<bool> _overwriteOption;
    private readonly Option<bool> _quietOption;

    public RepositoryFilesDownloadCommand() : base("download", """
        Download a file from a repository to the local filesystem
        
        Downloads files from registered repositories to your local machine.
        Can specify custom output paths or use the original filename.
        
        EXAMPLES:
          # Download to current directory with original name
          claude-server repos files download myproject README.md
          
          # Download with custom output path
          claude-server repos files download myproject src/main.cs --output ./downloaded-main.cs
          
          # Overwrite existing local file
          claude-server repos files download myproject config.json --output ./config.json --overwrite
        """)
    {
        _repositoryArgument = new Argument<string>(
            name: "repository",
            description: "Name of the repository"
        );

        _filePathArgument = new Argument<string>(
            name: "file",
            description: "Path to the file within the repository"
        );

        _outputOption = new Option<string>(
            aliases: ["--output", "-o"],
            description: "Output file path (default: use original filename in current directory)"
        );

        _overwriteOption = new Option<bool>(
            aliases: ["--overwrite"],
            description: "Overwrite existing local file if it exists",
            getDefaultValue: () => false
        );

        _quietOption = new Option<bool>(
            aliases: ["--quiet", "-q"],
            description: "Suppress progress messages and ANSI output (for testing/automation)",
            getDefaultValue: () => false
        );

        AddArgument(_repositoryArgument);
        AddArgument(_filePathArgument);
        AddOption(_outputOption);
        AddOption(_overwriteOption);
        AddOption(_quietOption);
    }

    protected override async Task<int> ExecuteAuthenticatedAsync(InvocationContext context, string profile, IApiClient apiClient)
    {
        var repository = context.ParseResult.GetValueForArgument(_repositoryArgument);
        var filePath = context.ParseResult.GetValueForArgument(_filePathArgument);
        var outputPath = context.ParseResult.GetValueForOption(_outputOption);
        var overwrite = context.ParseResult.GetValueForOption(_overwriteOption);
        var quiet = context.ParseResult.GetValueForOption(_quietOption);
        var cancellationToken = context.GetCancellationToken();

        // Determine output path
        if (string.IsNullOrEmpty(outputPath))
        {
            outputPath = Path.GetFileName(filePath);
        }

        // Check if output file already exists
        if (File.Exists(outputPath) && !overwrite)
        {
            WriteError($"Output file '{outputPath}' already exists");
            WriteInfo("Use --overwrite to replace the existing file");
            return 1;
        }

        try
        {
            if (!quiet)
            {
                WriteInfo($"Downloading '{filePath}' from repository '{repository}'...");
            }
            
            var fileContent = await apiClient.GetRepositoryFileContentAsync(repository, filePath, cancellationToken);
            
            await File.WriteAllTextAsync(outputPath, fileContent.Content, cancellationToken);
            
            if (!quiet)
            {
                WriteSuccess($"Downloaded '{filePath}' to '{outputPath}' ({FormatFileSize(fileContent.Size)})");
            }
            return 0;
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not found"))
        {
            WriteError($"File '{filePath}' not found in repository '{repository}'");
            return 1;
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("directory"))
        {
            WriteError($"'{filePath}' is a directory, not a file");
            return 1;
        }
        catch (UnauthorizedAccessException)
        {
            WriteError($"Permission denied writing to '{outputPath}'");
            return 1;
        }
        catch (Exception ex)
        {
            WriteError($"Failed to download file: {ex.Message}");
            return 1;
        }
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024:F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024 * 1024):F1} MB";
        return $"{bytes / (1024 * 1024 * 1024):F1} GB";
    }
}

public class RepositoryFilesSearchCommand : AuthenticatedCommand
{
    private readonly Argument<string> _repositoryArgument;
    private readonly Option<string> _patternOption;
    private readonly Option<string> _pathOption;
    private readonly Option<string> _formatOption;
    private readonly Option<bool> _caseSensitiveOption;
    private readonly Option<bool> _quietOption;

    public RepositoryFilesSearchCommand() : base("search", """
        Search for files in a repository using patterns
        
        Find files by name patterns using wildcards and regular expressions.
        Can search within specific directories or the entire repository.
        
        EXAMPLES:
          # Find all C# files
          claude-server repos files search myproject --pattern "*.cs"
          
          # Find files with 'test' in the name
          claude-server repos files search myproject --pattern "*test*"
          
          # Search only in specific directory
          claude-server repos files search myproject --pattern "*.json" --path src
          
          # Case-sensitive search
          claude-server repos files search myproject --pattern "README*" --case-sensitive
          
          # Export results as JSON
          claude-server repos files search myproject --pattern "*.md" --format json
        """)
    {
        _repositoryArgument = new Argument<string>(
            name: "repository",
            description: "Name of the repository to search"
        );

        _patternOption = new Option<string>(
            aliases: ["--pattern"],
            description: "Search pattern (supports wildcards like *.cs, *test*, README*)"
        ) { IsRequired = true };

        _pathOption = new Option<string>(
            aliases: ["--path"],
            description: "Search only within this path (default: entire repository)"
        );

        _formatOption = new Option<string>(
            aliases: ["--format", "-f"],
            description: "Output format: 'table', 'json', 'yaml'",
            getDefaultValue: () => "table"
        );

        _caseSensitiveOption = new Option<bool>(
            aliases: ["--case-sensitive", "-c"],
            description: "Perform case-sensitive search",
            getDefaultValue: () => false
        );

        _quietOption = new Option<bool>(
            aliases: ["--quiet", "-q"],
            description: "Suppress progress messages and ANSI output (for testing/automation)",
            getDefaultValue: () => false
        );

        AddArgument(_repositoryArgument);
        AddOption(_patternOption);
        AddOption(_pathOption);
        AddOption(_formatOption);
        AddOption(_caseSensitiveOption);
        AddOption(_quietOption);
    }

    protected override async Task<int> ExecuteAuthenticatedAsync(InvocationContext context, string profile, IApiClient apiClient)
    {
        var repository = context.ParseResult.GetValueForArgument(_repositoryArgument);
        var pattern = context.ParseResult.GetValueForOption(_patternOption)!;
        var path = context.ParseResult.GetValueForOption(_pathOption);
        var format = context.ParseResult.GetValueForOption(_formatOption) ?? "table";
        var caseSensitive = context.ParseResult.GetValueForOption(_caseSensitiveOption);
        var quiet = context.ParseResult.GetValueForOption(_quietOption);
        var cancellationToken = context.GetCancellationToken();

        try
        {
            // Only show progress info when not in quiet mode
            if (!quiet)
            {
                WriteInfo($"Searching for files matching '{pattern}' in repository '{repository}'{(path != null ? $" at path '{path}'" : "")}...");
            }
            
            // Get all files from the repository
            var allFiles = await apiClient.GetRepositoryFilesAsync(repository, path, cancellationToken);
            
            // Filter files based on pattern
            var matchingFiles = FilterFilesByPattern(allFiles, pattern, caseSensitive);
            
            if (!matchingFiles.Any())
            {
                if (!quiet)
                {
                    WriteInfo($"No files found matching pattern '{pattern}'");
                }
                return 0;
            }

            // Only show success message when not in quiet mode
            if (!quiet)
            {
                WriteSuccess($"Found {matchingFiles.Count()} files matching pattern '{pattern}'");
            }
            DisplayFiles(matchingFiles, format, context);
            return 0;
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not found"))
        {
            WriteError($"Repository '{repository}' not found");
            WriteInfo("Use 'claude-server repos list' to see available repositories");
            return 1;
        }
        catch (Exception ex)
        {
            WriteError($"Failed to search repository files: {ex.Message}");
            return 1;
        }
    }

    private static IEnumerable<FileInfoResponse> FilterFilesByPattern(IEnumerable<FileInfoResponse> files, string pattern, bool caseSensitive)
    {
        // Convert wildcard pattern to regex
        var regexPattern = "^" + Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";

        var regex = new Regex(regexPattern, caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase);

        return files.Where(file => regex.IsMatch(file.Name));
    }

    private void DisplayFiles(IEnumerable<FileInfoResponse> files, string format, InvocationContext context)
    {
        switch (format.ToLowerInvariant())
        {
            case "json":
                // Simple direct JSON serialization without any Spectre.Console involvement
                var filesList = files.ToList(); // Ensure enumeration is materialized
                var json = JsonSerializer.Serialize(filesList, new JsonSerializerOptions { WriteIndented = true });
                
                // Write directly to context console only (for test capture)
                context.Console.WriteLine(json);
                break;
                
            case "yaml":
                var serializer = new SerializerBuilder().Build();
                var yaml = serializer.Serialize(files);
                // Use WriteOutput to properly handle both console and test capture without markup parsing
                WriteOutput(context, yaml);
                break;
                
            case "table":
            default:
                var table = new Table();
                table.AddColumn("Type");
                table.AddColumn("Name");
                table.AddColumn("Size");
                table.AddColumn("Path");

                foreach (var file in files.OrderBy(f => f.IsDirectory ? 0 : 1).ThenBy(f => f.Name))
                {
                    var type = file.IsDirectory ? "[blue]DIR[/]" : "[green]FILE[/]";
                    var size = file.IsDirectory ? "-" : FormatFileSize(file.Size);
                    
                    table.AddRow(type, file.Name, size, file.Path);
                }

                AnsiConsole.Write(table);
                break;
        }
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024:F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024 * 1024):F1} MB";
        return $"{bytes / (1024 * 1024 * 1024):F1} GB";
    }
}