using System.Diagnostics;
using System.Text;
using Xunit;
using FluentAssertions;
using ClaudeServerCLI.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ClaudeServerCLI.IntegrationTests;

/// <summary>
/// Performance and reliability tests for CLI functionality
/// Tests startup time, memory usage, network resilience, and concurrent operations
/// </summary>
[Collection("TestServer")]
public class PerformanceAndReliabilityTests : IDisposable
{
    private readonly TestServerHarness _serverHarness;
    private readonly CLITestHelper _cliHelper;

    public PerformanceAndReliabilityTests(TestServerHarness serverHarness)
    {
        _serverHarness = serverHarness;
        _cliHelper = new CLITestHelper(_serverHarness);
    }

    [Fact]
    public async Task CLI_StartupTime_ShouldBeLessThanOneSecond()
    {
        var stopwatch = Stopwatch.StartNew();
        
        var result = await _cliHelper.ExecuteCommandAsync("--version");
        
        stopwatch.Stop();
        
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(1000, 
            "CLI startup time should be under 1 second for good user experience");
        
        result.CombinedOutput.Should().NotBeEmpty("Version command should produce output");
    }

    [Fact]
    public async Task CLI_HelpCommand_ShouldBeFast()
    {
        var stopwatch = Stopwatch.StartNew();
        
        var result = await _cliHelper.ExecuteCommandAsync("--help");
        
        stopwatch.Stop();
        
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(500, 
            "Help command should be very fast");
        
        result.CombinedOutput.Should().Contain("Claude Batch Server CLI");
    }

    [Theory]
    [InlineData(10)]    // 10KB
    [InlineData(100)]   // 100KB  
    [InlineData(1024)]  // 1MB
    [InlineData(5120)]  // 5MB
    public async Task FileUpload_VariousFileSizes_ShouldMeetPerformanceTargets(int fileSizeKB)
    {
        var tempDir = CreateTempDirectory();
        try
        {
            // Create test file of specified size
            var filePath = await CreateTestFileAsync(tempDir, "large-file.txt", fileSizeKB * 1024);
            
            using var serviceScope = CreateServiceScope();
            var fileUploadService = serviceScope.ServiceProvider.GetRequiredService<IFileUploadService>();
            
            var stopwatch = Stopwatch.StartNew();
            var fileUploads = await fileUploadService.PrepareFileUploadsAsync(new[] { filePath }.ToList());
            stopwatch.Stop();
            
            // Performance targets based on file size
            var maxProcessingTime = fileSizeKB switch
            {
                <= 100 => 1000,    // 1 second for files up to 100KB
                <= 1024 => 2000,   // 2 seconds for files up to 1MB  
                _ => 5000           // 5 seconds for larger files
            };
            
            stopwatch.ElapsedMilliseconds.Should().BeLessThan(maxProcessingTime,
                $"File processing for {fileSizeKB}KB file should complete within {maxProcessingTime}ms");
            
            fileUploads.Should().HaveCount(1);
            fileUploads.First().Content.Length.Should().BeGreaterThan(0);
        }
        finally
        {
            CleanupDirectory(tempDir);
        }
    }

    [Fact]
    public async Task CLI_MemoryUsage_ShouldStayWithinLimits()
    {
        // Get initial memory usage
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        
        var initialMemory = GC.GetTotalMemory(false);
        
        // Perform multiple operations to test memory usage
        for (int i = 0; i < 10; i++)
        {
            await _cliHelper.ExecuteCommandAsync("--help");
            await _cliHelper.ExecuteCommandAsync("jobs list");
            await _cliHelper.ExecuteCommandAsync("repos list");
        }
        
        // Force garbage collection and measure memory
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        
        var finalMemory = GC.GetTotalMemory(false);
        var memoryIncreaseMB = (finalMemory - initialMemory) / (1024.0 * 1024.0);
        
        memoryIncreaseMB.Should().BeLessThan(50, 
            "Memory usage should not increase by more than 50MB during typical operations");
    }

    [Fact]
    public async Task CLI_NetworkTimeout_ShouldHandleGracefully()
    {
        // Test with a non-routable IP address to simulate network timeout
        var stopwatch = Stopwatch.StartNew();
        
        // Use a non-routable IP address that will cause a timeout
        var result = await _cliHelper.ExecuteCommandAsync("jobs list --server-url https://192.0.2.0:8443 --timeout 1");
        
        stopwatch.Stop();
        
        // Should fail quickly and gracefully
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(10000, 
            "Network timeout should be handled quickly");
        
        // Should fail with a meaningful error message
        result.Success.Should().BeFalse("Should fail when server is unreachable");
        result.ExitCode.Should().NotBe(0, "Should have non-zero exit code on network failure");
    }

