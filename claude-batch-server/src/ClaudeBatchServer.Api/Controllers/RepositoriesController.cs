using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ClaudeBatchServer.Core.DTOs;
using ClaudeBatchServer.Core.Services;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Security.Claims;

namespace ClaudeBatchServer.Api.Controllers;

[ApiController]
[Route("repositories")]
public class RepositoriesController : ControllerBase
{
    private readonly IRepositoryService _repositoryService;
    private readonly ILogger<RepositoriesController> _logger;
    private readonly IConfiguration _configuration;

    public RepositoriesController(IRepositoryService repositoryService, ILogger<RepositoriesController> logger, IConfiguration configuration)
    {
        _repositoryService = repositoryService;
        _logger = logger;
        _configuration = configuration;
    }

    [HttpGet]
    public async Task<ActionResult<List<RepositoryResponse>>> GetRepositories()
    {
        try
        {
            var repositories = await _repositoryService.GetRepositoriesAsync();
            var response = repositories.Select(r => new RepositoryResponse
            {
                Name = r.Name,
                Path = r.Path,
                Description = r.Description,
                GitUrl = r.GitUrl,
                RegisteredAt = r.RegisteredAt,
                CloneStatus = r.CloneStatus
            }).ToList();

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting repositories");
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<ActionResult<RegisterRepositoryResponse>> RegisterRepository([FromBody] RegisterRepositoryRequest request)
    {
        try
        {
            // Manual JWT validation as temporary workaround
            var authHeader = HttpContext.Request.Headers.Authorization.FirstOrDefault();
            _logger.LogInformation("RegisterRepository called for {Name} from {GitUrl}. Authorization header: {AuthHeader}", request.Name, request.GitUrl, authHeader);
            
            string? username = null;
            
            if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer "))
            {
                var token = authHeader.Substring(7); // Remove "Bearer " prefix
                username = ValidateJwtTokenManually(token);
                _logger.LogInformation("Manual JWT validation returned username: {Username}", username);
            }
            
            if (string.IsNullOrEmpty(username))
            {
                _logger.LogWarning("Username is null or empty after manual JWT validation, returning Unauthorized");
                return Unauthorized();
            }

            if (string.IsNullOrWhiteSpace(request.Name))
                return BadRequest("Repository name is required");
            if (string.IsNullOrWhiteSpace(request.GitUrl))
                return BadRequest("Git URL is required");

            var repository = await _repositoryService.RegisterRepositoryAsync(request.Name, request.GitUrl, request.Description);

            var response = new RegisterRepositoryResponse
            {
                Name = repository.Name,
                Path = repository.Path,
                Description = repository.Description,
                GitUrl = repository.GitUrl,
                RegisteredAt = repository.RegisteredAt,
                CloneStatus = repository.CloneStatus
            };

            return CreatedAtAction(nameof(GetRepositories), response);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Invalid request for repository registration: {Name}", request.Name);
            return BadRequest(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Repository registration conflict: {Name}", request.Name);
            return Conflict(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error registering repository {Name}", request.Name);
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpDelete("{repoName}")]
    [AllowAnonymous]
    public async Task<ActionResult<UnregisterRepositoryResponse>> UnregisterRepository(string repoName)
    {
        try
        {
            // Manual JWT validation as temporary workaround
            var authHeader = HttpContext.Request.Headers.Authorization.FirstOrDefault();
            _logger.LogInformation("UnregisterRepository called for {RepoName}. Authorization header: {AuthHeader}", repoName, authHeader);
            
            string? username = null;
            
            if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer "))
            {
                var token = authHeader.Substring(7); // Remove "Bearer " prefix
                username = ValidateJwtTokenManually(token);
                _logger.LogInformation("Manual JWT validation returned username: {Username}", username);
            }
            
            if (string.IsNullOrEmpty(username))
            {
                _logger.LogWarning("Username is null or empty after manual JWT validation, returning Unauthorized");
                return Unauthorized();
            }

            if (string.IsNullOrWhiteSpace(repoName))
                return BadRequest("Repository name is required");

            var removed = await _repositoryService.UnregisterRepositoryAsync(repoName);

            if (!removed)
            {
                return NotFound($"Repository '{repoName}' not found");
            }

            var response = new UnregisterRepositoryResponse
            {
                Success = true,
                Removed = true,
                Message = $"Repository {repoName} successfully unregistered and removed from disk"
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error unregistering repository {RepoName}", repoName);
            return StatusCode(500, "Internal server error");
        }
    }

    private string? ValidateJwtTokenManually(string token)
    {
        try
        {
            var jwtKey = _configuration["Jwt:Key"];
            if (string.IsNullOrEmpty(jwtKey))
                return null;
                
            var key = Encoding.ASCII.GetBytes(jwtKey);
            var tokenHandler = new JwtSecurityTokenHandler();
            
            // Validate the token
            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(key),
                ValidateIssuer = false,
                ValidateAudience = false,
                ClockSkew = TimeSpan.Zero
            };
            
            var principal = tokenHandler.ValidateToken(token, validationParameters, out SecurityToken validatedToken);
            
            // Extract username from claims
            var jwtToken = validatedToken as JwtSecurityToken;
            var usernameClaim = jwtToken?.Claims?.FirstOrDefault(c => c.Type == "unique_name");
            
            return usernameClaim?.Value;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Manual JWT validation failed: {Message}", ex.Message);
            return null;
        }
    }
}