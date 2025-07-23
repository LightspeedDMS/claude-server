using Spectre.Console;
using System.Text;

namespace ClaudeServerCLI.UI;

/// <summary>
/// Interactive navigation components with keyboard shortcuts and search
/// </summary>
public static class InteractiveNavigation
{
    /// <summary>
    /// Interactive list selection with search and keyboard navigation
    /// </summary>
    public static T? SelectFromList<T>(
        List<T> items,
        Func<T, string> displaySelector,
        string title = "Select Item",
        string searchPlaceholder = "Type to search...",
        bool allowCancel = true) where T : class
    {
        if (!items.Any())
        {
            AnsiConsole.MarkupLine("[red]No items available[/]");
            return null;
        }

        var filteredItems = items.ToList();
        var selectedIndex = 0;
        var searchTerm = string.Empty;
        var searchMode = false;

        while (true)
        {
            Console.Clear();
            
            // Title
            AnsiConsole.MarkupLine($"[bold blue]{title}[/]");
            AnsiConsole.WriteLine();

            // Search bar
            if (searchMode)
            {
                AnsiConsole.MarkupLine($"[yellow]Search:[/] {searchTerm}[cyan]|[/]");
            }
            else
            {
                AnsiConsole.MarkupLine($"[grey]Press '/' to search, Enter to select, Esc to cancel[/]");
            }
            AnsiConsole.WriteLine();

            // Display items
            for (int i = 0; i < filteredItems.Count; i++)
            {
                var item = filteredItems[i];
                var display = displaySelector(item);
                
                if (i == selectedIndex)
                {
                    AnsiConsole.MarkupLine($"[bold green]► {display.EscapeMarkup()}[/]");
                }
                else
                {
                    AnsiConsole.MarkupLine($"  {display.EscapeMarkup()}");
                }
            }

            if (!filteredItems.Any())
            {
                AnsiConsole.MarkupLine("[yellow]No matches found[/]");
            }

            // Handle keyboard input
            var key = Console.ReadKey(true);
            
            switch (key.Key)
            {
                case ConsoleKey.UpArrow:
                    if (!searchMode && filteredItems.Any())
                    {
                        selectedIndex = selectedIndex > 0 ? selectedIndex - 1 : filteredItems.Count - 1;
                    }
                    break;
                    
                case ConsoleKey.DownArrow:
                    if (!searchMode && filteredItems.Any())
                    {
                        selectedIndex = selectedIndex < filteredItems.Count - 1 ? selectedIndex + 1 : 0;
                    }
                    break;
                    
                case ConsoleKey.Enter:
                    if (!searchMode && filteredItems.Any())
                    {
                        return filteredItems[selectedIndex];
                    }
                    else if (searchMode)
                    {
                        searchMode = false;
                        ApplySearch();
                    }
                    break;
                    
                case ConsoleKey.Escape:
                    if (searchMode)
                    {
                        searchMode = false;
                        searchTerm = string.Empty;
                        filteredItems = items.ToList();
                        selectedIndex = 0;
                    }
                    else if (allowCancel)
                    {
                        return null;
                    }
                    break;
                    
                case ConsoleKey.Oem2: // Forward slash
                    if (!searchMode)
                    {
                        searchMode = true;
                        searchTerm = string.Empty;
                    }
                    break;
                    
                case ConsoleKey.Backspace:
                    if (searchMode && searchTerm.Length > 0)
                    {
                        searchTerm = searchTerm[..^1];
                        ApplySearch();
                    }
                    break;
                    
                case ConsoleKey.F1:
                    ShowHelp();
                    break;
                    
                default:
                    if (searchMode && !char.IsControl(key.KeyChar))
                    {
                        searchTerm += key.KeyChar;
                        ApplySearch();
                    }
                    break;
            }
        }

        void ApplySearch()
        {
            if (string.IsNullOrEmpty(searchTerm))
            {
                filteredItems = items.ToList();
            }
            else
            {
                filteredItems = items
                    .Where(item => displaySelector(item)
                        .Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }
            selectedIndex = 0;
        }

        void ShowHelp()
        {
            Console.Clear();
            AnsiConsole.MarkupLine("[bold blue]Navigation Help[/]");
            AnsiConsole.WriteLine();
            
            var helpTable = new Table();
            helpTable.Border = TableBorder.None;
            helpTable.AddColumn("Key");
            helpTable.AddColumn("Action");
            
            helpTable.AddRow("[yellow]↑/↓[/]", "Navigate items");
            helpTable.AddRow("[yellow]Enter[/]", "Select item");
            helpTable.AddRow("[yellow]/[/]", "Start search");
            helpTable.AddRow("[yellow]Esc[/]", "Cancel search or exit");
            helpTable.AddRow("[yellow]F1[/]", "Show this help");
            
            AnsiConsole.Write(helpTable);
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[grey]Press any key to continue...[/]");
            Console.ReadKey(true);
        }
    }

    /// <summary>
    /// Real-time search with live filtering
    /// </summary>
    public static List<T> LiveSearch<T>(
        List<T> items,
        Func<T, string> searchSelector,
        string prompt = "Search")
    {
        var searchTerm = string.Empty;
        var results = items.ToList();

        AnsiConsole.MarkupLine($"[blue]{prompt}[/] (type to filter, Enter when done):");
        AnsiConsole.WriteLine();

        while (true)
        {
            // Display current search and results count
            Console.SetCursorPosition(0, Console.CursorTop - 1);
            AnsiConsole.MarkupLine($"[yellow]>[/] {searchTerm}[cyan]|[/] [grey]({results.Count} matches)[/]");
            
            var key = Console.ReadKey(true);
            
            switch (key.Key)
            {
                case ConsoleKey.Enter:
                    return results;
                    
                case ConsoleKey.Escape:
                    return items; // Return original list
                    
                case ConsoleKey.Backspace:
                    if (searchTerm.Length > 0)
                    {
                        searchTerm = searchTerm[..^1];
                        UpdateResults();
                    }
                    break;
                    
                default:
                    if (!char.IsControl(key.KeyChar))
                    {
                        searchTerm += key.KeyChar;
                        UpdateResults();
                    }
                    break;
            }
        }

        void UpdateResults()
        {
            if (string.IsNullOrEmpty(searchTerm))
            {
                results = items.ToList();
            }
            else
            {
                results = items
                    .Where(item => searchSelector(item)
                        .Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }
        }
    }

    /// <summary>
    /// Multi-select interface with checkboxes
    /// </summary>
    public static List<T> MultiSelect<T>(
        List<T> items,
        Func<T, string> displaySelector,
        string title = "Select Items",
        List<T>? preSelected = null) where T : class
    {
        var selected = new HashSet<T>(preSelected ?? new List<T>());
        var currentIndex = 0;

        while (true)
        {
            Console.Clear();
            
            AnsiConsole.MarkupLine($"[bold blue]{title}[/]");
            AnsiConsole.MarkupLine("[grey]Space to toggle, Enter to confirm, Esc to cancel[/]");
            AnsiConsole.WriteLine();

            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                var display = displaySelector(item);
                var checkbox = selected.Contains(item) ? "[green]☑[/]" : "[grey]☐[/]";
                var cursor = i == currentIndex ? "►" : " ";
                
                AnsiConsole.MarkupLine($"{cursor} {checkbox} {display.EscapeMarkup()}");
            }

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"[yellow]Selected: {selected.Count}/{items.Count}[/]");

            var key = Console.ReadKey(true);
            
            switch (key.Key)
            {
                case ConsoleKey.UpArrow:
                    currentIndex = currentIndex > 0 ? currentIndex - 1 : items.Count - 1;
                    break;
                    
                case ConsoleKey.DownArrow:
                    currentIndex = currentIndex < items.Count - 1 ? currentIndex + 1 : 0;
                    break;
                    
                case ConsoleKey.Spacebar:
                    var currentItem = items[currentIndex];
                    if (selected.Contains(currentItem))
                        selected.Remove(currentItem);
                    else
                        selected.Add(currentItem);
                    break;
                    
                case ConsoleKey.Enter:
                    return selected.ToList();
                    
                case ConsoleKey.Escape:
                    return preSelected?.ToList() ?? new List<T>();
                    
                case ConsoleKey.A when key.Modifiers.HasFlag(ConsoleModifiers.Control):
                    // Ctrl+A - Select all
                    foreach (var item in items)
                        selected.Add(item);
                    break;
                    
                case ConsoleKey.U when key.Modifiers.HasFlag(ConsoleModifiers.Control):
                    // Ctrl+U - Unselect all
                    selected.Clear();
                    break;
            }
        }
    }

