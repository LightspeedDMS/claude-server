using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using System.Text.Json;

namespace ClaudeServerCLI.IntegrationTests.E2E;

[Collection("TestServer")]
public class RepositoryManagementE2ETests : E2ETestBase
{
    private const string TestGitRepo = "https://github.com/jsbattig/tries.git";
    private readonly string TestRepoName;
    
    public RepositoryManagementE2ETests(ITestOutputHelper output, TestServerHarness serverHarness) 
        : base(output, serverHarness)
    {
        // Generate unique repository name for each test instance to avoid conflicts
        TestRepoName = $"tries-test-repo-{Guid.NewGuid():N}";
    }
    
    [Fact]
    public async Task ReposList_WhenNotAuthenticated_ShouldFail()
    {
        // Arrange
        await LogoutAsync();
        
        // Act
        var result = await CliHelper.ExecuteCommandAsync("repos list");
        
        // Assert
        result.Success.Should().BeFalse();
        result.CombinedOutput.Should().ContainAny("Not authenticated", "not authenticated", "Unauthorized", "401", "Failed to", "Error:");
    }
    
    [Fact]
    public async Task ReposList_WhenAuthenticated_ShouldSucceed()
    {
        // Arrange
        await LoginAsync();
        
        // Act
        var result = await CliHelper.ExecuteCommandAsync("repos list");
        
        Output.WriteLine($"Repos list result: {result.CombinedOutput}");
        
        // Assert
        result.Success.Should().BeTrue($"Command failed with output: {result.CombinedOutput}");
        // The output should either show "No repositories found" or contain table data
        // Even if headers are truncated (N…, T…), the command should succeed
        result.CombinedOutput.Should().NotBeNullOrWhiteSpace("Command should produce some output");
        
        // The command should not contain error messages
        result.CombinedOutput.Should().NotContainAny("Error", "Failed", "Exception", "error", "failed");
    }
    
    [Fact]
    public async Task ReposCreate_WithGitUrl_ShouldCloneAndRegister()
    {
        // Arrange
        await LoginAsync();
        
        // Ensure any leftover repository with the same name is cleaned up first
        try
        {
            await CliHelper.ExecuteCommandAsync($"repos delete {TestRepoName} --force");
            Output.WriteLine($"Cleaned up existing repository: {TestRepoName}");
        }
        catch
        {
            // Repository doesn't exist, which is fine
        }
        
        // Act
        var result = await CliHelper.ExecuteCommandAsync(
            $"repos create --name {TestRepoName} --git-url {TestGitRepo} --cidx-aware false");
        
        Output.WriteLine($"Create repo result: {result.CombinedOutput}");
        
        // Assert
        result.Success.Should().BeTrue($"Command failed with output: {result.CombinedOutput}");
        result.CombinedOutput.Should().Contain("successfully");
        result.CombinedOutput.Should().Contain(TestRepoName);
        
        // Track repo name for cleanup (since repos delete takes name, not ID)
        CreatedRepoIds.Add(TestRepoName);
        
        // Verify repo appears in list (note: table may truncate long names)
        var listResult = await CliHelper.ExecuteCommandAsync("repos list");
        listResult.Success.Should().BeTrue();
        // The repo should appear in the list, even if the name is truncated in table format
        // Look for partial name or use JSON format to verify
        var jsonListResult = await CliHelper.ExecuteCommandAsync("repos list --format json");
        jsonListResult.Success.Should().BeTrue();
        jsonListResult.CombinedOutput.Should().Contain(TestRepoName);
    }
    
    [Fact]
    public async Task ReposCreate_WithLocalPath_ShouldShowNotImplemented()
    {
        // Arrange
        await LoginAsync();
        var localRepoPath = await CliHelper.CreateTestGitRepositoryAsync();
        
        try
        {
            // Act
            var result = await CliHelper.ExecuteCommandAsync(
                $"repos create --name local-test-repo --path {localRepoPath}");
            
            Output.WriteLine($"Create local repo result: {result.CombinedOutput}");
            
            // Assert - Local path registration is not yet implemented
            result.Success.Should().BeFalse();
            result.CombinedOutput.Should().Contain("not yet implemented");
        }
        finally
        {
            CLITestHelper.CleanupDirectory(localRepoPath);
        }
    }
    
