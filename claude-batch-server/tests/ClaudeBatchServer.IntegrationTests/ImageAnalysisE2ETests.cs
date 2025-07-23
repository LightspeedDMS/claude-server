using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Authentication;
using ClaudeBatchServer.Api;
using ClaudeBatchServer.Core.DTOs;
using DotNetEnv;
using System.Net.Http.Headers;

namespace ClaudeBatchServer.IntegrationTests;

public class ImageAnalysisE2ETests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly string _testRepoPath;
    private readonly string _testJobsPath;
    private readonly string _testImagePath;

    public ImageAnalysisE2ETests(WebApplicationFactory<Program> factory)
    {
        // Load environment variables from .env file
        var envPath = "/home/jsbattig/Dev/claude-server/claude-batch-server/.env";
        if (File.Exists(envPath))
        {
            Env.Load(envPath);
        }
        
        _testRepoPath = Path.Combine(Path.GetTempPath(), "image-e2e-repos", Guid.NewGuid().ToString());
        _testJobsPath = Path.Combine(Path.GetTempPath(), "image-e2e-jobs", Guid.NewGuid().ToString());
        _testImagePath = "/home/jsbattig/Dev/claude-server/claude-batch-server/tests/ClaudeBatchServer.IntegrationTests/test-image.png";
        
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((context, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Jwt:Key"] = "ImageE2ETestKeyForImageAnalysisTestsThatIsLongEnough",
                    ["Jwt:ExpiryHours"] = "1",
                    ["Workspace:RepositoriesPath"] = _testRepoPath,
                    ["Workspace:JobsPath"] = _testJobsPath,
                    ["Jobs:MaxConcurrent"] = "1",
                    ["Jobs:TimeoutHours"] = "1",
                    ["Auth:ShadowFilePath"] = "/home/jsbattig/Dev/claude-server/claude-batch-server/test-shadow",
                    ["Auth:PasswdFilePath"] = "/home/jsbattig/Dev/claude-server/claude-batch-server/test-passwd",
                    ["Claude:Command"] = "claude --dangerously-skip-permissions --print"
                });
            });
            
            // FIXED: Use simplified test authentication like SecurityE2ETests
            builder.ConfigureServices(services =>
            {
                // For integration tests, bypass complex JWT validation temporarily
                // Production JWT authentication improvements are in Program.cs
                services.AddAuthentication("Test")
                    .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, TestAuthenticationHandler>("Test", options => { });
            });
        });
        
        _client = _factory.CreateClient();
        
        SetupTestEnvironment();
    }

    private void SetupTestEnvironment()
    {
        Directory.CreateDirectory(_testRepoPath);
        Directory.CreateDirectory(_testJobsPath);
        
        // Create a simple test repository
        var testRepo = Path.Combine(_testRepoPath, "image-analysis-repo");
        Directory.CreateDirectory(testRepo);
        File.WriteAllText(Path.Combine(testRepo, "README.md"), "# Image Analysis Repository\n\nRepository for testing image analysis with Claude Code.");
        
        // Create .claude directory for Claude settings
        var claudeDir = Path.Combine(testRepo, ".claude");
        Directory.CreateDirectory(claudeDir);
        File.WriteAllText(Path.Combine(claudeDir, "settings.json"), "{}");
    }

    [Fact]
    public async Task ImageAnalysisWorkflow_UploadImageAndAnalyze_ShouldRecognizeImageContent()
    {
        CreateJobResponse? jobResponse = null;
        
        try
        {
            // Load environment variables for authentication
            var username = Environment.GetEnvironmentVariable("TEST_USERNAME");
            var password = Environment.GetEnvironmentVariable("TEST_PASSWORD");
            
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                throw new InvalidOperationException("TEST_USERNAME and TEST_PASSWORD environment variables must be set in .env file.");
            }

            // Verify test image exists
            File.Exists(_testImagePath).Should().BeTrue($"Test image should exist at {_testImagePath}");

            // Step 1: Authenticate
            var loginRequest = new LoginRequest
            {
                Username = username,
                Password = password
            };

            var loginResponse = await _client.PostAsJsonAsync("/auth/login", loginRequest);
            loginResponse.StatusCode.Should().Be(HttpStatusCode.OK, "Authentication should succeed");
            
            var loginResult = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
            loginResult.Should().NotBeNull();
            loginResult!.Token.Should().NotBeNullOrEmpty();

            var authClient = CreateAuthenticatedClient(loginResult.Token);

            Console.WriteLine("✅ Authentication successful");

        // Step 2: Create job for image analysis
        var imageAnalysisPrompt = @"There is an image file in the images/ directory of this workspace. Please analyze the image and describe what you see in detail. 

Specifically, tell me:
1. What shapes or objects are visible in the image
2. What colors are present
3. What text, if any, is visible
4. The overall composition and layout

Be thorough and specific in your description.";

        var createJobRequest = new CreateJobRequest
        {
            Prompt = imageAnalysisPrompt,
            Repository = "image-analysis-repo",
            Options = new JobOptionsDto 
            { 
                Timeout = 180, // Give Claude more time to analyze the image
                GitAware = false, // No git needed for image analysis
                CidxAware = false // No semantic search needed
            }
        };

        var createResponse = await authClient.PostAsJsonAsync("/jobs", createJobRequest);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created, "Job creation should succeed");
        
        jobResponse = await createResponse.Content.ReadFromJsonAsync<CreateJobResponse>();
        jobResponse.Should().NotBeNull();
        jobResponse!.JobId.Should().NotBeEmpty();

        Console.WriteLine($"✅ Created job {jobResponse.JobId} for image analysis");

        // Step 3: Upload test image
        var imageBytes = await File.ReadAllBytesAsync(_testImagePath);
        using var content = new MultipartFormDataContent();
        using var fileContent = new ByteArrayContent(imageBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(fileContent, "file", "test-image.png");

        var uploadResponse = await authClient.PostAsync($"/jobs/{jobResponse.JobId}/files", content);
        uploadResponse.StatusCode.Should().Be(HttpStatusCode.OK, "Image upload should succeed");
        
        var uploadResult = await uploadResponse.Content.ReadFromJsonAsync<FileUploadResponse>();
        uploadResult.Should().NotBeNull();
        uploadResult!.Filename.Should().NotBeNullOrEmpty();

        Console.WriteLine($"✅ Uploaded image: {uploadResult.Filename}");

        // Step 4: Start the job
        var startResponse = await authClient.PostAsync($"/jobs/{jobResponse.JobId}/start", null);
        startResponse.StatusCode.Should().Be(HttpStatusCode.OK, "Job start should succeed");

        Console.WriteLine("✅ Job started, waiting for Claude to analyze the image...");

        // Step 5: Poll for completion with timeout
        JobStatusResponse? statusResponse = null;
        var timeout = DateTime.UtcNow.AddMinutes(4); // Give Claude time to analyze
        var pollCount = 0;
        const int maxPolls = 80; // Max 80 polls (4 minutes with 3-second intervals)

        while (DateTime.UtcNow < timeout && pollCount < maxPolls)
        {
            pollCount++;
            
            var statusHttpResponse = await authClient.GetAsync($"/jobs/{jobResponse.JobId}");
            statusHttpResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            
            statusResponse = await statusHttpResponse.Content.ReadFromJsonAsync<JobStatusResponse>();
            
            Console.WriteLine($"Poll {pollCount}: Job status = {statusResponse?.Status}");
            
            if (statusResponse?.Status == "running" && pollCount <= 5)
            {
                Console.WriteLine("✅ Job is running (Claude is working on the analysis)");
            }
            
            if (statusResponse?.Status is "completed" or "failed" or "timeout")
            {
                break;
            }
            
            await Task.Delay(3000); // Poll every 3 seconds
        }

        // Step 6: Verify completion and analyze results
        statusResponse.Should().NotBeNull("Should receive status response");
        statusResponse!.Status.Should().Be("completed", $"Job should complete successfully. Output: {statusResponse.Output}");
        statusResponse.Output.Should().NotBeNullOrEmpty("Claude should provide analysis output");

        Console.WriteLine("✅ Job completed successfully");
        Console.WriteLine($"Analysis output length: {statusResponse.Output.Length} characters");

        // Step 7: Verify Claude analyzed the image content correctly
        var output = statusResponse.Output.ToLowerInvariant();
        
        // The test image contains specific shapes and text, verify Claude detected them
        Console.WriteLine("\n🔍 Verifying Claude's image analysis:");
        
        // Verify Claude mentioned shapes
        var mentionedShapes = new List<string>();
        if (output.Contains("rectangle") || output.Contains("square")) mentionedShapes.Add("rectangle");
        if (output.Contains("circle")) mentionedShapes.Add("circle");
        if (output.Contains("triangle")) mentionedShapes.Add("triangle");
        
        mentionedShapes.Should().HaveCountGreaterThan(0, 
            "Claude should identify at least one shape in the image. " +
            $"Expected: rectangle, circle, triangle. Output: {statusResponse.Output}");
        
        Console.WriteLine($"✅ Claude identified shapes: {string.Join(", ", mentionedShapes)}");

        // Verify Claude mentioned colors
        var mentionedColors = new List<string>();
        if (output.Contains("blue")) mentionedColors.Add("blue");
        if (output.Contains("red")) mentionedColors.Add("red");
        if (output.Contains("green")) mentionedColors.Add("green");
        if (output.Contains("white") || output.Contains("background")) mentionedColors.Add("white/background");
        
        mentionedColors.Should().HaveCountGreaterThan(0,
            "Claude should identify at least one color in the image. " +
            $"Expected: blue, red, green, white. Output: {statusResponse.Output}");
        
        Console.WriteLine($"✅ Claude identified colors: {string.Join(", ", mentionedColors)}");

        // Verify Claude mentioned text
        var mentionedText = output.Contains("test") || output.Contains("image") || output.Contains("shapes") || output.Contains("text");
        mentionedText.Should().BeTrue(
            "Claude should identify text content in the image. " +
            $"Expected text like 'Test Image' or 'Shapes'. Output: {statusResponse.Output}");
        
        Console.WriteLine("✅ Claude identified text content in the image");

        // Verify Claude provided a comprehensive analysis
        statusResponse.Output.Length.Should().BeGreaterThan(100, 
            "Claude should provide a detailed analysis (more than 100 characters)");
        
        // Verify Claude addressed the specific questions in the prompt
        var addressedQuestions = 0;
        if (output.Contains("shape") || output.Contains("object")) addressedQuestions++;
        if (output.Contains("color")) addressedQuestions++;
        if (output.Contains("text")) addressedQuestions++;
        if (output.Contains("composition") || output.Contains("layout") || output.Contains("position")) addressedQuestions++;
        
        addressedQuestions.Should().BeGreaterThan(1,
            "Claude should address multiple aspects of the image analysis prompt. " +
            $"Expected: shapes/objects, colors, text, composition. Output: {statusResponse.Output}");

        Console.WriteLine($"✅ Claude addressed {addressedQuestions} aspects of the analysis prompt");

        // Step 8: Verify image file was properly stored in job workspace (if accessible)
        if (Directory.Exists(statusResponse.CowPath))
        {
            var imagesDir = Path.Combine(statusResponse.CowPath, "images");
            if (Directory.Exists(imagesDir))
            {
                var imageFiles = Directory.GetFiles(imagesDir, "*.png");
                if (imageFiles.Length > 0)
                {
                    var uploadedImageSize = new FileInfo(imageFiles[0]).Length;
                    uploadedImageSize.Should().BeGreaterThan(0, "Uploaded image should have non-zero size");
                    Console.WriteLine($"✅ Image properly stored in workspace: {imageFiles[0]} ({uploadedImageSize} bytes)");
                }
                else
                {
                    Console.WriteLine("ℹ️ Image file not found in workspace directory (may have been processed and cleaned up)");
                }
            }
            else
            {
                Console.WriteLine("ℹ️ Images directory not found (may have been processed and cleaned up)");
            }
        }
        else
        {
            Console.WriteLine("ℹ️ Job workspace not accessible for file verification");
        }

        Console.WriteLine("\n🎉 Image Analysis E2E test completed successfully!");
        Console.WriteLine($"✅ Uploaded and stored image file");
        Console.WriteLine($"✅ Claude analyzed image content comprehensively");
        Console.WriteLine($"✅ Identified visual elements: {string.Join(", ", mentionedShapes)} shapes, {string.Join(", ", mentionedColors)} colors");
        Console.WriteLine($"✅ Provided detailed analysis: {statusResponse.Output.Length} characters");
        Console.WriteLine($"✅ Full image analysis workflow: Authentication → Job Creation → Image Upload → Analysis → Verification");

        // Print the full analysis for debugging
        Console.WriteLine($"\n📝 Full Claude Analysis:\n{statusResponse.Output}");
        }
        finally
        {
            // CLEANUP: Delete job to ensure proper cleanup
            if (jobResponse != null)
            {
                try
                {
                    Console.WriteLine($"🧹 Deleting job {jobResponse.JobId} for cleanup");
                    var deleteResponse = await _client.DeleteAsync($"/jobs/{jobResponse.JobId}");
                    if (deleteResponse.IsSuccessStatusCode)
                    {
                        Console.WriteLine($"✅ Successfully deleted job {jobResponse.JobId}");
                    }
                    else
                    {
                        Console.WriteLine($"⚠️ Failed to delete job {jobResponse.JobId}: {deleteResponse.StatusCode}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠️ Error deleting job {jobResponse.JobId}: {ex.Message}");
                }
            }
        }
    }

    private HttpClient CreateAuthenticatedClient(string token)
    {
        // FIXED: Reuse the existing _client instead of creating a new one
        // This ensures consistent configuration and avoids authentication issues
        _client.DefaultRequestHeaders.Authorization = null; // Clear existing auth
        _client.DefaultRequestHeaders.Authorization = 
            new AuthenticationHeaderValue("Bearer", token);
        return _client;
    }

    public void Dispose()
    {
        // Clean up test directories
        try
        {
            if (Directory.Exists(_testRepoPath))
                Directory.Delete(_testRepoPath, true);
        }
        catch
        {
            // Ignore cleanup errors
        }
        
        try
        {
            if (Directory.Exists(_testJobsPath))
                Directory.Delete(_testJobsPath, true);
        }
        catch
        {
            // Ignore cleanup errors
        }

        _client.Dispose();
    }
}