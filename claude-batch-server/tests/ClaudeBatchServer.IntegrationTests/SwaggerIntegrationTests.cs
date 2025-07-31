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
        loginResponses.TryGetProperty("400", out _).Should().BeTrue();
        loginResponses.TryGetProperty("401", out _).Should().BeTrue();
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
    public async Task SwaggerJson_ShouldContainSecurityDefinitions()
    {
        // Act
        var response = await _client.GetAsync("/swagger/v1/swagger.json");
        var jsonContent = await response.Content.ReadAsStringAsync();
        var swaggerDoc = JsonDocument.Parse(jsonContent);
        var root = swaggerDoc.RootElement;
        
        // Assert - Security definitions for JWT Bearer tokens
        root.TryGetProperty("components", out var components).Should().BeTrue();
        components.TryGetProperty("securitySchemes", out var securitySchemes).Should().BeTrue();
        
        // Should have Bearer token security scheme
        securitySchemes.TryGetProperty("Bearer", out var bearerScheme).Should().BeTrue();
        bearerScheme.TryGetProperty("type", out var schemeType).Should().BeTrue();
        schemeType.GetString().Should().Be("http");
        bearerScheme.TryGetProperty("scheme", out var scheme).Should().BeTrue();
        scheme.GetString().Should().Be("bearer");
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
        
        // Assert - Key DTOs should be documented
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
            "FileInfoResponse",
            "AuthErrorResponse"
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
        
        // Assert - Login endpoint should have proper status codes
        var loginPost = paths.GetProperty("/auth/login").GetProperty("post");
        var loginResponses = loginPost.GetProperty("responses");
        
        loginResponses.TryGetProperty("200", out _).Should().BeTrue("Login should document 200 OK");
        loginResponses.TryGetProperty("400", out _).Should().BeTrue("Login should document 400 Bad Request");
        loginResponses.TryGetProperty("401", out _).Should().BeTrue("Login should document 401 Unauthorized");
        loginResponses.TryGetProperty("500", out _).Should().BeTrue("Login should document 500 Internal Server Error");
        
        // Assert - Jobs GET endpoint should have proper status codes
        var jobsGet = paths.GetProperty("/jobs").GetProperty("get");
        var jobsResponses = jobsGet.GetProperty("responses");
        
        jobsResponses.TryGetProperty("200", out _).Should().BeTrue("Jobs GET should document 200 OK");
        jobsResponses.TryGetProperty("401", out _).Should().BeTrue("Jobs GET should document 401 Unauthorized");
        jobsResponses.TryGetProperty("500", out _).Should().BeTrue("Jobs GET should document 500 Internal Server Error");
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
        
        // Verify responses for file upload
        fileUploadPost.TryGetProperty("responses", out var uploadResponses).Should().BeTrue();
        uploadResponses.TryGetProperty("200", out _).Should().BeTrue();
        uploadResponses.TryGetProperty("400", out _).Should().BeTrue();
        uploadResponses.TryGetProperty("401", out _).Should().BeTrue();
    }

    [Fact]
    public async Task SwaggerJson_AuthorizedEndpointsShouldHaveSecurityRequirement()
    {
        // Act
        var response = await _client.GetAsync("/swagger/v1/swagger.json");
        var jsonContent = await response.Content.ReadAsStringAsync();
        var swaggerDoc = JsonDocument.Parse(jsonContent);
        var paths = swaggerDoc.RootElement.GetProperty("paths");
        
        // Assert - Jobs endpoints should require authorization
        var jobsGet = paths.GetProperty("/jobs").GetProperty("get");
        jobsGet.TryGetProperty("security", out var jobsSecurity).Should().BeTrue();
        
        bool foundBearerSecurity = false;
        foreach (var securityReq in jobsSecurity.EnumerateArray())
        {
            if (securityReq.TryGetProperty("Bearer", out _))
            {
                foundBearerSecurity = true;
                break;
            }
        }
        foundBearerSecurity.Should().BeTrue("Jobs endpoint should require Bearer token");
        
        // Assert - Login endpoint should NOT require authorization
        var loginPost = paths.GetProperty("/auth/login").GetProperty("post");
        var hasLoginSecurity = loginPost.TryGetProperty("security", out _);
        if (hasLoginSecurity)
        {
            // If security is present, it should be empty array to override global security
            loginPost.GetProperty("security").GetArrayLength().Should().Be(0);
        }
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
    public async Task SwaggerJson_ShouldContainProperErrorResponseSchemas()
    {
        // Act
        var response = await _client.GetAsync("/swagger/v1/swagger.json");
        var jsonContent = await response.Content.ReadAsStringAsync();
        var swaggerDoc = JsonDocument.Parse(jsonContent);
        var components = swaggerDoc.RootElement.GetProperty("components");
        var schemas = components.GetProperty("schemas");
        
        // Assert - AuthErrorResponse should be properly defined
        schemas.TryGetProperty("AuthErrorResponse", out var authErrorSchema).Should().BeTrue();
        authErrorSchema.TryGetProperty("properties", out var authErrorProps).Should().BeTrue();
        authErrorProps.TryGetProperty("error", out _).Should().BeTrue();
        authErrorProps.TryGetProperty("errorType", out _).Should().BeTrue();
        authErrorProps.TryGetProperty("details", out _).Should().BeTrue();
        
        // Verify that endpoints reference error schemas
        var paths = swaggerDoc.RootElement.GetProperty("paths");
        var loginPost = paths.GetProperty("/auth/login").GetProperty("post");
        var loginResponses = loginPost.GetProperty("responses");
        var badRequestResponse = loginResponses.GetProperty("400");
        badRequestResponse.TryGetProperty("content", out var badRequestContent).Should().BeTrue();
        badRequestContent.TryGetProperty("application/json", out var jsonResponse).Should().BeTrue();
        jsonResponse.TryGetProperty("schema", out var responseSchema).Should().BeTrue();
        
        // Should reference AuthErrorResponse schema
        if (responseSchema.TryGetProperty("$ref", out var schemaRef))
        {
            schemaRef.GetString().Should().Contain("AuthErrorResponse");
        }
    }

    [Fact]
    public async Task SwaggerJson_ShouldDocumentAllRequiredRequestProperties()
    {
        // Act
        var response = await _client.GetAsync("/swagger/v1/swagger.json");
        var jsonContent = await response.Content.ReadAsStringAsync();
        var swaggerDoc = JsonDocument.Parse(jsonContent);
        var components = swaggerDoc.RootElement.GetProperty("components");
        var schemas = components.GetProperty("schemas");
        
        // Assert - LoginRequest should have required fields
        schemas.TryGetProperty("LoginRequest", out var loginRequestSchema).Should().BeTrue();
        loginRequestSchema.TryGetProperty("required", out var loginRequired).Should().BeTrue();
        var requiredFields = loginRequired.EnumerateArray().Select(r => r.GetString()).ToArray();
        requiredFields.Should().Contain("username");
        requiredFields.Should().Contain("password");
        
        // Assert - CreateJobRequest should have required fields
        schemas.TryGetProperty("CreateJobRequest", out var createJobSchema).Should().BeTrue();
        createJobSchema.TryGetProperty("required", out var jobRequired).Should().BeTrue();
        var jobRequiredFields = jobRequired.EnumerateArray().Select(r => r.GetString()).ToArray();
        jobRequiredFields.Should().Contain("prompt");
        jobRequiredFields.Should().Contain("repository");
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