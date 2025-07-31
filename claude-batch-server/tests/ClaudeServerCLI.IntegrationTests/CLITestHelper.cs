using System.Diagnostics;
using System.Text;
using ClaudeServerCLI.Services;
using ClaudeServerCLI.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

namespace ClaudeServerCLI.IntegrationTests;

/// <summary>
/// Helper class for executing CLI commands in integration tests with proper server integration
/// </summary>
public class CLITestHelper
{
    private readonly TestServerHarness _serverHarness;
    private readonly string _cliPath;
    private readonly int _defaultTimeoutMs;
    private readonly string? _persistentConfigPath;

    public CLITestHelper(TestServerHarness serverHarness, int defaultTimeoutMs = 30000, string? persistentConfigPath = null)
    {
        _serverHarness = serverHarness;
        _defaultTimeoutMs = defaultTimeoutMs;
        _persistentConfigPath = persistentConfigPath;
        
        // Find the CLI executable path
        var projectRoot = FindProjectRoot();
        _cliPath = Path.Combine(projectRoot, "src", "ClaudeServerCLI", "bin", "Debug", "net8.0", "claude-server.dll");
        
        if (!File.Exists(_cliPath))
        {
            throw new InvalidOperationException($"CLI executable not found at: {_cliPath}. Please build the project first.");
        }
    }

    /// <summary>
    /// Execute a CLI command and return the result
    /// </summary>
    public async Task<CliExecutionResult> ExecuteCommandAsync(string arguments, int timeoutMs = 0)
    {
        var actualTimeout = timeoutMs > 0 ? timeoutMs : _defaultTimeoutMs;
        
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"\"{_cliPath}\" --server-url \"{_serverHarness.ServerUrl}\" {arguments}",
            WorkingDirectory = Path.GetTempPath(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // Set environment variables for test
        startInfo.EnvironmentVariables["DOTNET_ENVIRONMENT"] = "Testing";
        startInfo.EnvironmentVariables["ASPNETCORE_ENVIRONMENT"] = "Testing";
        startInfo.EnvironmentVariables["CLAUDE_SERVER_SKIP_SSL_VALIDATION"] = "true";
        
        // Use persistent config path if provided, otherwise create isolated path
        string testConfigPath;
        if (_persistentConfigPath != null)
        {
            testConfigPath = _persistentConfigPath;
        }
        else
        {
            // Isolate config to prevent profile pollution between tests
            testConfigPath = Path.Combine(Path.GetTempPath(), $"cli-test-config-{Guid.NewGuid():N}");
            Directory.CreateDirectory(testConfigPath);
        }
        
        startInfo.EnvironmentVariables["HOME"] = testConfigPath;
        startInfo.EnvironmentVariables["USERPROFILE"] = testConfigPath;

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

        using var cts = new CancellationTokenSource(actualTimeout);
        
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
            
            throw new TimeoutException($"CLI command timed out after {actualTimeout}ms: {arguments}");
        }

        return new CliExecutionResult
        {
            ExitCode = process.ExitCode,
            StandardOutput = outputBuilder.ToString().Trim(),
            StandardError = errorBuilder.ToString().Trim(),
            Success = process.ExitCode == 0,
            Arguments = arguments
        };
    }

    /// <summary>
    /// Login with test credentials and return success status
    /// </summary>
    public async Task<bool> LoginAsync()
    {
        // Use --quiet flag to suppress ANSI output that contaminates JSON in tests
        var result = await ExecuteCommandAsync($"auth login --username {_serverHarness.TestUser} --password {_serverHarness.TestPassword} --quiet");
        return result.Success;
    }

    /// <summary>
    /// Logout and return success status
    /// </summary>
    public async Task<bool> LogoutAsync()
    {
        // Use --quiet flag to suppress ANSI output that contaminates JSON in tests
        var result = await ExecuteCommandAsync("auth logout --quiet");
        return result.Success;
    }

