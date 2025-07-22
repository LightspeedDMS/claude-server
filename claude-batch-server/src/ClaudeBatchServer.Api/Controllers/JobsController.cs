using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ClaudeBatchServer.Core.DTOs;
using ClaudeBatchServer.Core.Services;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.IdentityModel.Tokens;
using System.Text;

namespace ClaudeBatchServer.Api.Controllers;

[ApiController]
[Route("jobs")]
//[Authorize] // Temporarily comment out for debugging
public class JobsController : ControllerBase
{
    private readonly IJobService _jobService;
    private readonly ILogger<JobsController> _logger;
    private readonly IConfiguration _configuration;

    public JobsController(IJobService jobService, ILogger<JobsController> logger, IConfiguration configuration)
    {
        _jobService = jobService;
        _logger = logger;
        _configuration = configuration;
    }

    [HttpPost]
    public async Task<ActionResult<CreateJobResponse>> CreateJob([FromBody] CreateJobRequest request)
    {
        try
        {
            // Manual JWT validation as temporary workaround
            var authHeader = HttpContext.Request.Headers.Authorization.FirstOrDefault();
            _logger.LogInformation("CreateJob called. Authorization header: {AuthHeader}", authHeader);
            
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
            // Manual JWT validation as temporary workaround
            var authHeader = HttpContext.Request.Headers.Authorization.FirstOrDefault();
            string? username = null;
            
            if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer "))
            {
                var token = authHeader.Substring(7); // Remove "Bearer " prefix
                username = ValidateJwtTokenManually(token);
            }
            
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
            // Manual JWT validation as temporary workaround
            var authHeader = HttpContext.Request.Headers.Authorization.FirstOrDefault();
            string? username = null;
            
            if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer "))
            {
                var token = authHeader.Substring(7); // Remove "Bearer " prefix
                username = ValidateJwtTokenManually(token);
            }
            
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