using System.Text.Json;
using FluentAssertions;

namespace ClaudeBatchServer.IntegrationTests.Helpers;

/// <summary>
/// Helper utilities for Swagger/OpenAPI testing.
/// Provides common functionality for validating OpenAPI documents and schemas.
/// </summary>
public static class SwaggerTestHelper
{
    /// <summary>
    /// Validates that a JSON document represents a valid OpenAPI 3.x specification.
    /// </summary>
    /// <param name="swaggerJsonContent">The Swagger JSON content to validate</param>
    public static void ValidateOpenApiStructure(string swaggerJsonContent)
    {
        swaggerJsonContent.Should().NotBeNullOrEmpty();
        
        using var doc = JsonDocument.Parse(swaggerJsonContent);
        var root = doc.RootElement;
        
        // Validate OpenAPI version
        root.TryGetProperty("openapi", out var openApiVersion).Should().BeTrue("OpenAPI document should have 'openapi' field");
        var version = openApiVersion.GetString();
        version.Should().StartWith("3.", "Should be OpenAPI 3.x");
        
        // Validate required top-level fields
        root.TryGetProperty("info", out var info).Should().BeTrue("OpenAPI document should have 'info' section");
        root.TryGetProperty("paths", out var paths).Should().BeTrue("OpenAPI document should have 'paths' section");
        
        // Validate info section
        info.TryGetProperty("title", out _).Should().BeTrue("Info section should have 'title'");
        info.TryGetProperty("version", out _).Should().BeTrue("Info section should have 'version'");
        
        // Validate paths structure
        paths.ValueKind.Should().Be(JsonValueKind.Object, "Paths should be an object");
        
        // Validate components section if present
        if (root.TryGetProperty("components", out var components))
        {
            components.ValueKind.Should().Be(JsonValueKind.Object, "Components should be an object");
            
            if (components.TryGetProperty("schemas", out var schemas))
            {
                schemas.ValueKind.Should().Be(JsonValueKind.Object, "Schemas should be an object");
            }
        }
    }
    
    /// <summary>
    /// Validates that a specific endpoint is properly documented in the OpenAPI specification.
    /// </summary>
    /// <param name="swaggerDoc">The parsed Swagger document</param>
    /// <param name="path">The endpoint path (e.g., "/auth/login")</param>
    /// <param name="httpMethod">The HTTP method (e.g., "post")</param>
    /// <param name="shouldRequireAuth">Whether the endpoint should require authentication</param>
    /// <param name="expectedStatusCodes">Expected HTTP status codes that should be documented</param>
    public static void ValidateEndpointDocumentation(
        JsonDocument swaggerDoc, 
        string path, 
        string httpMethod, 
        bool shouldRequireAuth = true,
        params int[] expectedStatusCodes)
    {
        var paths = swaggerDoc.RootElement.GetProperty("paths");
        
        paths.TryGetProperty(path, out var pathItem).Should().BeTrue($"Path {path} should be documented");
        pathItem.TryGetProperty(httpMethod.ToLowerInvariant(), out var operation).Should().BeTrue(
            $"Method {httpMethod} should be documented for path {path}");
        
        // Validate responses
        operation.TryGetProperty("responses", out var responses).Should().BeTrue(
            $"Operation {httpMethod} {path} should have responses");
        
        foreach (var statusCode in expectedStatusCodes)
        {
            responses.TryGetProperty(statusCode.ToString(), out _).Should().BeTrue(
                $"Operation {httpMethod} {path} should document {statusCode} response");
        }
        
        // Validate authentication requirements
        if (shouldRequireAuth)
        {
            operation.TryGetProperty("security", out var security).Should().BeTrue(
                $"Protected endpoint {httpMethod} {path} should have security requirements");
                
            // Check for Bearer token security
            bool hasBearerSecurity = false;
            foreach (var securityReq in security.EnumerateArray())
            {
                if (securityReq.TryGetProperty("Bearer", out _))
                {
                    hasBearerSecurity = true;
                    break;
                }
            }
            hasBearerSecurity.Should().BeTrue($"Endpoint {httpMethod} {path} should require Bearer token");
        }
        else
        {
            // For public endpoints, security should either be absent or empty array
            if (operation.TryGetProperty("security", out var security))
            {
                security.GetArrayLength().Should().Be(0, 
                    $"Public endpoint {httpMethod} {path} should not require authentication");
            }
        }
    }
    
    /// <summary>
    /// Validates that a specific schema is properly defined in the OpenAPI components.
    /// </summary>
    /// <param name="swaggerDoc">The parsed Swagger document</param>
    /// <param name="schemaName">The name of the schema to validate</param>
    /// <param name="requiredProperties">Properties that should be marked as required</param>
    /// <param name="expectedProperties">Properties that should exist in the schema</param>
    public static void ValidateSchemaDefinition(
        JsonDocument swaggerDoc, 
        string schemaName, 
        string[]? requiredProperties = null,
        string[]? expectedProperties = null)
    {
        var components = swaggerDoc.RootElement.GetProperty("components");
        var schemas = components.GetProperty("schemas");
        
        schemas.TryGetProperty(schemaName, out var schema).Should().BeTrue(
            $"Schema {schemaName} should be defined");
        
        schema.TryGetProperty("type", out var schemaType).Should().BeTrue(
            $"Schema {schemaName} should have a type");
        
        if (expectedProperties != null)
        {
            schema.TryGetProperty("properties", out var properties).Should().BeTrue(
                $"Schema {schemaName} should have properties");
            
            foreach (var expectedProperty in expectedProperties)
            {
                properties.TryGetProperty(expectedProperty, out _).Should().BeTrue(
                    $"Schema {schemaName} should have property {expectedProperty}");
            }
        }
        
        if (requiredProperties != null && requiredProperties.Length > 0)
        {
            schema.TryGetProperty("required", out var required).Should().BeTrue(
                $"Schema {schemaName} should have required fields");
            
            var requiredFields = required.EnumerateArray().Select(r => r.GetString()).ToArray();
            
            foreach (var requiredProperty in requiredProperties)
            {
                requiredFields.Should().Contain(requiredProperty,
                    $"Schema {schemaName} should mark {requiredProperty} as required");
            }
        }
    }
    