    [Fact]
    public async Task ReposShow_WithValidId_ShouldDisplayDetails()
    {
        // Arrange
        await LoginAsync();
        
        // Ensure any leftover repository is cleaned up first
        try
        {
            await CliHelper.ExecuteCommandAsync($"repos delete {TestRepoName} --force");
            Output.WriteLine($"Cleaned up existing repository: {TestRepoName}");
        }
        catch
        {
            // Repository doesn't exist, which is fine
        }
        
        var createResult = await CliHelper.ExecuteCommandAsync(
            $"repos create --name {TestRepoName} --git-url {TestGitRepo} --cidx-aware false");
        createResult.Success.Should().BeTrue($"Failed to create repository: {createResult.CombinedOutput}");
        
        // Track repo name for cleanup
        CreatedRepoIds.Add(TestRepoName);
        
        // Act
        var result = await CliHelper.ExecuteCommandAsync($"repos show {TestRepoName}");
        
        Output.WriteLine($"Show repo result: {result.CombinedOutput}");
        
        // Assert
        result.Success.Should().BeTrue($"Command failed with output: {result.CombinedOutput}");
        
        // The display wraps content in tables, so check for parts that won't be split
        result.CombinedOutput.Should().Contain("Git");
        result.CombinedOutput.Should().Contain("github");
        result.CombinedOutput.Should().Contain("jsbattig");
        result.CombinedOutput.Should().Contain("tries");
        result.CombinedOutput.Should().Contain("Branch");
        result.CombinedOutput.Should().Contain("master");
    }
    
    [Fact]
    public async Task ReposShow_WithInvalidId_ShouldFail()
    {
        // Arrange
        await LoginAsync();
        
        // Act
        var result = await CliHelper.ExecuteCommandAsync("repos show non-existent-id");
        
        // Assert
        result.Success.Should().BeFalse();
        result.CombinedOutput.Should().ContainAny("not found", "404", "does not exist");
    }
    
    
    [Fact]
    public async Task ReposDelete_ShouldRemoveRepository()
    {
        // Arrange
        await LoginAsync();
        var deleteRepoName = $"{TestRepoName}-to-delete";
        var createResult = await CliHelper.ExecuteCommandAsync(
            $"repos create --name {deleteRepoName} --git-url {TestGitRepo} --cidx-aware false");
        createResult.Success.Should().BeTrue();
        
        // Don't track for cleanup since we're deleting it
        
        // Act
        var deleteResult = await CliHelper.ExecuteCommandAsync($"repos delete {deleteRepoName} --force");
        
        Output.WriteLine($"Delete repo result: {deleteResult.CombinedOutput}");
        
        // Assert
        deleteResult.Success.Should().BeTrue();
        deleteResult.CombinedOutput.Should().Contain("successfully");
        
        // Verify repo is gone
        var showResult = await CliHelper.ExecuteCommandAsync($"repos show {deleteRepoName}");
        showResult.Success.Should().BeFalse();
        showResult.CombinedOutput.Should().ContainAny("not found", "404", "does not exist");
    }
    
    
    [Fact]
    public async Task ReposList_WithJsonFormat_ShouldReturnValidJson()
    {
        // Arrange
        await LoginAsync();
        
        // Ensure any leftover repository is cleaned up first
        try
        {
            await CliHelper.ExecuteCommandAsync($"repos delete {TestRepoName} --force");
            Output.WriteLine($"Cleaned up existing repository: {TestRepoName}");
        }
        catch
        {
            // Repository doesn't exist, which is fine
        }
        
        var createResult = await CliHelper.ExecuteCommandAsync(
            $"repos create --name {TestRepoName} --git-url {TestGitRepo} --cidx-aware false");
        createResult.Success.Should().BeTrue($"Failed to create repository: {createResult.CombinedOutput}");
        
        // Track repo name for cleanup
        CreatedRepoIds.Add(TestRepoName);
        
        // Act
        var result = await CliHelper.ExecuteCommandAsync("repos list --format json");
        
        Output.WriteLine($"JSON list result: {result.CombinedOutput}");
        
        // Assert
        result.Success.Should().BeTrue();
        
        // Parse JSON
        var repos = ParseJsonOutput<List<Dictionary<string, object>>>(result.CombinedOutput);
        repos.Should().NotBeNull();
        repos.Should().HaveCountGreaterThan(0);
        repos[0].Should().ContainKey("name");
        repos[0].Should().ContainKey("path");
        repos[0].Should().ContainKey("type");
    }
    
    [Fact]
    public async Task CIDX_Test1_RepoRegistration_WithCidxFlag_ShouldEnableSemanticIndexing()
    {
        // Essential CIDX Test 1: Repository registration with CIDX enabled
        await LoginAsync();
        var cidxRepoName = $"{TestRepoName}-cidx";
        
        var result = await CliHelper.ExecuteCommandAsync(
            $"repos create --name {cidxRepoName} --git-url {TestGitRepo} --cidx");
        
        Output.WriteLine($"CIDX repo creation result: {result.CombinedOutput}");
        
        result.Success.Should().BeTrue();
        result.CombinedOutput.Should().Contain("successfully");
        CreatedRepoIds.Add(cidxRepoName);
        
        // Verify CIDX is enabled
        var showResult = await CliHelper.ExecuteCommandAsync($"repos show {cidxRepoName}");
        showResult.Success.Should().BeTrue();
        showResult.CombinedOutput.Should().ContainAny("CIDX", "Semantic", "Indexing", "Enabled");
    }

