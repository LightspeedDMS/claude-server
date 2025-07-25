using System.Text;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.IdentityModel.JsonWebTokens;
using Serilog;
using ClaudeBatchServer.Core.Services;
using ClaudeBatchServer.Api;
using System.IdentityModel.Tokens.Jwt;

var builder = WebApplication.CreateBuilder(args);

// FIXED: Clear default JWT claim mappings to prevent automatic conversion
// This is critical - without this, ASP.NET Core converts "name" to ClaimTypes.Name automatically
JwtSecurityTokenHandler.DefaultMapInboundClaims = false;
JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear();

// For .NET 8 compatibility, also clear JsonWebTokenHandler mappings
// JsonWebTokenHandler.DefaultInboundClaimTypeMap.Clear(); // COMMENTED OUT: This was causing JWT validation issues

builder.Host.UseSerilog((context, configuration) =>
    configuration.ReadFrom.Configuration(context.Configuration));

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var jwtKey = builder.Configuration["Jwt:Key"] ?? throw new InvalidOperationException("JWT key not configured");
var keyBytes = Encoding.ASCII.GetBytes(jwtKey);
var signingKey = new SymmetricSecurityKey(keyBytes) { KeyId = "jwt-key" };

Log.Information("[DEBUG] JWT Key configured: {Key} (length: {Length})", jwtKey, jwtKey.Length);
Log.Information("[DEBUG] JWT Key Base64: {KeyBase64}", Convert.ToBase64String(keyBytes));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Force use of JwtSecurityTokenHandler instead of JsonWebTokenHandler for .NET 8 compatibility
        options.UseSecurityTokenValidators = true;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = signingKey,
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true, // Validate token expiration
            ClockSkew = TimeSpan.Zero,
            RequireExpirationTime = true, // Require expiration time for security
            RequireSignedTokens = true, // Ensure tokens are signed
            NameClaimType = "unique_name", // Map the unique_name claim to Identity.Name
            RoleClaimType = ClaimTypes.Role // Also set role claim type for completeness
        };
        
        // Add debugging events
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var authHeader = context.Request.Headers.Authorization.ToString();
                
                // Manual token extraction if automatic extraction fails
                if (string.IsNullOrEmpty(context.Token) && !string.IsNullOrEmpty(authHeader))
                {
                    if (authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                    {
                        context.Token = authHeader.Substring("Bearer ".Length).Trim();
                        Log.Information("JWT OnMessageReceived: Manually extracted token={Token}", context.Token?.Substring(0, Math.Min(50, context.Token.Length)));
                    }
                }
                
                Log.Information("JWT OnMessageReceived: Token={Token}, AuthHeader={AuthHeader}", 
                    context.Token?.Substring(0, Math.Min(50, context.Token?.Length ?? 0)), 
                    authHeader);
                return Task.CompletedTask;
            },
            OnTokenValidated = context =>
            {
                Log.Information("JWT OnTokenValidated: User={User}, Claims={Claims}", 
                    context.Principal?.Identity?.Name, 
                    context.Principal?.Claims.Count());
                return Task.CompletedTask;
            },
            OnAuthenticationFailed = context =>
            {
                Log.Error(context.Exception, "JWT OnAuthenticationFailed: {Message}", context.Exception.Message);
                return Task.CompletedTask;
            },
            OnChallenge = context =>
            {
                Log.Warning("JWT OnChallenge: {Error}", context.Error);
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

// Register the signing key as a singleton so authentication service can use the same instance
builder.Services.AddSingleton<SymmetricSecurityKey>(signingKey);

builder.Services.AddScoped<IAuthenticationService, ShadowFileAuthenticationService>();
builder.Services.AddSingleton<IGitMetadataService, GitMetadataService>();
builder.Services.AddSingleton<IRepositoryService, CowRepositoryService>();
builder.Services.AddSingleton<IJobPersistenceService>(provider =>
{
    var config = provider.GetRequiredService<IConfiguration>();
    var logger = provider.GetRequiredService<ILogger<JobPersistenceService>>();
    var jobsPath = ExpandPath(config["Workspace:JobsPath"]);
    
    if (string.IsNullOrEmpty(jobsPath))
    {
        throw new InvalidOperationException("Workspace:JobsPath configuration is required but was not found or is empty. Please check your appsettings.json configuration.");
    }
    
    // Extract workspace path from jobs path (remove "/jobs" suffix)
    var workspacePath = Directory.GetParent(jobsPath)?.FullName ?? Path.GetDirectoryName(jobsPath) ?? throw new InvalidOperationException($"Unable to determine workspace path from JobsPath: {jobsPath}");
    
    logger.LogInformation("JobPersistenceService using workspace path: {WorkspacePath} (from JobsPath: {JobsPath})", workspacePath, jobsPath);
    
    return new JobPersistenceService(workspacePath, config, logger);
});
builder.Services.AddSingleton<IJobService, JobService>();
builder.Services.AddSingleton<IClaudeCodeExecutor, ClaudeCodeExecutor>();

builder.Services.AddHostedService<JobQueueHostedService>();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseSerilogRequestLogging();
app.UseCors();

// Enable static file serving for the file manager UI
app.UseDefaultFiles();
app.UseStaticFiles();

// CRITICAL: Authentication must come before Authorization
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

try
{
    Log.Information("Starting Claude Batch Server API");
    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

/// <summary>
/// Expand ~ to the user's home directory if the path starts with ~/
/// </summary>
static string ExpandPath(string? path)
{
    if (string.IsNullOrEmpty(path))
        return string.Empty;
        
    if (path.StartsWith("~/"))
    {
        var homeDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(homeDirectory, path[2..]);
    }
    return path;
}

public partial class Program { }