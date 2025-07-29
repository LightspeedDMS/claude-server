using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using System.Text.Json;

namespace ClaudeServerCLI.IntegrationTests.E2E;

[Collection("TestServer")]
public class RepositoryManagementE2ETests : E2ETestBase
{
    private const string TestGitRepo = "https://github.com/jsbattig/tries.git";
    private const string TestRepoName = "tries-test-repo";
    
    public RepositoryManagementE2ETests(ITestOutputHelper output, TestServerHarness serverHarness) 
        : base(output, serverHarness)
    {
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
        // The output should be a valid table format with headers
        result.CombinedOutput.Should().ContainAny("Name", "Type", "Size", "Status", "Last Modified");
    }
    
    [Fact]
    public async Task ReposCreate_WithGitUrl_ShouldCloneAndRegister()
    {
        // Arrange
        await LoginAsync();
        
        // Act
        var result = await CliHelper.ExecuteCommandAsync(
            $"repos create --name {TestRepoName} --git-url {TestGitRepo}");
        
        Output.WriteLine($"Create repo result: {result.CombinedOutput}");
        
        // Assert
        result.Success.Should().BeTrue();
        result.CombinedOutput.Should().Contain("successfully");
        result.CombinedOutput.Should().Contain(TestRepoName);
        
        // Track repo name for cleanup (since repos delete takes name, not ID)
        CreatedRepoIds.Add(TestRepoName);
        
        // Verify repo appears in list
        var listResult = await CliHelper.ExecuteCommandAsync("repos list");
        listResult.Success.Should().BeTrue();
        listResult.CombinedOutput.Should().Contain(TestRepoName);
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
        var createResult = await CliHelper.ExecuteCommandAsync(
            $"repos create --name {TestRepoName} --git-url {TestGitRepo}");
        createResult.Success.Should().BeTrue();
        
        // Track repo name for cleanup
        CreatedRepoIds.Add(TestRepoName);
        
        // Act
        var result = await CliHelper.ExecuteCommandAsync($"repos show {TestRepoName}");
        
        Output.WriteLine($"Show repo result: {result.CombinedOutput}");
        
        // Assert
        result.Success.Should().BeTrue();
        result.CombinedOutput.Should().Contain(TestRepoName);
        result.CombinedOutput.Should().Contain("Git URL");
        result.CombinedOutput.Should().Contain(TestGitRepo);
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
        var createResult = await CliHelper.ExecuteCommandAsync(
            $"repos create --name {TestRepoName}-to-delete --git-url {TestGitRepo}");
        createResult.Success.Should().BeTrue();
        
        // Don't track for cleanup since we're deleting it
        
        // Act
        var deleteResult = await CliHelper.ExecuteCommandAsync($"repos delete {TestRepoName}-to-delete --force");
        
        Output.WriteLine($"Delete repo result: {deleteResult.CombinedOutput}");
        
        // Assert
        deleteResult.Success.Should().BeTrue();
        deleteResult.CombinedOutput.Should().Contain("successfully");
        
        // Verify repo is gone
        var showResult = await CliHelper.ExecuteCommandAsync($"repos show {TestRepoName}-to-delete");
        showResult.Success.Should().BeFalse();
        showResult.CombinedOutput.Should().ContainAny("not found", "404", "does not exist");
    }
    
    
    [Fact]
    public async Task ReposList_WithJsonFormat_ShouldReturnValidJson()
    {
        // Arrange
        await LoginAsync();
        var createResult = await CliHelper.ExecuteCommandAsync(
            $"repos create --name {TestRepoName} --git-url {TestGitRepo}");
        createResult.Success.Should().BeTrue();
        
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
        repos[0].Should().ContainKey("id");
        repos[0].Should().ContainKey("name");
    }
    
    [Fact]
    public async Task ReposCreate_WithCidxFlag_ShouldEnableSemanticIndexing()
    {
        // Arrange
        await LoginAsync();
        
        // Act
        var result = await CliHelper.ExecuteCommandAsync(
            $"repos create --name {TestRepoName}-cidx --git-url {TestGitRepo} --cidx");
        
        Output.WriteLine($"Create repo with CIDX result: {result.CombinedOutput}");
        
        // Assert
        result.Success.Should().BeTrue();
        result.CombinedOutput.Should().Contain("successfully");
        
        // Track repo name for cleanup
        CreatedRepoIds.Add($"{TestRepoName}-cidx");
        
        // Verify CIDX is enabled
        var showResult = await CliHelper.ExecuteCommandAsync($"repos show {TestRepoName}-cidx");
        showResult.Success.Should().BeTrue();
        showResult.CombinedOutput.Should().ContainAny("CIDX", "Semantic", "Indexing", "Enabled");
    }
    
    [Fact]
    public async Task ReposCreate_DuplicateName_ShouldFail()
    {
        // Arrange
        await LoginAsync();
        
        // Create first repo
        var firstResult = await CliHelper.ExecuteCommandAsync(
            $"repos create --name duplicate-test --git-url {TestGitRepo}");
        firstResult.Success.Should().BeTrue();
        
        // Track repo name for cleanup
        CreatedRepoIds.Add("duplicate-test");
        
        // Act - Try to create with same name
        var secondResult = await CliHelper.ExecuteCommandAsync(
            $"repos create --name duplicate-test --git-url {TestGitRepo}");
        
        // Assert
        secondResult.Success.Should().BeFalse();
        secondResult.CombinedOutput.Should().ContainAny("already exists", "duplicate", "conflict");
    }
}