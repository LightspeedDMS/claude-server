using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;
using Xunit.Abstractions;

namespace ClaudeServerCLI.IntegrationTests;

public class Phase34IntegrationTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _cliPath;
    
    public Phase34IntegrationTests(ITestOutputHelper output)
    {
        _output = output;
        
        // Find the CLI executable path
        var currentDir = Directory.GetCurrentDirectory();
        var projectRoot = FindProjectRoot(currentDir);
        _cliPath = Path.Combine(projectRoot, "src", "ClaudeServerCLI", "bin", "Debug", "net8.0", "claude-server.dll");
        
        if (!File.Exists(_cliPath))
        {
            throw new InvalidOperationException($"CLI executable not found at: {_cliPath}. Please build the project first.");
        }
    }
    
    private string FindProjectRoot(string currentDir)
    {
        var dir = new DirectoryInfo(currentDir);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "ClaudeBatchServer.sln")))
        {
            dir = dir.Parent;
        }
        
        if (dir == null)
        {
            throw new InvalidOperationException("Could not find project root directory");
        }
        
        return dir.FullName;
    }
    
    [Fact]
    public async Task CLI_ShowsHelp_WhenHelpFlagProvided()
    {
        // Arrange & Act
        var result = await RunCliCommandAsync("--help");
        
        // Assert
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Claude Batch Server CLI", result.Output);
        Assert.Contains("Authentication commands", result.Output);
        Assert.Contains("Repository management commands", result.Output);
        Assert.Contains("Job management commands", result.Output);
    }
    
    [Fact]
    public async Task ReposCommand_ShowsHelp_WhenHelpFlagProvided()
    {
        // Arrange & Act
        var result = await RunCliCommandAsync("repos --help");
        
        // Assert
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Repository management commands", result.Output);
        Assert.Contains("list", result.Output);
        Assert.Contains("create", result.Output);
        Assert.Contains("show", result.Output);
        Assert.Contains("delete", result.Output);
    }
    
    [Fact]
    public async Task JobsCommand_ShowsHelp_WhenHelpFlagProvided()
    {
        // Arrange & Act
        var result = await RunCliCommandAsync("jobs --help");
        
        // Assert
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Job management commands", result.Output);
        Assert.Contains("list", result.Output);
        Assert.Contains("create", result.Output);
        Assert.Contains("show", result.Output);
        Assert.Contains("start", result.Output);
        Assert.Contains("cancel", result.Output);
        Assert.Contains("delete", result.Output);
        Assert.Contains("logs", result.Output);
    }
    
    [Fact]
    public async Task ReposList_RequiresAuthentication_WhenNotLoggedIn()
    {
        // Arrange & Act
        var result = await RunCliCommandAsync("repos list");
        
        // Assert
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Not authenticated", result.Output);
        Assert.Contains("claude-server login", result.Output);
    }
    
    [Fact]
    public async Task JobsList_RequiresAuthentication_WhenNotLoggedIn()
    {
        // Arrange & Act
        var result = await RunCliCommandAsync("jobs list");
        
        // Assert
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Not authenticated", result.Output);
        Assert.Contains("claude-server login", result.Output);
    }
    
    [Fact]
    public async Task ReposListCommand_ShowsCorrectHelp()
    {
        // Arrange & Act
        var result = await RunCliCommandAsync("repos list --help");
        
        // Assert
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("List all repositories", result.Output);
        Assert.Contains("--format", result.Output);
        Assert.Contains("table, json, yaml", result.Output);
        Assert.Contains("--watch", result.Output);
        Assert.Contains("real-time", result.Output);
    }
    
    [Fact]
    public async Task JobsCreateCommand_ShowsCorrectHelp()
    {
        // Arrange & Act
        var result = await RunCliCommandAsync("jobs create --help");
        
        // Assert
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Create a new job", result.Output);
        Assert.Contains("--repo", result.Output);
        Assert.Contains("--prompt", result.Output);
        Assert.Contains("--auto-start", result.Output);
        Assert.Contains("--watch", result.Output);
        Assert.Contains("--job-timeout", result.Output);
    }
    
    [Fact]
    public async Task JobsCreateCommand_RequiresMandatoryOptions()
    {
        // Arrange & Act
        var result = await RunCliCommandAsync("jobs create");
        
        // Assert
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("required", result.Output.ToLowerInvariant());
    }
    
    [Fact]
    public async Task ReposCreateCommand_ShowsCorrectHelp()
    {
        // Arrange & Act
        var result = await RunCliCommandAsync("repos create --help");
        
        // Assert
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Register a new repository", result.Output);
        Assert.Contains("--name", result.Output);
        Assert.Contains("--clone", result.Output);
        Assert.Contains("--path", result.Output);
        Assert.Contains("--description", result.Output);
        Assert.Contains("--watch", result.Output);
    }
    
    [Fact]
    public async Task JobsLogsCommand_ShowsCorrectHelp()
    {
        // Arrange & Act
        var result = await RunCliCommandAsync("jobs logs --help");
        
        // Assert
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("View job execution logs", result.Output);
        Assert.Contains("<jobId>", result.Output);
        Assert.Contains("--follow", result.Output);
        Assert.Contains("--watch", result.Output);
        Assert.Contains("--tail", result.Output);
        Assert.Contains("real-time", result.Output);
    }
    
    [Theory]
    [InlineData("repos show")]
    [InlineData("repos delete")]
    [InlineData("jobs show")]
    [InlineData("jobs start")]
    [InlineData("jobs cancel")]
    [InlineData("jobs delete")]
    [InlineData("jobs logs")]
    public async Task CommandsWithArguments_ShowHelp_WhenNoArgumentProvided(string command)
    {
        // Arrange & Act
        var result = await RunCliCommandAsync(command);
        
        // Assert
        // Commands should either show help or indicate missing argument, but not crash
        Assert.True(result.ExitCode == 0 || result.ExitCode == 1);
        Assert.False(string.IsNullOrWhiteSpace(result.Output));
    }
    
    private async Task<CliResult> RunCliCommandAsync(string arguments, int timeoutMs = 10000)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"\"{_cliPath}\" {arguments}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process();
        process.StartInfo = startInfo;

        var outputBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();

        process.OutputDataReceived += (sender, e) =>
        {
            if (e.Data != null)
            {
                outputBuilder.AppendLine(e.Data);
            }
        };

        process.ErrorDataReceived += (sender, e) =>
        {
            if (e.Data != null)
            {
                errorBuilder.AppendLine(e.Data);
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var cts = new CancellationTokenSource(timeoutMs);
        
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill();
            }
            catch
            {
                // Ignore kill errors
            }
            
            throw new TimeoutException($"CLI command timed out after {timeoutMs}ms: {arguments}");
        }

        var output = outputBuilder.ToString();
        var error = errorBuilder.ToString();
        var combinedOutput = string.IsNullOrWhiteSpace(error) ? output : $"{output}\n{error}";

        _output.WriteLine($"Command: dotnet {startInfo.Arguments}");
        _output.WriteLine($"Exit Code: {process.ExitCode}");
        _output.WriteLine($"Output: {combinedOutput}");

        return new CliResult(process.ExitCode, combinedOutput);
    }
    
    private record CliResult(int ExitCode, string Output);
    
    public void Dispose()
    {
        // Cleanup if needed
    }
}

