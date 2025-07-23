using System.Text;

namespace ClaudeServerCLI.Services;

/// <summary>
/// Service for handling advanced prompt input methods
/// </summary>
public interface IPromptService
{
    Task<string> GetPromptAsync(string? inlinePrompt, bool interactive, CancellationToken cancellationToken = default);
    string ResolveTemplates(string prompt, Dictionary<string, string> templateMappings);
    List<string> ExtractTemplateReferences(string prompt);
    bool ValidatePrompt(string prompt, out string validationMessage);
}

public class PromptService : IPromptService
{
    public async Task<string> GetPromptAsync(string? inlinePrompt, bool interactive, CancellationToken cancellationToken = default)
    {
        // Method 1: Inline prompt provided
        if (!string.IsNullOrEmpty(inlinePrompt))
        {
            return inlinePrompt;
        }

        // Method 2: Check if stdin has content (piped input)
        if (IsStdinAvailable())
        {
            return await ReadFromStdinAsync(cancellationToken);
        }

        // Method 3: Interactive mode
        if (interactive)
        {
            return InteractivePromptEntry();
        }

        throw new InvalidOperationException("No prompt provided. Use --prompt, pipe from stdin, or use --interactive mode.");
    }

    public string ResolveTemplates(string prompt, Dictionary<string, string> templateMappings)
    {
        var result = prompt;
        
        foreach (var mapping in templateMappings)
        {
            var placeholder = $"{{{{{mapping.Key}}}}}";
            result = result.Replace(placeholder, mapping.Value);
        }

        return result;
    }

    public List<string> ExtractTemplateReferences(string prompt)
    {
        var templates = new List<string>();
        var pattern = @"\{\{([^}]+)\}\}";
        var regex = new System.Text.RegularExpressions.Regex(pattern);
        
        var matches = regex.Matches(prompt);
        foreach (System.Text.RegularExpressions.Match match in matches)
        {
            var templateName = match.Groups[1].Value.Trim();
            if (!templates.Contains(templateName))
            {
                templates.Add(templateName);
            }
        }

        return templates;
    }

    public bool ValidatePrompt(string prompt, out string validationMessage)
    {
        validationMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(prompt))
        {
            validationMessage = "Prompt cannot be empty";
            return false;
        }

        if (prompt.Length > 100000) // 100KB limit
        {
            validationMessage = "Prompt is too long (max 100KB)";
            return false;
        }

        // Check for unresolved template references
        var templateRefs = ExtractTemplateReferences(prompt);
        if (templateRefs.Count > 20)
        {
            validationMessage = "Too many template references (max 20)";
            return false;
        }

        return true;
    }

    private bool IsStdinAvailable()
    {
        try
        {
            // Check if stdin is redirected (piped)
            return !Console.IsInputRedirected || Console.In.Peek() != -1;
        }
        catch
        {
            return false;
        }
    }

    private async Task<string> ReadFromStdinAsync(CancellationToken cancellationToken = default)
    {
        var content = new StringBuilder();
        
        try
        {
            using var reader = Console.In;
            string? line;
            
            while ((line = await reader.ReadLineAsync()) != null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                content.AppendLine(line);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to read from stdin: {ex.Message}", ex);
        }

        var result = content.ToString().TrimEnd('\n', '\r');
        
        if (string.IsNullOrWhiteSpace(result))
        {
            throw new InvalidOperationException("No content received from stdin");
        }

        return result;
    }

    private string InteractivePromptEntry()
    {
        try
        {
            return UI.InteractiveUI.EditMultiLineText(
                "Interactive Prompt Editor",
                helpText: "Create your prompt for Claude Code. You can reference uploaded files using {{filename.ext}} syntax."
            );
        }
        catch (OperationCanceledException)
        {
            throw new InvalidOperationException("Interactive prompt entry was cancelled");
        }
    }
}