    /// <summary>
    /// Create a configured API client for programmatic testing
    /// </summary>
    public IApiClient CreateApiClient()
    {
        var services = new ServiceCollection();
        
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ApiClient:BaseUrl"] = _serverHarness.ServerUrl,
                ["ApiClient:TimeoutSeconds"] = "30",
                ["ApiClient:RetryCount"] = "3",
                ["ApiClient:RetryDelayMs"] = "1000",
                ["Authentication:ConfigPath"] = Path.GetTempFileName() + ".json"
            })
            .Build();

        services.ConfigureServices(configuration);
        
        // Override the HttpClient configuration for testing to bypass SSL validation
        services.AddHttpClient<IApiClient, ClaudeServerCLI.Services.ApiClient>()
            .ConfigurePrimaryHttpMessageHandler(() =>
            {
                return new HttpClientHandler
                {
                    ClientCertificateOptions = ClientCertificateOption.Manual,
                    ServerCertificateCustomValidationCallback = (httpRequestMessage, cert, cetChain, policyErrors) => true
                };
            });
        
        var serviceProvider = services.BuildServiceProvider();
        return serviceProvider.GetRequiredService<IApiClient>();
    }

    /// <summary>
    /// Create test files in a temporary directory for testing file uploads
    /// </summary>
    public async Task<Dictionary<string, string>> CreateTestFilesAsync(string? baseDirectory = null)
    {
        var testDir = baseDirectory ?? Path.Combine(Path.GetTempPath(), $"cli-test-files-{Guid.NewGuid():N}");
        Directory.CreateDirectory(testDir);
        
        var files = new Dictionary<string, string>();
        
        var testFiles = new Dictionary<string, string>
        {
            {"test.txt", "This is a test text file for CLI integration testing"},
            {"config.json", "{\"setting\": \"value\", \"enabled\": true, \"items\": [1, 2, 3]}"},
            {"script.py", "#!/usr/bin/env python3\nprint('Hello from Python CLI test')\nprint('Testing file upload functionality')"},
            {"readme.md", "# Test Project\n\nThis is a test markdown file for CLI integration testing.\n\n## Features\n- File upload\n- Template processing"},
            {"data.yaml", "database:\n  host: localhost\n  port: 5432\n  name: testdb\nlogging:\n  level: info"},
            {"sample.csv", "name,age,city,occupation\nAlice,30,New York,Engineer\nBob,25,San Francisco,Designer\nCharlie,35,Chicago,Manager"}
        };

        foreach (var (fileName, content) in testFiles)
        {
            var filePath = Path.Combine(testDir, fileName);
            await File.WriteAllTextAsync(filePath, content);
            files[filePath] = content;
        }

        return files;
    }

    /// <summary>
    /// Create a temporary Git repository for testing repository operations
    /// </summary>
    public async Task<string> CreateTestGitRepositoryAsync()
    {
        var repoPath = Path.Combine(Path.GetTempPath(), $"cli-test-repo-{Guid.NewGuid():N}");
        Directory.CreateDirectory(repoPath);
        
        // Initialize git repository
        await RunGitCommandAsync(repoPath, "init");
        await RunGitCommandAsync(repoPath, "config user.name \"Test User\"");
        await RunGitCommandAsync(repoPath, "config user.email \"test@example.com\"");
        
        // Create a test file and commit
        var testFile = Path.Combine(repoPath, "README.md");
        await File.WriteAllTextAsync(testFile, "# Test Repository\n\nThis is a test repository for CLI integration testing.");
        
        await RunGitCommandAsync(repoPath, "add README.md");
        await RunGitCommandAsync(repoPath, "commit -m \"Initial commit\"");
        
        return repoPath;
    }

    /// <summary>
    /// Cleanup temporary directories and files
    /// </summary>
    public static void CleanupDirectory(string directory)
    {
        if (Directory.Exists(directory))
        {
            try
            {
                Directory.Delete(directory, true);
            }
            catch
            {
                // Best effort cleanup - ignore errors
            }
        }
    }

    private async Task RunGitCommandAsync(string workingDirectory, string arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo);
        if (process != null)
        {
            await process.WaitForExitAsync();
            if (process.ExitCode != 0)
            {
                var error = await process.StandardError.ReadToEndAsync();
                throw new InvalidOperationException($"Git command failed: {arguments}\nError: {error}");
            }
        }
    }

    private string FindProjectRoot()
    {
        // First check environment variable
        var envProjectRoot = Environment.GetEnvironmentVariable("CLAUDE_TEST_PROJECT_ROOT");
        if (!string.IsNullOrEmpty(envProjectRoot) && Directory.Exists(envProjectRoot))
        {
            return envProjectRoot;
        }
        
        // If not in environment, try to get current directory for fallback
        try
        {
            var currentDir = Directory.GetCurrentDirectory();
            var dir = new DirectoryInfo(currentDir);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "ClaudeBatchServer.sln")))
            {
                dir = dir.Parent;
            }
            
            if (dir != null)
            {
                return dir.FullName;
            }
        }
        catch
        {
            // If we can't get current directory, try from assembly location
        }
        
        // Try from assembly location as last resort
        var assemblyLocation = System.Reflection.Assembly.GetExecutingAssembly().Location;
        var assemblyDir = Path.GetDirectoryName(assemblyLocation);
        if (assemblyDir != null)
        {
            var dir = new DirectoryInfo(assemblyDir);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "ClaudeBatchServer.sln")))
            {
                dir = dir.Parent;
            }
            
            if (dir != null)
            {
                return dir.FullName;
            }
        }
        
        throw new InvalidOperationException("Could not find project root directory from environment variable, current directory, or assembly location");
    }
}

/// <summary>
/// Enhanced result of CLI command execution with detailed information
/// </summary>
public class CliExecutionResult
{
    public int ExitCode { get; set; }
    public string StandardOutput { get; set; } = string.Empty;
    public string StandardError { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string Arguments { get; set; } = string.Empty;
    
    public string CombinedOutput => 
        string.IsNullOrWhiteSpace(StandardError) 
            ? StandardOutput 
            : $"{StandardOutput}\n{StandardError}";

    public bool ContainsOutput(string text, StringComparison comparison = StringComparison.OrdinalIgnoreCase)
    {
        return CombinedOutput.Contains(text, comparison);
    }

    public bool OutputMatches(string pattern)
    {
        return System.Text.RegularExpressions.Regex.IsMatch(CombinedOutput, pattern);
    }

    public override string ToString()
    {
        return $"CLI Result: {Arguments} -> Exit: {ExitCode}, Success: {Success}\nOutput: {CombinedOutput}";
    }
}