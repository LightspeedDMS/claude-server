using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;
using FluentAssertions;
using YamlDotNet.Serialization;

namespace ClaudeServerCLI.IntegrationTests;

/// <summary>
/// Base class for all E2E integration tests that provides common functionality
/// for testing CLI commands against a real running server
/// </summary>
public abstract class E2ETestBase : IDisposable
{
    protected readonly ITestOutputHelper Output;
    protected readonly TestServerHarness ServerHarness;
    protected readonly CLITestHelper CliHelper;
    protected readonly string TestDataDirectory;
    protected readonly string TestConfigDirectory;
    protected readonly List<string> CreatedRepoIds = new();
    protected readonly List<string> CreatedJobIds = new();
    
    protected E2ETestBase(ITestOutputHelper output, TestServerHarness serverHarness)
    {
        Output = output;
        ServerHarness = serverHarness;
        
        // Create persistent config directory for this test instance
        TestConfigDirectory = Path.Combine(Path.GetTempPath(), $"e2e-test-config-{Guid.NewGuid():N}");
        Directory.CreateDirectory(TestConfigDirectory);
        
        // Pass the persistent config directory to CLITestHelper
        CliHelper = new CLITestHelper(serverHarness, persistentConfigPath: TestConfigDirectory);
        
        // Create a test data directory for this test run
        TestDataDirectory = Path.Combine(Path.GetTempPath(), $"e2e-test-data-{Guid.NewGuid():N}");
        Directory.CreateDirectory(TestDataDirectory);
        
        Output.WriteLine($"Test data directory: {TestDataDirectory}");
        Output.WriteLine($"Test config directory: {TestConfigDirectory}");
    }
    
    /// <summary>
    /// Login with test credentials and verify success
    /// </summary>
    protected async Task<bool> LoginAsync(string? profile = null)
    {
        var profileArg = profile != null ? $"--profile {profile}" : "";
        // Use --quiet flag to suppress ANSI output that contaminates JSON in tests
        var command = $"auth login --username {ServerHarness.TestUser} --password \"{ServerHarness.TestPassword}\" {profileArg} --quiet";
        
        Output.WriteLine($"Executing command: {command}");
        var result = await CliHelper.ExecuteCommandAsync(command);
        
        Output.WriteLine($"Login result: ExitCode={result.ExitCode}, Success={result.Success}");
        
        // In quiet mode, we only check exit code since there's no success message
        return result.Success;
    }
    
    /// <summary>
    /// Logout and verify success
    /// </summary>
    protected async Task<bool> LogoutAsync(string? profile = null)
    {
        var profileArg = profile != null ? $"--profile {profile}" : "";
        // Use --quiet flag to suppress ANSI output that contaminates JSON in tests
        var result = await CliHelper.ExecuteCommandAsync($"auth logout {profileArg} --quiet");
        
        Output.WriteLine($"Logout result: ExitCode={result.ExitCode}, Success={result.Success}");
        
        return result.Success;
    }
    