    /// <summary>
    /// Validates that all endpoints with the same tag have consistent documentation patterns.
    /// </summary>
    /// <param name="swaggerDoc">The parsed Swagger document</param>
    /// <param name="tagName">The tag to validate consistency for</param>
    public static void ValidateTagConsistency(JsonDocument swaggerDoc, string tagName)
    {
        var paths = swaggerDoc.RootElement.GetProperty("paths");
        var endpointsWithTag = new List<(string path, string method, JsonElement operation)>();
        
        // Find all endpoints with the specified tag
        foreach (var pathItem in paths.EnumerateObject())
        {
            foreach (var operation in pathItem.Value.EnumerateObject())
            {
                if (operation.Value.ValueKind == JsonValueKind.Object &&
                    operation.Value.TryGetProperty("tags", out var tags))
                {
                    var tagArray = tags.EnumerateArray().Select(t => t.GetString()).ToArray();
                    if (tagArray.Contains(tagName))
                    {
                        endpointsWithTag.Add((pathItem.Name, operation.Name, operation.Value));
                    }
                }
            }
        }
        
        endpointsWithTag.Should().NotBeEmpty($"Should find endpoints with tag {tagName}");
        
        // Validate that all endpoints in the tag have consistent response patterns
        foreach (var (path, method, operation) in endpointsWithTag)
        {
            operation.TryGetProperty("responses", out var responses).Should().BeTrue(
                $"Endpoint {method.ToUpper()} {path} with tag {tagName} should have responses");
            
            // Most endpoints should document 500 Internal Server Error
            responses.TryGetProperty("500", out _).Should().BeTrue(
                $"Endpoint {method.ToUpper()} {path} with tag {tagName} should document 500 response");
        }
    }
    
    /// <summary>
    /// Extracts all documented endpoints from the Swagger specification.
    /// </summary>
    /// <param name="swaggerDoc">The parsed Swagger document</param>
    /// <returns>List of endpoints in format "METHOD /path"</returns>
    public static List<string> ExtractAllEndpoints(JsonDocument swaggerDoc)
    {
        var endpoints = new List<string>();
        var paths = swaggerDoc.RootElement.GetProperty("paths");
        
        foreach (var pathItem in paths.EnumerateObject())
        {
            foreach (var operation in pathItem.Value.EnumerateObject())
            {
                if (operation.Value.ValueKind == JsonValueKind.Object)
                {
                    endpoints.Add($"{operation.Name.ToUpper()} {pathItem.Name}");
                }
            }
        }
        
        return endpoints.OrderBy(e => e).ToList();
    }
    
    /// <summary>
    /// Validates that parameter documentation matches the expected parameter structure.
    /// </summary>
    /// <param name="operation">The operation element from the Swagger document</param>
    /// <param name="paramName">The parameter name to validate</param>
    /// <param name="paramLocation">Where the parameter should be located (path, query, header, etc.)</param>
    /// <param name="isRequired">Whether the parameter should be required</param>
    /// <param name="expectedType">The expected parameter type</param>
    public static void ValidateParameterDocumentation(
        JsonElement operation, 
        string paramName, 
        string paramLocation, 
        bool isRequired, 
        string expectedType)
    {
        operation.TryGetProperty("parameters", out var parameters).Should().BeTrue(
            "Operation should have parameters");
        
        bool foundParameter = false;
        foreach (var param in parameters.EnumerateArray())
        {
            if (param.TryGetProperty("name", out var name) && name.GetString() == paramName)
            {
                foundParameter = true;
                
                param.TryGetProperty("in", out var inLocation).Should().BeTrue();
                inLocation.GetString().Should().Be(paramLocation);
                
                param.TryGetProperty("required", out var required).Should().BeTrue();
                required.GetBoolean().Should().Be(isRequired);
                
                param.TryGetProperty("schema", out var schema).Should().BeTrue();
                schema.TryGetProperty("type", out var type).Should().BeTrue();
                type.GetString().Should().Be(expectedType);
                
                break;
            }
        }
        
        foundParameter.Should().BeTrue($"Parameter {paramName} should be documented");
    }
    
    /// <summary>
    /// Gets all schema names defined in the OpenAPI components section.
    /// </summary>
    /// <param name="swaggerDoc">The parsed Swagger document</param>
    /// <returns>List of schema names</returns>
    public static List<string> GetAllSchemaNames(JsonDocument swaggerDoc)
    {
        var schemaNames = new List<string>();
        
        if (swaggerDoc.RootElement.TryGetProperty("components", out var components) &&
            components.TryGetProperty("schemas", out var schemas))
        {
            foreach (var schema in schemas.EnumerateObject())
            {
                schemaNames.Add(schema.Name);
            }
        }
        
        return schemaNames.OrderBy(name => name).ToList();
    }
}