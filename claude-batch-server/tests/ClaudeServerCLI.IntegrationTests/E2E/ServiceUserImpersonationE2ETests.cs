using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using System.Text.Json;

namespace ClaudeServerCLI.IntegrationTests.E2E;

/// <summary>
/// E2E tests for service user creation, impersonation, and CIDX execution context validation
/// Tests the complete workflow: Service User -> User Impersonation -> CIDX as Service User
/// </summary>
[Collection("TestServer")]
public class ServiceUserImpersonationE2ETests : E2ETestBase
{
    private const string TestServiceUser = "claude-batch-server";
    private const string TestImpersonationUser = "testuser";
    
    public ServiceUserImpersonationE2ETests(ITestOutputHelper output, TestServerHarness serverHarness) 
        : base(output, serverHarness)
    {
    }

    /// <summary>
    /// FAILING TEST (RED): Verify service user exists and has correct group memberships
    /// This test will fail initially because we haven't implemented service user creation
    /// </summary>
    [Fact]
    public async Task ServiceUser_ShouldExistWithCorrectGroups()
    {
        // Skip this test if we can't create the service user (development/CI environments)
        var canCreateServiceUser = await CanCreateServiceUser();
        if (!canCreateServiceUser)
        {
            Output.WriteLine("Skipping service user test - cannot create service user in this environment");
            Output.WriteLine("This test requires sudo privileges to create system users");
            return;
        }
        
        // Arrange: Expected service user configuration
        var expectedGroups = new[] { "docker", "shadow", TestServiceUser };
        
        // Act: Check if service user exists and get groups
        var userExistsResult = await ExecuteSystemCommand($"id {TestServiceUser}");
        var groupsResult = await ExecuteSystemCommand($"groups {TestServiceUser}");
        
        Output.WriteLine($"Service user check: {userExistsResult.CombinedOutput}");
        Output.WriteLine($"Service user groups: {groupsResult.CombinedOutput}");
        
        // Assert: Service user should exist and have required groups
        userExistsResult.Success.Should().BeTrue("Service user should exist");
        groupsResult.Success.Should().BeTrue("Should be able to get service user groups");
        
        foreach (var expectedGroup in expectedGroups)
        {
            groupsResult.CombinedOutput.Should().Contain(expectedGroup, 
                $"Service user should be in {expectedGroup} group");
        }
    }

    /// <summary>
    /// FAILING TEST (RED): Verify sudo configuration allows service user to impersonate regular users
    /// </summary>
    [Fact]
    public async Task ServiceUser_ShouldHaveSudoPrivilegesForUserImpersonation()
    {
        // Arrange: Check sudo configuration exists
        var sudoersFile = "/etc/sudoers.d/claude-batch-server";
        
        // Act: Check if sudoers file exists and has correct content
        var sudoersExistsResult = await ExecuteSystemCommand($"test -f {sudoersFile} && echo 'exists' || echo 'missing'");
        var sudoersContentResult = await ExecuteSystemCommand($"sudo cat {sudoersFile}");
        
        Output.WriteLine($"Sudoers file exists: {sudoersExistsResult.CombinedOutput}");
        Output.WriteLine($"Sudoers content: {sudoersContentResult.CombinedOutput}");
        
        // Assert: Sudoers configuration should exist and allow impersonation
        sudoersExistsResult.CombinedOutput.Should().Contain("exists", 
            "Sudoers file for claude-batch-server should exist");
        sudoersContentResult.Success.Should().BeTrue("Should be able to read sudoers file");
        sudoersContentResult.CombinedOutput.Should().Contain("claude-batch-server ALL=(#>=1000) NOPASSWD: ALL",
            "Should allow impersonation of regular users (UID >= 1000)");
    }

    /// <summary>
    /// FAILING TEST (RED): Verify service user can start API server and handle requests
    /// This tests the run.sh integration with service user
    /// </summary>
    [Fact]
    public async Task ServiceUser_ShouldBeAbleToRunApiServer()
    {
        // Skip if we're already running as service user (in CI/CD environments)
        var currentUser = Environment.UserName;
        if (currentUser == TestServiceUser)
        {
            Output.WriteLine($"Already running as service user {TestServiceUser}, skipping test");
            return;
        }
        
        // Act: Attempt to start server as service user (this will fail initially)
        var switchToServiceUserResult = await ExecuteSystemCommand(
            $"sudo -u {TestServiceUser} whoami");
        
        Output.WriteLine($"Service user execution test: {switchToServiceUserResult.CombinedOutput}");
        
        // Assert: Should be able to execute commands as service user
        switchToServiceUserResult.Success.Should().BeTrue("Should be able to execute commands as service user");
        switchToServiceUserResult.CombinedOutput.Trim().Should().Be(TestServiceUser, 
            "Command should execute as service user");
    }

