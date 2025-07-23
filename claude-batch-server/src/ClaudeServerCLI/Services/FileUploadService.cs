using ClaudeServerCLI.Models;
using ClaudeServerCLI.UI;
using Spectre.Console;

namespace ClaudeServerCLI.Services;

/// <summary>
/// Service for handling universal file uploads and template processing
/// </summary>
public interface IFileUploadService
{
    Task<List<FileUpload>> PrepareFileUploadsAsync(List<string> filePaths, IProgress<(string message, int percentage)>? progress = null, CancellationToken cancellationToken = default);
    Task<Dictionary<string, string>> UploadFilesAndGetMappingsAsync(IApiClient apiClient, string jobId, List<FileUpload> files, bool overwrite = false, IProgress<(string message, int percentage)>? progress = null, CancellationToken cancellationToken = default);
    bool ValidateFiles(List<string> filePaths, out List<string> validationErrors);
    string GetContentType(string filePath);
    bool IsFileTypeSupported(string filePath);
    long GetTotalUploadSize(List<string> filePaths);
}

public class FileUploadService : IFileUploadService
{
    private const long MaxFileSize = 50 * 1024 * 1024; // 50MB per file
    private const long MaxTotalSize = 200 * 1024 * 1024; // 200MB total

    private readonly Dictionary<string, string> _contentTypeMappings = new()
    {
        // Text files
        {".txt", "text/plain"},
        {".md", "text/markdown"},
        {".json", "application/json"},
        {".xml", "application/xml"},
        {".yaml", "text/yaml"},
        {".yml", "text/yaml"},
        {".csv", "text/csv"},
        {".log", "text/plain"},
        {".sql", "text/plain"},
        {".html", "text/html"},
        {".htm", "text/html"},
        {".css", "text/css"},
        {".js", "text/javascript"},
        {".ts", "text/typescript"},
        
        // Code files
        {".py", "text/x-python"},
        {".java", "text/x-java-source"},
        {".cs", "text/x-csharp"},
        {".cpp", "text/x-c++src"},
        {".c", "text/x-csrc"},
        {".h", "text/x-chdr"},
        {".php", "text/x-php"},
        {".rb", "text/x-ruby"},
        {".go", "text/x-go"},
        {".rs", "text/x-rust"},
        {".scala", "text/x-scala"},
        {".sh", "text/x-shellscript"},
        {".bat", "text/plain"},
        {".ps1", "text/plain"},
        
        // Configuration files
        {".ini", "text/plain"},
        {".cfg", "text/plain"},
        {".conf", "text/plain"},
        {".config", "application/xml"},
        {".toml", "text/plain"},
        {".dockerfile", "text/plain"},
        
        // Image files
        {".png", "image/png"},
        {".jpg", "image/jpeg"},
        {".jpeg", "image/jpeg"},
        {".gif", "image/gif"},
        {".bmp", "image/bmp"},
        {".svg", "image/svg+xml"},
        {".webp", "image/webp"},
        {".ico", "image/x-icon"},
        
        // Document files
        {".pdf", "application/pdf"},
        {".doc", "application/msword"},
        {".docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document"},
        {".xls", "application/vnd.ms-excel"},
        {".xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"},
        {".ppt", "application/vnd.ms-powerpoint"},
        {".pptx", "application/vnd.openxmlformats-officedocument.presentationml.presentation"},
        {".rtf", "application/rtf"},
        
        // Archive files
        {".zip", "application/zip"},
        {".tar", "application/x-tar"},
        {".gz", "application/gzip"},
        {".rar", "application/vnd.rar"},
        {".7z", "application/x-7z-compressed"},
        
        // Binary files
        {".exe", "application/octet-stream"},
        {".dll", "application/octet-stream"},
        {".so", "application/octet-stream"},
        {".dylib", "application/octet-stream"},
        
        // Data files
        {".db", "application/octet-stream"},
        {".sqlite", "application/vnd.sqlite3"},
        {".sqlite3", "application/vnd.sqlite3"}
    };

    public async Task<List<FileUpload>> PrepareFileUploadsAsync(List<string> filePaths, IProgress<(string message, int percentage)>? progress = null, CancellationToken cancellationToken = default)
    {
        var fileUploads = new List<FileUpload>();
        
        for (int i = 0; i < filePaths.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            var filePath = filePaths[i];
            var fileName = Path.GetFileName(filePath);
            
            progress?.Report(($"Reading {fileName}...", (i * 100) / filePaths.Count));
            
            try
            {
                var content = await File.ReadAllBytesAsync(filePath, cancellationToken);
                var contentType = GetContentType(filePath);
                
                fileUploads.Add(new FileUpload
                {
                    FilePath = filePath,
                    FileName = fileName,
                    Content = content,
                    ContentType = contentType
                });
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to read file '{filePath}': {ex.Message}", ex);
            }
        }
        
        progress?.Report(("File preparation complete", 100));
        return fileUploads;
    }