    /// <summary>
    /// Create a test repository from a local directory
    /// </summary>
    protected async Task<string> CreateTestRepositoryAsync(string name, Dictionary<string, string>? files = null)
    {
        // Create repo directory
        var repoPath = Path.Combine(TestDataDirectory, name);
        Directory.CreateDirectory(repoPath);
        
        // Initialize git
        await RunGitCommandAsync(repoPath, "init");
        await RunGitCommandAsync(repoPath, "config user.name \"E2E Test\"");
        await RunGitCommandAsync(repoPath, "config user.email \"e2e@test.com\"");
        
        // Create test files
        if (files == null)
        {
            files = new Dictionary<string, string>
            {
                { "README.md", $"# {name}\n\nTest repository for E2E testing" },
                { "test.txt", "Test file content" }
            };
        }
        
        foreach (var (filename, content) in files)
        {
            var filePath = Path.Combine(repoPath, filename);
            var fileDir = Path.GetDirectoryName(filePath);
            if (fileDir != null && !Directory.Exists(fileDir))
            {
                Directory.CreateDirectory(fileDir);
            }
            await File.WriteAllTextAsync(filePath, content);
        }
        
        // Commit files
        await RunGitCommandAsync(repoPath, "add .");
        await RunGitCommandAsync(repoPath, "commit -m \"Initial commit\"");
        
        // Register with server
        var result = await CliHelper.ExecuteCommandAsync($"repos create --name {name} --path {repoPath}");
        
        Output.WriteLine($"Create repo result: {result.CombinedOutput}");
        
        result.Success.Should().BeTrue($"Failed to create repository: {result.CombinedOutput}");
        
        // Extract repo ID from output
        var idMatch = Regex.Match(result.CombinedOutput, @"ID:\s*([a-zA-Z0-9-]+)");
        if (idMatch.Success)
        {
            var repoId = idMatch.Groups[1].Value;
            CreatedRepoIds.Add(repoId);
            return repoId;
        }
        
        throw new InvalidOperationException($"Could not extract repository ID from output: {result.CombinedOutput}");
    }
    
    /// <summary>
    /// Create test files for job upload
    /// </summary>
    protected async Task<List<string>> CreateTestFilesAsync(string subdirectory, Dictionary<string, string> files)
    {
        var fileDir = Path.Combine(TestDataDirectory, subdirectory);
        Directory.CreateDirectory(fileDir);
        
        var filePaths = new List<string>();
        
        foreach (var (filename, content) in files)
        {
            var filePath = Path.Combine(fileDir, filename);
            await File.WriteAllTextAsync(filePath, content);
            filePaths.Add(filePath);
        }
        
        return filePaths;
    }
    
    /// <summary>
    /// Validate JSON output from a command
    /// </summary>
    protected T ParseJsonOutput<T>(string output)
    {
        try
        {
            // Strip ANSI escape codes first
            var cleanOutput = Regex.Replace(output, @"\x1B\[[0-?]*[ -/]*[@-~]", "");
            
            // Extract JSON from output (might have other text like "Token set on ApiClient" messages)
            var jsonMatch = Regex.Match(cleanOutput, @"(\[[\s\S]*\]|\{[\s\S]*\})", RegexOptions.Multiline);
            if (!jsonMatch.Success)
            {
                throw new InvalidOperationException($"No JSON found in clean output: {cleanOutput}");
            }
            
            var json = jsonMatch.Value;
            var result = JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            
            return result ?? throw new InvalidOperationException("Deserialized to null");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to parse JSON output: {ex.Message}\nOutput: {output}");
        }
    }
    
    /// <summary>
    /// Validate YAML output from a command
    /// </summary>
    protected T ParseYamlOutput<T>(string output)
    {
        try
        {
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(YamlDotNet.Serialization.NamingConventions.CamelCaseNamingConvention.Instance)
                .Build();
            
            return deserializer.Deserialize<T>(output);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to parse YAML output: {ex.Message}\nOutput: {output}");
        }
    }
    
    /// <summary>
    /// Wait for a condition with timeout
    /// </summary>
    protected async Task WaitForConditionAsync(Func<Task<bool>> condition, TimeSpan timeout, string description)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        
        while (stopwatch.Elapsed < timeout)
        {
            if (await condition())
            {
                Output.WriteLine($"Condition met: {description} (took {stopwatch.ElapsedMilliseconds}ms)");
                return;
            }
            
            await Task.Delay(500);
        }
        