    /// <summary>
    /// Confirmation dialog with customizable options
    /// </summary>
    public static bool ConfirmAction(string message, bool defaultYes = false, string? warningText = null)
    {
        Console.Clear();
        
        if (!string.IsNullOrEmpty(warningText))
        {
            var warningPanel = new Panel(warningText.EscapeMarkup())
            {
                Border = BoxBorder.None,
                BorderStyle = new Style(Color.Red),
                Padding = new Padding(1, 0, 1, 0)
            };
            AnsiConsole.Write(warningPanel);
            AnsiConsole.WriteLine();
        }

        AnsiConsole.MarkupLine($"[yellow]{message.EscapeMarkup()}[/]");
        AnsiConsole.WriteLine();

        var yesOption = defaultYes ? "[bold green]Yes[/]" : "Yes";
        var noOption = defaultYes ? "No" : "[bold red]No[/]";
        
        AnsiConsole.MarkupLine($"Press [yellow]Y[/] for {yesOption} or [yellow]N[/] for {noOption}");
        if (defaultYes)
            AnsiConsole.MarkupLine("[grey]Press Enter for default (Yes)[/]");
        else
            AnsiConsole.MarkupLine("[grey]Press Enter for default (No)[/]");

        while (true)
        {
            var key = Console.ReadKey(true);
            
            switch (key.Key)
            {
                case ConsoleKey.Y:
                    return true;
                    
                case ConsoleKey.N:
                    return false;
                    
                case ConsoleKey.Enter:
                    return defaultYes;
                    
                case ConsoleKey.Escape:
                    return false;
            }
        }
    }

