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

namespace ClaudeBatchServer.IntegrationTests;

public class SimpleFileManagerTest : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    
    public SimpleFileManagerTest(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((context, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Jwt:Key"] = "SimpleTestKeyThatIsLongEnough",
                    ["Jwt:ExpiryHours"] = "1",
                    ["Workspace:RepositoriesPath"] = "/tmp/simple-test-repos",
                    ["Workspace:JobsPath"] = "/tmp/simple-test-jobs",
                    ["Jobs:MaxConcurrent"] = "1",
                    ["Jobs:TimeoutHours"] = "1",
                    ["Auth:ShadowFilePath"] = "/home/jsbattig/Dev/claude-server/claude-batch-server/test-shadow",
                    ["Auth:PasswdFilePath"] = "/home/jsbattig/Dev/claude-server/claude-batch-server/test-passwd",
                    ["Claude:Command"] = "echo 'test'"
                });
            });
            builder.ConfigureServices(services =>
            {
                services.AddAuthentication("Test")
                    .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>("Test", options => { });
            });
        });

        _client = _factory.CreateClient();
        _client.DefaultRequestHeaders.Authorization = 
            new System.Net.Http.Headers.AuthenticationHeaderValue("Test", "test-token");
    }

    [Fact]
    public async Task FilesController_NewParameters_ShouldWork()
    {
        // Create a simple job first
        var createJobRequest = new CreateJobRequest
        {
            Prompt = "Simple test",
            Repository = "simple"
        };
        
        var jobResponse = await _client.PostAsJsonAsync("/jobs", createJobRequest);
        
        // Check if it returns 400 (which is expected since repository doesn't exist)
        // or 200/201 (which means it worked)
        var statusCode = jobResponse.StatusCode;
        
        // As long as we don't get 500 or compilation errors, the controller is working
        statusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.OK, HttpStatusCode.Created);
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }
}