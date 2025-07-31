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
    private readonly IJobPersistenceService _jobPersistenceService;
    private readonly ILogger<FilesController> _logger;

    public FilesController(
        IJobService jobService, 
        IRepositoryService repositoryService,
        IJobPersistenceService jobPersistenceService,
        ILogger<FilesController> logger)
    {
        _jobService = jobService;
        _repositoryService = repositoryService;
        _jobPersistenceService = jobPersistenceService;
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

            byte[]? fileData = null;

            // CRITICAL FIX: Check both CoW workspace AND staging area for file downloads
            // Jobs in "created" status have files in staging area, processed jobs have files in CoW workspace
            if (!string.IsNullOrEmpty(job.CowPath))
            {
                // First try CoW workspace (for processed jobs)
                fileData = await _repositoryService.DownloadFileAsync(job.CowPath, path);
            }

            if (fileData == null)
            {
                // Fallback to staging area (for unprocessed jobs)
                // Use JobPersistenceService to get the correct staging path
                var stagingPath = _jobPersistenceService.GetJobStagingPath(jobId);
                var stagingFilePath = Path.Combine(stagingPath, path);
                
                // First try exact filename match
                if (System.IO.File.Exists(stagingFilePath))
                {
                    fileData = await System.IO.File.ReadAllBytesAsync(stagingFilePath);
                    _logger.LogDebug("Downloaded file {Path} from staging area (exact match) for job {JobId}", path, jobId);
                }
                else if (Directory.Exists(stagingPath))
                {
                    // If exact match fails, try to find file with suffix pattern
                    // Server generates unique filenames: {nameWithoutExtension}_{guid}{extension}
                    var nameWithoutExtension = Path.GetFileNameWithoutExtension(path);
                    var extension = Path.GetExtension(path);
                    var pattern = $"{nameWithoutExtension}_*{extension}";
                    
                    _logger.LogDebug("PATTERN MATCHING DEBUG: Looking for file {Path} in staging {StagingPath} with pattern {Pattern}", 
                        path, stagingPath, pattern);
                    
                    var matchingFiles = Directory.GetFiles(stagingPath, pattern);
                    _logger.LogDebug("PATTERN MATCHING DEBUG: Found {FileCount} matching files: {Files}", 
                        matchingFiles.Length, string.Join(", ", matchingFiles.Select(f => Path.GetFileName(f))));
                    
                    if (matchingFiles.Length > 0)
                    {
                        // Use the first matching file (should be unique due to GUID suffix)
                        var actualFilePath = matchingFiles[0];
                        fileData = await System.IO.File.ReadAllBytesAsync(actualFilePath);
                        _logger.LogDebug("Downloaded file {Path} from staging area (pattern match: {ActualFile}) for job {JobId}", 
                            path, Path.GetFileName(actualFilePath), jobId);
                    }
                    else
                    {
                        // List all files in staging directory for debugging
                        var allFiles = Directory.GetFiles(stagingPath);
                        _logger.LogDebug("PATTERN MATCHING DEBUG: No pattern matches. All files in staging: {AllFiles}", 
                            string.Join(", ", allFiles.Select(f => Path.GetFileName(f))));
                    }
                }
            }

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