using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using ClaudeBatchServer.Core.Services;
using ClaudeBatchServer.Api;
using System.IdentityModel.Tokens.Jwt;

var builder = WebApplication.CreateBuilder(args);

// Clear default JWT claim mappings to use the original claim names
JwtSecurityTokenHandler.DefaultMapInboundClaims = false;

builder.Host.UseSerilog((context, configuration) =>
    configuration.ReadFrom.Configuration(context.Configuration));

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var jwtKey = builder.Configuration["Jwt:Key"] ?? throw new InvalidOperationException("JWT key not configured");
var key = Encoding.ASCII.GetBytes(jwtKey);

Log.Information("JWT Key configured: {Key} (length: {Length})", jwtKey, jwtKey.Length);

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
            NameClaimType = "unique_name" // Map the JWT unique_name claim to Identity.Name
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