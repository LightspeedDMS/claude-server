using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ClaudeBatchServer.Api;
using ClaudeBatchServer.Core.DTOs;

namespace ClaudeBatchServer.IntegrationTests;

/// <summary>
/// Tests that validate Swagger schemas match actual API responses.
/// These tests ensure the documentation accurately reflects what the API actually returns.
/// </summary>
public class SwaggerResponseValidationTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly string _tempReposPath;
    private readonly string _tempJobsPath;

    public SwaggerResponseValidationTests(WebApplicationFactory<Program> factory)
    {
        _tempReposPath = Path.Combine(Path.GetTempPath(), "swagger-response-test-repos", Guid.NewGuid().ToString());
        _tempJobsPath = Path.Combine(Path.GetTempPath(), "swagger-response-test-jobs", Guid.NewGuid().ToString());
        
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((context, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Jwt:Key"] = "ResponseValidationTestKeyThatIsLongEnough",
                    ["Jwt:ExpiryHours"] = "1",
                    ["Workspace:RepositoriesPath"] = _tempReposPath,
                    ["Workspace:JobsPath"] = _tempJobsPath,
                    ["Jobs:MaxConcurrent"] = "2",
                    ["Jobs:TimeoutHours"] = "1",
                    ["Claude:Command"] = "echo",
                    ["ASPNETCORE_ENVIRONMENT"] = "Development"
                });
            });
        });
        
        _client = _factory.CreateClient();
        
        // Create directories
        Directory.CreateDirectory(_tempReposPath);
        Directory.CreateDirectory(_tempJobsPath);
    }

    [Fact]
    public async Task HealthEndpoint_ResponseShouldMatchSwaggerSchema()
    {
        // Act - Get actual response
        var healthResponse = await _client.GetAsync("/health");
        var healthContent = await healthResponse.Content.ReadAsStringAsync();
        var actualHealthResponse = JsonDocument.Parse(healthContent);
        
        // Act - Get Swagger schema
        var swaggerResponse = await _client.GetAsync("/swagger/v1/swagger.json");
        var swaggerContent = await swaggerResponse.Content.ReadAsStringAsync();
        var swaggerDoc = JsonDocument.Parse(swaggerContent);
        
        // Assert - Response should match schema structure
        healthResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        
        var actualRoot = actualHealthResponse.RootElement;
        actualRoot.TryGetProperty("status", out var status).Should().BeTrue();
        status.GetString().Should().Be("healthy");
        
        actualRoot.TryGetProperty("timestamp", out var timestamp).Should().BeTrue();
        timestamp.ValueKind.Should().Be(JsonValueKind.String);
        
        actualRoot.TryGetProperty("version", out var version).Should().BeTrue();
        version.GetString().Should().NotBeNullOrEmpty();
        
        actualRoot.TryGetProperty("environment", out var environment).Should().BeTrue();
        environment.GetString().Should().NotBeNullOrEmpty();
        
        // Verify response structure matches what would be expected from Swagger documentation
        var paths = swaggerDoc.RootElement.GetProperty("paths");
        var healthPath = paths.GetProperty("/health").GetProperty("get");
        var responses = healthPath.GetProperty("responses");
        responses.TryGetProperty("200", out var okResponse).Should().BeTrue();
    }

    [Fact]
    public async Task AuthLogin_BadRequestErrorResponse_ShouldMatchSwaggerSchema()
    {
        // Arrange - Invalid login request to trigger 400 response
        var invalidLoginRequest = new LoginRequest
        {
            Username = "", // Empty username should trigger validation error
            Password = ""
        };

        // Act - Get actual error response
        var loginResponse = await _client.PostAsJsonAsync("/auth/login", invalidLoginRequest);
        var loginContent = await loginResponse.Content.ReadAsStringAsync();
        var actualErrorResponse = JsonDocument.Parse(loginContent);
        
        // Act - Get Swagger schema
        var swaggerResponse = await _client.GetAsync("/swagger/v1/swagger.json");
        var swaggerContent = await swaggerResponse.Content.ReadAsStringAsync();
        var swaggerDoc = JsonDocument.Parse(swaggerContent);
        
        // Assert - Response should match AuthErrorResponse schema
        loginResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        
        var actualRoot = actualErrorResponse.RootElement;
        actualRoot.TryGetProperty("error", out var error).Should().BeTrue();
        error.GetString().Should().NotBeNullOrEmpty();
        
        actualRoot.TryGetProperty("errorType", out var errorType).Should().BeTrue();
        errorType.GetString().Should().Be("ValidationError");
        
        actualRoot.TryGetProperty("details", out var details).Should().BeTrue();
        details.GetString().Should().NotBeNullOrEmpty();
        
        // Verify Swagger documents this response structure
        var components = swaggerDoc.RootElement.GetProperty("components");
        var schemas = components.GetProperty("schemas");
        schemas.TryGetProperty("AuthErrorResponse", out var authErrorSchema).Should().BeTrue();
        
        var properties = authErrorSchema.GetProperty("properties");
        properties.TryGetProperty("error", out _).Should().BeTrue();
        properties.TryGetProperty("errorType", out _).Should().BeTrue();
        properties.TryGetProperty("details", out _).Should().BeTrue();
    }

    [Fact]
    public async Task AuthLogin_UnauthorizedErrorResponse_ShouldMatchSwaggerSchema()
    {
        // Arrange - Invalid credentials to trigger 401 response
        var invalidLoginRequest = new LoginRequest
        {
            Username = "nonexistentuser",
            Password = "wrongpassword"
        };

        // Act - Get actual error response
        var loginResponse = await _client.PostAsJsonAsync("/auth/login", invalidLoginRequest);
        var loginContent = await loginResponse.Content.ReadAsStringAsync();
        
        // Assert - Response structure should match expected format
        loginResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        
        if (!string.IsNullOrEmpty(loginContent))
        {
            var actualErrorResponse = JsonDocument.Parse(loginContent);
            var actualRoot = actualErrorResponse.RootElement;
            
            // Should follow AuthErrorResponse structure
            actualRoot.TryGetProperty("error", out var error).Should().BeTrue();
            error.GetString().Should().NotBeNullOrEmpty();
            
            actualRoot.TryGetProperty("errorType", out var errorType).Should().BeTrue();
            errorType.GetString().Should().NotBeNullOrEmpty();
            
            actualRoot.TryGetProperty("details", out var details).Should().BeTrue();
            details.GetString().Should().NotBeNullOrEmpty();
        }
    }

    [Fact]
    public async Task UnauthorizedEndpoint_ShouldReturn401AsDocumented()
    {
        // Act - Try to access protected endpoint without auth
        var jobsResponse = await _client.GetAsync("/jobs");
        
        // Assert - Should return 401 as documented in Swagger
        jobsResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        
        // Verify Swagger documents this behavior
        var swaggerResponse = await _client.GetAsync("/swagger/v1/swagger.json");
        var swaggerContent = await swaggerResponse.Content.ReadAsStringAsync();
        var swaggerDoc = JsonDocument.Parse(swaggerContent);
        
        var paths = swaggerDoc.RootElement.GetProperty("paths");
        var jobsPath = paths.GetProperty("/jobs").GetProperty("get");
        var responses = jobsPath.GetProperty("responses");
        responses.TryGetProperty("401", out _).Should().BeTrue("Jobs endpoint should document 401 response");
    }

    [Fact]
    public async Task NotFoundEndpoint_ShouldReturn404()
    {
        // Act - Try to access non-existent endpoint
        var notFoundResponse = await _client.GetAsync("/nonexistent");
        
        // Assert - Should return 404
        notFoundResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task SwaggerJson_ShouldHaveValidContentType()
    {
        // Act
        var swaggerResponse = await _client.GetAsync("/swagger/v1/swagger.json");
        
        // Assert
        swaggerResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        swaggerResponse.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
        swaggerResponse.Content.Headers.ContentType?.CharSet.Should().BeOneOf("utf-8", null);
    }

    [Fact]
    public async Task SwaggerUI_ShouldHaveValidContentType()
    {
        // Act
        var swaggerUIResponse = await _client.GetAsync("/swagger/index.html");
        
        // Assert
        swaggerUIResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        swaggerUIResponse.Content.Headers.ContentType?.MediaType.Should().Be("text/html");
    }

    [Fact]
    public async Task SwaggerJson_ShouldParseAsValidJson()
    {
        // Act
        var swaggerResponse = await _client.GetAsync("/swagger/v1/swagger.json");
        var jsonContent = await swaggerResponse.Content.ReadAsStringAsync();
        
        // Assert - Should parse without throwing
        var parseAction = () => JsonDocument.Parse(jsonContent);
        parseAction.Should().NotThrow();
        
        // Verify minimum required OpenAPI structure
        using var doc = JsonDocument.Parse(jsonContent);
        var root = doc.RootElement;
        
        root.TryGetProperty("openapi", out var openapi).Should().BeTrue();
        openapi.GetString().Should().StartWith("3.");
        
        root.TryGetProperty("info", out _).Should().BeTrue();
        root.TryGetProperty("paths", out _).Should().BeTrue();
    }

    [Fact]
    public async Task SwaggerJson_ResponseSchemas_ShouldHaveRequiredFields()
    {
        // Act
        var swaggerResponse = await _client.GetAsync("/swagger/v1/swagger.json");
        var jsonContent = await swaggerResponse.Content.ReadAsStringAsync();
        var swaggerDoc = JsonDocument.Parse(jsonContent);
        
        var components = swaggerDoc.RootElement.GetProperty("components");
        var schemas = components.GetProperty("schemas");
        
        // Assert - LoginResponse schema should have token field
        if (schemas.TryGetProperty("LoginResponse", out var loginResponseSchema))
        {
            var properties = loginResponseSchema.GetProperty("properties");
            properties.TryGetProperty("token", out _).Should().BeTrue("LoginResponse should have token field");
            
            // Check if token is marked as required
            if (loginResponseSchema.TryGetProperty("required", out var required))
            {
                var requiredFields = required.EnumerateArray().Select(r => r.GetString()).ToArray();
                requiredFields.Should().Contain("token", "Token should be required in LoginResponse");
            }
        }
        
        // Assert - JobStatusResponse should have essential fields
        if (schemas.TryGetProperty("JobStatusResponse", out var jobStatusSchema))
        {
            var properties = jobStatusSchema.GetProperty("properties");
            properties.TryGetProperty("jobId", out _).Should().BeTrue("JobStatusResponse should have jobId field");
            properties.TryGetProperty("status", out _).Should().BeTrue("JobStatusResponse should have status field");
            properties.TryGetProperty("createdAt", out _).Should().BeTrue("JobStatusResponse should have createdAt field");
        }
    }

    [Fact]
    public async Task SwaggerJson_AllEndpoints_ShouldHave500ErrorResponse()
    {
        // Act
        var swaggerResponse = await _client.GetAsync("/swagger/v1/swagger.json");
        var jsonContent = await swaggerResponse.Content.ReadAsStringAsync();
        var swaggerDoc = JsonDocument.Parse(jsonContent);
        
        var paths = swaggerDoc.RootElement.GetProperty("paths");
        
        // Assert - All endpoints should document 500 Internal Server Error
        var endpointsWithout500 = new List<string>();
        
        foreach (var pathItem in paths.EnumerateObject())
        {
            foreach (var operation in pathItem.Value.EnumerateObject())
            {
                if (operation.Value.ValueKind == JsonValueKind.Object && 
                    operation.Value.TryGetProperty("responses", out var responses))
                {
                    if (!responses.TryGetProperty("500", out _))
                    {
                        endpointsWithout500.Add($"{operation.Name.ToUpper()} {pathItem.Name}");
                    }
                }
            }
        }
        
        endpointsWithout500.Should().BeEmpty(
            $"All endpoints should document 500 responses. Missing: {string.Join(", ", endpointsWithout500)}");
    }

    [Fact]
    public async Task SwaggerJson_FileUploadEndpoint_ShouldDocumentMultipartFormData()
    {
        // Act
        var swaggerResponse = await _client.GetAsync("/swagger/v1/swagger.json");
        var jsonContent = await swaggerResponse.Content.ReadAsStringAsync();
        var swaggerDoc = JsonDocument.Parse(jsonContent);
        
        var paths = swaggerDoc.RootElement.GetProperty("paths");
        var fileUploadEndpoint = paths.GetProperty("/jobs/{jobId}/files").GetProperty("post");
        
        // Assert - Should accept multipart/form-data
        fileUploadEndpoint.TryGetProperty("requestBody", out var requestBody).Should().BeTrue();
        requestBody.TryGetProperty("content", out var content).Should().BeTrue();
        content.TryGetProperty("multipart/form-data", out var multipart).Should().BeTrue();
        
        // Should document file parameter
        multipart.TryGetProperty("schema", out var schema).Should().BeTrue();
        schema.TryGetProperty("properties", out var properties).Should().BeTrue();
        properties.TryGetProperty("file", out var fileProperty).Should().BeTrue();
        
        // File should be of type string with binary format
        fileProperty.TryGetProperty("type", out var fileType).Should().BeTrue();
        fileType.GetString().Should().Be("string");
        fileProperty.TryGetProperty("format", out var fileFormat).Should().BeTrue();
        fileFormat.GetString().Should().Be("binary");
    }

    public void Dispose()
    {
        _client?.Dispose();
        
        // Cleanup temp directories
        try
        {
            if (Directory.Exists(_tempReposPath))
                Directory.Delete(_tempReposPath, recursive: true);
                
            if (Directory.Exists(_tempJobsPath))
                Directory.Delete(_tempJobsPath, recursive: true);
        }
        catch
        {
            // Ignore cleanup errors in tests
        }
    }
}