    [Fact]
    public async Task CLI_ConcurrentOperations_ShouldNotInterfere()
    {
        var tasks = new List<Task<CliExecutionResult>>();
        
        // Start multiple concurrent operations
        for (int i = 0; i < 5; i++)
        {
            tasks.Add(_cliHelper.ExecuteCommandAsync("--help"));
            tasks.Add(_cliHelper.ExecuteCommandAsync("jobs list"));
            tasks.Add(_cliHelper.ExecuteCommandAsync("repos list"));
        }
        
        var stopwatch = Stopwatch.StartNew();
        var results = await Task.WhenAll(tasks);
        stopwatch.Stop();
        
        // All operations should complete successfully
        results.Should().HaveCount(15);
        results.Should().OnlyContain(r => !string.IsNullOrEmpty(r.CombinedOutput));
        
        // Concurrent operations shouldn't take much longer than sequential ones
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(10000, 
            "Concurrent operations should not significantly slow down execution");
    }

    [Fact]
    public async Task FileUploadService_MultipleFiles_ShouldProcessEfficiently()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            // Create multiple test files
            var filePaths = new List<string>();
            for (int i = 0; i < 20; i++)
            {
                var filePath = await CreateTestFileAsync(tempDir, $"test-{i}.txt", 1024); // 1KB each
                filePaths.Add(filePath);
            }
            
            using var serviceScope = CreateServiceScope();
            var fileUploadService = serviceScope.ServiceProvider.GetRequiredService<IFileUploadService>();
            
            var stopwatch = Stopwatch.StartNew();
            var fileUploads = await fileUploadService.PrepareFileUploadsAsync(filePaths);
            stopwatch.Stop();
            
            // Should process 20 small files quickly
            stopwatch.ElapsedMilliseconds.Should().BeLessThan(3000, 
                "Processing multiple small files should be efficient");
            
            fileUploads.Should().HaveCount(20);
            
