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
            if (string.IsNullOrEmpty(request.Username) || string.IsNullOrEmpty(request.Password))
            {
                return BadRequest("Username and password are required");
            }

            var result = await _authService.AuthenticateAsync(request);
            if (result == null)
            {
                _logger.LogWarning("Failed login attempt for user {Username}", request.Username);
                return Unauthorized("Invalid credentials");
            }

            _logger.LogInformation("Successful login for user {Username}", request.Username);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during login for user {Username}", request.Username);
            return StatusCode(500, "Internal server error");
        }
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