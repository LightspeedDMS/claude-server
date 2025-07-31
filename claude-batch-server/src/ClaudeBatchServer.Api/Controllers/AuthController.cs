using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ClaudeBatchServer.Core.DTOs;
using ClaudeBatchServer.Core.Services;

namespace ClaudeBatchServer.Api.Controllers;

[ApiController]
[Route("auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthenticationService _authService;
    private readonly ILogger<AuthController> _logger;

    public AuthController(IAuthenticationService authService, ILogger<AuthController> logger)
    {
        _authService = authService;
        _logger = logger;
    }

    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> Login([FromBody] LoginRequest request)
    {
        try
        {
            // Enhanced validation with specific error responses
            if (string.IsNullOrEmpty(request.Username) || string.IsNullOrEmpty(request.Password))
            {
                var error = new AuthErrorResponse
                {
                    Error = "Username and password are required",
                    ErrorType = "ValidationError",
                    Details = "Both username and password fields must be provided"
                };
                return BadRequest(error);
            }

            // Validate hash format if authType indicates hash
            if (request.AuthType == "hash" && !IsValidHashFormat(request.Password))
            {
                var error = new AuthErrorResponse
                {
                    Error = "Invalid hash format provided",
                    ErrorType = "MalformedHash",
                    Details = "Hash must be in shadow file format (e.g., $y$..., $6$..., $5$..., $1$...)"
                };
                return BadRequest(error);
            }

            var result = await _authService.AuthenticateAsync(request);
            if (result == null)
            {
                _logger.LogWarning("Failed login attempt for user {Username}", request.Username);
                
                // Try to determine the specific failure reason
                var errorType = await DetermineAuthenticationFailureType(request.Username, request.Password);
                var error = new AuthErrorResponse
                {
                    Error = GetErrorMessage(errorType),
                    ErrorType = errorType,
                    Details = GetErrorDetails(errorType)
                };
                
                return Unauthorized(error);
            }

            _logger.LogInformation("Successful login for user {Username}", request.Username);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during login for user {Username}", request.Username);
            var error = new AuthErrorResponse
            {
                Error = "Internal server error during authentication",
                ErrorType = "InternalError",
                Details = "Please try again or contact support"
            };
            return StatusCode(500, error);
        }
    }

    private bool IsValidHashFormat(string password)
    {
        // Shadow file hash format: $algorithm$salt$hash
        // Valid algorithms: $1$ (MD5), $5$ (SHA-256), $6$ (SHA-512), $y$ (yescrypt)
        if (!password.StartsWith("$")) return false;
        
        var parts = password.Split('$');
        if (parts.Length < 4) return false;
        
        // Check if algorithm is supported
        var algorithm = parts[1];
        return algorithm is "1" or "5" or "6" or "y";
    }

    private async Task<string> DetermineAuthenticationFailureType(string username, string password)
    {
        try
        {
            // Use reflection to access private methods for error analysis
            var authServiceType = _authService.GetType();
            var userExistsMethod = authServiceType.GetMethod("UserExistsAsync", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            
            if (userExistsMethod != null)
            {
                var userExists = await (Task<bool>)userExistsMethod.Invoke(_authService, new object[] { username })!;
                if (!userExists)
                {
                    return "UserNotFound";
                }
            }
            
            // If user exists but auth failed, it's invalid credentials
            return "InvalidCredentials";
        }
        catch
        {
            // If we can't determine specifics, return generic invalid credentials
            return "InvalidCredentials";
        }
    }

    private string GetErrorMessage(string errorType)
    {
        return errorType switch
        {
            "UserNotFound" => "User not found",
            "InvalidCredentials" => "Invalid username or password",
            "MalformedHash" => "Invalid hash format provided",
            "ValidationError" => "Username and password are required",
            _ => "Authentication failed"
        };
    }

    private string GetErrorDetails(string errorType)
    {
        return errorType switch
        {
            "UserNotFound" => "The specified user does not exist in the system",
            "InvalidCredentials" => "The provided credentials are incorrect",
            "MalformedHash" => "Hash must be in shadow file format (e.g., $y$..., $6$..., $5$..., $1$...)",
            "ValidationError" => "Both username and password fields must be provided",
            _ => "Please check your credentials and try again"
        };
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<ActionResult<LogoutResponse>> Logout()
    {
        try
        {
            var token = HttpContext.Request.Headers["Authorization"]
                .FirstOrDefault()?.Split(" ").Last();

            if (!string.IsNullOrEmpty(token))
            {
                await _authService.LogoutAsync(token);
            }

            var username = HttpContext.User.Identity?.Name;
            _logger.LogInformation("User {Username} logged out", username);

            return Ok(new LogoutResponse { Success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during logout");
            return StatusCode(500, "Internal server error");
        }
    }
}