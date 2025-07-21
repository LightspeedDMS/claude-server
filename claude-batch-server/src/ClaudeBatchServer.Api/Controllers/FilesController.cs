using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ClaudeBatchServer.Core.DTOs;
using ClaudeBatchServer.Core.Services;

namespace ClaudeBatchServer.Api.Controllers;

[ApiController]
[Route("jobs/{jobId}/files")]
[Authorize]
public class FilesController : ControllerBase
{
    private readonly IJobService _jobService;
    private readonly IRepositoryService _repositoryService;
    private readonly ILogger<FilesController> _logger;

    public FilesController(
        IJobService jobService, 
        IRepositoryService repositoryService, 
        ILogger<FilesController> logger)
    {
        _jobService = jobService;
        _repositoryService = repositoryService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<List<FileInfoResponse>>> GetFiles(Guid jobId, [FromQuery] string? path = null)
    {
        try
        {
            var username = GetCurrentUsername();
            if (string.IsNullOrEmpty(username))
                return Unauthorized();

            var job = await _jobService.GetJobStatusAsync(jobId, username);
            if (job == null)
                return NotFound("Job not found");

            var files = await _repositoryService.GetFilesAsync(job.CowPath, path);
            var response = files.Select(f => new FileInfoResponse
            {
                Name = f.Name,
                Type = f.Type,
                Path = f.Path,
                Size = f.Size,
                Modified = f.Modified
            }).ToList();

            return Ok(response);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting files for job {JobId}", jobId);
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpGet("download")]
    public async Task<IActionResult> DownloadFile(Guid jobId, [FromQuery] string path)
    {
        try
        {
            var username = GetCurrentUsername();
            if (string.IsNullOrEmpty(username))
                return Unauthorized();

            if (string.IsNullOrEmpty(path))
                return BadRequest("Path is required");

            var job = await _jobService.GetJobStatusAsync(jobId, username);
            if (job == null)
                return NotFound("Job not found");

            var fileData = await _repositoryService.DownloadFileAsync(job.CowPath, path);
            if (fileData == null)
                return NotFound("File not found");

            var fileName = Path.GetFileName(path);
            var contentType = GetContentType(fileName);

            return File(fileData, contentType, fileName);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading file {Path} for job {JobId}", path, jobId);
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpGet("content")]
    public async Task<ActionResult<FileContentResponse>> GetFileContent(Guid jobId, [FromQuery] string path)
    {
        try
        {
            var username = GetCurrentUsername();
            if (string.IsNullOrEmpty(username))
                return Unauthorized();

            if (string.IsNullOrEmpty(path))
                return BadRequest("Path is required");

            var job = await _jobService.GetJobStatusAsync(jobId, username);
            if (job == null)
                return NotFound("Job not found");

            var content = await _repositoryService.GetFileContentAsync(job.CowPath, path);
            if (content == null)
                return NotFound("File not found");

            return Ok(new FileContentResponse
            {
                Content = content,
                Encoding = "utf8"
            });
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting file content {Path} for job {JobId}", path, jobId);
            return StatusCode(500, "Internal server error");
        }
    }

    private string? GetCurrentUsername()
    {
        return HttpContext.User.Identity?.Name;
    }

    private string GetContentType(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension switch
        {
            ".txt" => "text/plain",
            ".html" => "text/html",
            ".css" => "text/css",
            ".js" => "application/javascript",
            ".json" => "application/json",
            ".xml" => "application/xml",
            ".pdf" => "application/pdf",
            ".zip" => "application/zip",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".svg" => "image/svg+xml",
            _ => "application/octet-stream"
        };
    }
}