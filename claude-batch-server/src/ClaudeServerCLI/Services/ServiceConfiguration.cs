using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Extensions.Http;
using ClaudeServerCLI.Models;

namespace ClaudeServerCLI.Services;

public static class ServiceConfiguration
{
    public static IServiceCollection ConfigureServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Configuration options
        services.Configure<ApiClientOptions>(configuration.GetSection("ApiClient"));
        services.Configure<AuthenticationOptions>(configuration.GetSection("Authentication"));

        // Core services
        services.AddSingleton<IConfigService, ConfigService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IPromptService, PromptService>();
        services.AddScoped<IFileUploadService, FileUploadService>();

        // HTTP client with Polly retry policy
        services.AddHttpClient<IApiClient, ApiClient>();

        // Logging
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Warning); // CLI should be quiet by default
        });

        return services;
    }

    private static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                onRetry: (outcome, timespan, retryCount, context) =>
                {
                    // Could log retry attempts here if needed
                });
    }

    public static IHost CreateHost(string[] args)
    {
        var builder = Host.CreateDefaultBuilder(args);
        
        builder.ConfigureAppConfiguration((context, config) =>
        {
            // Add command line arguments to configuration
            config.AddCommandLine(args);
            config.AddEnvironmentVariables("CLAUDE_SERVER_");
            
            // Add default configuration values
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ApiClient:BaseUrl"] = "https://localhost:8443",
                ["ApiClient:TimeoutSeconds"] = "30",
                ["ApiClient:RetryCount"] = "3",
                ["ApiClient:RetryDelayMs"] = "1000",
                ["ApiClient:EnableLogging"] = "false",
                ["Authentication:Profile"] = "default",
                ["Authentication:TokenEnvironmentVariable"] = "CLAUDE_SERVER_TOKEN"
            });
        });

        builder.ConfigureServices((context, services) =>
        {
            services.ConfigureServices(context.Configuration);
        });

        return builder.Build();
    }
}