        throw new TimeoutException($"Timeout waiting for: {description}");
    }
    
    /// <summary>
    /// Clean up any leftover repositories from previous test runs
    /// </summary>
    private async Task CleanupLeftoverRepositoriesAsync()
    {
        try
        {
            // Login first
            var loginSuccess = await LoginAsync();
            if (!loginSuccess)
            {
                Output.WriteLine("Could not login for global cleanup");
                return;
            }
            
            // Get list of all repositories
            var listResult = await CliHelper.ExecuteCommandAsync("repos list --format json");
            if (!listResult.Success)
            {
                Output.WriteLine($"Could not list repositories for cleanup: {listResult.CombinedOutput}");
                // Ensure logout even if list fails
                await LogoutAsync();
                return;
            }
            
            // Parse the JSON to get repository names
            var repos = ParseJsonOutput<List<Dictionary<string, object>>>(listResult.CombinedOutput);
            if (repos == null || repos.Count == 0)
            {
                Output.WriteLine("No repositories found for cleanup");
                // Ensure logout before returning
                await LogoutAsync();
                return;
            }
            
            // Delete all test repositories (those starting with "tries-test-repo")
            foreach (var repo in repos)
            {
                if (repo.TryGetValue("name", out var nameObj) && nameObj is string name)
                {
                    if (name.StartsWith("tries-test-repo"))
                    {
                        try
                        {
                            var deleteResult = await CliHelper.ExecuteCommandAsync($"repos delete {name} --force");
                            Output.WriteLine($"Global cleanup removed repository: {name} - Success: {deleteResult.Success}");
                        }
                        catch (Exception ex)
                        {
                            Output.WriteLine($"Failed to cleanup repository {name}: {ex.Message}");
                        }
                    }
                }
            }
            
            // Always logout after cleanup to not interfere with test authentication state
            await LogoutAsync();
        }
        catch (Exception ex)
        {
            Output.WriteLine($"Global repository cleanup failed: {ex.Message}");
            // Ensure logout even if exception occurs
            try
            {
                await LogoutAsync();
            }
            catch
            {
                // Best effort logout
            }
        }
    }
    
    /// <summary>
    /// Run a git command in a directory
    /// </summary>
    private async Task RunGitCommandAsync(string workingDirectory, string arguments)
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "git",
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        
        using var process = System.Diagnostics.Process.Start(startInfo);
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
    
    public virtual void Dispose()
    {
        // Cleanup using Task.Run to avoid deadlocks with .Result/.Wait()
        Task.Run(async () =>
        {
            // Cleanup created jobs
            foreach (var jobId in CreatedJobIds)
            {
                try
                {
                    var result = await CliHelper.ExecuteCommandAsync($"jobs delete {jobId} --force");
                    Output.WriteLine($"Cleanup job {jobId}: {result.Success}");
                }
                catch { /* Best effort */ }
            }
            
            // Cleanup created repos
            foreach (var repoId in CreatedRepoIds)
            {
                try
                {
                    // First ensure we're authenticated for cleanup
                    await LoginAsync();
                    
                    var result = await CliHelper.ExecuteCommandAsync($"repos delete {repoId} --force");
                    Output.WriteLine($"Cleanup repo {repoId}: {result.Success} - Output: {result.CombinedOutput}");
                    
                    if (!result.Success)
                    {
                        Output.WriteLine($"WARNING: Failed to cleanup repo {repoId}: {result.CombinedOutput}");
                    }
                }
                catch (Exception ex)
                {
                    Output.WriteLine($"ERROR: Exception during repo cleanup {repoId}: {ex.Message}");
                }
            }
            
            // Ensure we're logged out
            try
            {
                await LogoutAsync();
            }
            catch { /* Best effort */ }
        }).GetAwaiter().GetResult();
        
        // Cleanup test data directory
        if (Directory.Exists(TestDataDirectory))
        {
            try
            {
                Directory.Delete(TestDataDirectory, true);
            }
            catch { /* Best effort */ }
        }
        
        // Cleanup test config directory
        if (Directory.Exists(TestConfigDirectory))
        {
            try
            {
                Directory.Delete(TestConfigDirectory, true);
            }
            catch { /* Best effort */ }
        }
    }
}