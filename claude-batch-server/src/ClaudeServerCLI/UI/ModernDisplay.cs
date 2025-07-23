using Spectre.Console;
using ClaudeServerCLI.Models;
using ClaudeBatchServer.Core.DTOs;

namespace ClaudeServerCLI.UI;

/// <summary>
/// Modern, clean UI display components without box characters
/// </summary>
public static class ModernDisplay
{
    /// <summary>
    /// Display jobs in clean table format
    /// </summary>
    public static void DisplayJobs(IEnumerable<JobInfo> jobs, string format = "table")
    {
        var jobList = jobs.ToList();
        
        switch (format.ToLowerInvariant())
        {
            case "json":
                var json = System.Text.Json.JsonSerializer.Serialize(jobList, new System.Text.Json.JsonSerializerOptions 
                { 
                    WriteIndented = true,
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
                });
                Console.WriteLine(json);
                break;
                
            case "yaml":
                var serializer = new YamlDotNet.Serialization.SerializerBuilder().Build();
                var yaml = serializer.Serialize(jobList);
                Console.WriteLine(yaml);
                break;
                
            case "table":
            default:
                if (!jobList.Any())
                {
                    AnsiConsole.MarkupLine("[yellow]No jobs found.[/]");
                    AnsiConsole.MarkupLine("[grey]Use 'claude-server jobs create' to create a job.[/]");
                    return;
                }

                // Clean table without borders
                var table = new Table();
                table.Border = TableBorder.None;
                table.ShowHeaders = true;
                
                table.AddColumn(new TableColumn("[bold]ID[/]").PadRight(8));
                table.AddColumn(new TableColumn("[bold]Repository[/]").PadRight(20));
                table.AddColumn(new TableColumn("[bold]Status[/]").PadRight(15));
                table.AddColumn(new TableColumn("[bold]Duration[/]").PadRight(12));
                table.AddColumn(new TableColumn("[bold]User[/]").PadRight(15));
                table.AddColumn(new TableColumn("[bold]Created[/]").PadRight(12));

                foreach (var job in jobList)
                {
                    var shortId = job.JobId.ToString()[..8];
                    var status = GetJobStatusDisplay(job.Status);
                    var duration = GetJobDurationDisplay(job);
                    var created = job.CreatedAt.ToString("MM-dd HH:mm");
                    var user = job.User.Length > 12 ? job.User[..12] + "..." : job.User;
                    var repository = job.Repository.Length > 18 ? job.Repository[..18] + "..." : job.Repository;

                    table.AddRow(
                        $"[grey]{shortId}[/]",
                        repository.EscapeMarkup(),
                        status,
                        duration,
                        $"[grey]{user.EscapeMarkup()}[/]",
                        $"[grey]{created}[/]"
                    );
                }

                AnsiConsole.Write(table);
                break;
        }
    }

