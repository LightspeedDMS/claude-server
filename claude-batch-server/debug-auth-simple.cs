using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using ClaudeBatchServer.Core.DTOs;
using ClaudeBatchServer.Api;
using DotNetEnv;

namespace DebugAuth;

public class SimpleAuthTest
{
    public static async Task Main(string[] args)
    {
        // Load environment variables
        var envPath = "/home/jsbattig/Dev/claude-server/claude-batch-server/.env";
        if (File.Exists(envPath))
        {
            Env.Load(envPath);
        }

        var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((context, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Jwt:Key"] = "DebugTestKeyThatIsLongEnoughForJwtRequirements123",
                        ["Jwt:ExpiryHours"] = "1",
                        ["Workspace:RepositoriesPath"] = "/tmp/debug-repos",
                        ["Workspace:JobsPath"] = "/tmp/debug-jobs",
                        ["Auth:ShadowFilePath"] = "/home/jsbattig/Dev/claude-server/claude-batch-server/test-shadow",
                        ["Auth:PasswdFilePath"] = "/home/jsbattig/Dev/claude-server/claude-batch-server/test-passwd"
                    });
                });
            });

        var client = factory.CreateClient();

        try
        {
            // Step 1: Test login
            Console.WriteLine("=== TESTING LOGIN ===");
            var loginRequest = new LoginRequest 
            { 
                Username = "jsbattig", 
                Password = "test123" 
            };

            var loginResponse = await client.PostAsJsonAsync("/auth/login", loginRequest);
            Console.WriteLine($"Login Status: {loginResponse.StatusCode}");
            
            var loginContent = await loginResponse.Content.ReadAsStringAsync();
            Console.WriteLine($"Login Response: {loginContent}");

            if (loginResponse.StatusCode != HttpStatusCode.OK)
            {
                Console.WriteLine("❌ LOGIN FAILED!");
                return;
            }

            var loginResult = System.Text.Json.JsonSerializer.Deserialize<LoginResponse>(loginContent, 
                new System.Text.Json.JsonSerializerOptions 
                { 
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase 
                });

            var token = loginResult?.Token;
            if (string.IsNullOrEmpty(token))
            {
                Console.WriteLine("❌ NO TOKEN RECEIVED!");
                return;
            }

            Console.WriteLine($"✅ Token received: {token[..Math.Min(50, token.Length)]}...");

            // Step 2: Test authenticated request
            Console.WriteLine("\n=== TESTING AUTHENTICATED REQUEST ===");
            client.DefaultRequestHeaders.Authorization = 
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            var repoResponse = await client.GetAsync("/repositories");
            Console.WriteLine($"Repositories Status: {repoResponse.StatusCode}");
            
            var repoContent = await repoResponse.Content.ReadAsStringAsync();
            Console.WriteLine($"Repositories Response: {repoContent}");

            if (repoResponse.StatusCode == HttpStatusCode.OK)
            {
                Console.WriteLine("✅ AUTHENTICATION WORKING!");
            }
            else
            {
                Console.WriteLine("❌ AUTHENTICATION FAILED!");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ ERROR: {ex.Message}");
            Console.WriteLine($"Stack: {ex.StackTrace}");
        }
        finally
        {
            factory.Dispose();
        }
    }
}