    /// <summary>
    /// Progress display with real-time updates
    /// </summary>
    public static async Task ShowLiveProgress<T>(
        Func<IProgress<(string message, int percentage)>, CancellationToken, Task<T>> operation,
        string title = "Processing...",
        CancellationToken cancellationToken = default)
    {
        var currentMessage = title;
        var currentPercentage = 0;
        
        var progress = new Progress<(string message, int percentage)>(update =>
        {
            currentMessage = update.message;
            currentPercentage = update.percentage;
        });

        var operationTask = operation(progress, cancellationToken);
        
        // Display progress until operation completes
        while (!operationTask.IsCompleted)
        {
            Console.SetCursorPosition(0, Console.CursorTop);
            ModernDisplay.DisplayProgress(currentMessage, currentPercentage);
            
            try
            {
                await Task.Delay(100, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        // Show final result
        if (operationTask.IsCompletedSuccessfully)
        {
            ModernDisplay.DisplayProgress(currentMessage, 100, "Complete");
        }
        else if (operationTask.IsCanceled)
        {
            ModernDisplay.DisplayProgress("Cancelled", 0);
        }
        else if (operationTask.IsFaulted)
        {
            ModernDisplay.DisplayProgress("Failed", 0, operationTask.Exception?.GetBaseException().Message);
        }
        
        await operationTask;
    }
}