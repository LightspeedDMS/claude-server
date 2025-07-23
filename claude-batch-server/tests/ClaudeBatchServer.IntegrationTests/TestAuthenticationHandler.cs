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
        ILoggerFactory logger, UrlEncoder encoder, ISystemClock clock)
        : base(options, logger, encoder, clock)
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
        
        // If auth header doesn't start with Bearer, fail
        if (!authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }
        
        // Extract token (for basic validation)
        var token = authHeader.Substring("Bearer ".Length).Trim();
        
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