    [Fact]
    public async Task CIDX_Test2_JobLaunch_CidxJobOnCidxRepo_ShouldSucceed()
    {
        // Essential CIDX Test 2: Launch CIDX-enabled job on CIDX-enabled repo
        await LoginAsync();
        var cidxRepoName = $"{TestRepoName}-cidx-job";
        
        // Create CIDX-enabled repo
        var repoResult = await CliHelper.ExecuteCommandAsync(
            $"repos create --name {cidxRepoName} --git-url {TestGitRepo} --cidx");
        repoResult.Success.Should().BeTrue();
        CreatedRepoIds.Add(cidxRepoName);
        
        // Launch CIDX-enabled job
        var jobResult = await CliHelper.ExecuteCommandAsync(
            $"jobs create --repo {cidxRepoName} --prompt \"Test CIDX job\" --cidx");
        
        Output.WriteLine($"CIDX job creation result: {jobResult.CombinedOutput}");
        jobResult.Success.Should().BeTrue();
        jobResult.CombinedOutput.Should().Contain("Job created");
    }

    [Fact] 
    public async Task CIDX_Test3_JobLaunch_NonCidxJobOnCidxRepo_ShouldSucceed()
    {
        // Essential CIDX Test 3: Launch non-CIDX job on CIDX-enabled repo (should work)
        await LoginAsync();
        var cidxRepoName = $"{TestRepoName}-cidx-mixed";
        
        // Create CIDX-enabled repo
        var repoResult = await CliHelper.ExecuteCommandAsync(
            $"repos create --name {cidxRepoName} --git-url {TestGitRepo} --cidx");
        repoResult.Success.Should().BeTrue();
        CreatedRepoIds.Add(cidxRepoName);
        
        // Launch non-CIDX job on CIDX repo (should work fine)
        var jobResult = await CliHelper.ExecuteCommandAsync(
            $"jobs create --repo {cidxRepoName} --prompt \"Test non-CIDX job on CIDX repo\"");
        
        Output.WriteLine($"Non-CIDX job on CIDX repo result: {jobResult.CombinedOutput}");
        jobResult.Success.Should().BeTrue();
        jobResult.CombinedOutput.Should().Contain("Job created");
    }

    [Fact]
    public async Task CIDX_Test4_JobLaunch_CidxJobOnNonCidxRepo_ShouldFail()
    {
        // Essential CIDX Test 4: Fail to launch CIDX job on non-CIDX repo
        await LoginAsync();
        var nonCidxRepoName = $"{TestRepoName}-no-cidx";
        
        // Create non-CIDX repo
        var repoResult = await CliHelper.ExecuteCommandAsync(
            $"repos create --name {nonCidxRepoName} --git-url {TestGitRepo} --cidx-aware false");
        repoResult.Success.Should().BeTrue();
        CreatedRepoIds.Add(nonCidxRepoName);
        
        // Try to launch CIDX job on non-CIDX repo (should fail)
        var jobResult = await CliHelper.ExecuteCommandAsync(
            $"jobs create --repo {nonCidxRepoName} --prompt \"Test CIDX job\" --cidx");
        
        Output.WriteLine($"CIDX job on non-CIDX repo result: {jobResult.CombinedOutput}");
        jobResult.Success.Should().BeFalse();
        jobResult.CombinedOutput.Should().ContainAny("CIDX", "not enabled", "not supported", "incompatible", "error", "failed");
    }
    
    [Fact]
    public async Task ReposCreate_DuplicateName_ShouldFail()
    {
        // Arrange
        await LoginAsync();
        var duplicateRepoName = $"{TestRepoName}-duplicate-test";
        
        // Create first repo
        var firstResult = await CliHelper.ExecuteCommandAsync(
            $"repos create --name {duplicateRepoName} --git-url {TestGitRepo} --cidx-aware false");
        firstResult.Success.Should().BeTrue();
        
        // Track repo name for cleanup
        CreatedRepoIds.Add(duplicateRepoName);
        
        // Act - Try to create with same name
        var secondResult = await CliHelper.ExecuteCommandAsync(
            $"repos create --name {duplicateRepoName} --git-url {TestGitRepo} --cidx-aware false");
        
        // Assert
        secondResult.Success.Should().BeFalse();
        secondResult.CombinedOutput.Should().ContainAny("already exists", "duplicate", "conflict");
    }
}