public class Phase34EndToEndTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    
    public Phase34EndToEndTests(ITestOutputHelper output)
    {
        _output = output;
    }
    
    [Fact]
    public void CLI_StructuralIntegrity_IsValid()
    {
        // This test verifies that all the major components are properly wired together
        
        // Verify that the command structure is correctly implemented
        var authCommand = new Commands.AuthCommand();
        var reposCommand = new Commands.ReposCommand();
        var jobsCommand = new Commands.JobsCommand();
        
        Assert.NotNull(authCommand);
        Assert.NotNull(reposCommand);
        Assert.NotNull(jobsCommand);
        
        // Verify subcommands exist
        Assert.True(authCommand.Subcommands.Any(c => c.Name == "login"));
        Assert.True(authCommand.Subcommands.Any(c => c.Name == "logout"));
        Assert.True(authCommand.Subcommands.Any(c => c.Name == "whoami"));
        
        Assert.True(reposCommand.Subcommands.Any(c => c.Name == "list"));
        Assert.True(reposCommand.Subcommands.Any(c => c.Name == "create"));
        Assert.True(reposCommand.Subcommands.Any(c => c.Name == "show"));
        Assert.True(reposCommand.Subcommands.Any(c => c.Name == "delete"));
        
        Assert.True(jobsCommand.Subcommands.Any(c => c.Name == "list"));
        Assert.True(jobsCommand.Subcommands.Any(c => c.Name == "create"));
        Assert.True(jobsCommand.Subcommands.Any(c => c.Name == "show"));
        Assert.True(jobsCommand.Subcommands.Any(c => c.Name == "start"));
        Assert.True(jobsCommand.Subcommands.Any(c => c.Name == "cancel"));
        Assert.True(jobsCommand.Subcommands.Any(c => c.Name == "delete"));
        Assert.True(jobsCommand.Subcommands.Any(c => c.Name == "logs"));
    }
    
    public void Dispose()
    {
        // Cleanup if needed
    }
}