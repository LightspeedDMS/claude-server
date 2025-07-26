using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Xunit;
using Moq;
using ClaudeBatchServer.Core.Services;
using ClaudeBatchServer.Core.Models;
using System.Reflection;

namespace ClaudeBatchServer.Tests.Services;

public class PlaceholderReplacementTests
{
    private readonly Mock<IConfiguration> _mockConfiguration;
    private readonly Mock<ILogger<ClaudeCodeExecutor>> _mockLogger;
    private readonly ClaudeCodeExecutor _executor;

    public PlaceholderReplacementTests()
    {
        _mockConfiguration = new Mock<IConfiguration>();
        _mockLogger = new Mock<ILogger<ClaudeCodeExecutor>>();
        
        // Setup basic configuration
        _mockConfiguration.Setup(c => c["Claude:Command"]).Returns("claude --dangerously-skip-permissions --print");
        
        var mockRepositoryService = new Mock<IRepositoryService>();
        _executor = new ClaudeCodeExecutor(_mockConfiguration.Object, _mockLogger.Object, mockRepositoryService.Object);
    }

    [Fact]
    public void ProcessPromptPlaceholders_WithSpecificFilePlaceholder_ReplacesCorrectly()
    {
        // Arrange
        var prompt = "Please analyze the file {{document.pdf}} and tell me about it.";
        var uploadedFiles = new List<string> { "document.pdf", "image.jpg" };
        
        // Act
        var result = InvokeProcessPromptPlaceholders(prompt, uploadedFiles);
        
        // Assert
        Assert.Equal("Please analyze the file ./files/document.pdf and tell me about it.", result);
    }

    [Fact]
    public void ProcessPromptPlaceholders_WithGenericFilePlaceholder_ReplacesWithAllFiles()
    {
        // Arrange
        var prompt = "Please analyze these files: {{filename}}";
        var uploadedFiles = new List<string> { "document.pdf", "image.jpg", "script.py" };
        
        // Act
        var result = InvokeProcessPromptPlaceholders(prompt, uploadedFiles);
        
        // Assert
        Assert.Equal("Please analyze these files: ./files/document.pdf ./files/image.jpg ./files/script.py", result);
    }

    [Fact]
    public void ProcessPromptPlaceholders_WithMultipleSpecificPlaceholders_ReplacesAll()
    {
        // Arrange
        var prompt = "Compare {{file1.txt}} with {{file2.txt}} and show differences.";
        var uploadedFiles = new List<string> { "file1.txt", "file2.txt", "readme.md" };
        
        // Act
        var result = InvokeProcessPromptPlaceholders(prompt, uploadedFiles);
        
        // Assert
        Assert.Equal("Compare ./files/file1.txt with ./files/file2.txt and show differences.", result);
    }

    [Fact]
    public void ProcessPromptPlaceholders_WithMixedPlaceholders_ReplacesCorrectly()
    {
        // Arrange
        var prompt = "Analyze {{script.py}} and also check all files: {{filename}}";
        var uploadedFiles = new List<string> { "script.py", "config.json" };
        
        // Act
        var result = InvokeProcessPromptPlaceholders(prompt, uploadedFiles);
        
        // Assert
        Assert.Equal("Analyze ./files/script.py and also check all files: ./files/script.py ./files/config.json", result);
    }

    [Fact]
    public void ProcessPromptPlaceholders_WithNonExistentFile_LeavesPlaceholderUnchanged()
    {
        // Arrange
        var prompt = "Please analyze {{nonexistent.txt}} file.";
        var uploadedFiles = new List<string> { "document.pdf" };
        
        // Act
        var result = InvokeProcessPromptPlaceholders(prompt, uploadedFiles);
        
        // Assert
        Assert.Equal("Please analyze {{nonexistent.txt}} file.", result);
    }

    [Fact]
    public void ProcessPromptPlaceholders_WithEmptyUploadedFiles_ReturnsOriginalPrompt()
    {
        // Arrange
        var prompt = "Please analyze {{document.pdf}} and {{filename}}";
        var uploadedFiles = new List<string>();
        
        // Act
        var result = InvokeProcessPromptPlaceholders(prompt, uploadedFiles);
        
        // Assert
        Assert.Equal(prompt, result);
    }

    [Fact]
    public void ProcessPromptPlaceholders_WithNullUploadedFiles_ReturnsOriginalPrompt()
    {
        // Arrange
        var prompt = "Please analyze {{document.pdf}}";
        List<string>? uploadedFiles = null;
        
        // Act
        var result = InvokeProcessPromptPlaceholders(prompt, uploadedFiles);
        
        // Assert
        Assert.Equal(prompt, result);
    }

    [Fact]
    public void ProcessPromptPlaceholders_WithEmptyPrompt_ReturnsEmpty()
    {
        // Arrange
        var prompt = "";
        var uploadedFiles = new List<string> { "document.pdf" };
        
        // Act
        var result = InvokeProcessPromptPlaceholders(prompt, uploadedFiles);
        
        // Assert
        Assert.Equal("", result);
    }

    [Fact]
    public void ProcessPromptPlaceholders_WithFilenamesContainingSpecialCharacters_HandlesCorrectly()
    {
        // Arrange
        var prompt = "Analyze {{my-file_v2.1.pdf}} please.";
        var uploadedFiles = new List<string> { "my-file_v2.1.pdf" };
        
        // Act
        var result = InvokeProcessPromptPlaceholders(prompt, uploadedFiles);
        
        // Assert
        Assert.Equal("Analyze ./files/my-file_v2.1.pdf please.", result);
    }

    [Fact]
    public void ProcessPromptPlaceholders_WithNoPlaceholders_ReturnsOriginalPrompt()
    {
        // Arrange
        var prompt = "This is a regular prompt without any placeholders.";
        var uploadedFiles = new List<string> { "document.pdf", "image.jpg" };
        
        // Act
        var result = InvokeProcessPromptPlaceholders(prompt, uploadedFiles);
        
        // Assert
        Assert.Equal(prompt, result);
    }

    /// <summary>
    /// Helper method to invoke the private ProcessPromptPlaceholders method using reflection
    /// </summary>
    private string InvokeProcessPromptPlaceholders(string prompt, List<string>? uploadedFiles)
    {
        var method = typeof(ClaudeCodeExecutor).GetMethod("ProcessPromptPlaceholders", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);
        
        var result = method.Invoke(_executor, new object?[] { prompt, uploadedFiles });
        return result?.ToString() ?? "";
    }
}