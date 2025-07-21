using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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
            var repositories = await _repositoryService.GetRepositoriesAsync();
            var response = repositories.Select(r => new RepositoryResponse
            {
                Name = r.Name,
                Path = r.Path,
                Description = r.Description
            }).ToList();

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting repositories");
            return StatusCode(500, "Internal server error");
        }
    }
}