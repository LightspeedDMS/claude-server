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
            return CreatedAtAction(nameof(GetJobStatus), new { jobId = result.JobId }, result);
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

    [HttpPost("{jobId}/files")]
    public async Task<ActionResult<FileUploadResponse>> UploadFile(
        Guid jobId, 
        IFormFile file,
        [FromQuery] bool overwrite = false)
    {
        try
        {
            var username = GetCurrentUsername();
            if (string.IsNullOrEmpty(username))
                return Unauthorized();

            if (file == null || file.Length == 0)
                return BadRequest("No file provided");

            // Support all file types - no restrictions for universal file upload
            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            
            // Security validation for file name
            if (!SecurityUtils.IsValidPath(file.FileName))
                return BadRequest("Invalid file name contains dangerous characters");

            // Check file size limit (50MB max)
            const long maxFileSize = 50 * 1024 * 1024; 
            if (file.Length > maxFileSize)
                return BadRequest($"File size exceeds maximum allowed size of {maxFileSize / 1024 / 1024}MB");

            using var stream = file.OpenReadStream();
            var result = await _jobService.UploadFileAsync(jobId, username, file.FileName, stream, overwrite);
            
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
            _logger.LogError(ex, "Error uploading file for job {JobId}", jobId);
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
            _logger.LogWarning("StartJob failed with ArgumentException: {Message}", ex.Message);
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

    [HttpPost("{jobId}/cancel")]
    public async Task<ActionResult<CancelJobResponse>> CancelJob(Guid jobId)
    {
        try
        {
            var username = GetCurrentUsername();
            if (string.IsNullOrEmpty(username))
                return Unauthorized();

            var result = await _jobService.CancelJobAsync(jobId, username);
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
            _logger.LogError(ex, "Error cancelling job {JobId}", jobId);
            return StatusCode(500, "Internal server error");
        }
    }

    private string? GetCurrentUsername()
    {
        // Try Identity.Name first (if claims are mapped correctly)
        var identityName = HttpContext.User.Identity?.Name;
        if (!string.IsNullOrEmpty(identityName))
            return identityName;
            
        // Fallback to direct claim lookup for JWT tokens
        var nameClaim = HttpContext.User.Claims.FirstOrDefault(c => 
            c.Type == "unique_name" || 
            c.Type == "name" || 
            c.Type == System.Security.Claims.ClaimTypes.Name ||
            c.Type == System.Security.Claims.ClaimTypes.NameIdentifier);
            
        return nameClaim?.Value;
    }
    
}