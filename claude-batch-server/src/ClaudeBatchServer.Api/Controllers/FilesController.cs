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


    [HttpGet("directories")]
    public async Task<ActionResult<List<DirectoryInfoResponse>>> GetDirectories(
        Guid jobId, 
        [FromQuery] string? path = null)
    {
        try
        {
            var username = GetCurrentUsername();
            if (string.IsNullOrEmpty(username))
                return Unauthorized();

            var job = await _jobService.GetJobStatusAsync(jobId, username);
            if (job == null)
                return NotFound("Job not found");

            var directories = await _repositoryService.GetDirectoriesAsync(job.CowPath, path ?? "");
            
            if (directories == null)
                return NotFound("Path not found");

            var response = directories.Select(d => new DirectoryInfoResponse
            {
                Name = d.Name,
                Path = d.Path,
                Modified = d.Modified,
                HasSubdirectories = d.HasSubdirectories,
                FileCount = d.FileCount
            }).ToList();

            return Ok(response);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting directories for job {JobId}", jobId);
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpGet]
    public async Task<ActionResult<List<FileInfoResponse>>> GetFilesInDirectory(
        Guid jobId, 
        [FromQuery] string? path = null,
        [FromQuery] string? mask = null,
        [FromQuery] string? type = null,
        [FromQuery] int? depth = null)
    {
        try
        {
            var username = GetCurrentUsername();
            if (string.IsNullOrEmpty(username))
                return Unauthorized();

            var job = await _jobService.GetJobStatusAsync(jobId, username);
            if (job == null)
                return NotFound("Job not found");

            // Validate mask parameter for security
            if (!string.IsNullOrEmpty(mask) && !IsValidFileMask(mask))
                return BadRequest("Invalid file mask contains dangerous characters");

            var response = new List<FileInfoResponse>();

            // Handle type filtering: files, directories, or both (default)
            bool includeFiles = type == null || type.Equals("files", StringComparison.OrdinalIgnoreCase);
            bool includeDirectories = type == null || type.Equals("directories", StringComparison.OrdinalIgnoreCase);

            if (includeFiles)
            {
                var files = await _repositoryService.GetFilesInDirectoryAsync(job.CowPath, path ?? "", mask);
                if (files != null)
                {
                    response.AddRange(files.Select(f => new FileInfoResponse
                    {
                        Name = f.Name,
                        Type = f.Type,
                        Path = f.Path,
                        Size = f.Size,
                        Modified = f.Modified
                    }));
                }
            }

            if (includeDirectories)
            {
                var directories = await _repositoryService.GetDirectoriesAsync(job.CowPath, path ?? "");
                if (directories != null)
                {
                    var filteredDirectories = directories.AsEnumerable();
                    
                    // Apply mask filtering to directories if specified
                    if (!string.IsNullOrEmpty(mask))
                    {
                        var patterns = mask.Split(',', StringSplitOptions.RemoveEmptyEntries);
                        filteredDirectories = filteredDirectories.Where(d => 
                            patterns.Any(pattern => System.IO.Path.GetFileName(d.Name)
                                .Equals(pattern.Replace("*", ""), StringComparison.OrdinalIgnoreCase) ||
                                (pattern.Contains("*") && IsMatchPattern(d.Name, pattern))));
                    }
                    
                    response.AddRange(filteredDirectories.Select(d => new FileInfoResponse
                    {
                        Name = d.Name,
                        Type = "directory",
                        Path = d.Path,
                        Size = 0,
                        Modified = d.Modified
                    }));
                }
            }

            // Apply depth filtering if specified
            if (depth.HasValue && depth.Value >= 0)
            {
                var basePath = path ?? "";
                var maxDepth = basePath.Split('/', StringSplitOptions.RemoveEmptyEntries).Length + depth.Value;
                response = response.Where(item => 
                    item.Path.Split('/', StringSplitOptions.RemoveEmptyEntries).Length <= maxDepth
                ).ToList();
            }

            // Return 404 if no results and path might not exist
            if (!response.Any() && !string.IsNullOrEmpty(path))
            {
                return NotFound("Path not found");
            }

            return Ok(response);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting files for job {JobId} in path {Path}", jobId, path);
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

    private bool IsValidFileMask(string mask)
    {
        // Check for dangerous path characters
        if (mask.Contains("..") || mask.Contains("/") || mask.Contains("\\"))
            return false;

        // Allow only alphanumeric characters, dots, asterisks, commas, and spaces
        return mask.All(c => char.IsLetterOrDigit(c) || c == '.' || c == '*' || c == ',' || c == ' ');
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

    private static bool IsMatchPattern(string fileName, string pattern)
    {
        // Simple wildcard matching for file patterns
        if (pattern == "*")
            return true;

        if (pattern.StartsWith("*."))
        {
            var extension = pattern[2..];
            return fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase);
        }

        // More complex pattern matching could be added here
        return fileName.Equals(pattern, StringComparison.OrdinalIgnoreCase);
    }
}