    public async Task<Dictionary<string, string>> UploadFilesAndGetMappingsAsync(IApiClient apiClient, string jobId, List<FileUpload> files, bool overwrite = false, IProgress<(string message, int percentage)>? progress = null, CancellationToken cancellationToken = default)
    {
        var templateMappings = new Dictionary<string, string>();
        
        if (!files.Any())
        {
            return templateMappings;
        }

        try
        {
            progress?.Report(("Uploading files...", 0));
            
            var uploadResponses = await apiClient.UploadFilesAsync(jobId, files, overwrite, cancellationToken);
            var responseList = uploadResponses.ToList();
            
            progress?.Report(("Processing upload responses...", 80));
            
            foreach (var response in responseList)
            {
                // Map filename to server path for template substitution
                templateMappings[response.Filename] = response.ServerPath;
            }
            
            progress?.Report(("Upload complete", 100));
            return templateMappings;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"File upload failed: {ex.Message}", ex);
        }
    }

    public bool ValidateFiles(List<string> filePaths, out List<string> validationErrors)
    {
        validationErrors = new List<string>();
        
        if (!filePaths.Any())
        {
            return true; // No files is valid
        }

        var totalSize = 0L;
        var fileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        
        foreach (var filePath in filePaths)
        {
            // Check if file exists
            if (!File.Exists(filePath))
            {
                validationErrors.Add($"File not found: {filePath}");
                continue;
            }

            var fileInfo = new FileInfo(filePath);
            var fileName = fileInfo.Name;
            
            // Check file size
            if (fileInfo.Length > MaxFileSize)
            {
                validationErrors.Add($"File too large (max {MaxFileSize / 1024 / 1024}MB): {fileName} ({fileInfo.Length / 1024 / 1024}MB)");
                continue;
            }
            
            totalSize += fileInfo.Length;
            
            // Check for duplicate filenames
            if (fileNames.Contains(fileName))
            {
                validationErrors.Add($"Duplicate filename (filenames must be unique): {fileName}");
                continue;
            }
            fileNames.Add(fileName);
            
            // Check if file type is supported
            if (!IsFileTypeSupported(filePath))
            {
                var extension = Path.GetExtension(filePath);
                validationErrors.Add($"Unsupported file type: {fileName} ({extension})");
                continue;
            }
            
            // Check file accessibility
            try
            {
                using var stream = File.OpenRead(filePath);
            }
            catch (Exception ex)
            {
                validationErrors.Add($"Cannot read file: {fileName} - {ex.Message}");
            }
        }

        // Check total size
        if (totalSize > MaxTotalSize)
        {
            validationErrors.Add($"Total upload size too large (max {MaxTotalSize / 1024 / 1024}MB): {totalSize / 1024 / 1024}MB");
        }

        // Check file count
        if (filePaths.Count > 50)
        {
            validationErrors.Add($"Too many files (max 50): {filePaths.Count}");
        }

        return !validationErrors.Any();
    }

    public string GetContentType(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        
        if (_contentTypeMappings.TryGetValue(extension, out var contentType))
        {
            return contentType;
        }
        
        return "application/octet-stream";
    }

    public bool IsFileTypeSupported(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        
        // All file types with known content types are supported
        if (_contentTypeMappings.ContainsKey(extension))
        {
            return true;
        }
        
        // Also support files without extensions (like Dockerfile, README, etc.)
        if (string.IsNullOrEmpty(extension))
        {
            return true;
        }
        
        return false;
    }

    public long GetTotalUploadSize(List<string> filePaths)
    {
        return filePaths.Where(File.Exists).Sum(filePath => new FileInfo(filePath).Length);
    }
}

/// <summary>
/// Extension methods for file operations
/// </summary>
public static class FileExtensions
{
    public static string GetFileTypeDescription(this FileInfo fileInfo)
    {
        var extension = fileInfo.Extension.ToLowerInvariant();
        
        return extension switch
        {
            ".txt" => "Text file",
            ".md" => "Markdown document",
            ".json" => "JSON data",
            ".xml" => "XML document",
            ".yaml" or ".yml" => "YAML configuration",
            ".csv" => "CSV data",
            ".pdf" => "PDF document",
            ".docx" => "Word document",
            ".xlsx" => "Excel spreadsheet",
            ".png" => "PNG image",
            ".jpg" or ".jpeg" => "JPEG image",
            ".gif" => "GIF image",
            ".py" => "Python script",
            ".java" => "Java source",
            ".cs" => "C# source",
            ".js" => "JavaScript",
            ".ts" => "TypeScript",
            ".html" => "HTML document",
            ".css" => "CSS stylesheet",
            ".sql" => "SQL script",
            ".log" => "Log file",
            ".zip" => "ZIP archive",
            ".tar" => "TAR archive",
            ".gz" => "GZIP archive",
            "" => "File (no extension)",
            _ => $"{extension.TrimStart('.')} file".ToUpperInvariant()
        };
    }
}