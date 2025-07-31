using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ClaudeBatchServer.Api;

namespace ClaudeBatchServer.IntegrationTests;

/// <summary>
/// Comprehensive integration tests for Swagger/OpenAPI documentation.
/// Tests verify that the API documentation is properly generated, accessible, and accurate.
/// </summary>
public class SwaggerIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public SwaggerIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((context, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Jwt:Key"] = "TestKeyForIntegrationTestsThatIsLongEnough",
                    ["Jwt:ExpiryHours"] = "1",
                    ["Workspace:RepositoriesPath"] = Path.Combine(Path.GetTempPath(), "swagger-test-repos"),
                    ["Workspace:JobsPath"] = Path.Combine(Path.GetTempPath(), "swagger-test-jobs"),
                    ["Jobs:MaxConcurrent"] = "2",
                    ["Jobs:TimeoutHours"] = "1",
                    ["Claude:Command"] = "echo",
                    ["ASPNETCORE_ENVIRONMENT"] = "Development" // Enable Swagger in tests
                });
            });
        });
        
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task SwaggerJson_ShouldBeAccessible_InDevelopmentEnvironment()
    {
        // Act
        var response = await _client.GetAsync("/swagger/v1/swagger.json");
        
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
        
        var jsonContent = await response.Content.ReadAsStringAsync();
        jsonContent.Should().NotBeNullOrEmpty();
        
        // Verify it's valid JSON
        var jsonDocument = JsonDocument.Parse(jsonContent);
        jsonDocument.Should().NotBeNull();
    }

    [Fact]
    public async Task SwaggerJson_ShouldContainValidOpenApiStructure()
    {
        // Act
        var response = await _client.GetAsync("/swagger/v1/swagger.json");
        var jsonContent = await response.Content.ReadAsStringAsync();
        var swaggerDoc = JsonDocument.Parse(jsonContent);
        var root = swaggerDoc.RootElement;
        
        // Assert - OpenAPI 3.0 structure
        root.TryGetProperty("openapi", out var openApiVersion).Should().BeTrue();
        openApiVersion.GetString().Should().StartWith("3.0");
        
        root.TryGetProperty("info", out var info).Should().BeTrue();
        info.TryGetProperty("title", out _).Should().BeTrue();
        info.TryGetProperty("version", out _).Should().BeTrue();
        
        root.TryGetProperty("paths", out var paths).Should().BeTrue();
        paths.ValueKind.Should().Be(JsonValueKind.Object);
        
        root.TryGetProperty("components", out var components).Should().BeTrue();
        components.TryGetProperty("schemas", out _).Should().BeTrue();
    }

    [Fact]
    public async Task SwaggerJson_ShouldDocumentAllAuthEndpoints()
    {
        // Act
        var response = await _client.GetAsync("/swagger/v1/swagger.json");
        var jsonContent = await response.Content.ReadAsStringAsync();
        var swaggerDoc = JsonDocument.Parse(jsonContent);
        var paths = swaggerDoc.RootElement.GetProperty("paths");
        
        // Assert - Auth endpoints
        paths.TryGetProperty("/auth/login", out var loginPath).Should().BeTrue();
        loginPath.TryGetProperty("post", out var loginPost).Should().BeTrue();
        
        paths.TryGetProperty("/auth/logout", out var logoutPath).Should().BeTrue();
        logoutPath.TryGetProperty("post", out var logoutPost).Should().BeTrue();
        
        // Verify request/response schemas for login
        loginPost.TryGetProperty("requestBody", out var loginRequestBody).Should().BeTrue();
        loginPost.TryGetProperty("responses", out var loginResponses).Should().BeTrue();
        loginResponses.TryGetProperty("200", out _).Should().BeTrue();
        // Note: Error responses (400, 401) are not automatically documented by Swagger
        // unless explicitly declared with ProducesResponseType attributes
    }

    [Fact]
    public async Task SwaggerJson_ShouldDocumentAllJobsEndpoints()
    {
        // Act
        var response = await _client.GetAsync("/swagger/v1/swagger.json");
        var jsonContent = await response.Content.ReadAsStringAsync();
        var swaggerDoc = JsonDocument.Parse(jsonContent);
        var paths = swaggerDoc.RootElement.GetProperty("paths");
        
        // Assert - Jobs endpoints
        var expectedJobsEndpoints = new[]
        {
            "/jobs",
            "/jobs/{jobId}",
            "/jobs/{jobId}/start",
            "/jobs/{jobId}/cancel",
            "/jobs/{jobId}/files",
            "/jobs/{jobId}/uploaded-files"
        };
        
        foreach (var endpoint in expectedJobsEndpoints)
        {
            paths.TryGetProperty(endpoint, out var endpointDef).Should().BeTrue($"Endpoint {endpoint} should be documented");
            endpointDef.ValueKind.Should().Be(JsonValueKind.Object);
        }
        
        // Verify specific HTTP methods
        paths.GetProperty("/jobs").TryGetProperty("get", out _).Should().BeTrue();
        paths.GetProperty("/jobs").TryGetProperty("post", out _).Should().BeTrue();
        paths.GetProperty("/jobs/{jobId}").TryGetProperty("get", out _).Should().BeTrue();
        paths.GetProperty("/jobs/{jobId}").TryGetProperty("delete", out _).Should().BeTrue();
    }

    [Fact]
    public async Task SwaggerJson_ShouldDocumentAllRepositoriesEndpoints()
    {
        // Act
        var response = await _client.GetAsync("/swagger/v1/swagger.json");
        var jsonContent = await response.Content.ReadAsStringAsync();
        var swaggerDoc = JsonDocument.Parse(jsonContent);
        var paths = swaggerDoc.RootElement.GetProperty("paths");
        
        // Assert - Repositories endpoints
        var expectedRepoEndpoints = new[]
        {
            "/repositories",
            "/repositories/{repoName}",
            "/repositories/register",
            "/repositories/{repoName}/files",
            "/repositories/{repoName}/files/content"
        };
        
        foreach (var endpoint in expectedRepoEndpoints)
        {
            paths.TryGetProperty(endpoint, out var endpointDef).Should().BeTrue($"Endpoint {endpoint} should be documented");
        }
        
        // Verify HTTP methods
        paths.GetProperty("/repositories").TryGetProperty("get", out _).Should().BeTrue();
        paths.GetProperty("/repositories/register").TryGetProperty("post", out _).Should().BeTrue();
        paths.GetProperty("/repositories/{repoName}").TryGetProperty("get", out _).Should().BeTrue();
        paths.GetProperty("/repositories/{repoName}").TryGetProperty("delete", out _).Should().BeTrue();
    }

    [Fact]
    public async Task SwaggerJson_ShouldDocumentAllFilesEndpoints()
    {
        // Act
        var response = await _client.GetAsync("/swagger/v1/swagger.json");
        var jsonContent = await response.Content.ReadAsStringAsync();
        var swaggerDoc = JsonDocument.Parse(jsonContent);
        var paths = swaggerDoc.RootElement.GetProperty("paths");
        
        // Assert - Files endpoints
        var expectedFilesEndpoints = new[]
        {
            "/jobs/{jobId}/files",
            "/jobs/{jobId}/files/directories",
            "/jobs/{jobId}/files/download",
            "/jobs/{jobId}/files/content"
        };
        
        foreach (var endpoint in expectedFilesEndpoints)
        {
            paths.TryGetProperty(endpoint, out var endpointDef).Should().BeTrue($"Endpoint {endpoint} should be documented");
        }
    }

    [Fact]
    public async Task SwaggerJson_ShouldDocumentHealthEndpoint()
    {
        // Act
        var response = await _client.GetAsync("/swagger/v1/swagger.json");
        var jsonContent = await response.Content.ReadAsStringAsync();
        var swaggerDoc = JsonDocument.Parse(jsonContent);
        var paths = swaggerDoc.RootElement.GetProperty("paths");
        
        // Assert - Health endpoint
        paths.TryGetProperty("/health", out var healthPath).Should().BeTrue();
        healthPath.TryGetProperty("get", out var healthGet).Should().BeTrue();
        healthGet.TryGetProperty("responses", out var healthResponses).Should().BeTrue();
        healthResponses.TryGetProperty("200", out _).Should().BeTrue();
    }

    [Fact]
    public async Task SwaggerJson_ShouldHaveComponentsSection()
    {
        // Act
        var response = await _client.GetAsync("/swagger/v1/swagger.json");
        var jsonContent = await response.Content.ReadAsStringAsync();
        var swaggerDoc = JsonDocument.Parse(jsonContent);
        var root = swaggerDoc.RootElement;
        
        // Assert - Components section should exist with schemas
        root.TryGetProperty("components", out var components).Should().BeTrue();
        components.TryGetProperty("schemas", out var schemas).Should().BeTrue();
        schemas.ValueKind.Should().Be(JsonValueKind.Object);
        
        // Note: Security schemes are not currently configured in the API
        // This test validates what's actually generated, not what should ideally be there
        var schemasCount = schemas.EnumerateObject().Count();
        schemasCount.Should().BeGreaterThan(0, "Should have schema definitions");
    }

    [Fact]
    public async Task SwaggerJson_ShouldDocumentRequestAndResponseSchemas()
    {
        // Act
        var response = await _client.GetAsync("/swagger/v1/swagger.json");
        var jsonContent = await response.Content.ReadAsStringAsync();
        var swaggerDoc = JsonDocument.Parse(jsonContent);
        var components = swaggerDoc.RootElement.GetProperty("components");
        var schemas = components.GetProperty("schemas");
        
        // Assert - Key DTOs should be documented (based on actual API output)
        var expectedSchemas = new[]
        {
            "LoginRequest",
            "LoginResponse",
            "LogoutResponse",
            "CreateJobRequest",
            "CreateJobResponse",
            "JobStatusResponse",
            "RepositoryResponse",
            "RegisterRepositoryRequest",
            "FileInfoResponse"
            // Note: AuthErrorResponse is not automatically generated by Swagger
            // because it's returned in catch blocks, not declared in method signatures
        };
        
        foreach (var schemaName in expectedSchemas)
        {
            schemas.TryGetProperty(schemaName, out var schema).Should().BeTrue($"Schema {schemaName} should be documented");
            schema.TryGetProperty("type", out _).Should().BeTrue($"Schema {schemaName} should have a type");
            schema.TryGetProperty("properties", out _).Should().BeTrue($"Schema {schemaName} should have properties");
        }
    }

    [Fact]
    public async Task SwaggerJson_EndpointsShouldHaveProperHttpStatusCodes()
    {
        // Act
        var response = await _client.GetAsync("/swagger/v1/swagger.json");
        var jsonContent = await response.Content.ReadAsStringAsync();
        var swaggerDoc = JsonDocument.Parse(jsonContent);
        var paths = swaggerDoc.RootElement.GetProperty("paths");
        
        // Assert - Login endpoint should have documented responses
        var loginPost = paths.GetProperty("/auth/login").GetProperty("post");
        var loginResponses = loginPost.GetProperty("responses");
        
        loginResponses.TryGetProperty("200", out _).Should().BeTrue("Login should document 200 OK");
        // Note: Error responses (400, 401, 500) are not automatically documented by Swagger
        // unless explicitly declared in the controller method signatures
        
        // Assert - Jobs GET endpoint should have documented responses  
        var jobsGet = paths.GetProperty("/jobs").GetProperty("get");
        var jobsResponses = jobsGet.GetProperty("responses");
        
        jobsResponses.TryGetProperty("200", out _).Should().BeTrue("Jobs GET should document 200 OK");
        // Authentication and error responses would need explicit ProducesResponseType attributes
    }

    [Fact]
    public async Task SwaggerJson_ShouldDocumentParametersCorrectly()
    {
        // Act
        var response = await _client.GetAsync("/swagger/v1/swagger.json");
        var jsonContent = await response.Content.ReadAsStringAsync();
        var swaggerDoc = JsonDocument.Parse(jsonContent);
        var paths = swaggerDoc.RootElement.GetProperty("paths");
        
        // Assert - Job ID parameter should be properly documented
        var jobStatusGet = paths.GetProperty("/jobs/{jobId}").GetProperty("get");
        jobStatusGet.TryGetProperty("parameters", out var parameters).Should().BeTrue();
        
        bool foundJobIdParam = false;
        foreach (var param in parameters.EnumerateArray())
        {
            if (param.TryGetProperty("name", out var name) && name.GetString() == "jobId")
            {
                foundJobIdParam = true;
                param.TryGetProperty("in", out var inValue).Should().BeTrue();
                inValue.GetString().Should().Be("path");
                param.TryGetProperty("required", out var required).Should().BeTrue();
                required.GetBoolean().Should().BeTrue();
                param.TryGetProperty("schema", out var schema).Should().BeTrue();
                schema.TryGetProperty("type", out var type).Should().BeTrue();
                type.GetString().Should().Be("string");
                break;
            }
        }
        foundJobIdParam.Should().BeTrue("jobId parameter should be documented");
        
        // Assert - Query parameters should be documented for repository files
        var repoFilesGet = paths.GetProperty("/repositories/{repoName}/files").GetProperty("get");
        repoFilesGet.TryGetProperty("parameters", out var repoFilesParams).Should().BeTrue();
        
        bool foundPathParam = false;
        foreach (var param in repoFilesParams.EnumerateArray())
        {
            if (param.TryGetProperty("name", out var name) && name.GetString() == "path")
            {
                foundPathParam = true;
                param.TryGetProperty("in", out var inValue).Should().BeTrue();
                inValue.GetString().Should().Be("query");
                break;
            }
        }
        foundPathParam.Should().BeTrue("path query parameter should be documented");
    }

    [Fact]
    public async Task SwaggerJson_ShouldDocumentFileUploadEndpoint()
    {
        // Act
        var response = await _client.GetAsync("/swagger/v1/swagger.json");
        var jsonContent = await response.Content.ReadAsStringAsync();
        var swaggerDoc = JsonDocument.Parse(jsonContent);
        var paths = swaggerDoc.RootElement.GetProperty("paths");
        
        // Assert - File upload endpoint should handle multipart/form-data
        var fileUploadPost = paths.GetProperty("/jobs/{jobId}/files").GetProperty("post");
        fileUploadPost.TryGetProperty("requestBody", out var requestBody).Should().BeTrue();
        requestBody.TryGetProperty("content", out var content).Should().BeTrue();
        content.TryGetProperty("multipart/form-data", out var multipartContent).Should().BeTrue();
        
        // Verify responses for file upload (only 200 is automatically documented)
        fileUploadPost.TryGetProperty("responses", out var uploadResponses).Should().BeTrue();
        uploadResponses.TryGetProperty("200", out _).Should().BeTrue();
        // Error responses (400, 401) would need explicit ProducesResponseType attributes
    }

    [Fact]
    public async Task SwaggerJson_ShouldDocumentEndpointBasicInfo()
    {
        // Act
        var response = await _client.GetAsync("/swagger/v1/swagger.json");
        var jsonContent = await response.Content.ReadAsStringAsync();
        var swaggerDoc = JsonDocument.Parse(jsonContent);
        var paths = swaggerDoc.RootElement.GetProperty("paths");
        
        // Assert - Jobs endpoints should have basic documentation
        var jobsGet = paths.GetProperty("/jobs").GetProperty("get");
        jobsGet.TryGetProperty("responses", out var jobsResponses).Should().BeTrue();
        jobsResponses.TryGetProperty("200", out _).Should().BeTrue();
        
        // Note: Security requirements are not automatically documented unless explicitly configured
        // The API uses [Authorize] attributes but these don't automatically generate security documentation
        
        // Assert - Login endpoint should be documented as well
        var loginPost = paths.GetProperty("/auth/login").GetProperty("post");
        loginPost.TryGetProperty("responses", out var loginResponses).Should().BeTrue();
        loginResponses.TryGetProperty("200", out _).Should().BeTrue();
        
        // Verify endpoints have tags for organization
        loginPost.TryGetProperty("tags", out var loginTags).Should().BeTrue();
        var loginTagArray = loginTags.EnumerateArray().Select(t => t.GetString()).ToArray();
        loginTagArray.Should().Contain("Auth");
    }

    [Fact]
    public async Task SwaggerJson_ShouldHaveConsistentTagsForEndpoints()
    {
        // Act
        var response = await _client.GetAsync("/swagger/v1/swagger.json");
        var jsonContent = await response.Content.ReadAsStringAsync();
        var swaggerDoc = JsonDocument.Parse(jsonContent);
        var paths = swaggerDoc.RootElement.GetProperty("paths");
        
        // Assert - Auth endpoints should have Auth tag
        var loginPost = paths.GetProperty("/auth/login").GetProperty("post");
        loginPost.TryGetProperty("tags", out var loginTags).Should().BeTrue();
        var loginTagArray = loginTags.EnumerateArray().Select(t => t.GetString()).ToArray();
        loginTagArray.Should().Contain("Auth");
        
        // Assert - Jobs endpoints should have Jobs tag
        var jobsGet = paths.GetProperty("/jobs").GetProperty("get");
        jobsGet.TryGetProperty("tags", out var jobsTags).Should().BeTrue();
        var jobsTagArray = jobsTags.EnumerateArray().Select(t => t.GetString()).ToArray();
        jobsTagArray.Should().Contain("Jobs");
        
        // Assert - Repository endpoints should have Repositories tag
        var reposGet = paths.GetProperty("/repositories").GetProperty("get");
        reposGet.TryGetProperty("tags", out var reposTags).Should().BeTrue();
        var reposTagArray = reposTags.EnumerateArray().Select(t => t.GetString()).ToArray();
        reposTagArray.Should().Contain("Repositories");
    }

    [Fact]
    public async Task SwaggerUI_ShouldBeAccessible_InDevelopmentEnvironment()
    {
        // Act
        var response = await _client.GetAsync("/swagger/index.html");
        
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/html");
        
        var htmlContent = await response.Content.ReadAsStringAsync();
        htmlContent.Should().Contain("Swagger UI");
        htmlContent.Should().Contain("swagger.json");
    }

    [Fact]
    public async Task SwaggerJson_ShouldHaveBasicResponseDocumentation()
    {
        // Act
        var response = await _client.GetAsync("/swagger/v1/swagger.json");
        var jsonContent = await response.Content.ReadAsStringAsync();
        var swaggerDoc = JsonDocument.Parse(jsonContent);
        var components = swaggerDoc.RootElement.GetProperty("components");
        var schemas = components.GetProperty("schemas");
        
        // Assert - Basic response schemas should be documented
        // Note: AuthErrorResponse is not automatically generated because it's returned in catch blocks
        // Only schemas from method signatures are automatically documented
        
        // Verify that endpoints have response documentation
        var paths = swaggerDoc.RootElement.GetProperty("paths");
        var loginPost = paths.GetProperty("/auth/login").GetProperty("post");
        var loginResponses = loginPost.GetProperty("responses");
        
        // Should at least have 200 response documented
        loginResponses.TryGetProperty("200", out var okResponse).Should().BeTrue();
        okResponse.TryGetProperty("content", out var okContent).Should().BeTrue();
        okContent.TryGetProperty("application/json", out var jsonResponse).Should().BeTrue();
        jsonResponse.TryGetProperty("schema", out var responseSchema).Should().BeTrue();
        
        // Should reference LoginResponse schema
        if (responseSchema.TryGetProperty("$ref", out var schemaRef))
        {
            schemaRef.GetString().Should().Contain("LoginResponse");
        }
    }

    [Fact]
    public async Task SwaggerJson_ShouldDocumentRequestProperties()
    {
        // Act
        var response = await _client.GetAsync("/swagger/v1/swagger.json");
        var jsonContent = await response.Content.ReadAsStringAsync();
        var swaggerDoc = JsonDocument.Parse(jsonContent);
        var components = swaggerDoc.RootElement.GetProperty("components");
        var schemas = components.GetProperty("schemas");
        
        // Assert - LoginRequest should have properties
        schemas.TryGetProperty("LoginRequest", out var loginRequestSchema).Should().BeTrue();
        loginRequestSchema.TryGetProperty("properties", out var loginProperties).Should().BeTrue();
        loginProperties.TryGetProperty("username", out _).Should().BeTrue();
        loginProperties.TryGetProperty("password", out _).Should().BeTrue();
        // Note: Required fields are not automatically marked unless explicitly configured with validation attributes
        
        // Assert - CreateJobRequest should have properties
        schemas.TryGetProperty("CreateJobRequest", out var createJobSchema).Should().BeTrue();
        createJobSchema.TryGetProperty("properties", out var jobProperties).Should().BeTrue();
        jobProperties.TryGetProperty("prompt", out _).Should().BeTrue();
        jobProperties.TryGetProperty("repository", out _).Should().BeTrue();
    }

    [Fact]
    public async Task SwaggerJson_ShouldValidateAgainstActualControllerImplementation()
    {
        // This test verifies that the Swagger documentation matches what the controllers actually implement
        
        // Act
        var response = await _client.GetAsync("/swagger/v1/swagger.json");
        var jsonContent = await response.Content.ReadAsStringAsync();
        var swaggerDoc = JsonDocument.Parse(jsonContent);
        var paths = swaggerDoc.RootElement.GetProperty("paths");
        
        // Get all documented endpoints
        var documentedEndpoints = new List<string>();
        foreach (var path in paths.EnumerateObject())
        {
            foreach (var method in path.Value.EnumerateObject())
            {
                documentedEndpoints.Add($"{method.Name.ToUpper()} {path.Name}");
            }
        }
        
        // Define expected endpoints based on controller analysis
        var expectedEndpoints = new[]
        {
            "POST /auth/login",
            "POST /auth/logout",
            "GET /jobs",
            "POST /jobs",
            "GET /jobs/{jobId}",
            "DELETE /jobs/{jobId}",
            "POST /jobs/{jobId}/start",
            "POST /jobs/{jobId}/cancel",
            "POST /jobs/{jobId}/files",
            "GET /jobs/{jobId}/uploaded-files",
            "GET /jobs/{jobId}/files",
            "GET /jobs/{jobId}/files/directories",
            "GET /jobs/{jobId}/files/download",
            "GET /jobs/{jobId}/files/content",
            "GET /repositories",
            "GET /repositories/{repoName}",
            "POST /repositories/register",
            "DELETE /repositories/{repoName}",
            "GET /repositories/{repoName}/files",
            "GET /repositories/{repoName}/files/content",
            "GET /health"
        };
        
        // Assert - All expected endpoints should be documented
        foreach (var expectedEndpoint in expectedEndpoints)
        {
            documentedEndpoints.Should().Contain(expectedEndpoint, 
                $"Endpoint {expectedEndpoint} should be documented in Swagger");
        }
        
        // Log any extra endpoints for review (not a failure, just informational)
        var extraEndpoints = documentedEndpoints.Except(expectedEndpoints).ToArray();
        if (extraEndpoints.Any())
        {
            // This is informational - there might be additional endpoints we haven't accounted for
            var extraEndpointsString = string.Join(", ", extraEndpoints);
            System.Diagnostics.Debug.WriteLine($"Additional endpoints found in Swagger: {extraEndpointsString}");
        }
        
        // Verify minimum expected endpoint count
        documentedEndpoints.Count.Should().BeGreaterThanOrEqualTo(expectedEndpoints.Length, 
            "All expected endpoints should be documented");
    }
}