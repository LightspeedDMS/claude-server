using System.Text;
using Spectre.Console;

namespace ClaudeServerCLI.UI;

/// <summary>
/// Provides interactive UI components for enhanced CLI experience
/// </summary>
public static class InteractiveUI
{
    /// <summary>
    /// Shows a multi-line text editor for prompt input
    /// </summary>
    public static string EditMultiLineText(string title, string? defaultValue = null, string? helpText = null)
    {
        AnsiConsole.Clear();
        
        var rule = new Rule($"[blue]{title}[/]");
        rule.Justification = Justify.Left;
        AnsiConsole.Write(rule);
        
        if (!string.IsNullOrEmpty(helpText))
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"[grey]{helpText}[/]");
        }
        
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[yellow]Instructions:[/]");
        AnsiConsole.MarkupLine("• Type your prompt (multiple lines supported)");
        AnsiConsole.MarkupLine("• Press [bold]Ctrl+D[/] on empty line to finish");
        AnsiConsole.MarkupLine("• Press [bold]Ctrl+C[/] to cancel");
        AnsiConsole.WriteLine();

        var lines = new List<string>();
        
        // Add default value if provided
        if (!string.IsNullOrEmpty(defaultValue))
        {
            lines.AddRange(defaultValue.Split('\n', StringSplitOptions.None));
            AnsiConsole.MarkupLine("[grey]Default content loaded. Continue editing or press Ctrl+D to accept.[/]");
            
            // Display the default content
            foreach (var line in lines)
            {
                AnsiConsole.MarkupLine($"[grey]> {line.EscapeMarkup()}[/]");
            }
        }

        AnsiConsole.MarkupLine("[blue]Enter your prompt:[/]");
        
        while (true)
        {
            try
            {
                AnsiConsole.Markup("[blue]> [/]");
                var line = Console.ReadLine();
                
                // Ctrl+D equivalent - null input
                if (line == null)
                {
                    break;
                }
                
                lines.Add(line);
            }
            catch (OperationCanceledException)
            {
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[red]Cancelled[/]");
                throw;
            }
        }

        var result = string.Join("\n", lines).TrimEnd();
        
        if (string.IsNullOrWhiteSpace(result))
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[red]No content entered[/]");
            return string.Empty;
        }

        // Show preview
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[green]Preview[/]").RuleStyle("green"));
        AnsiConsole.WriteLine();
        
        var panel = new Panel(result.EscapeMarkup())
        {
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.Green),
            Header = new PanelHeader(" Your Prompt ")
        };
        AnsiConsole.Write(panel);
        
        AnsiConsole.WriteLine();
        return result;
    }

    /// <summary>
    /// Shows a file selector dialog
    /// </summary>
    public static List<string> SelectFiles(string title, string? initialDirectory = null)
    {
        AnsiConsole.Clear();
        
        var rule = new Rule($"[blue]{title}[/]");
        rule.Justification = Justify.Left;
        AnsiConsole.Write(rule);
        
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[yellow]Instructions:[/]");
        AnsiConsole.MarkupLine("• Enter file paths one by one");
        AnsiConsole.MarkupLine("• Press [bold]Enter[/] on empty line to finish");
        AnsiConsole.MarkupLine("• Relative paths will be resolved from current directory");
        AnsiConsole.WriteLine();

        var files = new List<string>();
        var currentDir = initialDirectory ?? Directory.GetCurrentDirectory();
        
        AnsiConsole.MarkupLine($"[grey]Current directory: {currentDir}[/]");
        AnsiConsole.WriteLine();

        while (true)
        {
            AnsiConsole.Markup($"[blue]File {files.Count + 1} (or empty to finish): [/]");
            var input = Console.ReadLine();
            
            if (string.IsNullOrWhiteSpace(input))
            {
                break;
            }

            // Resolve relative paths
            var filePath = Path.IsPathRooted(input) ? input : Path.Combine(currentDir, input);
            
            if (!File.Exists(filePath))
            {
                AnsiConsole.MarkupLine($"[red]File not found: {filePath}[/]");
                continue;
            }

            if (files.Contains(filePath))
            {
                AnsiConsole.MarkupLine($"[yellow]File already added: {filePath}[/]");
                continue;
            }

            files.Add(filePath);
            var fileInfo = new FileInfo(filePath);
            AnsiConsole.MarkupLine($"[green]✓ Added: {filePath} ({fileInfo.Length} bytes)[/]");
        }

        if (files.Count > 0)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.Write(new Rule("[green]Selected Files[/]").RuleStyle("green"));
            
            var table = new Table();
            table.Border = TableBorder.Rounded;
            table.AddColumn("File");
            table.AddColumn("Size");
            table.AddColumn("Type");

            foreach (var file in files)
            {
                var fileInfo = new FileInfo(file);
                var size = FormatFileSize(fileInfo.Length);
                var ext = Path.GetExtension(file).TrimStart('.');
                var type = string.IsNullOrEmpty(ext) ? "Unknown" : ext.ToUpperInvariant();
                
                table.AddRow(
                    file.EscapeMarkup(),
                    size,
                    type
                );
            }

            AnsiConsole.Write(table);
        }

        return files;
    }

    /// <summary>
    /// Shows a repository selection dialog
    /// </summary>
    public static Task<string?> SelectRepositoryAsync(IEnumerable<string> repositories, string title = "Select Repository")
    {
        var repoList = repositories.ToList();
        if (!repoList.Any())
        {
            AnsiConsole.MarkupLine("[red]No repositories available[/]");
            return Task.FromResult<string?>(null);
        }

        if (repoList.Count == 1)
        {
            var singleRepo = repoList.First();
            if (AnsiConsole.Confirm($"Use repository '{singleRepo}'?"))
            {
                return Task.FromResult<string?>(singleRepo);
            }
            return Task.FromResult<string?>(null);
        }

        var selection = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title(title)
                .PageSize(10)
                .MoreChoicesText("[grey](Move up and down to reveal more repositories)[/]")
                .AddChoices(repoList));

        return Task.FromResult<string?>(selection);
    }

    /// <summary>
    /// Shows a template assistance dialog for prompt editing
    /// </summary>
    public static string ShowTemplateAssistance(string prompt, List<string> fileNames)
    {
        if (!fileNames.Any())
        {
            return prompt;
        }

        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[yellow]Template Assistance[/]").RuleStyle("yellow"));
        
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[yellow]Available files for template references:[/]");
        
        var table = new Table();
        table.Border = TableBorder.Rounded;
        table.AddColumn("Template Reference");
        table.AddColumn("File");

        foreach (var fileName in fileNames)
        {
            var name = Path.GetFileName(fileName);
            table.AddRow($"{{{{ {name} }}}}", fileName.EscapeMarkup());
        }

        AnsiConsole.Write(table);
        
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[grey]You can reference these files in your prompt using {{{{ filename.ext }}}} syntax.[/]");
        
        if (AnsiConsole.Confirm("Would you like to edit your prompt to add file references?"))
        {
            return EditMultiLineText("Edit Prompt with Template References", prompt, 
                "Add template references like {{filename.ext}} to reference uploaded files");
        }

        return prompt;
    }

    /// <summary>
    /// Shows a configuration preview before job creation
    /// </summary>
    public static bool ShowJobConfigurationPreview(string repository, string prompt, List<string> files, Dictionary<string, object> options)
    {
        AnsiConsole.Clear();
        AnsiConsole.Write(new Rule("[green]Job Configuration Preview[/]").RuleStyle("green"));
        
        var layout = new Layout("Root")
            .SplitColumns(
                new Layout("Left"),
                new Layout("Right"));

        // Left panel - Basic info
        var basicInfo = new Panel(
            new Rows(
                new Markup($"[bold]Repository:[/] {repository}"),
                new Markup($"[bold]Files:[/] {files.Count} file(s)"),
                new Markup($"[bold]Auto-start:[/] {(options.GetValueOrDefault("autoStart", false))}"),
                new Markup($"[bold]Watch:[/] {(options.GetValueOrDefault("watch", false))}"),
                new Markup($"[bold]Timeout:[/] {options.GetValueOrDefault("timeout", 300)}s")
            ))
        {
            Header = new PanelHeader(" Configuration "),
            Border = BoxBorder.Rounded
        };

        layout["Left"].Update(basicInfo);

        // Right panel - Files
        if (files.Any())
        {
            var fileTable = new Table();
            fileTable.Border = TableBorder.Rounded;
            fileTable.AddColumn("File");
            fileTable.AddColumn("Size");

            foreach (var file in files)
            {
                var fileInfo = new FileInfo(file);
                var size = FormatFileSize(fileInfo.Length);
                var fileName = Path.GetFileName(file);
                fileTable.AddRow(fileName.EscapeMarkup(), size);
            }

            var filesPanel = new Panel(fileTable)
            {
                Header = new PanelHeader(" Files to Upload "),
                Border = BoxBorder.Rounded
            };
            
            layout["Right"].Update(filesPanel);
        }
        else
        {
            layout["Right"].Update(new Panel("No files selected")
            {
                Header = new PanelHeader(" Files to Upload "),
                Border = BoxBorder.Rounded
            });
        }

        AnsiConsole.Write(layout);

        // Show prompt
        AnsiConsole.WriteLine();
        var promptPanel = new Panel(prompt.EscapeMarkup())
        {
            Header = new PanelHeader(" Prompt "),
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.Blue)
        };
        AnsiConsole.Write(promptPanel);

        // Template resolution preview
        if (files.Any())
        {
            var resolvedPrompt = ResolveTemplatePreview(prompt, files);
            if (resolvedPrompt != prompt)
            {
                AnsiConsole.WriteLine();
                var resolvedPanel = new Panel(resolvedPrompt.EscapeMarkup())
                {
                    Header = new PanelHeader(" Resolved Prompt Preview "),
                    Border = BoxBorder.Rounded,
                    BorderStyle = new Style(Color.Green)
                };
                AnsiConsole.Write(resolvedPanel);
            }
        }

        AnsiConsole.WriteLine();
        return AnsiConsole.Confirm("[green]Create job with this configuration?[/]");
    }

    /// <summary>
    /// Shows progress during file uploads
    /// </summary>
    public static async Task<T> ShowProgress<T>(Func<IProgress<(string message, int percentage)>, Task<T>> operation, string title = "Processing...")
    {
        return await AnsiConsole.Progress()
            .Columns(new ProgressColumn[] 
            {
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new SpinnerColumn()
            })
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask(title);
                
                var progress = new Progress<(string message, int percentage)>(update =>
                {
                    task.Description = update.message;
                    task.Value = update.percentage;
                });

                return await operation(progress);
            });
    }

    private static string ResolveTemplatePreview(string prompt, List<string> files)
    {
        var result = prompt;
        foreach (var file in files)
        {
            var fileName = Path.GetFileName(file);
            var placeholder = $"{{{{{fileName}}}}}";
            if (result.Contains(placeholder))
            {
                result = result.Replace(placeholder, $"[SERVER_FILE_PATH_FOR_{fileName.ToUpperInvariant()}]");
            }
        }
        return result;
    }

    private static string FormatFileSize(long bytes)
    {
        string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
        int counter = 0;
        decimal number = bytes;
        while (Math.Round(number / 1024) >= 1)
        {
            number /= 1024;
            counter++;
        }
        return $"{number:n1} {suffixes[counter]}";
    }
}