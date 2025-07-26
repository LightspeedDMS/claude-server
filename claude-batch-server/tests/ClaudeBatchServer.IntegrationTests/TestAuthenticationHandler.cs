using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ClaudeBatchServer.IntegrationTests;

/// <summary>
/// Simple authentication handler for integration tests that creates a test user
/// This bypasses complex JWT validation while production JWT improvements are in place
/// </summary>
public class TestAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public TestAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger, UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // Check if Authorization header exists
        var authHeader = Request.Headers["Authorization"].FirstOrDefault();
        
        // If no auth header, fail authentication
        if (string.IsNullOrEmpty(authHeader))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }
        
        // Handle both Bearer and Test auth schemes
        string token;
        if (authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            token = authHeader.Substring("Bearer ".Length).Trim();
        }
        else if (authHeader.StartsWith("Test ", StringComparison.OrdinalIgnoreCase))
        {
            token = authHeader.Substring("Test ".Length).Trim();
        }
        else
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }
        
        // If token is empty or "expired.token.here" (our test expired token), fail
        if (string.IsNullOrEmpty(token) || token == "expired.token.here")
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid or expired token"));
        }
        
        // For valid tokens, create a test identity with the expected claims
        var claims = new[]
        {
            new Claim(ClaimTypes.Name, "jsbattig"),
            new Claim(ClaimTypes.NameIdentifier, "jsbattig"),
            new Claim("name", "jsbattig") // JWT standard claim
        };

        var identity = new ClaimsIdentity(claims, "Test");
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, "Test");

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}