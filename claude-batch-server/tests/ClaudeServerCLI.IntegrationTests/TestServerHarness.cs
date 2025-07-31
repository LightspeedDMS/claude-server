using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace ClaudeServerCLI.IntegrationTests;

/// <summary>
/// Test harness that manages the API server lifecycle for CLI E2E tests
/// Automatically starts the server before tests and stops it after
/// </summary>
public class TestServerHarness : IAsyncLifetime
{
    private readonly ITestOutputHelper? _output;
    private Process? _serverProcess;
    private readonly int _serverPort;
    private readonly string _serverUrl;
    private readonly string _testWorkspaceRoot;
    private readonly string _testConfigPath;
    private readonly string _testPasswdPath;
    private readonly string _testShadowPath;
    
    public string ServerUrl => _serverUrl;
    public string TestWorkspaceRoot => _testWorkspaceRoot;
    public string TestUser => "testuser";
    public string TestPassword => "TestPass123!";
    public string TestPasswordHash => "$5$testsalt$GFbozh8usqmWm9UnDbf75M6L8M96mgoyVbDr+lXlUxE";

    public TestServerHarness()
    {
        _output = null;
        _serverPort = GetAvailablePort();
        _serverUrl = $"https://localhost:{_serverPort}";
        
        // Create test workspace in temp directory
        _testWorkspaceRoot = Path.Combine(Path.GetTempPath(), $"cli-e2e-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testWorkspaceRoot);
        Directory.CreateDirectory(Path.Combine(_testWorkspaceRoot, "repos"));
        Directory.CreateDirectory(Path.Combine(_testWorkspaceRoot, "jobs"));
        
        // Create test authentication files
        _testConfigPath = Path.Combine(_testWorkspaceRoot, "test-appsettings.json");
        _testPasswdPath = Path.Combine(_testWorkspaceRoot, "test-passwd");
        _testShadowPath = Path.Combine(_testWorkspaceRoot, "test-shadow");
        
        Log($"Test server will run on: {_serverUrl}");
        Log($"Test workspace: {_testWorkspaceRoot}");
    }

    public async Task InitializeAsync()
    {
        Log("Initializing test server harness...");
        
        try
        {
            // Set environment variable for project root before anything else
            string currentDir;
            try
            {
                currentDir = Directory.GetCurrentDirectory();
            }
            catch
            {
                // If we can't get current directory, use temp path
                currentDir = Path.GetTempPath();
            }
            var projectRoot = FindProjectRootForSetup(currentDir);
            Environment.SetEnvironmentVariable("CLAUDE_TEST_PROJECT_ROOT", projectRoot);
            Log($"Set CLAUDE_TEST_PROJECT_ROOT to: {projectRoot}");
            
            // Create test authentication files
            await CreateTestAuthenticationFiles();
            
            // Create test configuration
            await CreateTestConfiguration();
            
            // Start the API server
            await StartServerAsync();
            
            // Wait for server to be ready
            await WaitForServerReady();
            
            Log("Test server harness initialized successfully");
        }
        catch (Exception ex)
        {
            Log($"Failed to initialize test server harness: {ex.Message}");
            await DisposeAsync(); // Cleanup on failure
            throw;
        }
    }

    public async Task DisposeAsync()
    {
        Log("Disposing test server harness...");
        
        try
        {
            // Stop the server
            await StopServerAsync();
            
            // Cleanup test workspace
            if (Directory.Exists(_testWorkspaceRoot))
            {
                try
                {
                    Directory.Delete(_testWorkspaceRoot, true);
                    Log($"Cleaned up test workspace: {_testWorkspaceRoot}");
                }
                catch (Exception ex)
                {
                    Log($"Warning: Failed to cleanup test workspace: {ex.Message}");
                }
            }
            
            // Clear the environment variable
            Environment.SetEnvironmentVariable("CLAUDE_TEST_PROJECT_ROOT", null);
            Log("Cleared CLAUDE_TEST_PROJECT_ROOT environment variable");
            
            // Clean up test config from API directory
            try
            {
                string currentDir;
                try
                {
                    currentDir = Directory.GetCurrentDirectory();
                }
                catch (Exception ex)
                {
                    Log($"Warning: Could not get current directory during cleanup ({ex.Message}), using environment variable");
                    currentDir = Environment.GetEnvironmentVariable("CLAUDE_TEST_PROJECT_ROOT") ?? "/tmp";
                }
                var projectRoot = FindProjectRoot(currentDir);
                var apiProjectPath = Path.Combine(projectRoot, "src", "ClaudeBatchServer.Api");
                var testConfigInApiDir = Path.Combine(apiProjectPath, "appsettings.Testing.json");
                if (File.Exists(testConfigInApiDir))
                {
                    File.Delete(testConfigInApiDir);
                    Log($"Deleted test config: {testConfigInApiDir}");
                }
            }
            catch (Exception ex)
            {
                Log($"Warning: Failed to cleanup test config: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            Log($"Warning: Error during test server disposal: {ex.Message}");
        }
        
        Log("Test server harness disposed");
    }

    private async Task CreateTestAuthenticationFiles()
    {
        Log("Creating test authentication files...");
        
        // Create test passwd file (standard unix format)
        var passwdContent = $"{TestUser}:x:1000:1000:Test User:/home/{TestUser}:/bin/bash\n";
        await File.WriteAllTextAsync(_testPasswdPath, passwdContent);
        
        // Create test shadow file with proper format
        // For testing, we'll use a known SHA-256 hash
        // Password: TestPass123!
        // Salt: testsalt
        // The server uses simple SHA256(password+salt) then base64 encoding
        var shadowContent = $"{TestUser}:$5$testsalt$GFbozh8usqmWm9UnDbf75M6L8M96mgoyVbDr+lXlUxE:19000:0:99999:7:::\n";
        await File.WriteAllTextAsync(_testShadowPath, shadowContent);
        
        Log($"Created test passwd file: {_testPasswdPath}");
        Log($"Passwd content: {passwdContent.Trim()}");
        Log($"Created test shadow file: {_testShadowPath}");
        Log($"Shadow content: {shadowContent.Trim()}");
    }

    private async Task CreateTestConfiguration()
    {
        Log("Creating test configuration...");
        
        var config = new
        {
            Logging = new
            {
                LogLevel = new
                {
                    Default = "Warning",
                    Microsoft = "Warning",
                    System = "Warning"
                }
            },
            Kestrel = new
            {
                Endpoints = new
                {
                    Https = new
                    {
                        Url = _serverUrl,
                        Certificate = new
                        {
                            Subject = "localhost",
                            Store = "My",
                            Location = "CurrentUser",
                            AllowInvalid = true
                        }
                    }
                }
            },
            ConnectionStrings = new
            {
                DefaultConnection = $"Data Source={Path.Combine(_testWorkspaceRoot, "test.db")}"
            },
            Workspace = new
            {
                BasePath = _testWorkspaceRoot,
                JobsPath = Path.Combine(_testWorkspaceRoot, "jobs"),
                ReposPath = Path.Combine(_testWorkspaceRoot, "repos"),
                MaxJobs = 10,
                JobTimeoutMinutes = 30,
                CleanupIntervalMinutes = 60
            },
            Auth = new
            {
                PasswdFilePath = _testPasswdPath,
                ShadowFilePath = _testShadowPath,
                HashAlgorithm = "SHA256",
                EnableShadowFileAuth = true
            },
            Jwt = new
            {
                Key = "test-jwt-key-for-integration-tests-32-chars-minimum-length-required",
                Issuer = "TestServer",
                Audience = "TestClient",
                ExpirationHours = 24
            },
            AllowedHosts = "*"
        };
        
        var json = System.Text.Json.JsonSerializer.Serialize(config, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true
        });
        
        await File.WriteAllTextAsync(_testConfigPath, json);
        Log($"Created test configuration: {_testConfigPath}");
        Log($"Config content preview: {json.Substring(0, Math.Min(500, json.Length))}...");
    }

    private async Task StartServerAsync()
    {
        Log("Starting test API server...");
        
        // Find the API project path - use environment variable if current directory is invalid
        string currentDir;
        try
        {
            currentDir = Directory.GetCurrentDirectory();
        }
        catch (Exception ex)
        {
            Log($"Warning: Could not get current directory ({ex.Message}), using environment variable");
            currentDir = Environment.GetEnvironmentVariable("CLAUDE_TEST_PROJECT_ROOT") ?? "/tmp";
        }
        var projectRoot = FindProjectRoot(currentDir);
        var apiProjectPath = Path.Combine(projectRoot, "src", "ClaudeBatchServer.Api");
        var apiDllPath = Path.Combine(apiProjectPath, "bin", "Debug", "net8.0", "ClaudeBatchServer.Api.dll");
        
        if (!File.Exists(apiDllPath))
        {
            throw new InvalidOperationException($"API server DLL not found at: {apiDllPath}. Please build the project first.");
        }
        
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"\"{apiDllPath}\"",
            WorkingDirectory = apiProjectPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        
        // Set environment variables for test configuration
        startInfo.EnvironmentVariables["ASPNETCORE_ENVIRONMENT"] = "Testing";
        startInfo.EnvironmentVariables["ASPNETCORE_URLS"] = _serverUrl;
        startInfo.EnvironmentVariables["DOTNET_ENVIRONMENT"] = "Testing";
        
        // Copy config to API project directory as appsettings.Testing.json
        var testConfigInApiDir = Path.Combine(apiProjectPath, "appsettings.Testing.json");
        File.Copy(_testConfigPath, testConfigInApiDir, overwrite: true);
        Log($"Copied test config to: {testConfigInApiDir}");
        
        // Also set via environment variables for redundancy
        startInfo.EnvironmentVariables["Auth__PasswdFilePath"] = _testPasswdPath;
        startInfo.EnvironmentVariables["Auth__ShadowFilePath"] = _testShadowPath;
        
        // Override workspace paths to use test directories
        startInfo.EnvironmentVariables["Workspace__JobsPath"] = Path.Combine(_testWorkspaceRoot, "jobs");
        startInfo.EnvironmentVariables["Workspace__ReposPath"] = Path.Combine(_testWorkspaceRoot, "repos");
        startInfo.EnvironmentVariables["Workspace__BasePath"] = _testWorkspaceRoot;
        
        _serverProcess = new Process { StartInfo = startInfo };
        
        var outputBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();
        
        _serverProcess.OutputDataReceived += (sender, e) =>
        {
            if (e.Data != null)
            {
                outputBuilder.AppendLine(e.Data);
                Log($"[SERVER] {e.Data}");
            }
        };
        
        _serverProcess.ErrorDataReceived += (sender, e) =>
        {
            if (e.Data != null)
            {
                errorBuilder.AppendLine(e.Data);
                Log($"[SERVER ERROR] {e.Data}");
            }
        };
        
        _serverProcess.Start();
        _serverProcess.BeginOutputReadLine();
        _serverProcess.BeginErrorReadLine();
        
        Log($"Started server process with PID: {_serverProcess.Id}");
        
        // Give the server a moment to start
        await Task.Delay(2000);
        
        if (_serverProcess.HasExited)
        {
            var output = outputBuilder.ToString();
            var error = errorBuilder.ToString();
            throw new InvalidOperationException($"Server process exited immediately. Output: {output}\nError: {error}");
        }
    }

    private async Task StopServerAsync()
    {
        if (_serverProcess != null && !_serverProcess.HasExited)
        {
            Log("Stopping test API server...");
            
            try
            {
                _serverProcess.Kill();
                await _serverProcess.WaitForExitAsync();
                Log($"Server process stopped with exit code: {_serverProcess.ExitCode}");
            }
            catch (Exception ex)
            {
                Log($"Warning: Error stopping server process: {ex.Message}");
            }
            finally
            {
                _serverProcess.Dispose();
                _serverProcess = null;
            }
        }
    }

    private async Task WaitForServerReady(int maxAttempts = 30, int delayMs = 1000)
    {
        Log("Waiting for server to be ready...");
        
        using var httpClient = new HttpClient();
        httpClient.Timeout = TimeSpan.FromSeconds(5);
        
        // Skip SSL certificate validation for test server
        var handler = new HttpClientHandler()
        {
            ClientCertificateOptions = ClientCertificateOption.Manual,
            ServerCertificateCustomValidationCallback = (httpRequestMessage, cert, cetChain, policyErrors) => true
        };
        
        using var testClient = new HttpClient(handler);
        testClient.Timeout = TimeSpan.FromSeconds(5);
        
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                // Try to hit a simple endpoint to see if server is responding
                var response = await testClient.GetAsync($"{_serverUrl}/health");
                if (response.StatusCode == HttpStatusCode.OK || response.StatusCode == HttpStatusCode.NotFound)
                {
                    Log($"Server is ready after {attempt} attempts");
                    return;
                }
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                Log($"Server not ready yet (attempt {attempt}/{maxAttempts}): {ex.Message}");
            }
            
            if (attempt < maxAttempts)
            {
                await Task.Delay(delayMs);
            }
        }
        
        throw new TimeoutException($"Server failed to become ready after {maxAttempts} attempts");
    }