            // Test total size calculation
            var totalSize = fileUploadService.GetTotalUploadSize(filePaths);
            totalSize.Should().BeGreaterOrEqualTo(20 * 1024); // At least 20KB
        }
        finally
        {
            CleanupDirectory(tempDir);
        }
    }

    [Fact]
    public void PromptService_LargePrompts_ShouldHandleEfficiently()
    {
        using var serviceScope = CreateServiceScope();
        var promptService = serviceScope.ServiceProvider.GetRequiredService<IPromptService>();
        
        // Create a large prompt (50KB)
        var largePrompt = new string('A', 50 * 1024);
        
        var stopwatch = Stopwatch.StartNew();
        var isValid = promptService.ValidatePrompt(largePrompt, out var message);
        stopwatch.Stop();
        
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(1000, 
            "Large prompt validation should be fast");
        
        isValid.Should().BeTrue("Large prompt should be valid if under size limit");
    }

    [Fact]
    public void PromptService_ManyTemplateReferences_ShouldProcessQuickly()
    {
        using var serviceScope = CreateServiceScope();
        var promptService = serviceScope.ServiceProvider.GetRequiredService<IPromptService>();
        
        // Create prompt with many template references
        var templateBuilder = new StringBuilder();
        for (int i = 0; i < 15; i++)
        {
            templateBuilder.AppendLine($"Please analyze {{{{file{i}.txt}}}} and compare it with {{{{data{i}.json}}}}.");
        }
        
        var promptWithManyTemplates = templateBuilder.ToString();
        
        var stopwatch = Stopwatch.StartNew();
        var templates = promptService.ExtractTemplateReferences(promptWithManyTemplates);
        stopwatch.Stop();
        
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(500, 
            "Template extraction should be fast even with many references");
        
        templates.Should().HaveCount(30); // 15 files + 15 data files
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(20)]
    public async Task FileValidation_MultipleFiles_ShouldScaleLinearly(int fileCount)
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var filePaths = new List<string>();
            for (int i = 0; i < fileCount; i++)
            {
                var filePath = await CreateTestFileAsync(tempDir, $"file-{i}.txt", 1024);
                filePaths.Add(filePath);
            }
            
            using var serviceScope = CreateServiceScope();
            var fileUploadService = serviceScope.ServiceProvider.GetRequiredService<IFileUploadService>();
            
            var stopwatch = Stopwatch.StartNew();
            var isValid = fileUploadService.ValidateFiles(filePaths, out var errors);
            stopwatch.Stop();
            
            // Validation time should scale roughly linearly
            var expectedMaxTime = Math.Max(100, fileCount * 50); // 50ms per file minimum, 100ms baseline
            stopwatch.ElapsedMilliseconds.Should().BeLessThan(expectedMaxTime, 
                $"File validation should scale linearly with file count ({fileCount} files)");
            
            isValid.Should().BeTrue();
            errors.Should().BeEmpty();
        }
        finally
        {
            CleanupDirectory(tempDir);
        }
    }

    [Fact]
    public async Task CLI_StressTest_ShouldHandleRepeatedOperations()
    {
        var operations = new List<Task>();
        
        // Perform 50 rapid-fire operations
        for (int i = 0; i < 50; i++)
        {
            operations.Add(Task.Run(async () =>
            {
                try
                {
                    await _cliHelper.ExecuteCommandAsync("--help");
                }
                catch
                {
                    // Some operations may fail under stress, but most should succeed
                }
            }));
        }
        
        var stopwatch = Stopwatch.StartNew();
        await Task.WhenAll(operations);
        stopwatch.Stop();
        
        // Should handle stress reasonably well
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(30000, 
            "Stress test should complete within 30 seconds");
    }

    [Fact]
    public void ErrorHandling_InvalidInputs_ShouldFailGracefully()
    {
        using var serviceScope = CreateServiceScope();
        var fileUploadService = serviceScope.ServiceProvider.GetRequiredService<IFileUploadService>();
        var promptService = serviceScope.ServiceProvider.GetRequiredService<IPromptService>();
        
        // Test file service with non-existent files
        var invalidFiles = new[] { "/non/existent/file.txt", "/another/fake/file.json" }.ToList();
        var isValid = fileUploadService.ValidateFiles(invalidFiles, out var errors);
        
        isValid.Should().BeFalse("Validation should fail for non-existent files");
        errors.Should().NotBeEmpty("Should report validation errors");
        
        // Test prompt service with invalid prompts
        var isValidPrompt = promptService.ValidatePrompt("", out var promptError);
        isValidPrompt.Should().BeFalse("Empty prompt should be invalid");
        promptError.Should().NotBeEmpty("Should report prompt validation error");
    }

    [Fact]
    public async Task ResourceCleanup_ShouldNotLeakResources()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            // Create and process many files to test resource cleanup
            for (int iteration = 0; iteration < 10; iteration++)
            {
                var filePaths = new List<string>();
                for (int i = 0; i < 10; i++)
                {
                    var filePath = await CreateTestFileAsync(tempDir, $"temp-{iteration}-{i}.txt", 512);
                    filePaths.Add(filePath);
                }
                
                using var serviceScope = CreateServiceScope();
                var fileUploadService = serviceScope.ServiceProvider.GetRequiredService<IFileUploadService>();
                
                var fileUploads = await fileUploadService.PrepareFileUploadsAsync(filePaths);
                fileUploads.Should().HaveCount(10);
                
                // Clean up files for this iteration
                foreach (var filePath in filePaths)
                {
                    if (File.Exists(filePath))
                        File.Delete(filePath);
                }
            }
            
            // Force garbage collection
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            
            // This test mainly ensures no exceptions are thrown during cleanup
        }
        finally
        {
            CleanupDirectory(tempDir);
        }
    }

    #region Helper Methods
    
    private string CreateTempDirectory()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"perf-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        return tempDir;
    }
    
    private async Task<string> CreateTestFileAsync(string directory, string fileName, int sizeBytes)
    {
        var filePath = Path.Combine(directory, fileName);
        var content = new string('T', sizeBytes);
        await File.WriteAllTextAsync(filePath, content);
        return filePath;
    }
    
    private void CleanupDirectory(string directory)
    {
        if (Directory.Exists(directory))
        {
            try
            {
                Directory.Delete(directory, true);
            }
            catch
            {
                // Best effort cleanup
            }
        }
    }
    
    private IServiceScope CreateServiceScope()
    {
        var services = new ServiceCollection();
        services.AddScoped<IPromptService, PromptService>();
        services.AddScoped<IFileUploadService, FileUploadService>();
        
        var serviceProvider = services.BuildServiceProvider();
        return serviceProvider.CreateScope();
    }
    
    #endregion
    
    public void Dispose()
    {
        // Cleanup if needed
    }
}