using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using ClaudeBatchServer.Core.DTOs;
using ClaudeBatchServer.Core.Services;

namespace ClaudeBatchServer.Api.Controllers;

[ApiController]
[Route("repositories")]
[Authorize]
public class RepositoriesController : ControllerBase
{
    private readonly IRepositoryService _repositoryService;
    private readonly ILogger<RepositoriesController> _logger;

    public RepositoriesController(IRepositoryService repositoryService, ILogger<RepositoriesController> logger)
    {
        _repositoryService = repositoryService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<List<RepositoryResponse>>> GetRepositories()
    {
        try
        {
            var repositories = await _repositoryService.GetRepositoriesWithMetadataAsync();
            return Ok(repositories);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting repositories");
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpGet("{repoName}")]
    public async Task<ActionResult<RepositoryResponse>> GetRepository(string repoName)
    {
        try
        {
            var username = GetCurrentUsername();
            if (string.IsNullOrEmpty(username))
                return Unauthorized();

            _logger.LogInformation("GetRepository called for {RepoName} by user {Username}", repoName, username);

            if (string.IsNullOrWhiteSpace(repoName))
                return BadRequest("Repository name is required");

            var repositories = await _repositoryService.GetRepositoriesWithMetadataAsync();
            var repository = repositories.FirstOrDefault(r => r.Name.Equals(repoName, StringComparison.OrdinalIgnoreCase));

            if (repository == null)
            {
                return NotFound($"Repository '{repoName}' not found");
            }

            return Ok(repository);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting repository {RepoName}", repoName);
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpPost("register")]
    [Authorize]
    public async Task<ActionResult<RegisterRepositoryResponse>> RegisterRepository([FromBody] RegisterRepositoryRequest request)
    {
        try
        {
            var username = GetCurrentUsername();
            if (string.IsNullOrEmpty(username))
                return Unauthorized();

            _logger.LogInformation("RegisterRepository called for {Name} from {GitUrl} by user {Username}", request.Name, request.GitUrl, username);

            if (string.IsNullOrWhiteSpace(request.Name))
                return BadRequest("Repository name is required");
            if (string.IsNullOrWhiteSpace(request.GitUrl))
                return BadRequest("Git URL is required");

            var repository = await _repositoryService.RegisterRepositoryAsync(request.Name, request.GitUrl, request.Description, request.CidxAware);

            var response = new RegisterRepositoryResponse
            {
                Name = repository.Name,
                Path = repository.Path,
                Description = repository.Description,
                GitUrl = repository.GitUrl,
                RegisteredAt = repository.RegisteredAt,
                CloneStatus = repository.CloneStatus,
                CidxAware = request.CidxAware
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
    [Authorize]
    public async Task<ActionResult<UnregisterRepositoryResponse>> UnregisterRepository(string repoName)
    {
        try
        {
            var username = GetCurrentUsername();
            if (string.IsNullOrEmpty(username))
                return Unauthorized();

            _logger.LogInformation("UnregisterRepository called for {RepoName} by user {Username}", repoName, username);

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

    private string? GetCurrentUsername()
    {
        // FIXED: Robust username extraction from JWT claims
        // First try the standard Identity.Name
        var identityName = HttpContext.User.Identity?.Name;
        if (!string.IsNullOrEmpty(identityName))
            return identityName;
        
        // Fallback: Search claims directly (for debugging JWT issues)
        var nameClaim = HttpContext.User.Claims.FirstOrDefault(c => 
            c.Type == ClaimTypes.Name || 
            c.Type == "name" ||
            c.Type == ClaimTypes.NameIdentifier);
        
        var username = nameClaim?.Value;
        
        // Debug logging for authentication issues
        if (string.IsNullOrEmpty(username))
        {
            _logger.LogWarning("No username found in JWT claims. Available claims: {Claims}", 
                string.Join(", ", HttpContext.User.Claims.Select(c => $"{c.Type}={c.Value}")));
        }
        
        return username;
    }
}