    private string FindProjectRoot(string currentDir)
    {
        // First check environment variable
        var envProjectRoot = Environment.GetEnvironmentVariable("CLAUDE_TEST_PROJECT_ROOT");
        if (!string.IsNullOrEmpty(envProjectRoot) && Directory.Exists(envProjectRoot))
        {
            Console.WriteLine($"[DEBUG] Using project root from environment variable: {envProjectRoot}");
            return envProjectRoot;
        }
        
        // If not in environment, use the setup method
        return FindProjectRootForSetup(currentDir);
    }
    
    private string FindProjectRootForSetup(string currentDir)
    {
        // Try from current directory
        var dir = new DirectoryInfo(currentDir);
        Console.WriteLine($"[DEBUG] Starting directory: {dir.FullName}");
        
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "ClaudeBatchServer.sln")))
        {
            Console.WriteLine($"[DEBUG] Checking directory: {dir.FullName}");
            dir = dir.Parent;
        }
        
        // If not found, try from assembly location
        if (dir == null)
        {
            Console.WriteLine($"[DEBUG] Trying from assembly location");
            var assemblyLocation = System.Reflection.Assembly.GetExecutingAssembly().Location;
            var assemblyDir = Path.GetDirectoryName(assemblyLocation);
            if (assemblyDir != null)
            {
                dir = new DirectoryInfo(assemblyDir);
            }
            
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "ClaudeBatchServer.sln")))
            {
                Console.WriteLine($"[DEBUG] Checking directory: {dir.FullName}");
                dir = dir.Parent;
            }
        }
        
        if (dir == null)
        {
            Console.WriteLine($"[DEBUG] Could not find ClaudeBatchServer.sln starting from {currentDir} or assembly location");
            throw new InvalidOperationException($"Could not find project root directory starting from {currentDir}");
        }
        
        Console.WriteLine($"[DEBUG] Found project root: {dir.FullName}");
        return dir.FullName;
    }

    private static int GetAvailablePort()
    {
        // Use a random starting point to reduce race conditions when multiple tests start simultaneously
        var random = new Random();
        int startPort = random.Next(8444, 8480); // Random start in first part of range
        
        // Try ports starting from random point
        for (int i = 0; i < 56; i++) // Check all 56 ports in range
        {
            int port = 8444 + ((startPort - 8444 + i) % 56);
            if (IsPortAvailable(port))
            {
                return port;
            }
        }
        
        throw new InvalidOperationException("No available ports found in range 8444-8499");
    }

    private static bool IsPortAvailable(int port)
    {
        var ipGlobalProperties = IPGlobalProperties.GetIPGlobalProperties();
        var tcpConnInfoArray = ipGlobalProperties.GetActiveTcpListeners();
        
        return !tcpConnInfoArray.Any(endpoint => endpoint.Port == port);
    }

    private void Log(string message)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        var logMessage = $"[{timestamp}] [TestHarness] {message}";
        
        _output?.WriteLine(logMessage);
        Console.WriteLine(logMessage); // Also log to console for debugging
    }
}

/// <summary>
/// Collection definition for sharing the test server harness across test classes
/// IMPORTANT: All tests in this collection must run SEQUENTIALLY (not in parallel)
/// because they share the same TestServerHarness instance, server port, and CLI processes.
/// Parallel execution causes resource contention, deadlocks, and test failures.
/// </summary>
[CollectionDefinition("TestServer")]
public class TestServerCollection : ICollectionFixture<TestServerHarness>
{
    // This class has no code, and is never created. Its purpose is simply
    // to be the place to apply [CollectionFixture<>] and all the ICollectionFixture<> interfaces.
}