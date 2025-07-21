using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ClaudeBatchServer.Core.DTOs;
using ClaudeBatchServer.Core.Services;

namespace ClaudeBatchServer.Api.Controllers;

[ApiController]
[Route("jobs")]
[Authorize]
public class JobsController : ControllerBase
{
    private readonly IJobService _jobService;
    private readonly ILogger<JobsController> _logger;

    public JobsController(IJobService jobService, ILogger<JobsController> logger)
    {
        _jobService = jobService;
        _logger = logger;
    }

    [HttpPost]
    public async Task<ActionResult<CreateJobResponse>> CreateJob([FromBody] CreateJobRequest request)
    {
        try
        {
            var username = GetCurrentUsername();
            if (string.IsNullOrEmpty(username))
                return Unauthorized();

            var result = await _jobService.CreateJobAsync(request, username);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating job");
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpPost("{jobId}/images")]
    public async Task<ActionResult<ImageUploadResponse>> UploadImage(
        Guid jobId, 
        IFormFile file)
    {
        try
        {
            var username = GetCurrentUsername();
            if (string.IsNullOrEmpty(username))
                return Unauthorized();

            if (file == null || file.Length == 0)
                return BadRequest("No file provided");

            var allowedExtensions = new[] { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp" };
            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            
            if (!allowedExtensions.Contains(extension))
                return BadRequest("Invalid file type. Only image files are allowed.");

            using var stream = file.OpenReadStream();
            var result = await _jobService.UploadImageAsync(jobId, username, file.FileName, stream);
            
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading image for job {JobId}", jobId);
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpPost("{jobId}/start")]
    public async Task<ActionResult<StartJobResponse>> StartJob(Guid jobId)
    {
        try
        {
            var username = GetCurrentUsername();
            if (string.IsNullOrEmpty(username))
                return Unauthorized();

            var result = await _jobService.StartJobAsync(jobId, username);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting job {JobId}", jobId);
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpGet("{jobId}")]
    public async Task<ActionResult<JobStatusResponse>> GetJobStatus(Guid jobId)
    {
        try
        {
            var username = GetCurrentUsername();
            if (string.IsNullOrEmpty(username))
                return Unauthorized();

            var result = await _jobService.GetJobStatusAsync(jobId, username);
            if (result == null)
                return NotFound();

            return Ok(result);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting job status for {JobId}", jobId);
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpDelete("{jobId}")]
    public async Task<ActionResult<DeleteJobResponse>> DeleteJob(Guid jobId)
    {
        try
        {
            var username = GetCurrentUsername();
            if (string.IsNullOrEmpty(username))
                return Unauthorized();

            var result = await _jobService.DeleteJobAsync(jobId, username);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting job {JobId}", jobId);
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpGet]
    public async Task<ActionResult<List<JobListResponse>>> GetJobs()
    {
        try
        {
            var username = GetCurrentUsername();
            if (string.IsNullOrEmpty(username))
                return Unauthorized();

            var result = await _jobService.GetUserJobsAsync(username);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting jobs for user");
            return StatusCode(500, "Internal server error");
        }
    }

    private string? GetCurrentUsername()
    {
        return HttpContext.User.Identity?.Name;
    }
}