    /// <summary>
    /// Display single job details in clean format
    /// </summary>
    public static void DisplayJobDetails(JobStatusResponse job, string format = "table")
    {
        switch (format.ToLowerInvariant())
        {
            case "json":
                var json = System.Text.Json.JsonSerializer.Serialize(job, new System.Text.Json.JsonSerializerOptions 
                { 
                    WriteIndented = true,
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
                });
                Console.WriteLine(json);
                break;
                
            case "yaml":
                var serializer = new YamlDotNet.Serialization.SerializerBuilder().Build();
                var yaml = serializer.Serialize(job);
                Console.WriteLine(yaml);
                break;
                
            case "table":
            default:
                // Clean header
                AnsiConsole.MarkupLine($"[bold blue]Job Details: {job.JobId.ToString()[..8]}[/]");
                AnsiConsole.WriteLine();

                // Clean key-value display without borders
                var details = new List<(string key, string value)>
                {
                    ("ID", job.JobId.ToString()[..8]),
                    ("Status", GetJobStatusDisplay(job.Status)),
                    ("Repository", job.CowPath ?? "N/A")
                };

                if (job.QueuePosition > 0)
                    details.Add(("Queue Position", job.QueuePosition.ToString()));
                
                details.Add(("Created", job.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss")));
                
                if (job.StartedAt.HasValue)
                {
                    details.Add(("Started", job.StartedAt.Value.ToString("yyyy-MM-dd HH:mm:ss")));
                    var duration = (job.CompletedAt ?? DateTime.UtcNow) - job.StartedAt.Value;
                    details.Add(("Duration", FormatDuration(duration)));
                }
                
                if (job.CompletedAt.HasValue)
                    details.Add(("Completed", job.CompletedAt.Value.ToString("yyyy-MM-dd HH:mm:ss")));
                
                if (job.ExitCode.HasValue)
                    details.Add(("Exit Code", job.ExitCode.Value.ToString()));
                
                details.Add(("Git Status", GetStatusDisplay(job.GitStatus)));
                details.Add(("Cidx Status", GetStatusDisplay(job.CidxStatus)));

                // Display details in clean format
                foreach (var (key, value) in details)
                {
                    AnsiConsole.MarkupLine($"[grey]{key.PadRight(15)}:[/] {value}");
                }

                // Output section
                if (!string.IsNullOrEmpty(job.Output))
                {
                    AnsiConsole.WriteLine();
                    AnsiConsole.MarkupLine("[bold]Output:[/]");
                    AnsiConsole.WriteLine();
                    
                    // Truncate output if too long for display
                    var output = job.Output.Length > 1000 
                        ? job.Output[..1000] + "\n\n[... output truncated ...]"
                        : job.Output;
                    
                    // Display output with subtle styling
                    var outputPanel = new Panel(output.EscapeMarkup())
                    {
                        Border = BoxBorder.None,
                        Padding = new Padding(1, 0, 1, 0)
                    };
                    AnsiConsole.Write(outputPanel);
                }
                break;
        }
    }

    /// <summary>
    /// Display repositories in clean format
    /// </summary>
    public static void DisplayRepositories(IEnumerable<RepositoryInfo> repositories, string format = "table")
    {
        var repoList = repositories.ToList();
        
        switch (format.ToLowerInvariant())
        {
            case "json":
                var json = System.Text.Json.JsonSerializer.Serialize(repoList, new System.Text.Json.JsonSerializerOptions 
                { 
                    WriteIndented = true,
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
                });
                Console.WriteLine(json);
                break;
                
            case "yaml":
                var serializer = new YamlDotNet.Serialization.SerializerBuilder().Build();
                var yaml = serializer.Serialize(repoList);
                Console.WriteLine(yaml);
                break;
                
            case "table":
            default:
                if (!repoList.Any())
                {
                    AnsiConsole.MarkupLine("[yellow]No repositories found.[/]");
                    AnsiConsole.MarkupLine("[grey]Use 'claude-server repos create' to add a repository.[/]");
                    return;
                }

                var table = new Table();
                table.Border = TableBorder.None;
                table.ShowHeaders = true;
                
                table.AddColumn(new TableColumn("[bold]Name[/]").PadRight(20));
                table.AddColumn(new TableColumn("[bold]Type[/]").PadRight(8));
                table.AddColumn(new TableColumn("[bold]Size[/]").PadRight(10));
                table.AddColumn(new TableColumn("[bold]Branch[/]").PadRight(15));
                table.AddColumn(new TableColumn("[bold]Last Modified[/]").PadRight(15));

                foreach (var repo in repoList)
                {
                    var name = repo.Name.Length > 18 ? repo.Name[..18] + "..." : repo.Name;
                    var type = repo.Type;
                    var size = FormatFileSize(repo.Size);
                    var branch = repo.CurrentBranch ?? "unknown";
                    var modified = repo.LastModified.ToString("MM-dd HH:mm");

                    table.AddRow(
                        name.EscapeMarkup(),
                        type.EscapeMarkup(),
                        size,
                        branch.EscapeMarkup(),
                        $"[grey]{modified}[/]"
                    );
                }

                AnsiConsole.Write(table);
                break;
        }
    }

    /// <summary>
    /// Display progress with clean visual indicators
    /// </summary>
    public static void DisplayProgress(string operation, int percentage, string? detail = null)
    {
        var progressBar = CreateProgressBar(percentage);
        var statusIcon = GetProgressIcon(percentage);
        
        AnsiConsole.MarkupLine($"{statusIcon} [blue]{operation}[/] {progressBar} {percentage}%");
        
        if (!string.IsNullOrEmpty(detail))
        {
            AnsiConsole.MarkupLine($"   [grey]{detail}[/]");
        }
    }

    /// <summary>
    /// Display a clean status summary
    /// </summary>
    public static void DisplayStatusSummary(Dictionary<string, object> stats)
    {
        AnsiConsole.MarkupLine("[bold]Status Summary[/]");
        AnsiConsole.WriteLine();

        foreach (var (key, value) in stats)
        {
            var displayValue = value switch
            {
                bool b => b ? "[green]✓[/]" : "[red]✗[/]",
                int i => i.ToString(),
                string s => s.EscapeMarkup(),
                _ => value.ToString()?.EscapeMarkup() ?? "N/A"
            };

            AnsiConsole.MarkupLine($"[grey]{key.PadRight(20)}:[/] {displayValue}");
        }
    }

    private static string GetJobStatusDisplay(string status)
    {
        return status.ToLowerInvariant() switch
        {
            "pending" => "[yellow]⏸ Pending[/]",
            "running" => "[green]⚡ Running[/]",
            "completed" => "[green]✓ Complete[/]",
            "failed" => "[red]✗ Failed[/]",
            "cancelled" => "[grey]⏹ Cancelled[/]",
            _ => "[grey]? Unknown[/]"
        };
    }

    private static string GetStatusDisplay(string status)
    {
        return status.ToLowerInvariant() switch
        {
            "ready" => "[green]✓ Ready[/]",
            "not_checked" => "[grey]? Not Checked[/]",
            "not_started" => "[grey]⏸ Not Started[/]",
            "indexing" => "[yellow]⚡ Indexing[/]",
            "failed" => "[red]✗ Failed[/]",
            _ => $"[grey]{status}[/]"
        };
    }

    private static string GetJobDurationDisplay(JobInfo job)
    {
        if (job.StartedAt == null) return "-";
        
        var endTime = job.CompletedAt ?? DateTime.UtcNow;
        var duration = endTime - job.StartedAt.Value;
        
        return FormatDuration(duration);
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalDays >= 1)
            return $"{duration.Days}d {duration.Hours}h";
        else if (duration.TotalHours >= 1)
            return $"{duration.Hours}h {duration.Minutes}m";
        else if (duration.TotalMinutes >= 1)
            return $"{duration.Minutes}m {duration.Seconds}s";
        else
            return $"{duration.TotalSeconds:0}s";
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

    private static string CreateProgressBar(int percentage)
    {
        const int barWidth = 20;
        var filled = (int)Math.Round(barWidth * percentage / 100.0);
        var bar = new string('█', filled) + new string('░', barWidth - filled);
        return $"[blue]{bar}[/]";
    }

    private static string GetProgressIcon(int percentage)
    {
        return percentage switch
        {
            100 => "[green]✓[/]",
            _ when percentage > 0 => "[yellow]⚡[/]",
            _ => "[grey]⏸[/]"
        };
    }
}