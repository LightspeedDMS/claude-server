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
JsonWebTokenHandler.DefaultInboundClaimTypeMap.Clear();

builder.Host.UseSerilog((context, configuration) =>
    configuration.ReadFrom.Configuration(context.Configuration));

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var jwtKey = builder.Configuration["Jwt:Key"] ?? throw new InvalidOperationException("JWT key not configured");
var key = Encoding.ASCII.GetBytes(jwtKey);

Log.Information("[DEBUG] JWT Key configured: {Key} (length: {Length})", jwtKey, jwtKey.Length);
Log.Information("[DEBUG] JWT Key Base64: {KeyBase64}", Convert.ToBase64String(key));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(key),
            ValidateIssuer = false,
            ValidateAudience = false,
            ClockSkew = TimeSpan.Zero,
            RequireExpirationTime = false, // Allow tokens without explicit expiration for debugging
            NameClaimType = ClaimTypes.Name, // FIXED: Map the name claim to Identity.Name
            RoleClaimType = ClaimTypes.Role // Also set role claim type for completeness
        };
        
        // Add debugging events
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                Log.Information("JWT OnMessageReceived: Token={Token}", context.Token);
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

builder.Services.AddScoped<IAuthenticationService, ShadowFileAuthenticationService>();
builder.Services.AddSingleton<IGitMetadataService, GitMetadataService>();
builder.Services.AddSingleton<IRepositoryService, CowRepositoryService>();
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

public partial class Program { }