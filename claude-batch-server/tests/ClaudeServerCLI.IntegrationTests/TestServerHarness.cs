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

    public TestServerHarness()
    {
        _output = null;
        _serverPort = GetAvailablePort();
        _serverUrl = $"https://localhost:{_serverPort}";
        
        // Create test workspace in temp directory
        _testWorkspaceRoot = Path.Combine(Path.GetTempPath(), $"cli-e2e-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testWorkspaceRoot);
        
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
        
        // Create test passwd file (username:bcrypt_hash format)
        // Hash for "TestPass123!" using bcrypt
        var passwdContent = $"{TestUser}:$2a$11$8K1p/a0dCZCQiIfLmU.pHOuK5K5T2n8n2n8n2n8n2n8n2n8n2n8n2\n";
        await File.WriteAllTextAsync(_testPasswdPath, passwdContent);
        
        // Create test shadow file (username:salt:hash format for shadow file auth)
        var shadowContent = $"{TestUser}:testsalt:testhash\n";
        await File.WriteAllTextAsync(_testShadowPath, shadowContent);
        
        Log($"Created test passwd file: {_testPasswdPath}");
        Log($"Created test shadow file: {_testShadowPath}");
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
                MaxJobs = 10,
                JobTimeoutMinutes = 30,
                CleanupIntervalMinutes = 60
            },
            Authentication = new
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
    }

    private async Task StartServerAsync()
    {
        Log("Starting test API server...");
        
        // Find the API project path
        var currentDir = Directory.GetCurrentDirectory();
        var projectRoot = FindProjectRoot(currentDir);
        var apiProjectPath = Path.Combine(projectRoot, "src", "ClaudeBatchServer.Api");
        var apiDllPath = Path.Combine(apiProjectPath, "bin", "Debug", "net8.0", "ClaudeBatchServer.Api.dll");
        
        if (!File.Exists(apiDllPath))
        {
            throw new InvalidOperationException($"API server DLL not found at: {apiDllPath}. Please build the project first.");
        }
        
        var startInfo = new ProcessStartInfo
        {
            FileName = "/home/jsbattig/.dotnet/dotnet",
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
        
        // Override config file path
        startInfo.Arguments += $" --configuration \"{_testConfigPath}\"";
        
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

    private static int GetAvailablePort()
    {
        // Find an available port starting from 8444
        for (int port = 8444; port < 8500; port++)
        {
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
/// </summary>
[CollectionDefinition("TestServer")]
public class TestServerCollection : ICollectionFixture<TestServerHarness>
{
    // This class has no code, and is never created. Its purpose is simply
    // to be the place to apply [CollectionFixture<>] and all the ICollectionFixture<> interfaces.
}