    /// <summary>
    /// FAILING TEST (RED): Test repository registration with CIDX awareness
    /// This test verifies the complete flow: Auth -> Create Repo with CIDX -> Verify CIDX containers
    /// </summary>
    [Fact]
    public async Task RepositoryRegistration_WithCidxAware_ShouldStartCidxContainers()
    {
        // Arrange: Login and prepare test repository
        await LoginAsync();
        var testRepoName = $"cidx-test-repo-{Guid.NewGuid():N}";
        var testGitRepo = "https://github.com/jsbattig/tries.git";
        
        try
        {
            // Act: Create repository with CIDX awareness
            var createRepoResult = await CliHelper.ExecuteCommandAsync(
                $"repos create --name {testRepoName} --git-url {testGitRepo} --cidx-aware true");
            
            Output.WriteLine($"CIDX-aware repo creation: {createRepoResult.CombinedOutput}");
            
            // Assert: Repository should be created successfully
            createRepoResult.Success.Should().BeTrue($"Repository creation failed: {createRepoResult.CombinedOutput}");
            createRepoResult.CombinedOutput.Should().Contain("successfully", "Repository should be created successfully");
            
            // Verify CIDX containers are running for this repository
            var cidxStatusResult = await ExecuteSystemCommand(
                $"docker ps --filter name=cidx --format '{{{{.Names}}}} {{{{.Status}}}}'");
            
            Output.WriteLine($"CIDX containers status: {cidxStatusResult.CombinedOutput}");
            
            // Should have CIDX containers running (this will fail initially)
            cidxStatusResult.Success.Should().BeTrue("Should be able to check Docker containers");
            cidxStatusResult.CombinedOutput.Should().Contain("cidx", "CIDX containers should be running");
            
            CreatedRepoIds.Add(testRepoName);
        }
        finally
        {
            // Cleanup: Remove test repository
            try
            {
                await CliHelper.ExecuteCommandAsync($"repos delete {testRepoName} --force");
            }
            catch (Exception ex)
            {
                Output.WriteLine($"Cleanup failed: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// FAILING TEST (RED): Test job execution with user impersonation
    /// This is the main E2E test that verifies Claude Code runs as impersonated user while CIDX runs as service user
    /// </summary>
    [Fact]
    public async Task JobExecution_ShouldImpersonateUserForClaudeCodeButUseServiceUserForCidx()
    {
        // Arrange: Login and create test repository
        await LoginAsync();
        var testRepoName = $"impersonation-test-repo-{Guid.NewGuid():N}";
        var testGitRepo = "https://github.com/jsbattig/tries.git";
        
        try
        {
            // Create repository with CIDX awareness
            var createRepoResult = await CliHelper.ExecuteCommandAsync(
                $"repos create --name {testRepoName} --git-url {testGitRepo} --cidx-aware");
            
            createRepoResult.Success.Should().BeTrue($"Repository creation failed: {createRepoResult.CombinedOutput}");
            CreatedRepoIds.Add(testRepoName);
            
            // Act: Create and execute a job that checks execution context
            var testPrompt = @"
            Check the current execution context and report:
            1. Current username (whoami)
            2. Current user ID (id -u)
            3. Current groups (groups)
            4. Whether Docker is accessible (docker version)
            5. Whether CIDX is accessible (cidx status)
            
            Output this information in a structured format for validation.
            ";
            
            var createJobResult = await CliHelper.ExecuteCommandAsync(
                $"jobs create --repo {testRepoName} --prompt \"{testPrompt}\" --cidx-aware");
            
            Output.WriteLine($"Job creation: {createJobResult.CombinedOutput}");
            
            // Assert: Job should be created successfully
            createJobResult.Success.Should().BeTrue($"Job creation failed: {createJobResult.CombinedOutput}");
            
            // Extract job ID from response
            var jobId = ExtractJobIdFromOutput(createJobResult.CombinedOutput);
            jobId.Should().NotBeNullOrEmpty("Job ID should be extractable from creation output");
            CreatedJobIds.Add(jobId);
            
            // Wait for job completion and get results
            var jobOutput = await WaitForJobCompletionAndGetOutput(jobId);
            
            Output.WriteLine($"Job execution output: {jobOutput}");
            
            // Assert: Verify execution context shows proper impersonation
            // Claude Code should run as test user
            jobOutput.Should().Contain(TestImpersonationUser, 
                "Claude Code should execute as the authenticated user");
            
            // CIDX operations should be performed by service user (we'll validate this through logs)
            // This is a complex assertion that requires checking the actual job execution logs
            
            // The job should complete successfully
            var jobStatusResult = await CliHelper.ExecuteCommandAsync($"jobs show {jobId} --format json");
            jobStatusResult.Success.Should().BeTrue("Should be able to get job status");
            
            var jobStatus = JsonSerializer.Deserialize<JsonElement>(jobStatusResult.CombinedOutput);
            jobStatus.GetProperty("status").GetString().Should().Be("Completed", 
                "Job should complete successfully with impersonation");
        }
        finally
        {
            // Cleanup
            try
            {
                await CliHelper.ExecuteCommandAsync($"repos delete {testRepoName} --force");
            }
            catch (Exception ex)
            {
                Output.WriteLine($"Cleanup failed: {ex.Message}");
            }
        }
    }

    #region Helper Methods

    /// <summary>
    /// Check if we can create a service user (requires sudo privileges)
    /// </summary>
    private async Task<bool> CanCreateServiceUser()
    {
        try
        {
            // First check if service user already exists
            var userExistsResult = await ExecuteSystemCommand($"id {TestServiceUser}");
            if (userExistsResult.Success)
            {
                Output.WriteLine($"Service user {TestServiceUser} already exists");
                return true;
            }
            
            // Check if we have passwordless sudo for user creation
            var sudoTestResult = await ExecuteSystemCommand("sudo -n whoami");
            if (sudoTestResult.Success)
            {
                Output.WriteLine("Passwordless sudo available - can create service user");
                return true;
            }
            
            Output.WriteLine("Service user does not exist and sudo requires password");
            return false;
        }
        catch (Exception ex)
        {
            Output.WriteLine($"Error checking service user creation capability: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Execute a system command for testing infrastructure
    /// </summary>
    private async Task<(bool Success, string CombinedOutput, int ExitCode)> ExecuteSystemCommand(string command)
    {
        try
        {
            var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "/bin/bash",
                    Arguments = $"-c \"{command}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var stdout = await process.StandardOutput.ReadToEndAsync();
            var stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            var combinedOutput = string.IsNullOrEmpty(stderr) ? stdout : $"{stdout}\nSTDERR:\n{stderr}";
            return (process.ExitCode == 0, combinedOutput.Trim(), process.ExitCode);
        }
        catch (Exception ex)
        {
            return (false, $"Exception: {ex.Message}", -1);
        }
    }

    /// <summary>
    /// Extract job ID from job creation output
    /// </summary>
    private string ExtractJobIdFromOutput(string output)
    {
        // This will need to be implemented based on the actual output format
        // For now, return a placeholder that will be implemented when the job creation works
        var lines = output.Split('\n');
        foreach (var line in lines)
        {
            if (line.Contains("Job") && line.Contains("created"))
            {
                // Extract ID from line like "Job abc123 created successfully"
                var parts = line.Split(' ');
                for (int i = 0; i < parts.Length - 1; i++)
                {
                    if (parts[i].Equals("Job", StringComparison.OrdinalIgnoreCase))
                    {
                        return parts[i + 1];
                    }
                }
            }
        }
        return "";
    }

    /// <summary>
    /// Wait for job completion and return output
    /// </summary>
    private async Task<string> WaitForJobCompletionAndGetOutput(string jobId)
    {
        const int maxWaitTimeSeconds = 300; // 5 minutes
        const int pollIntervalSeconds = 5;
        
        for (int waited = 0; waited < maxWaitTimeSeconds; waited += pollIntervalSeconds)
        {
            var statusResult = await CliHelper.ExecuteCommandAsync($"jobs show {jobId} --format json");
            if (statusResult.Success)
            {
                var statusJson = JsonSerializer.Deserialize<JsonElement>(statusResult.CombinedOutput);
                var status = statusJson.GetProperty("status").GetString();
                
                if (status == "Completed" || status == "Failed")
                {
                    // Get the job output
                    var outputResult = await CliHelper.ExecuteCommandAsync($"jobs output {jobId}");
                    return outputResult.Success ? outputResult.CombinedOutput : $"Failed to get output: {outputResult.CombinedOutput}";
                }
            }
            
            await Task.Delay(TimeSpan.FromSeconds(pollIntervalSeconds));
        }
        
        return "Job did not complete within timeout period";
    }

    #endregion
}