using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Extensions.Http;
using ClaudeBatchServer.Core.DTOs;
using ClaudeServerCLI.Models;
using System.Net;

namespace ClaudeServerCLI.Services;

public class ApiClient : IApiClient, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ApiClient> _logger;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly IAsyncPolicy<HttpResponseMessage> _retryPolicy;
    private string? _authToken;

    public ApiClient(HttpClient httpClient, IOptions<ApiClientOptions> options, ILogger<ApiClient> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        var opts = options.Value;
        _httpClient.BaseAddress = new Uri(opts.BaseUrl);
        _httpClient.Timeout = TimeSpan.FromSeconds(opts.TimeoutSeconds);
        _httpClient.DefaultRequestHeaders.Accept.Clear();
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        // Configure retry policy with exponential backoff
        _retryPolicy = Policy
            .HandleResult<HttpResponseMessage>(r => !r.IsSuccessStatusCode && r.StatusCode != HttpStatusCode.Unauthorized)
            .Or<HttpRequestException>()
            .WaitAndRetryAsync(
                retryCount: opts.RetryCount,
                sleepDurationProvider: retryAttempt => TimeSpan.FromMilliseconds(opts.RetryDelayMs * Math.Pow(2, retryAttempt - 1)),
                onRetry: (outcome, timespan, retryCount, context) =>
                {
                    _logger.LogWarning("API request attempt {RetryCount} failed. Retrying in {Delay}ms. Error: {Error}", 
                        retryCount, timespan.TotalMilliseconds, outcome.Exception?.Message ?? outcome.Result?.StatusCode.ToString());
                });
    }

    // Authentication
    public async Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Attempting login for user: {Username}", request.Username);
        
        var response = await PostAsync<LoginRequest, LoginResponse>("auth/login", request, cancellationToken, requireAuth: false);
        
        if (response != null && !string.IsNullOrEmpty(response.Token))
        {
            SetAuthToken(response.Token);
            _logger.LogInformation("Login successful for user: {Username}", request.Username);
        }
        
        return response ?? throw new InvalidOperationException("Login failed: No response received");
    }

    public async Task<LogoutResponse> LogoutAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Attempting logout");
        
        try
        {
            var response = await PostAsync<object, LogoutResponse>("auth/logout", new { }, cancellationToken);
            ClearAuthToken();
            _logger.LogInformation("Logout successful");
            return response ?? new LogoutResponse { Success = true };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Logout request failed, clearing local token anyway");
            ClearAuthToken();
            return new LogoutResponse { Success = true };
        }
    }

    // Repository Management
    public async Task<RegisterRepositoryResponse> CreateRepositoryAsync(RegisterRepositoryRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Creating repository: {Name}", request.Name);
        var response = await PostAsync<RegisterRepositoryRequest, RegisterRepositoryResponse>("repositories/register", request, cancellationToken);
        return response ?? throw new InvalidOperationException("Failed to create repository");
    }

    public async Task<IEnumerable<RepositoryInfo>> GetRepositoriesAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Getting repositories list");
        var response = await GetAsync<IEnumerable<RepositoryResponse>>("repositories", cancellationToken);
        return response?.Select(MapRepositoryInfo) ?? Enumerable.Empty<RepositoryInfo>();
    }

    public async Task<RepositoryInfo> GetRepositoryAsync(string name, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Getting repository: {Name}", name);
        var response = await GetAsync<RepositoryResponse>($"repositories/{Uri.EscapeDataString(name)}", cancellationToken);
        return response != null ? MapRepositoryInfo(response) : throw new InvalidOperationException($"Repository '{name}' not found");
    }

    public async Task<UnregisterRepositoryResponse> DeleteRepositoryAsync(string name, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Deleting repository: {Name}", name);
        var response = await DeleteAsync<UnregisterRepositoryResponse>($"repositories/{Uri.EscapeDataString(name)}", cancellationToken);
        return response ?? throw new InvalidOperationException($"Failed to delete repository '{name}'");
    }

    public async Task<IEnumerable<FileInfoResponse>> GetRepositoryFilesAsync(string repoName, string? path = null, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Getting files for repository: {RepoName}, path: {Path}", repoName, path ?? "root");
        var url = $"repositories/{Uri.EscapeDataString(repoName)}/files";
        if (!string.IsNullOrEmpty(path))
        {
            url += $"?path={Uri.EscapeDataString(path)}";
        }
        var response = await GetAsync<IEnumerable<FileInfoResponse>>(url, cancellationToken);
        return response ?? Enumerable.Empty<FileInfoResponse>();
    }

    public async Task<FileContentResponse> GetRepositoryFileContentAsync(string repoName, string filePath, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Getting file content for repository: {RepoName}, file: {FilePath}", repoName, filePath);
        var url = $"repositories/{Uri.EscapeDataString(repoName)}/files/{Uri.EscapeDataString(filePath)}/content";
        var response = await GetAsync<FileContentResponse>(url, cancellationToken);
        return response ?? throw new InvalidOperationException($"Failed to get content for file '{filePath}' in repository '{repoName}'");
    }

    // Job Management
    public async Task<CreateJobResponse> CreateJobAsync(CreateJobRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Creating job for repository: {Repository}", request.Repository);
        var response = await PostAsync<CreateJobRequest, CreateJobResponse>("jobs", request, cancellationToken);
        return response ?? throw new InvalidOperationException("Failed to create job");
    }

    public async Task<JobStatusResponse> GetJobAsync(string jobId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Getting job status: {JobId}", jobId);
        var response = await GetAsync<JobStatusResponse>($"jobs/{jobId}", cancellationToken);
        return response ?? throw new InvalidOperationException($"Job '{jobId}' not found");
    }

    public async Task<IEnumerable<JobInfo>> GetJobsAsync(CliJobFilter? filter = null, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Getting jobs list with filter");
        
        var url = "jobs";
        var queryParams = new List<string>();
        
        if (filter != null)
        {
            if (!string.IsNullOrEmpty(filter.Repository))
                queryParams.Add($"repository={Uri.EscapeDataString(filter.Repository)}");
            if (!string.IsNullOrEmpty(filter.Status))
                queryParams.Add($"status={Uri.EscapeDataString(filter.Status)}");
            if (!string.IsNullOrEmpty(filter.User))
                queryParams.Add($"user={Uri.EscapeDataString(filter.User)}");
            if (filter.CreatedAfter.HasValue)
                queryParams.Add($"createdAfter={filter.CreatedAfter.Value:yyyy-MM-ddTHH:mm:ss}");
            if (filter.CreatedBefore.HasValue)
                queryParams.Add($"createdBefore={filter.CreatedBefore.Value:yyyy-MM-ddTHH:mm:ss}");
            if (filter.Limit.HasValue)
                queryParams.Add($"limit={filter.Limit.Value}");
            if (filter.Skip.HasValue)
                queryParams.Add($"skip={filter.Skip.Value}");
        }
        
        if (queryParams.Any())
        {
            url += "?" + string.Join("&", queryParams);
        }
        
        var response = await GetAsync<IEnumerable<JobListResponse>>(url, cancellationToken);
        return response?.Select(MapJobInfo) ?? Enumerable.Empty<JobInfo>();
    }

    public async Task<StartJobResponse> StartJobAsync(string jobId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Starting job: {JobId}", jobId);
        var response = await PostAsync<object, StartJobResponse>($"jobs/{jobId}/start", new { }, cancellationToken);
        return response ?? throw new InvalidOperationException($"Failed to start job '{jobId}'");
    }

    public async Task<CancelJobResponse> CancelJobAsync(string jobId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Cancelling job: {JobId}", jobId);
        var response = await PostAsync<object, CancelJobResponse>($"jobs/{jobId}/cancel", new { }, cancellationToken);
        return response ?? throw new InvalidOperationException($"Failed to cancel job '{jobId}'");
    }

    public async Task<DeleteJobResponse> DeleteJobAsync(string jobId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Deleting job: {JobId}", jobId);
        var response = await DeleteAsync<DeleteJobResponse>($"jobs/{jobId}", cancellationToken);
        return response ?? throw new InvalidOperationException($"Failed to delete job '{jobId}'");
    }

    // File Upload - Updated to match API's single-file per request pattern
    public async Task<IEnumerable<FileUploadResponse>> UploadFilesAsync(string jobId, IEnumerable<FileUpload> files, bool overwrite = false, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Uploading files for job: {JobId}", jobId);
        
        var results = new List<FileUploadResponse>();
        
        foreach (var file in files)
        {
            var result = await UploadSingleFileAsync(jobId, file, overwrite, cancellationToken);
            results.Add(result);
        }
        
        return results;
    }

    // Single file upload to match the API endpoint that expects IFormFile
    public async Task<FileUploadResponse> UploadSingleFileAsync(string jobId, FileUpload file, bool overwrite = false, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Uploading single file for job: {JobId}, file: {FileName}", jobId, file.FileName);
        
        using var content = new MultipartFormDataContent();
        
        var fileContent = new ByteArrayContent(file.Content);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse(file.ContentType);
        content.Add(fileContent, "file", file.FileName); // Changed from "files" to "file" to match API
        
        if (overwrite)
        {
            content.Add(new StringContent("true"), "overwrite");
        }

        var response = await SendAsync($"jobs/{jobId}/files", HttpMethod.Post, content, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var result = JsonSerializer.Deserialize<FileUploadResponse>(json, _jsonOptions);
        return result ?? throw new InvalidOperationException($"Failed to upload file '{file.FileName}'");
    }

    public async Task<IEnumerable<JobFile>> GetJobFilesAsync(string jobId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Getting files for job: {JobId}", jobId);
        var response = await GetAsync<IEnumerable<FileInfoResponse>>($"jobs/{jobId}/files/files?path=", cancellationToken);
        return response?.Select(MapJobFile) ?? Enumerable.Empty<JobFile>();
    }

    public async Task<Stream> DownloadJobFileAsync(string jobId, string fileName, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Downloading file for job: {JobId}, file: {FileName}", jobId, fileName);
        var response = await SendAsync($"jobs/{jobId}/files/download?path={Uri.EscapeDataString(fileName)}", HttpMethod.Get, null, cancellationToken);
        return await response.Content.ReadAsStreamAsync(cancellationToken);
    }

    // Image Upload - Currently not supported by API (no /images endpoint exists)
    public async Task<ImageUploadResponse> UploadImageAsync(string jobId, byte[] imageData, string fileName, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Uploading image as regular file for job: {JobId}, file: {FileName}", jobId, fileName);
        
        // Since there's no dedicated image endpoint, upload as regular file
        var fileUpload = new FileUpload
        {
            FileName = fileName,
            Content = imageData,
            ContentType = GetImageContentType(fileName),
            FilePath = fileName
        };
        
        var result = await UploadSingleFileAsync(jobId, fileUpload, false, cancellationToken);
        
        // Convert FileUploadResponse to ImageUploadResponse for compatibility
        return new ImageUploadResponse
        {
            Filename = result.Filename,
            Path = result.Path
        };
    }
    
    private static string GetImageContentType(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            ".svg" => "image/svg+xml",
            ".webp" => "image/webp",
            ".ico" => "image/x-icon",
            _ => "image/*"
        };
    }

    // Configuration
    public void SetBaseUrl(string baseUrl)
    {
        _httpClient.BaseAddress = new Uri(baseUrl);
        _logger.LogDebug("Base URL set to: {BaseUrl}", baseUrl);
    }

    public void SetTimeout(TimeSpan timeout)
    {
        _httpClient.Timeout = timeout;
        _logger.LogDebug("Timeout set to: {Timeout}", timeout);
    }

    public void SetAuthToken(string token)
    {
        _authToken = token;
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        _logger.LogDebug("Authentication token set");
    }

    public void ClearAuthToken()
    {
        _authToken = null;
        _httpClient.DefaultRequestHeaders.Authorization = null;
        _logger.LogDebug("Authentication token cleared");
    }

    // Health Check
    public async Task<bool> IsServerHealthyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Checking server health");
            var response = await SendAsync("health", HttpMethod.Get, null, cancellationToken, requireAuth: false);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Server health check failed");
            return false;
        }
    }

    // Private helper methods
    private async Task<T?> GetAsync<T>(string endpoint, CancellationToken cancellationToken, bool requireAuth = true)
    {
        var response = await SendAsync(endpoint, HttpMethod.Get, null, cancellationToken, requireAuth);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonSerializer.Deserialize<T>(json, _jsonOptions);
    }

    private async Task<TResponse?> PostAsync<TRequest, TResponse>(string endpoint, TRequest request, CancellationToken cancellationToken, bool requireAuth = true)
    {
        var json = JsonSerializer.Serialize(request, _jsonOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        
        var response = await SendAsync(endpoint, HttpMethod.Post, content, cancellationToken, requireAuth);
        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonSerializer.Deserialize<TResponse>(responseJson, _jsonOptions);
    }

    private async Task<T?> DeleteAsync<T>(string endpoint, CancellationToken cancellationToken, bool requireAuth = true)
    {
        var response = await SendAsync(endpoint, HttpMethod.Delete, null, cancellationToken, requireAuth);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonSerializer.Deserialize<T>(json, _jsonOptions);
    }

    private async Task<HttpResponseMessage> SendAsync(string endpoint, HttpMethod method, HttpContent? content, CancellationToken cancellationToken, bool requireAuth = true)
    {
        if (requireAuth && string.IsNullOrEmpty(_authToken))
        {
            throw new UnauthorizedAccessException("Authentication token is required but not set");
        }

        using var request = new HttpRequestMessage(method, endpoint);
        if (content != null)
        {
            request.Content = content;
        }

        var response = await _retryPolicy.ExecuteAsync(async () =>
        {
            var clonedRequest = await CloneRequestAsync(request, cancellationToken);
            return await _httpClient.SendAsync(clonedRequest, cancellationToken);
        });

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("API request failed. Status: {StatusCode}, Content: {Content}", response.StatusCode, errorContent);
            
            switch (response.StatusCode)
            {
                case HttpStatusCode.Unauthorized:
                    throw new UnauthorizedAccessException("Authentication failed or token expired");
                case HttpStatusCode.Forbidden:
                    throw new UnauthorizedAccessException("Access denied");
                case HttpStatusCode.NotFound:
                    throw new InvalidOperationException("Resource not found");
                case HttpStatusCode.BadRequest:
                    throw new ArgumentException($"Bad request: {errorContent}");
                default:
                    throw new HttpRequestException($"API request failed with status {response.StatusCode}: {errorContent}");
            }
        }

        return response;
    }

    private static async Task<HttpRequestMessage> CloneRequestAsync(HttpRequestMessage original, CancellationToken cancellationToken)
    {
        var clone = new HttpRequestMessage(original.Method, original.RequestUri);

        foreach (var header in original.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (original.Content != null)
        {
            var contentBytes = await original.Content.ReadAsByteArrayAsync(cancellationToken);
            clone.Content = new ByteArrayContent(contentBytes);
            
            foreach (var header in original.Content.Headers)
            {
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        return clone;
    }

    // Mapping methods
    private static RepositoryInfo MapRepositoryInfo(RepositoryResponse response) => new()
    {
        Name = response.Name,
        Path = response.Path,
        Type = response.Type,
        Size = response.Size,
        LastModified = response.LastModified,
        GitUrl = response.GitUrl,
        Description = response.Description,
        RegisteredAt = response.RegisteredAt,
        LastPull = response.LastPull,
        LastPullStatus = response.LastPullStatus,
        RemoteUrl = response.RemoteUrl,
        CurrentBranch = response.CurrentBranch,
        CommitHash = response.CommitHash,
        CommitMessage = response.CommitMessage,
        CommitAuthor = response.CommitAuthor,
        CommitDate = response.CommitDate,
        HasUncommittedChanges = response.HasUncommittedChanges
    };

    private static JobInfo MapJobInfo(JobListResponse response) => new()
    {
        JobId = response.JobId,
        User = response.User,
        Status = response.Status,
        Repository = response.Repository,
        CreatedAt = response.Started,
        CowPath = ""
    };

    private static JobFile MapJobFile(FileInfoResponse response) => new()
    {
        Name = response.Name,
        Type = response.Type,
        Path = response.Path,
        Size = response.Size,
        Modified = response.Modified
    };

    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}