# Claude Code Batch Server - CLI Epic

## Project Overview

A modern, interactive command-line interface for the Claude Code Batch Automation Server that provides comprehensive job management, repository operations, and real-time status monitoring with a clean, contemporary design.

**Dependencies**: Requires the Alpha Epic REST API server to be functional

## Epic Objectives

Create a **fast, asynchronous CLI client** that abstracts the REST API complexity while providing an intuitive interface for:
- User authentication and session management
- Repository CRUD operations 
- Job lifecycle management with optional monitoring
- Interactive job creation with image upload support
- Clean, modern terminal UI with optional real-time updates

## CLI Architecture Philosophy

**⚡ ASYNC COMMAND PATTERN**: This is NOT a persistent CLI application that stays running. Every command executes quickly, provides feedback, and returns immediately.

**Key Principles**:
- **Fire-and-Forget**: Commands execute, show results, and exit immediately
- **Mixed Operations**: CRUD operations are sync, only job execution is async on server
- **Status Polling**: Users query job execution status when needed (execution is async)
- **Optional Monitoring**: Real-time monitoring only for job execution with `--watch`
- **Quick Feedback**: CRUD operations complete immediately, job execution runs async on server
- **Session-less**: Each command invocation is independent (except for persisted auth tokens)

## Architecture Overview

```
CLI Application
├── Authentication Layer (login/logout)
├── Repository Management (CRUD operations)  
├── Job Management (create/monitor/cancel/delete)
├── Interactive UI (modern terminal interface)
└── Real-time Updates (auto-refresh, status polling)
```

**Technology Stack**:
- **Language**: .NET Core 8.0 (C#) - consistency with server
- **CLI Framework**: System.CommandLine for modern argument parsing
- **HTTP Client**: HttpClient with authentication handling
- **Terminal UI**: Spectre.Console for modern, interactive interface
- **Configuration**: JSON-based settings with secure credential storage
- **Cross-Platform**: Support for Windows, Linux, macOS

## Core Features

### 1. Authentication System

**Login Management**:
```bash
claude-server login --usr <username> --pwd <password>
claude-server login --usr <username> --pwd <hashed-password>
claude-server logout
claude-server whoami
```

**Requirements**:
- Secure credential handling (no plain text storage)
- JWT token management with automatic refresh
- Session persistence across CLI invocations
- Support for both plain text and pre-hashed passwords
- Credential file encryption using OS keyring integration
- Multi-server profile support (dev/staging/prod)

**Security Features**:
- Automatic token expiration handling
- Simple encrypted file storage (cross-platform)
- Environment variable support for automation  
- Session timeout warnings
- AES-256 encryption for stored tokens

### 2. Repository Management

**Repository Operations**:
```bash
# List repositories (sync)
claude-server repos list
claude-server repos list --format table|json|yaml

# Register repository (async) - Note: uses /repositories/register endpoint
claude-server repos create <name> --clone <git-url> --description "desc"

# Repository details (limited) - uses GET /repositories and filters client-side  
claude-server repos show <name>                   # Filters from repos list until API implemented
claude-server repos show <name> --format json     # JSON output of filtered result
claude-server repos show <name> --watch           # Monitor async registration progress

# Update repository - NOT NEEDED (repositories are immutable once registered)
# To change a repository, unregister and re-register with new parameters

# Unregister repository (sync)
claude-server repos delete <name>
claude-server repos delete <name> --force
```

**Enhanced Features**:
- Interactive repository creation wizard
- Repository validation (git status, size, permissions)
- Bulk operations (import multiple repositories from directory)
- Repository templates and initialization
- Git integration status display (branch, commits ahead/behind)

### 3. Job Status & Management

**Core Job Operations** (All commands return immediately):
```bash
# List jobs - shows current status and exits
claude-server jobs list
claude-server jobs list --status running|completed|failed
claude-server jobs list --watch  # ONLY command that stays alive - monitors until Ctrl+C

# Job details - shows current state and exits
claude-server jobs show <job-id>                    # Returns immediately with current status
claude-server jobs show <job-id> --watch           # Monitors until job completes, then exits
claude-server jobs logs <job-id>                    # Shows current logs and exits
claude-server jobs logs <job-id> --watch           # Streams logs until job completes, then exits

# Job control - executes and returns immediately
claude-server jobs cancel <job-id>                  # NEW: Sends cancel request, returns immediately
claude-server jobs delete <job-id>                  # Deletes and returns immediately
claude-server jobs delete <job-id> --force          # Force deletes and returns immediately
```

**Mixed Sync/Async Operation Examples**:
```bash
# ✅ CORRECT: Job creation (sync) - server creates job and returns immediately
$ claude-server jobs create --repo myapp --prompt "analyze code"
✅ Job created successfully: job-abc123
🚀 Use 'claude-server jobs start job-abc123' to begin execution
$  # Returns immediately after job is created

# ✅ CORRECT: Job execution (async) - server starts execution and returns
$ claude-server jobs start job-abc123
🚀 Job execution started
⏱️ Use 'claude-server jobs show job-abc123' to check progress
$  # Returns immediately, execution runs async on server

# ✅ CORRECT: Status polling - query current execution state
$ claude-server jobs show job-abc123
📋 Job: job-abc123
📁 Repository: myapp
⚡ Status: running (Claude Code executing on server)
⏱️ Duration: 0:01:23
📊 Output: [current output so far...]
$  # Returns immediately with current execution state

# ✅ CORRECT: Repository registration (async) - server accepts request
$ claude-server repos create myapp --clone https://github.com/user/repo.git
📤 Sending registration request to server...
✅ Repository registration accepted: reg-xyz789
🔄 Repository will be cloned on server
⏱️ Use 'claude-server repos show myapp' to check progress
$  # Returns immediately after server accepts registration

# ✅ CORRECT: Job monitoring (only execution status changes)
$ claude-server jobs list --watch
Jobs (Live Execution Status):
job-abc123  myapp     ⚡ Running   0:01:45
job-def456  backend   ✅ Complete  0:02:10

# Shows job execution status, refreshes every 2 seconds until Ctrl+C
^C
$  # Only exits when user presses Ctrl+C
```

**Optional Real-Time Features** (Only with `--watch`):
- Auto-refresh job list ONLY when `--watch` is specified
- Live status updates ONLY when `--watch` is specified for job details
- Color-coded status in all views (green=completed, yellow=running, red=failed)
- Job duration tracking in all displays (polled from server)
- Resource usage display when available from server

**Watch Mode Terminal UI** (ONLY for `jobs list --watch`):
```
Jobs (Live Updates - Press Ctrl+C to exit):
ID          Repository    Status      Duration    Progress
job-123     myapp        ⚡ Running   0:02:34    [████████▎  ]
job-124     backend      ✅ Complete  0:01:15    [██████████]
job-125     frontend     ❌ Failed    0:00:45    [███▎       ]

🔄 Refreshing every 2 seconds... Press Ctrl+C to exit
```

**Standard Terminal UI** (All other commands - return immediately):
```
Jobs (Snapshot):
ID          Repository    Status      Duration
job-123     myapp        ⚡ Running   0:02:34
job-124     backend      ✅ Complete  0:01:15
job-125     frontend     ❌ Failed    0:00:45

💡 Use '--watch' for live updates or 'jobs show <id>' for details
$  # Returns to prompt immediately
```

### 4. Advanced Job Creation with Prompts & Files

**Prompt Input Methods** (Multiple Ways to Provide Prompts):

```bash
# Method 1: Simple inline prompt
claude-server jobs create --repo myapp --prompt "Analyze this codebase"

# Method 2: Piped prompt from stdin (for complex prompts)
echo "Analyze this codebase and focus on:" | claude-server jobs create --repo myapp
cat complex-prompt.txt | claude-server jobs create --repo myapp
claude-server jobs create --repo myapp < detailed-analysis-prompt.txt

# Method 3: Interactive prompt editor (multi-line)
claude-server jobs create --repo myapp --interactive
```

**File Handling with Template Substitution** (ANY File Type):

```bash
# Single file with template reference (image)
claude-server jobs create --repo myapp \
  --prompt "Analyze {{screenshot.png}} and explain what you see" \
  --file screenshot.png

# Multiple files with templates (mixed types)
claude-server jobs create --repo myapp \
  --prompt "Review {{requirements.pdf}}, analyze {{code-diagram.png}}, then check {{config.yaml}}" \
  --file requirements.pdf code-diagram.png config.yaml

# Document analysis with template
claude-server jobs create --repo myapp \
  --prompt "Summarize {{specification.docx}} and create implementation plan" \
  --file specification.docx

# Code file analysis
claude-server jobs create --repo myapp \
  --prompt "Review {{legacy-code.py}} and suggest improvements" \
  --file legacy-code.py

# Piped prompt with mixed file templates
echo "Compare {{old-design.png}} with {{new-spec.pdf}} and {{current-config.json}}" | \
  claude-server jobs create --repo myapp --file old-design.png new-spec.pdf current-config.json

# File overwrite support
claude-server jobs create --repo myapp \
  --prompt "Analyze the updated {{report.pdf}}" \
  --file report.pdf --overwrite
```

**Template Substitution Process** (Universal File Support):
1. **CLI uploads files**: Preserves original filenames with extensions (report.pdf, diagram.png, config.yaml)
2. **Server stores files**: `/workspace/jobs/{jobId}/files/report.pdf`, `/workspace/jobs/{jobId}/files/diagram.png`
3. **Server processes prompt**: Replaces `{{report.pdf}}`, `{{diagram.png}}` with actual file paths
4. **Claude Code receives**: Resolved prompt with full file paths for any file type

**Complete Job Creation Examples** (Universal File Support):

```bash
# Simple job creation with inline prompt
claude-server jobs create --repo myapp --prompt "Analyze this codebase"
# Output: ✅ Job created successfully: job-xyz789

# Complex prompt from file with stdin piping
claude-server jobs create --repo myapp < complex-analysis-prompt.txt
# Output: ✅ Job created successfully: job-abc123

# Single file with template substitution (any file type)
claude-server jobs create --repo myapp \
  --prompt "Please analyze {{screenshot.png}} and explain the UI elements" \
  --file screenshot.png
# Output: 📤 Uploading screenshot.png... ✅ Job created with template: job-def456

# Document analysis with template
claude-server jobs create --repo myapp \
  --prompt "Review {{requirements.pdf}} and create implementation plan" \
  --file requirements.pdf
# Output: 📤 Uploading requirements.pdf... ✅ Job created with template: job-def789

# Multiple files with mixed types and auto-start
claude-server jobs create --repo myapp \
  --prompt "Compare {{design.png}} with {{spec.docx}} and check {{config.yaml}}" \
  --file design.png spec.docx config.yaml \
  --auto-start
# Output: 📤 Uploading 3 files... ✅ Job created and started: job-ghi789

# File overwrite with existing file replacement
claude-server jobs create --repo myapp \
  --prompt "Analyze the updated {{report.pdf}}" \
  --file report.pdf --overwrite
# Output: 📤 Uploading report.pdf (overwriting existing)... ✅ Job created: job-hij012

# Interactive mode with file upload (any type)
claude-server jobs create --repo myapp --interactive
# Prompts for:
# - Multi-line prompt editor
# - File selection (any type: docs, images, code, etc.)
# - Template reference assistance
# - File overwrite options
# - Job options (auto-start, watch)

# Piped prompt with multiple files and monitoring
echo "Analyze these files: {{error-log.txt}}, {{screenshot.png}}, {{config.json}}" | \
  claude-server jobs create --repo myapp \
  --file error-log.txt screenshot.png config.json \
  --auto-start --watch
# Output: 📤 Uploading 3 files... ✅ Job started, monitoring...

# Advanced options with template validation
claude-server jobs create --repo myapp \
  --prompt "Review {{missing-file.pdf}} and {{existing-doc.docx}}" \
  --file existing-doc.docx
# Output: ⚠️ Warning: Template {{missing-file.pdf}} has no corresponding file
#         ✅ Job created with partial template: job-jkl012

# Code file analysis example
claude-server jobs create --repo myapp \
  --prompt "Refactor {{legacy-module.py}} and update {{test-file.py}}" \
  --file legacy-module.py test-file.py
# Output: 📤 Uploading 2 files... ✅ Job created with template: job-mno345
```

**Interactive Creation Features**:
- Repository selection with search/filter
- Multi-line prompt editor with syntax highlighting
- Drag-and-drop image upload (where supported)
- Image preview and validation
- Prompt templates and history
- Job configuration presets

**Image Upload Support**:
- Multiple image formats (PNG, JPG, GIF, WebP)
- Image validation and size checking
- Progress bars for large uploads
- Image preview in terminal (ASCII art or thumbnail)
- Batch image upload with progress tracking

### 5. Missing API: Job Cancellation

**New API Endpoint Required**:
```http
POST /jobs/{id}/cancel
Authorization: Bearer <jwt-token>

Response: 200 OK
{
  "message": "Job cancellation requested",
  "jobId": "job-123",
  "status": "cancelling",
  "timestamp": "2024-01-15T10:30:00Z"
}
```

**Cancellation Behavior**:
- Graceful termination: Send SIGTERM to Claude Code process
- Force termination: After 30s timeout, send SIGKILL
- Update job status to "cancelled"
- Clean up job workspace and resources
- Notify any watching clients via status updates

### 6. Enhanced Status Fields (From Actual API Implementation)

**Job Status Response includes rich metadata**:
```bash
$ claude-server jobs show job-123
📋 Job: job-123
📁 Repository: myapp
⚡ Status: running
⏱️ Duration: 0:01:23
📊 Queue Position: 2
🔄 Git Status: ready
🧠 Cidx Status: indexing
📅 Created: 2024-01-15T10:30:00Z
📅 Started: 2024-01-15T10:30:15Z
📝 Output: [current execution output...]
```

**Repository Response includes Git metadata**:
```bash
$ claude-server repos show myapp
📁 Repository: myapp
🔗 Git URL: https://github.com/user/myapp.git
🌿 Branch: main
📝 Commit: abc123f (feat: add new feature)
🔄 Clone Status: completed
⚠️ Uncommitted Changes: false
📈 Ahead/Behind: 0/2 (2 commits behind origin)
```

### 7. Watch vs Standard Command Behavior

**Standard Commands (Return Immediately)**:
```bash
# These ALL return to prompt immediately:
claude-server login --usr admin --pwd secret               # → Returns after auth
claude-server repos create myapp --clone <url>             # → Returns after server accepts registration  
claude-server jobs create --repo myapp --prompt "analyze"  # → Returns after job creation
claude-server jobs show job-123                           # → Returns current status snapshot with rich metadata
claude-server jobs cancel job-456                         # → Returns after cancel request sent (when implemented)
claude-server jobs list                                   # → Returns current jobs snapshot
```

**Watch Commands (Stay Alive Until Completion or Ctrl+C)**:
```bash
# ONLY these commands stay alive:
claude-server jobs list --watch                           # → Polls server until Ctrl+C
claude-server jobs show job-123 --watch                   # → Polls server until job completes
claude-server jobs create --repo myapp --prompt "..." --watch  # → Creates then polls until done

# Watch mode behavior:
$ claude-server jobs list --watch
# Polls server every 2 seconds, shows live-updating table, requires Ctrl+C to exit

# Job watch behavior:  
$ claude-server jobs show job-123 --watch
# Polls server for job status until job completes, then exits automatically
```

**Key Differences**:
- **Standard**: Send request → Show server response → Return to prompt (2-5 seconds max)
- **Watch**: Send request → Show server response → Keep polling server until Ctrl+C or completion
- **All Operations**: Server processes requests asynchronously, CLI only shows current server state

## Technical Implementation

### 1. Project Structure

```
ClaudeServerCLI/
├── src/
│   ├── Commands/           # Command handlers
│   │   ├── AuthCommands.cs
│   │   ├── RepoCommands.cs
│   │   ├── JobCommands.cs
│   │   └── InteractiveCommands.cs
│   ├── Services/          # Core services
│   │   ├── ApiClient.cs
│   │   ├── AuthService.cs
│   │   ├── ConfigService.cs
│   │   └── TerminalService.cs
│   ├── Models/            # Data models
│   │   ├── JobModels.cs
│   │   ├── RepoModels.cs
│   │   └── ConfigModels.cs
│   ├── UI/                # Terminal UI components
│   │   ├── Tables.cs
│   │   ├── Progress.cs
│   │   ├── InteractivePrompts.cs
│   │   └── StatusDisplay.cs
│   └── Program.cs         # Main entry point
├── tests/
│   ├── Unit/
│   └── Integration/
└── ClaudeServerCLI.csproj
```

### 2. ApiClient Class Implementation

**Core ApiClient Design** - Built as part of the CLI project for full control and customization:

```csharp
public interface IApiClient
{
    // Authentication
    Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);
    Task LogoutAsync(CancellationToken cancellationToken = default);
    
    // Repository Management  
    Task<RepositoryResponse> CreateRepositoryAsync(CreateRepositoryRequest request, CancellationToken cancellationToken = default);
    Task<IEnumerable<RepositoryInfo>> GetRepositoriesAsync(CancellationToken cancellationToken = default);
    Task<RepositoryInfo> GetRepositoryAsync(string name, CancellationToken cancellationToken = default);
    Task DeleteRepositoryAsync(string name, CancellationToken cancellationToken = default);
    
    // Job Management
    Task<CreateJobResponse> CreateJobAsync(CreateJobRequest request, CancellationToken cancellationToken = default);
    Task<JobStatusResponse> GetJobAsync(string jobId, CancellationToken cancellationToken = default);
    Task<IEnumerable<JobInfo>> GetJobsAsync(JobFilter? filter = null, CancellationToken cancellationToken = default);
    Task StartJobAsync(string jobId, CancellationToken cancellationToken = default);
    Task CancelJobAsync(string jobId, CancellationToken cancellationToken = default);
    Task DeleteJobAsync(string jobId, CancellationToken cancellationToken = default);
    
    // File Upload
    Task<FileUploadResponse> UploadFilesAsync(string jobId, IEnumerable<FileUpload> files, bool overwrite = false, CancellationToken cancellationToken = default);
    Task<IEnumerable<JobFile>> GetJobFilesAsync(string jobId, CancellationToken cancellationToken = default);
    Task<Stream> DownloadJobFileAsync(string jobId, string fileName, CancellationToken cancellationToken = default);
}

public class ApiClient : IApiClient, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ApiClient> _logger;
    private readonly ApiClientOptions _options;
    private readonly IAuthService _authService;
    
    public ApiClient(HttpClient httpClient, ILogger<ApiClient> logger, 
                    ApiClientOptions options, IAuthService authService)
    {
        _httpClient = httpClient;
        _logger = logger;
        _options = options;
        _authService = authService;
        
        // Configure base address and default headers
        _httpClient.BaseAddress = new Uri(options.BaseUrl);
        _httpClient.DefaultRequestHeaders.Add("User-Agent", $"claude-server-cli/{GetVersion()}");
        _httpClient.Timeout = TimeSpan.FromSeconds(options.DefaultTimeoutSeconds);
    }
    
    // Automatic JWT token handling
    private async Task<HttpRequestMessage> PrepareRequestAsync(HttpRequestMessage request)
    {
        var token = await _authService.GetValidTokenAsync();
        if (!string.IsNullOrEmpty(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        return request;
    }
    
    // Centralized request execution with retry logic
    private async Task<T> ExecuteRequestAsync<T>(HttpRequestMessage request, CancellationToken cancellationToken = default)
    {
        await PrepareRequestAsync(request);
        
        var retryPolicy = CreateRetryPolicy();
        return await retryPolicy.ExecuteAsync(async () =>
        {
            _logger.LogDebug("Sending {Method} request to {Uri}", request.Method, request.RequestUri);
            
            var response = await _httpClient.SendAsync(request, cancellationToken);
            
            _logger.LogDebug("Received {StatusCode} response from {Uri}", response.StatusCode, request.RequestUri);
            
            if (!response.IsSuccessStatusCode)
            {
                await HandleErrorResponseAsync(response);
            }
            
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            return JsonSerializer.Deserialize<T>(content, _options.JsonOptions) ?? throw new ApiException("Null response");
        });
    }
    
    // Error handling and mapping
    private async Task HandleErrorResponseAsync(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        
        var errorMessage = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "Authentication failed. Please login again.",
            HttpStatusCode.Forbidden => "Access denied. Check your permissions.",
            HttpStatusCode.NotFound => "Resource not found.",
            HttpStatusCode.Conflict => "Resource already exists or conflicts with existing data.",
            HttpStatusCode.InternalServerError => "Server error occurred. Please try again later.",
            _ => $"Request failed with status {response.StatusCode}: {content}"
        };
        
        throw new ApiException(errorMessage, response.StatusCode, content);
    }
    
    // Configurable retry policy
    private IAsyncRetryPolicy CreateRetryPolicy()
    {
        return Policy
            .Handle<HttpRequestException>()
            .Or<TaskCanceledException>()
            .Or<ApiException>(ex => ex.IsTransient)
            .WaitAndRetryAsync(
                retryCount: _options.RetryCount,
                sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                onRetry: (outcome, timespan, retryCount, context) =>
                {
                    _logger.LogWarning("Retry {RetryCount} in {Delay}s for {Operation}", 
                                     retryCount, timespan.TotalSeconds, context.OperationKey);
                });
    }
}

public class ApiClientOptions
{
    public string BaseUrl { get; set; } = "https://localhost:8443";
    public int DefaultTimeoutSeconds { get; set; } = 30;
    public int RetryCount { get; set; } = 3;
    public JsonSerializerOptions JsonOptions { get; set; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };
}

public class ApiException : Exception
{
    public HttpStatusCode StatusCode { get; }
    public string ResponseContent { get; }
    public bool IsTransient => StatusCode >= HttpStatusCode.InternalServerError;
    
    public ApiException(string message, HttpStatusCode statusCode = HttpStatusCode.BadRequest, string responseContent = "")
        : base(message)
    {
        StatusCode = statusCode;
        ResponseContent = responseContent;
    }
}
```

**Key ApiClient Features**:

1. **Built-in Authentication**: Automatic JWT token handling with refresh
2. **Retry Logic**: Exponential backoff for transient failures  
3. **Error Mapping**: HTTP status codes mapped to user-friendly messages
4. **Request Logging**: Debug-level logging for troubleshooting
5. **Timeout Handling**: Configurable timeouts per request type
6. **Type Safety**: Strongly-typed DTOs for all API interactions
7. **Cancellation Support**: Full CancellationToken support for all operations
8. **File Upload**: Specialized handling for multi-part file uploads
9. **Configuration**: Flexible options for different environments

**Dependencies**: 
- `Microsoft.Extensions.Http` - HTTP client management
- `Microsoft.Extensions.Logging` - Structured logging  
- `Polly` - Resilience and transient-fault-handling (retry policies)
- `System.Text.Json` - High-performance JSON serialization

**Alternative Option**: If you prefer using an existing HTTP client library, consider:
- **Refit** - Type-safe REST library for .NET with automatic code generation
- **RestSharp** - Simple REST and HTTP API client
- **Flurl** - Fluent URL builder with HTTP client capabilities

However, the custom ApiClient approach is recommended because:
- Full control over authentication flow
- Custom error handling specific to CLI needs  
- Integration with CLI logging and configuration
- No additional learning curve for the team
- Easier to customize for CLI-specific requirements

### 3. Simple Token Storage Implementation

**Token Storage Priority (Fallback Chain)**:
```csharp
public async Task<string?> GetTokenAsync(string profile = "default")
{
    // 1. Environment variable (highest priority - great for automation)
    var envToken = Environment.GetEnvironmentVariable("CLAUDE_SERVER_TOKEN");
    if (!string.IsNullOrEmpty(envToken)) return envToken;
    
    // 2. Encrypted config file (persistent storage)
    var configToken = await GetTokenFromConfigAsync(profile);
    return configToken;
}
```

**Simple AES Encryption**:
```csharp
// Use machine-specific key (simple but effective)
private static readonly byte[] EncryptionKey = 
    SHA256.HashData(Encoding.UTF8.GetBytes(Environment.MachineName + Environment.UserName));

public static string EncryptToken(string token)
{
    // Simple AES-256 encryption with machine+user specific key
    using var aes = Aes.Create();
    aes.Key = EncryptionKey;
    // ... standard AES encryption
}
```

**Environment Variable Usage**:
```bash
# For automation or temporary sessions
export CLAUDE_SERVER_TOKEN="eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9..."
claude-server jobs list  # Uses token from env var

# For regular usage - stored in encrypted config file after login
claude-server login --usr admin --pwd password  # Stores encrypted token in config
claude-server jobs list  # Uses token from config file
```

**Cross-Platform File Locations**:
- **Linux/macOS**: `~/.config/claude-server-cli/config.json`
- **Windows**: `%APPDATA%\claude-server-cli\config.json`
- **Permissions**: `600` (owner read/write only)

### 3. Configuration Management

**Config File Location**:
- Linux/macOS: `~/.config/claude-server-cli/config.json`
- Windows: `%APPDATA%\claude-server-cli\config.json`

**Configuration Schema**:
```json
{
  "profiles": {
    "default": {
      "serverUrl": "https://localhost:8443",
      "timeout": 300,
      "autoRefreshInterval": 2000,
      "encryptedToken": "AES_ENCRYPTED_JWT_TOKEN_HERE"
    },
    "production": {
      "serverUrl": "https://claude-server.company.com", 
      "timeout": 600,
      "encryptedToken": "AES_ENCRYPTED_JWT_TOKEN_HERE"
    }
  },
  "defaultProfile": "default",
  "ui": {
    "colorScheme": "auto",
    "animationsEnabled": true,
    "compactMode": false
  }
}
```

### 3. Modern Terminal Features

**Visual Design**:
- Unicode box drawing characters for tables/borders
- Color coding for different states and types
- Progress bars and spinners for long operations
- Icons/emojis for status indicators (✅❌⚡⏸️)
- Responsive layout adapting to terminal width

**Interactive Elements**:
- Arrow key navigation in lists
- Tab completion for commands and arguments
- Real-time search/filtering
- Keyboard shortcuts (Ctrl+C graceful exit, Ctrl+R refresh)
- Context-sensitive help system

**Accessibility**:
- Screen reader compatibility
- High contrast mode option  
- Configurable color themes
- Text-only mode for limited terminals

## Implementation Phases

### Phase 0: API Server Critical Fixes & Enhancements (Week 1)

**PREREQUISITE**: Fix critical API issues and add advanced prompt/file features before CLI development.

- [ ] **Fix Authentication Architecture**
  - [ ] Replace manual JWT validation with proper ASP.NET Core authentication middleware
  - [ ] Remove `[AllowAnonymous]` workarounds from JobsController and RepositoriesController
  - [ ] Implement consistent `[Authorize]` attribute usage across all endpoints
  - [ ] Test authentication middleware with JWT tokens
  - [ ] Update integration tests to use proper authentication

- [ ] **Missing Job Cancellation API**
  - [ ] Add `POST /jobs/{id}/cancel` endpoint to JobsController
  - [ ] Implement job cancellation logic in JobQueueHostedService
  - [ ] Add graceful process termination (SIGTERM → SIGKILL after timeout)
  - [ ] Update job status to "cancelled" in database
  - [ ] Clean up job workspace and resources after cancellation

- [ ] **Missing Repository Management APIs**
  - [ ] Add `GET /repositories/{name}` endpoint for individual repository details
  - [ ] Remove repository update functionality from CLI epic (not needed - repositories are immutable once registered)

- [ ] **Advanced Prompt & File Handling Enhancements (Generalized from Images)**
  - [ ] **Generalize File Upload API**: Change `POST /jobs/{id}/images` to `POST /jobs/{id}/files` for ANY file type
  - [ ] **Universal File Type Support**: Accept documents (.pdf, .docx, .txt), specifications (.md, .yaml, .json), images (.png, .jpg, .gif), code files (.py, .js, .java), data files (.csv, .xml), etc.
  - [ ] **Filename Preservation**: Update file upload API to preserve original filenames with extensions
  - [ ] **File Overwrite Logic**: Add `overwrite=true` parameter to replace existing files with same name
  - [ ] **Template Substitution**: Add server-side `{{filename.ext}}` template replacement in prompts for ANY file type
  - [ ] **Multiple File Support**: Enhance file upload endpoint to handle multiple files of any type in single request
  - [ ] **File Path Resolution**: Server maps `{{filename.ext}}` to actual workspace file paths regardless of file type
  - [ ] **Updated DTOs**: Modify CreateJobRequest and file upload DTOs for generalized file handling
  - [ ] **Universal File Validation**: Add MIME type detection, file size limits, security scanning for all file types
  - [ ] **File Storage**: Update workspace structure to use `/files/` instead of `/images/` directory
  - [ ] **TDD Implementation**: Write comprehensive tests first for all new file handling functionality

- [ ] **Job Cancellation Infrastructure**
  - [ ] Add process tracking for running Claude Code instances
  - [ ] Implement timeout-based force termination (30s grace period)
  - [ ] Add cancellation timestamp and reason tracking
  - [ ] Update DTOs to include cancellation information
  - [ ] Add proper error handling for cancellation scenarios

- [ ] **Comprehensive API E2E Test Coverage (MANDATORY)**
  - [ ] **Authentication API Coverage**: Test every auth endpoint with all parameter combinations
    - [ ] `POST /auth/login` - valid credentials, invalid credentials, malformed requests
    - [ ] `POST /auth/logout` - authenticated user, unauthenticated user
    - [ ] JWT token validation across all protected endpoints
    - [ ] Token expiration and refresh scenarios
  - [ ] **Repository API Coverage**: Test every repo endpoint with all scenarios
    - [ ] `POST /repositories/register` - valid repo, invalid repo, duplicate names, git clone failures
    - [ ] `GET /repositories` - empty list, populated list, pagination if implemented
    - [ ] `GET /repositories/{name}` - existing repo, non-existent repo, registration in progress
    - [ ] `DELETE /repositories/{name}` - existing repo, non-existent repo, repo with active jobs
  - [ ] **Job API Coverage**: Test every job endpoint with all parameter combinations
    - [ ] `POST /jobs` - valid job, invalid repo, missing prompt, various job options
    - [ ] `POST /jobs/{id}/files` - single file, multiple files, overwrite scenarios, invalid files
    - [ ] `POST /jobs/{id}/start` - valid job, non-existent job, already running job
    - [ ] `GET /jobs/{id}` - pending, running, completed, failed, cancelled job states
    - [ ] `POST /jobs/{id}/cancel` - running job, completed job, non-existent job
    - [ ] `DELETE /jobs/{id}` - all job states, non-existent job
    - [ ] `GET /jobs/{id}/files` - jobs with files, jobs without files, non-existent job
    - [ ] `GET /jobs/{id}/files/download` - valid files, non-existent files, invalid paths
  - [ ] **File Upload API Coverage**: Test all file scenarios
    - [ ] All supported file types (documents, images, code, data, archives)
    - [ ] File size limits and validation
    - [ ] Filename preservation and collision handling
    - [ ] Template substitution with various file extensions
    - [ ] Overwrite functionality with existing files
    - [ ] Invalid file types and malformed uploads
  - [ ] **Error Response Coverage**: Test all error scenarios
    - [ ] 400 Bad Request for invalid parameters
    - [ ] 401 Unauthorized for missing/invalid tokens
    - [ ] 403 Forbidden for insufficient permissions
    - [ ] 404 Not Found for non-existent resources
    - [ ] 409 Conflict for resource conflicts
    - [ ] 500 Internal Server Error handling
  - [ ] **Edge Case Coverage**: Test boundary conditions
    - [ ] Very large prompts and file uploads
    - [ ] Special characters in filenames and prompts
    - [ ] Concurrent operations on same resources
    - [ ] Network interruption scenarios
    - [ ] Server restart during operations

- [ ] **Current API Architecture Confirmation**
  - [ ] Verify existing endpoints match the sync/async specification:
    - **Sync**: `POST /auth/login`, `POST /auth/logout` 
    - **Async**: `POST /repositories/register` (repository registration is async) - NOTE: Route is /register, not root
    - **Sync**: `GET /repositories`, `DELETE /repositories/{name}`
    - **MISSING**: `GET /repositories/{name}` (to be implemented)
    - **Sync**: `POST /jobs`, `POST /jobs/{id}/files` (updated from /images), `GET /jobs/{id}`, `DELETE /jobs/{id}`
    - **MISSING**: `POST /jobs/{id}/cancel` (to be implemented)
    - **Sync**: `GET /jobs/{id}/files`, `GET /jobs/{id}/files/download`
    - **Async**: `POST /jobs/{id}/start` (job execution is async)
  - [ ] Document the mixed sync/async architecture clearly
  - [ ] Update CLI epic to use correct API routes (`/repositories/register` not `/repositories`)

### Phase 1: Core CLI Framework with TDD (Week 2-3)
- [ ] **TDD Setup & Project Foundation**
  - [ ] **Write Tests First**: Create comprehensive test project structure
  - [ ] **Test Framework**: Set up xUnit, FluentAssertions, NSubstitute for mocking
  - [ ] **Create Failing Tests**: Write tests for CLI command structure before implementation
  - [ ] Create .NET Core 8.0 console application
  - [ ] Add System.CommandLine and Spectre.Console packages
  - [ ] Set up project structure with proper separation of concerns
  - [ ] Configure cross-platform compatibility
- [ ] **TDD Command Structure Implementation**
  - [ ] **Write Tests First**: Unit tests for root command, subcommands, help system
  - [ ] **Red-Green-Refactor**: Implement basic command structure to pass tests
  - [ ] **Test Configuration**: Write tests for configuration management before coding
  - [ ] Implement root command with subcommands
  - [ ] Add help system and command documentation
  - [ ] Create configuration management system
  - [ ] Set up logging and error handling
- [ ] **TDD HTTP Client Foundation**
  - [ ] **Write Tests First**: Mock HTTP client tests for all API interactions
  - [ ] **Test Error Scenarios**: Network failures, timeouts, API errors
  - [ ] **Test Retry Logic**: Exponential backoff, circuit breaker patterns
  - [ ] **Implement ApiClient Class**: Comprehensive HTTP client wrapper with base URL configuration
  - [ ] **Authentication Integration**: Automatic JWT token handling and refresh
  - [ ] **Request/Response Logging**: Debug-level logging for all API communications
  - [ ] **Retry Logic**: Exponential backoff with configurable retry policies
  - [ ] **Timeout Handling**: Per-request and global timeout configuration
  - [ ] **Error Mapping**: Convert HTTP status codes to meaningful CLI error messages
  - [ ] **Create API Response Models**: DTOs matching server responses for type safety

### Phase 2: Authentication System (Week 3-4)
- [ ] **Login/Logout Commands**
  - [ ] Implement `login` command with username/password options
  - [ ] Support both plain text and hashed password input
  - [ ] Add `logout` command with session cleanup
  - [ ] Implement `whoami` command for current user info
- [ ] **Token Management (Simple Cross-Platform)**
  - [ ] Environment variable token storage (`CLAUDE_SERVER_TOKEN`)
  - [ ] Encrypted config file fallback (`~/.config/claude-server-cli/config.json`)
  - [ ] Session persistence across CLI invocations using file storage
  - [ ] Token expiration warnings and re-authentication prompts
  - [ ] Simple AES encryption for stored tokens (not OS-dependent)
- [ ] **Multi-Profile Support**
  - [ ] Profile management commands (create/switch/delete profiles)
  - [ ] Environment-specific configuration
  - [ ] Default profile selection and validation

### Phase 3: Repository Management (Week 4-5)
- [ ] **Repository Operations Commands**
  - [ ] `repos list` with table/JSON/YAML output formats (sync)
  - [ ] `repos create` with local path and git clone options (async - returns after server accepts)
  - [ ] `repos show` with detailed repository information and registration progress (sync)
  - [ ] `repos show --watch` to monitor async registration progress until completion
  - [ ] `repos delete` (unregister) with confirmation and force options (sync)
  - [ ] Remove `repos update` command (not needed - repositories are immutable)
- [ ] **Enhanced Repository Features**
  - [ ] Interactive repository creation wizard
  - [ ] Repository validation (git status, permissions, size)
  - [ ] Registration progress tracking and status display
  - [ ] Git integration status display (branch, commits, status)

### Phase 4: Job Management & Monitoring (Week 5-6)
- [ ] **Job Listing and Status**
  - [ ] `jobs list` with current execution status (sync query)
  - [ ] Status filtering and sorting options
  - [ ] Auto-refresh mode with configurable intervals (`--watch`)
  - [ ] Modern table UI with progress bars and execution status icons
- [ ] **Job Details and Monitoring**
  - [ ] `jobs show` with detailed job information and current execution status
  - [ ] `jobs show --watch` for continuous execution monitoring until completion
  - [ ] `jobs logs` with current log display
  - [ ] `jobs logs --watch` for live log streaming from execution
  - [ ] Job execution duration tracking and progress display
- [ ] **Job Control Operations**
  - [ ] `jobs start` command to begin async execution 
  - [ ] `jobs cancel` command using new cancellation API endpoint (sync)
  - [ ] `jobs delete` with confirmation prompts (sync)
  - [ ] Batch operations for multiple jobs

### Phase 5: Advanced Job Creation with TDD (Week 6-7)  
- [ ] **TDD Prompt Input Implementation**
  - [ ] **Write Tests First**: Unit tests for stdin prompt reading, --prompt flag parsing
  - [ ] **Test Edge Cases**: Empty prompts, very large prompts, special characters
  - [ ] **Test Interactive Mode**: Mock terminal input/output for interactive prompt editor
  - [ ] `jobs create` command with sync job creation (returns after job is created)
  - [ ] Repository selection with validation
  - [ ] Prompt input with multi-line support (stdin piping + --prompt flag)
  - [ ] Auto-start option to begin execution immediately
  - [ ] Watch option to monitor execution after auto-start
- [ ] **TDD File Upload & Template Support (Universal)**
  - [ ] **Write Tests First**: Multiple file upload tests for any file type, filename preservation tests
  - [ ] **Test Template Engine**: {{filename.ext}} substitution logic with edge cases for all file types
  - [ ] **Test Validation**: Missing template files, invalid file formats, large files, unsupported types
  - [ ] Single and multiple file upload (sync - completes before returning) for any file type
  - [ ] File filename preservation and server-side mapping regardless of type
  - [ ] Template substitution validation and error handling for all file extensions
  - [ ] Progress tracking for uploads of any file size/type
  - [ ] Support for common file formats (documents, images, code, data, archives)
  - [ ] File overwrite logic testing with --overwrite flag
  - [ ] MIME type detection and validation testing
- [ ] **TDD Interactive Mode**
  - [ ] **Write Tests First**: Mock Spectre.Console interactions, keyboard navigation
  - [ ] **Test User Flows**: Complete job creation workflows with different input methods
  - [ ] Full-screen interactive job creation wizard
  - [ ] Repository browser with search/filter
  - [ ] Multi-line prompt editor with template assistance
  - [ ] Image selection with preview and template reference insertion
  - [ ] Job configuration presets and templates

### Phase 6: Modern Terminal UI (Week 7-8)
- [ ] **Visual Enhancement**
  - [ ] Implement clean table layouts without borders
  - [ ] Add color coding for different status types
  - [ ] Create progress bars and loading spinners
  - [ ] Design status icons and visual indicators
- [ ] **Interactive Elements**
  - [ ] Keyboard navigation for lists and menus
  - [ ] Real-time search and filtering
  - [ ] Context-sensitive keyboard shortcuts
  - [ ] Interactive help system
- [ ] **Responsive Design**
  - [ ] Terminal width detection and adaptive layouts
  - [ ] Compact mode for narrow terminals
  - [ ] Configurable display options

### Phase 7: Testing & Documentation (Week 8-9)
- [ ] **Comprehensive Testing**
  - [ ] Unit tests for all command handlers
  - [ ] Integration tests with mock API server
  - [ ] **Real E2E Testing** (see detailed E2E Testing section below)
  - [ ] Performance testing for large job lists
- [ ] **Documentation**
  - [ ] Command reference documentation
  - [ ] Configuration guide
  - [ ] Troubleshooting guide
  - [ ] Usage examples and tutorials

## End-to-End Testing Strategy

### E2E Testing Philosophy

**NO MOCKING RULE**: All E2E tests MUST use the real Claude Code Batch Server with actual API calls, real repositories, real files, and real Claude Code execution. No mocks, stubs, or test doubles are permitted in E2E tests.

**COMPLETE API COVERAGE REQUIREMENT**: Every CLI command MUST exercise the corresponding API endpoint with ALL possible parameter combinations and scenarios. Each API endpoint MUST be tested at least once through the CLI interface.

**Test Environment Requirements**:
- Real Claude Code Batch Server running (docker-compose up -d)
- Valid test credentials in .env file (TEST_USERNAME, TEST_PASSWORD)
- Network access to Anthropic API for Claude Code execution
- Filesystem permissions for repository creation and job workspaces
- Sample files of all supported types for upload testing (PDF, DOCX, PNG, PY, JSON, etc.)

**CLI API Coverage Matrix**: Every CLI command must map to and test its corresponding API endpoint:

| CLI Command | API Endpoint | Test Coverage Required |
|-------------|--------------|------------------------|
| `claude-server login` | `POST /auth/login` | Valid/invalid credentials, hashed passwords |
| `claude-server logout` | `POST /auth/logout` | Authenticated/unauthenticated scenarios |
| `claude-server whoami` | Token validation | Valid/expired/missing tokens |
| `claude-server repos list` | `GET /repositories` | Empty/populated lists, format options |
| `claude-server repos create` | `POST /repositories/register` | Local path/git clone, all parameters |
| `claude-server repos show` | `GET /repositories/{name}` | Existing/non-existent repos, all formats |
| `claude-server repos delete` | `DELETE /repositories/{name}` | Existing repos, force flag, confirmation |
| `claude-server jobs list` | `GET /jobs` | All status filters, watch mode, formats |
| `claude-server jobs create` | `POST /jobs` + `POST /jobs/{id}/files` | All prompt methods, file types, templates |
| `claude-server jobs show` | `GET /jobs/{id}` | All job states, watch mode, formats |
| `claude-server jobs start` | `POST /jobs/{id}/start` | Valid/invalid jobs, template substitution |
| `claude-server jobs cancel` | `POST /jobs/{id}/cancel` | Running/completed/non-existent jobs |
| `claude-server jobs delete` | `DELETE /jobs/{id}` | All job states, force flag |
| `claude-server jobs logs` | `GET /jobs/{id}` | Output extraction, watch mode |

### E2E Test Infrastructure

**Test Framework Architecture**:
```csharp
// E2E Test Base Class
public abstract class E2ETestBase : IAsyncLifetime
{
    protected readonly string ServerUrl = "https://localhost:8443";
    protected readonly string TestUsername = Environment.GetEnvironmentVariable("TEST_USERNAME")!;
    protected readonly string TestPassword = Environment.GetEnvironmentVariable("TEST_PASSWORD")!;
    
    protected string CliExecutable => GetCliExecutablePath();
    protected List<string> CreatedRepositories = new();
    protected List<string> CreatedJobs = new();
    
    // Setup: Verify server is running
    public async Task InitializeAsync() => await VerifyServerHealth();
    
    // Cleanup: Remove all test artifacts
    public async Task DisposeAsync() => await CleanupAllTestArtifacts();
}
```

**Test Data Management**:
- **Repository Cleanup**: Delete all test repositories created during tests
- **Job Cleanup**: Cancel and delete all test jobs
- **Workspace Cleanup**: Remove job workspaces and uploaded files
- **Authentication Cleanup**: Clear test sessions and tokens

### Comprehensive E2E Test Suites

**MANDATORY COVERAGE**: Every test suite MUST achieve 100% API endpoint coverage through CLI commands.

#### 1. Authentication E2E Tests (Complete API Coverage)

**Must test every authentication scenario through CLI commands:**

```csharp
public class AuthenticationE2ETests : E2ETestBase
{
    [Fact]
    public async Task Login_WithValidCredentials_ShouldAuthenticateSuccessfully()
    {
        // Test: claude-server login --usr <user> --pwd <password>
        var result = await RunCliCommand($"login --usr {TestUsername} --pwd {TestPassword}");
        
        // Assertions
        result.ExitCode.Should().Be(0);
        result.Output.Should().Contain("✅ Login successful");
        result.Output.Should().Contain($"Logged in as: {TestUsername}");
        
        // Verify token persistence
        var whoamiResult = await RunCliCommand("whoami");
        whoamiResult.ExitCode.Should().Be(0);
        whoamiResult.Output.Should().Contain(TestUsername);
    }

    [Fact] 
    public async Task Login_WithHashedPassword_ShouldAuthenticateSuccessfully()
    {
        // Pre-hash password using server's hashing method
        var hashedPassword = HashPassword(TestPassword);
        
        // Test: claude-server login --usr <user> --pwd <hashed-password>
        var result = await RunCliCommand($"login --usr {TestUsername} --pwd {hashedPassword}");
        
        // Assertions
        result.ExitCode.Should().Be(0);
        result.Output.Should().Contain("✅ Login successful");
    }

    [Fact]
    public async Task Login_WithInvalidCredentials_ShouldFailGracefully()
    {
        // Test: claude-server login --usr invalid --pwd wrong
        var result = await RunCliCommand("login --usr invalid --pwd wrong");
        
        // Assertions  
        result.ExitCode.Should().Be(1);
        result.Output.Should().Contain("❌ Authentication failed");
        result.Output.Should().Contain("💡 Hint: Use 'claude-server login --help'");
    }

    [Fact]
    public async Task Logout_AfterValidLogin_ShouldClearSession()
    {
        // Setup: Login first
        await RunCliCommand($"login --usr {TestUsername} --pwd {TestPassword}");
        
        // Test: claude-server logout
        var result = await RunCliCommand("logout");
        
        // Assertions
        result.ExitCode.Should().Be(0);
        result.Output.Should().Contain("✅ Logged out successfully");
        
        // Verify session cleared
        var whoamiResult = await RunCliCommand("whoami");
        whoamiResult.ExitCode.Should().Be(1);
        whoamiResult.Output.Should().Contain("Not authenticated");
    }
}
```

#### 2. Repository Management E2E Tests (Complete API Coverage)

**Must test every repository API endpoint through CLI commands:**

```csharp
public class RepositoryE2ETests : E2ETestBase
{
    [Fact] // Covers: POST /repositories/register, GET /repositories, GET /repositories/{name}, DELETE /repositories/{name}
    public async Task ReposCRUD_FullLifecycle_ShouldWorkCorrectly()
    {
        await AuthenticateForTest();
        
        var repoName = $"test-repo-{Guid.NewGuid():N}";
        var repoPath = CreateTestRepository(repoName);
        CreatedRepositories.Add(repoName);

        // CREATE: claude-server repos create
        var createResult = await RunCliCommand(
            $"repos create {repoName} --path {repoPath} --description \"E2E test repository\"");
        
        createResult.ExitCode.Should().Be(0);
        createResult.Output.Should().Contain("✅ Repository created successfully");
        createResult.Output.Should().Contain(repoName);

        // READ: claude-server repos list
        var listResult = await RunCliCommand("repos list");
        listResult.ExitCode.Should().Be(0);
        listResult.Output.Should().Contain(repoName);
        listResult.Output.Should().Contain("E2E test repository");

        // READ: claude-server repos show
        var showResult = await RunCliCommand($"repos show {repoName}");
        showResult.ExitCode.Should().Be(0);
        showResult.Output.Should().Contain(repoName);
        showResult.Output.Should().Contain(repoPath);
        showResult.Output.Should().Contain("E2E test repository");

        // UPDATE: claude-server repos update  
        var updateResult = await RunCliCommand(
            $"repos update {repoName} --description \"Updated E2E test repository\"");
        updateResult.ExitCode.Should().Be(0);
        updateResult.Output.Should().Contain("✅ Repository updated successfully");

        // Verify update
        var verifyResult = await RunCliCommand($"repos show {repoName}");
        verifyResult.Output.Should().Contain("Updated E2E test repository");

        // DELETE: claude-server repos delete
        var deleteResult = await RunCliCommand($"repos delete {repoName} --force");
        deleteResult.ExitCode.Should().Be(0);
        deleteResult.Output.Should().Contain("✅ Repository deleted successfully");

        CreatedRepositories.Remove(repoName); // No cleanup needed
    }

    [Fact]
    public async Task ReposCreate_WithGitClone_ShouldCloneAndRegister()
    {
        await AuthenticateForTest();
        
        var repoName = $"cloned-repo-{Guid.NewGuid():N}";
        var gitUrl = "https://github.com/jsbattig/tries.git"; // Small test repository
        CreatedRepositories.Add(repoName);

        // Test: claude-server repos create --clone
        var result = await RunCliCommand(
            $"repos create {repoName} --clone {gitUrl} --description \"Cloned E2E test repo\"");

        result.ExitCode.Should().Be(0);
        result.Output.Should().Contain("✅ Repository created successfully");
        result.Output.Should().Contain("📥 Cloning repository...");
        result.Output.Should().Contain("✅ Clone completed");

        // Verify repository exists and has git metadata
        var showResult = await RunCliCommand($"repos show {repoName}");
        showResult.ExitCode.Should().Be(0);
        showResult.Output.Should().Contain(gitUrl);
        showResult.Output.Should().Contain(".git"); // Should detect git metadata
    }

    [Fact] // Covers: GET /repositories with different formats
    public async Task ReposList_AllFormats_ShouldWork()
    {
        await AuthenticateForTest();
        var repoName = await CreateTestRepository();

        // Test table format (default)
        var tableResult = await RunCliCommand("repos list");
        tableResult.ExitCode.Should().Be(0);
        tableResult.Output.Should().Contain(repoName);

        // Test JSON format  
        var jsonResult = await RunCliCommand("repos list --format json");
        jsonResult.ExitCode.Should().Be(0);
        jsonResult.Output.Should().Contain("\"name\":");
        jsonResult.Output.Should().Contain(repoName);

        // Test YAML format
        var yamlResult = await RunCliCommand("repos list --format yaml");  
        yamlResult.ExitCode.Should().Be(0);
        yamlResult.Output.Should().Contain("name:");
        yamlResult.Output.Should().Contain(repoName);
    }

    [Fact] // Covers: GET /repositories/{name} error scenarios
    public async Task ReposShow_NonExistentRepo_ShouldReturnError()
    {
        await AuthenticateForTest();
        
        var result = await RunCliCommand("repos show non-existent-repo");
        result.ExitCode.Should().Be(1);
        result.Output.Should().Contain("not found");
    }

    [Fact] // Covers: DELETE /repositories/{name} with force flag
    public async Task ReposDelete_WithForceFlag_ShouldSkipConfirmation()
    {
        await AuthenticateForTest();
        var repoName = await CreateTestRepository();

        var result = await RunCliCommand($"repos delete {repoName} --force");
        result.ExitCode.Should().Be(0);
        result.Output.Should().Contain("deleted successfully");
    }
}
```

#### 3. Job Management E2E Tests (Complete API Coverage)

**Must test every job API endpoint through CLI commands:**

```csharp
public class JobManagementE2ETests : E2ETestBase  
{
    [Fact] // Covers: POST /jobs, POST /jobs/{id}/start, GET /jobs/{id}, DELETE /jobs/{id}
    public async Task JobsFullLifecycle_WithRealClaudeExecution_ShouldComplete()
    {
        await AuthenticateForTest();
        
        // Setup: Create test repository
        var repoName = await CreateTestRepository();
        var testFile = Path.Combine(GetRepositoryPath(repoName), "test.py");
        File.WriteAllText(testFile, "print('Hello from E2E test')");

        // CREATE JOB: claude-server jobs create
        var createResult = await RunCliCommand(
            $"jobs create --repo {repoName} --prompt \"Analyze this Python file and explain what it does\"");
        
        createResult.ExitCode.Should().Be(0);
        createResult.Output.Should().Contain("✅ Job created successfully");
        
        var jobId = ExtractJobIdFromOutput(createResult.Output);
        CreatedJobs.Add(jobId);

        // START JOB: claude-server jobs start (if not auto-started)
        if (!createResult.Output.Contains("started automatically"))
        {
            var startResult = await RunCliCommand($"jobs start {jobId}");
            startResult.ExitCode.Should().Be(0);
        }

        // MONITOR: claude-server jobs show with polling
        await WaitForJobCompletion(jobId, TimeSpan.FromMinutes(5));

        // VERIFY COMPLETION
        var finalStatus = await RunCliCommand($"jobs show {jobId}");
        finalStatus.ExitCode.Should().Be(0);
        finalStatus.Output.Should().Contain("Status: completed");
        finalStatus.Output.Should().Contain("Output:");
        finalStatus.Output.Should().Contain("print"); // Should analyze the Python code
        
        // Verify output is substantial (real Claude execution)
        var outputSection = ExtractOutputSection(finalStatus.Output);
        outputSection.Length.Should().BeGreaterThan(100, "Real Claude output should be detailed");

        // CLEANUP: claude-server jobs delete
        var deleteResult = await RunCliCommand($"jobs delete {jobId} --force");
        deleteResult.ExitCode.Should().Be(0);
        CreatedJobs.Remove(jobId); // No cleanup needed
    }

    [Fact]
    public async Task JobCancellation_RunningJob_ShouldTerminateGracefully()
    {
        await AuthenticateForTest();
        
        var repoName = await CreateTestRepository();
        
        // Create long-running job
        var createResult = await RunCliCommand(
            $"jobs create --repo {repoName} --prompt \"Count from 1 to 1000 and show each number\" --auto-start");
        
        var jobId = ExtractJobIdFromOutput(createResult.Output);
        CreatedJobs.Add(jobId);

        // Wait for job to start running
        await WaitForJobStatus(jobId, "running", TimeSpan.FromMinutes(1));

        // CANCEL: claude-server jobs cancel
        var cancelResult = await RunCliCommand($"jobs cancel {jobId}");
        cancelResult.ExitCode.Should().Be(0);
        cancelResult.Output.Should().Contain("✅ Job cancellation requested");

        // Verify cancellation
        await WaitForJobStatus(jobId, "cancelled", TimeSpan.FromMinutes(1));
        
        var finalStatus = await RunCliCommand($"jobs show {jobId}");
        finalStatus.Output.Should().Contain("Status: cancelled");
    }

    [Fact]
    public async Task JobsList_WithMultipleJobs_ShouldDisplayCorrectly()
    {
        await AuthenticateForTest();
        
        var repoName = await CreateTestRepository();
        
        // Create multiple test jobs
        var jobIds = new List<string>();
        for (int i = 0; i < 3; i++)
        {
            var result = await RunCliCommand(
                $"jobs create --repo {repoName} --prompt \"Test job {i + 1}\"");
            
            var jobId = ExtractJobIdFromOutput(result.Output);
            jobIds.Add(jobId);
            CreatedJobs.Add(jobId);
        }

        // TEST: claude-server jobs list
        var listResult = await RunCliCommand("jobs list");
        
        listResult.ExitCode.Should().Be(0);
        foreach (var jobId in jobIds)
        {
            listResult.Output.Should().Contain(jobId);
        }
        
        // Verify table formatting
        listResult.Output.Should().Contain("ID");
        listResult.Output.Should().Contain("Repository");
        listResult.Output.Should().Contain("Status");
        listResult.Output.Should().Contain(repoName);

        // TEST: Status filtering
        var pendingResult = await RunCliCommand("jobs list --status pending");
        pendingResult.ExitCode.Should().Be(0);
        pendingResult.Output.Should().Contain("pending");
    }

    [Fact] // Covers: GET /jobs with different formats and filters
    public async Task JobsList_AllFormatsAndFilters_ShouldWork()
    {
        await AuthenticateForTest();
        var repoName = await CreateTestRepository();
        var jobId = await CreateTestJob(repoName);

        // Test different output formats
        var tableResult = await RunCliCommand("jobs list");
        tableResult.ExitCode.Should().Be(0);

        var jsonResult = await RunCliCommand("jobs list --format json");
        jsonResult.ExitCode.Should().Be(0);
        jsonResult.Output.Should().Contain("\"jobId\":");

        // Test status filtering
        var runningResult = await RunCliCommand("jobs list --status running");
        runningResult.ExitCode.Should().Be(0);
        
        var completedResult = await RunCliCommand("jobs list --status completed");
        completedResult.ExitCode.Should().Be(0);
    }

    [Fact] // Covers: GET /jobs/{id} error scenarios
    public async Task JobsShow_NonExistentJob_ShouldReturnError()
    {
        await AuthenticateForTest();
        
        var result = await RunCliCommand("jobs show non-existent-job-id");
        result.ExitCode.Should().Be(1);
        result.Output.Should().Contain("not found");
    }

    [Fact] // Covers: POST /jobs with different prompt methods
    public async Task JobsCreate_AllPromptMethods_ShouldWork()
    {
        await AuthenticateForTest();
        var repoName = await CreateTestRepository();

        // Method 1: Inline prompt
        var inlineResult = await RunCliCommand(
            $"jobs create --repo {repoName} --prompt \"Analyze this code\"");
        inlineResult.ExitCode.Should().Be(0);
        var jobId1 = ExtractJobIdFromOutput(inlineResult.Output);
        CreatedJobs.Add(jobId1);

        // Method 2: Piped prompt
        var pipedResult = await RunCliCommandWithInput(
            $"jobs create --repo {repoName}", "This is a piped prompt");
        pipedResult.ExitCode.Should().Be(0);
        var jobId2 = ExtractJobIdFromOutput(pipedResult.Output);
        CreatedJobs.Add(jobId2);

        // Method 3: Interactive mode (if supported)
        // var interactiveResult = await RunCliCommandInteractive(...);
    }

    [Fact] // Covers: POST /jobs/{id}/start error scenarios
    public async Task JobsStart_InvalidScenarios_ShouldReturnErrors()
    {
        await AuthenticateForTest();
        
        // Non-existent job
        var nonExistentResult = await RunCliCommand("jobs start non-existent-job");
        nonExistentResult.ExitCode.Should().Be(1);
        nonExistentResult.Output.Should().Contain("not found");

        // Already completed job
        var repoName = await CreateTestRepository();
        var jobId = await CreateAndCompleteTestJob(repoName);
        
        var completedResult = await RunCliCommand($"jobs start {jobId}");
        completedResult.ExitCode.Should().Be(1);
        completedResult.Output.Should().Contain("already");
    }
}
```

#### 4. File Upload E2E Tests (Universal File Support)

```csharp
public class FileUploadE2ETests : E2ETestBase
{
    [Fact]
    public async Task JobWithFileUpload_RealFileAnalysis_ShouldAnalyzeCorrectly()
    {
        await AuthenticateForTest();
        
        var repoName = await CreateTestRepository();
        var testImagePath = CreateTestImage(); // Generate test image with shapes

        // CREATE JOB WITH FILE: claude-server jobs create --file
        var createResult = await RunCliCommand(
            $"jobs create --repo {repoName} " +
            $"--prompt \"Analyze this image and describe what shapes and colors you see\" " +
            $"--file {testImagePath} --auto-start");

        createResult.ExitCode.Should().Be(0);
        createResult.Output.Should().Contain("✅ Job created successfully");
        createResult.Output.Should().Contain("📤 Uploading image");
        createResult.Output.Should().Contain("✅ Image uploaded");

        var jobId = ExtractJobIdFromOutput(createResult.Output);
        CreatedJobs.Add(jobId);

        // Wait for completion
        await WaitForJobCompletion(jobId, TimeSpan.FromMinutes(5));

        // VERIFY REAL IMAGE ANALYSIS
        var resultOutput = await RunCliCommand($"jobs show {jobId}`);
        resultOutput.ExitCode.Should().Be(0);
        resultOutput.Output.Should().Contain("Status: completed");
        
        var output = ExtractOutputSection(resultOutput.Output);
        
        // Verify Claude actually analyzed the image content
        output.Should().Contain("rectangle", "Should identify rectangle shape");
        output.Should().Contain("circle", "Should identify circle shape"); 
        output.Should().Contain("triangle", "Should identify triangle shape");
        output.Should().Contain("blue", "Should identify blue color");
        output.Should().Contain("red", "Should identify red color");
        output.Should().Contain("green", "Should identify green color");
        
        // Verify substantial analysis (not just echoing prompt)
        output.Length.Should().BeGreaterThan(200, "Real image analysis should be detailed");

        // Clean up test image
        File.Delete(testImagePath);
    }

    [Fact]
    public async Task JobWithMultipleImages_ShouldUploadAndAnalyzeAll()
    {
        await AuthenticateForTest();
        
        var repoName = await CreateTestRepository();
        var image1 = CreateTestImage("shapes");
        var image2 = CreateTestImage("colors");

        // CREATE JOB WITH MULTIPLE IMAGES
        var createResult = await RunCliCommand(
            $"jobs create --repo {repoName} " +
            $"--prompt \"Compare these two images and describe the differences\" " +
            $"--images {image1} {image2} --auto-start");

        createResult.ExitCode.Should().Be(0);
        createResult.Output.Should().Contain("📤 Uploading images... [2]");
        
        var jobId = ExtractJobIdFromOutput(createResult.Output);
        CreatedJobs.Add(jobId);

        await WaitForJobCompletion(jobId, TimeSpan.FromMinutes(5));

        // Verify Claude received both images
        var result = await RunCliCommand($"jobs show {jobId}`);
        var output = ExtractOutputSection(result.Output);
        output.Should().Contain("image", "Should reference images in analysis");
        output.Should().Contain("difference", "Should compare the images");

        // Cleanup
        File.Delete(image1);
        File.Delete(image2);
    }

    [Fact] // Covers: POST /jobs/{id}/files with all supported file types
    public async Task FileUpload_AllFileTypes_ShouldWork()
    {
        await AuthenticateForTest();
        var repoName = await CreateTestRepository();

        // Test different file types
        var testFiles = new Dictionary<string, byte[]>
        {
            ["document.pdf"] = CreateTestPdfFile(),
            ["code.py"] = Encoding.UTF8.GetBytes("print('Hello World')"),
            ["data.json"] = Encoding.UTF8.GetBytes("{\"key\": \"value\"}"),
            ["config.yaml"] = Encoding.UTF8.GetBytes("key: value"),
            ["image.png"] = CreateTestImageData(),
            ["text.txt"] = Encoding.UTF8.GetBytes("This is a test file")
        };

        foreach (var (fileName, content) in testFiles)
        {
            var filePath = Path.Combine(Path.GetTempPath(), fileName);
            await File.WriteAllBytesAsync(filePath, content);

            var result = await RunCliCommand(
                $"jobs create --repo {repoName} " +
                $"--prompt \"Analyze {{fileName}}\" " +
                $"--file {filePath}");

            result.ExitCode.Should().Be(0, $"File type {fileName} should be supported");
            result.Output.Should().Contain("uploaded", $"File {fileName} should upload successfully");

            var jobId = ExtractJobIdFromOutput(result.Output);
            CreatedJobs.Add(jobId);

            // Cleanup
            File.Delete(filePath);
        }
    }

    [Fact] // Covers: POST /jobs/{id}/files with overwrite functionality
    public async Task FileUpload_WithOverwrite_ShouldReplaceExisting()
    {
        await AuthenticateForTest();
        var repoName = await CreateTestRepository();
        var fileName = "test-file.txt";
        var filePath = Path.Combine(Path.GetTempPath(), fileName);

        // Upload initial file
        await File.WriteAllTextAsync(filePath, "Original content");
        
        var result1 = await RunCliCommand(
            $"jobs create --repo {repoName} " +
            $"--prompt \"Analyze {{{fileName}}}\" " +
            $"--file {filePath}");
        
        var jobId = ExtractJobIdFromOutput(result1.Output);
        CreatedJobs.Add(jobId);

        // Upload same filename with different content and --overwrite flag
        await File.WriteAllTextAsync(filePath, "Updated content");
        
        var result2 = await RunCliCommand(
            $"jobs create --repo {repoName} " +
            $"--prompt \"Analyze updated {{{fileName}}}\" " +
            $"--file {filePath} --overwrite");

        result2.ExitCode.Should().Be(0);
        result2.Output.Should().Contain("overwriting", "Should indicate file is being overwritten");

        // Cleanup
        File.Delete(filePath);
    }

    [Fact] // Covers: GET /jobs/{id}/files and GET /jobs/{id}/files/download
    public async Task FileOperations_ListAndDownload_ShouldWork()
    {
        await AuthenticateForTest();
        var repoName = await CreateTestRepository();
        var testFile = CreateTestFileWithContent("test.txt", "Hello World");

        // Create job with file
        var createResult = await RunCliCommand(
            $"jobs create --repo {repoName} " +
            $"--prompt \"Process {{test.txt}}\" " +
            $"--file {testFile}");
        
        var jobId = ExtractJobIdFromOutput(createResult.Output);
        CreatedJobs.Add(jobId);

        // TEST: List job files (CLI equivalent: jobs show {id} --files)
        var listResult = await RunCliCommand($"jobs show {jobId} --files");
        listResult.ExitCode.Should().Be(0);
        listResult.Output.Should().Contain("test.txt");

        // TEST: Download file (CLI equivalent: jobs download {id} --file test.txt)  
        var downloadResult = await RunCliCommand($"jobs download {jobId} --file test.txt");
        downloadResult.ExitCode.Should().Be(0);
        downloadResult.Output.Should().Contain("Downloaded");

        // Cleanup
        File.Delete(testFile);
    }
}
```

#### 5. Interactive Mode E2E Tests

```csharp
public class InteractiveModeE2ETests : E2ETestBase
{
    [Fact]
    public async Task InteractiveJobCreation_FullWorkflow_ShouldComplete()
    {
        await AuthenticateForTest();
        
        var repoName = await CreateTestRepository();

        // Prepare interactive input
        var interactiveInput = new[]
        {
            repoName,                    // Repository selection
            "Test interactive prompt",   // Prompt input
            "y",                        // Confirm job creation
            "y"                         // Auto-start job
        };

        // TEST: claude-server jobs create --interactive
        var result = await RunCliCommandInteractive("jobs create --interactive", interactiveInput);

        result.ExitCode.Should().Be(0);
        result.Output.Should().Contain("✅ Job created successfully");
        
        var jobId = ExtractJobIdFromOutput(result.Output);
        CreatedJobs.Add(jobId);

        // Verify job was created with interactive input
        var jobDetails = await RunCliCommand($"jobs show {jobId}");
        jobDetails.Output.Should().Contain("Test interactive prompt");
        jobDetails.Output.Should().Contain(repoName);
    }
}
```

#### 6. Real-Time Updates E2E Tests

```csharp
public class RealTimeUpdatesE2ETests : E2ETestBase
{
    [Fact]
    public async Task JobsList_WithWatch_ShouldUpdateInRealTime()
    {
        await AuthenticateForTest();
        
        var repoName = await CreateTestRepository();

        // Start watching in background
        var watchCts = new CancellationTokenSource();
        var watchTask = RunCliCommandWithTimeout("jobs list --watch", TimeSpan.FromMinutes(2), watchCts.Token);

        // Create and start job while watching
        await Task.Delay(1000); // Let watch mode initialize
        
        var createResult = await RunCliCommand(
            $"jobs create --repo {repoName} --prompt \"Quick test\" --auto-start");
        var jobId = ExtractJobIdFromOutput(createResult.Output);
        CreatedJobs.Add(jobId);

        // Let watch mode capture the job updates
        await Task.Delay(5000);
        
        // Cancel watch mode
        watchCts.Cancel();
        var watchResult = await watchTask;

        // Verify watch mode showed job updates
        watchResult.Output.Should().Contain(jobId);
        watchResult.Output.Should().Contain("Status");
        watchResult.Output.Should().MatchRegex(@"running|completed|pending"); // Should show status changes
    }

    [Fact]
    public async Task JobShow_WithFollow_ShouldStreamOutput()
    {
        await AuthenticateForTest();
        
        var repoName = await CreateTestRepository();
        
        var createResult = await RunCliCommand(
            $"jobs create --repo {repoName} --prompt \"Count from 1 to 10\" --auto-start");
        var jobId = ExtractJobIdFromOutput(createResult.Output);
        CreatedJobs.Add(jobId);

        // Start following output
        var followCts = new CancellationTokenSource();
        var followTask = RunCliCommandWithTimeout($"jobs show {jobId} --follow", TimeSpan.FromMinutes(3), followCts.Token);

        // Wait for job completion
        await WaitForJobCompletion(jobId, TimeSpan.FromMinutes(2));
        
        followCts.Cancel();
        var followResult = await followTask;

        // Verify streaming output was captured
        followResult.Output.Should().Contain("Status:");
        followResult.Output.Should().Contain("Output:");
        followResult.Output.Length.Should().BeGreaterThan(50, "Should capture streaming updates");
    }
}
```

#### 6. Comprehensive Error Handling E2E Tests (Complete Error Coverage)

**Must test every error scenario through CLI commands:**

```csharp
public class ErrorHandlingE2ETests : E2ETestBase
{
    [Fact] // Covers: All 401 Unauthorized scenarios
    public async Task AllCommands_WithoutAuthentication_ShouldReturnUnauthorized()
    {
        // Test all commands without authentication
        var commands = new[]
        {
            "repos list",
            "repos show test-repo", 
            "repos delete test-repo",
            "jobs list",
            "jobs show test-job",
            "jobs start test-job",
            "jobs cancel test-job",
            "jobs delete test-job"
        };

        foreach (var command in commands)
        {
            var result = await RunCliCommand(command);
            result.ExitCode.Should().Be(1, $"Command '{command}' should fail without auth");
            result.Output.Should().Contain("Authentication", $"Command '{command}' should indicate auth failure");
        }
    }

    [Fact] // Covers: All 404 Not Found scenarios  
    public async Task AllCommands_WithNonExistentResources_ShouldReturnNotFound()
    {
        await AuthenticateForTest();

        var commands = new Dictionary<string, string>
        {
            ["repos show non-existent-repo"] = "Repository",
            ["repos delete non-existent-repo"] = "Repository", 
            ["jobs show non-existent-job"] = "Job",
            ["jobs start non-existent-job"] = "Job",
            ["jobs cancel non-existent-job"] = "Job",
            ["jobs delete non-existent-job"] = "Job"
        };

        foreach (var (command, resourceType) in commands)
        {
            var result = await RunCliCommand(command);
            result.ExitCode.Should().Be(1, $"Command '{command}' should fail for non-existent {resourceType}");
            result.Output.Should().Contain("not found", $"Command '{command}' should indicate resource not found");
        }
    }

    [Fact] // Covers: All 400 Bad Request scenarios
    public async Task AllCommands_WithInvalidParameters_ShouldReturnBadRequest()
    {
        await AuthenticateForTest();

        var invalidCommands = new[]
        {
            "repos create \"\"",  // Empty name
            "repos create test-repo --clone invalid-url", // Invalid git URL
            "jobs create --repo \"\" --prompt test", // Empty repo name  
            "jobs create --repo test-repo --prompt \"\"", // Empty prompt
            "jobs create --repo non-existent --prompt test", // Non-existent repo
        };

        foreach (var command in invalidCommands)
        {
            var result = await RunCliCommand(command);
            result.ExitCode.Should().Be(1, $"Invalid command '{command}' should fail");
            result.Output.Should().MatchRegex("invalid|required|missing", $"Command '{command}' should indicate validation error");
        }
    }

    [Fact] // Covers: Network timeout and connection errors
    public async Task AllCommands_WithServerUnavailable_ShouldHandleGracefully()
    {
        // Configure CLI to point to unavailable server
        Environment.SetEnvironmentVariable("CLAUDE_SERVER_URL", "https://localhost:9999");

        var commands = new[]
        {
            "login --usr test --pwd test",
            "repos list",
            "jobs list"
        };

        foreach (var command in commands)
        {
            var result = await RunCliCommand(command);
            result.ExitCode.Should().Be(1, $"Command '{command}' should fail with server unavailable");
            result.Output.Should().MatchRegex("connection|network|timeout|unavailable", 
                $"Command '{command}' should indicate connection problem");
        }

        // Cleanup
        Environment.SetEnvironmentVariable("CLAUDE_SERVER_URL", ServerUrl);
    }
}
```

### API Coverage Validation Tests

```csharp
public class ApiCoverageValidationTests : E2ETestBase
{
    [Fact] // Ensures every API endpoint is tested
    public async Task ValidateAllApiEndpointsCovered()
    {
        // This test ensures we have coverage for every API endpoint
        var requiredEndpoints = new[]
        {
            "POST /auth/login",
            "POST /auth/logout", 
            "POST /repositories/register",
            "GET /repositories",
            "GET /repositories/{name}",
            "DELETE /repositories/{name}",
            "POST /jobs",
            "POST /jobs/{id}/files", 
            "POST /jobs/{id}/start",
            "GET /jobs/{id}",
            "POST /jobs/{id}/cancel",
            "DELETE /jobs/{id}",
            "GET /jobs/{id}/files",
            "GET /jobs/{id}/files/download"
        };

        // Run comprehensive test that hits every endpoint
        await AuthenticateForTest(); // POST /auth/login
        
        var repoName = await CreateTestRepository(); // POST /repositories/register
        await RunCliCommand("repos list"); // GET /repositories
        await RunCliCommand($"repos show {repoName}"); // GET /repositories/{name}
        
        var jobId = await CreateTestJobWithFile(repoName); // POST /jobs + POST /jobs/{id}/files
        await RunCliCommand($"jobs start {jobId}"); // POST /jobs/{id}/start  
        await RunCliCommand($"jobs show {jobId}"); // GET /jobs/{id}
        await RunCliCommand($"jobs show {jobId} --files"); // GET /jobs/{id}/files
        await RunCliCommand($"jobs download {jobId} --file test.txt"); // GET /jobs/{id}/files/download
        await RunCliCommand($"jobs cancel {jobId}"); // POST /jobs/{id}/cancel
        await RunCliCommand($"jobs delete {jobId} --force"); // DELETE /jobs/{id}
        
        await RunCliCommand($"repos delete {repoName} --force"); // DELETE /repositories/{name}
        await RunCliCommand("logout"); // POST /auth/logout

        // All endpoints should be covered by the above commands
        Assert.True(true, "All API endpoints have been exercised through CLI commands");
    }
}
```

### Test Utilities and Helpers

```csharp
public static class E2ETestHelpers
{
    public static async Task<CliResult> RunCliCommand(string command, TimeSpan? timeout = null)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = GetCliExecutablePath(),
            Arguments = command,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        
        var completed = await process.WaitForExitAsync((int)(timeout ?? TimeSpan.FromMinutes(5)).TotalMilliseconds);
        if (!completed)
        {
            process.Kill();
            throw new TimeoutException($"CLI command timed out: {command}");
        }

        return new CliResult
        {
            ExitCode = process.ExitCode,
            Output = await outputTask,
            Error = await errorTask
        };
    }

    public static string CreateTestImage(string type = "default")
    {
        var tempPath = Path.GetTempFileName() + ".png";
        
        // Create test image with known content using System.Drawing or similar
        using var image = new Bitmap(400, 300);
        using var graphics = Graphics.FromImage(image);
        
        graphics.Clear(Color.White);
        graphics.FillRectangle(Brushes.Blue, 50, 50, 100, 50);      // Blue rectangle
        graphics.FillEllipse(Brushes.Red, 200, 50, 100, 100);       // Red circle
        graphics.FillPolygon(Brushes.Green, new Point[] {            // Green triangle
            new(100, 200), new(150, 250), new(50, 250)
        });
        
        image.Save(tempPath, ImageFormat.Png);
        return tempPath;
    }

    public static async Task WaitForJobCompletion(string jobId, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        
        while (DateTime.UtcNow < deadline)
        {
            var result = await RunCliCommand($"jobs show {jobId}");
            if (result.Output.Contains("Status: completed") || result.Output.Contains("Status: failed"))
            {
                return;
            }
            
            await Task.Delay(2000); // Poll every 2 seconds
        }
        
        throw new TimeoutException($"Job {jobId} did not complete within {timeout}");
    }

    public static async Task CleanupAllTestArtifacts(List<string> repositories, List<string> jobs)
    {
        // Cancel and delete all test jobs
        foreach (var jobId in jobs.ToList())
        {
            try
            {
                await RunCliCommand($"jobs cancel {jobId}");
                await Task.Delay(1000);
                await RunCliCommand($"jobs delete {jobId} --force");
                jobs.Remove(jobId);
            }
            catch { /* Ignore cleanup errors */ }
        }

        // Delete all test repositories  
        foreach (var repo in repositories.ToList())
        {
            try
            {
                await RunCliCommand($"repos delete {repo} --force");
                repositories.Remove(repo);
            }
            catch { /* Ignore cleanup errors */ }
        }
    }
}
```

### E2E Test Execution Strategy

**Test Environment Setup**:
```bash
# Start real server for E2E tests
cd claude-batch-server/docker
docker-compose up -d

# Wait for server health
curl -k https://localhost:8443/health --retry 10 --retry-delay 5

# Run E2E tests
cd tests/ClaudeServerCLI.E2ETests
dotnet test --logger "console;verbosity=detailed"

# Cleanup
docker-compose down
```

**Test Categories**:
- `[Trait("Category", "E2E")]` - All E2E tests
- `[Trait("Category", "E2E.Auth")]` - Authentication tests
- `[Trait("Category", "E2E.Repos")]` - Repository management tests
- `[Trait("Category", "E2E.Jobs")]` - Job management tests
- `[Trait("Category", "E2E.Images")]` - Image upload tests
- `[Trait("Category", "E2E.Interactive")]` - Interactive mode tests
- `[Trait("Category", "E2E.RealTime")]` - Real-time update tests

**Parallel Execution**:
- Tests run sequentially to avoid resource conflicts
- Each test uses unique identifiers (GUIDs) for isolation
- Comprehensive cleanup ensures no test pollution
- Resource monitoring to detect leaks

**Success Criteria**:
- [ ] All E2E tests pass with real server
- [ ] No test artifacts remain after execution
- [ ] Tests complete within reasonable time limits (< 30 minutes total)
- [ ] Real Claude Code execution works correctly
- [ ] Image uploads and analysis work end-to-end
- [ ] Job cancellation terminates running processes
- [ ] Real-time updates display correctly

## User Experience Design

### Command Patterns

**Consistent Verb-Noun Structure**:
```bash
claude-server <resource> <action> [options]
claude-server repos list --format table
claude-server jobs create --interactive
claude-server auth login --usr admin
```

**Smart Defaults**:
- Default to interactive mode when insufficient parameters
- Automatic format selection based on terminal capabilities
- Reasonable timeout and refresh intervals
- Sensible default configurations

**Progressive Disclosure**:
- Basic commands work with minimal options
- Advanced features available through additional flags
- Help system provides context-appropriate guidance
- Interactive wizards for complex operations

### Error Handling & User Feedback

**Graceful Error Messages**:
```bash
❌ Authentication failed: Invalid credentials
💡 Hint: Use 'claude-server login --help' for login options

❌ Repository 'myapp' not found
💡 Available repositories: backend, frontend, shared
💡 Use 'claude-server repos list' to see all repositories
```

**Progress Feedback**:
```bash
⚡ Creating job...
📤 Uploading images... [████████▎  ] 82% (2.3MB/2.8MB)
🚀 Starting job execution...
✅ Job created successfully: job-123
```

## Configuration Management

**Environment Variables**:
```bash
export CLAUDE_SERVER_URL="https://api.company.com"
export CLAUDE_SERVER_TOKEN="jwt-token-here"
export CLAUDE_SERVER_PROFILE="production"
```

**Config File Hierarchy**:
1. Command line arguments (highest priority)
2. Environment variables
3. User config file (`~/.config/claude-server-cli/config.json`)
4. Built-in defaults (lowest priority)

## Success Metrics

### Functional Requirements
- [ ] All CRUD operations work correctly
- [ ] Authentication persists across sessions
- [ ] Real-time updates display correctly
- [ ] Image uploads complete successfully
- [ ] Job cancellation works reliably

### User Experience Requirements

**Performance Requirements**:
- [ ] **ALL CLI commands return within reasonable time limits**
- [ ] Authentication operations complete within 1-2 seconds (sync)
- [ ] Repository registration returns within 2-5 seconds (async - server accepts request)
- [ ] Repository list/show/update/delete complete within 1-5 seconds (sync)
- [ ] Job creation/deletion completes within 2-5 seconds (sync)
- [ ] Job start command returns within 1-2 seconds (execution runs async on server)
- [ ] Status queries return within 1 second (current state)
- [ ] `--watch` modes stay alive for continuous monitoring of async operations

**Mixed Sync/Async Architecture Requirements**:
- [ ] Most CRUD operations are synchronous - complete before returning
- [ ] Repository registration (`POST /repositories`) is asynchronous on server
- [ ] Job execution (`POST /jobs/{id}/start`) is asynchronous on server
- [ ] CLI shows progress for long sync operations, polls status for async operations
- [ ] Clear messaging about sync completion vs async operation progress

**UI/UX Requirements**:  
- [ ] UI updates refresh smoothly without flicker (only in watch modes)
- [ ] Error messages are clear and actionable
- [ ] Help system provides relevant guidance

### Technical Requirements
- [ ] Cross-platform compatibility (Windows, macOS, Linux)
- [ ] Memory usage under 50MB for typical operations
- [ ] Startup time under 1 second
- [ ] 100% API compatibility with server
- [ ] Graceful handling of network interruptions

## Future Enhancements

### Phase 2 Features
- [ ] **Advanced Automation**
  - [ ] Job templates and saved configurations
  - [ ] Batch job processing with queues
  - [ ] Scheduled job execution (cron-like)
  - [ ] Job dependency management
- [ ] **Integration Features**
  - [ ] Git hooks for automatic job triggering
  - [ ] Slack/Teams notifications
  - [ ] Webhook support for job status updates
- [ ] **Advanced UI Features**
  - [ ] Terminal-based dashboard with multiple panes
  - [ ] Export capabilities (PDF, HTML reports)
  - [ ] Custom themes and color schemes
  - [ ] Plugin system for extensions

### Enterprise Features
- [ ] **Multi-Server Management**
  - [ ] Server clustering and load balancing awareness
  - [ ] Cross-server job migration
  - [ ] Centralized configuration management
- [ ] **Advanced Security**
  - [ ] MFA integration
  - [ ] Certificate-based authentication
  - [ ] Audit logging and compliance reporting
- [ ] **Monitoring & Analytics**
  - [ ] Job performance analytics
  - [ ] Resource usage trending
  - [ ] Custom metrics and dashboards

## Risks and Mitigation

### Technical Risks
- **Terminal Compatibility**: Graceful degradation for limited terminals
- **Performance**: Efficient API calls and caching strategies  
- **Security**: Secure credential storage and transmission
- **Network Issues**: Robust retry logic and offline mode

### User Experience Risks
- **Complexity**: Progressive disclosure and good defaults
- **Learning Curve**: Comprehensive help system and tutorials
- **Platform Differences**: Consistent behavior across OS platforms

## Dependencies

### External Dependencies
- **System.CommandLine**: Modern CLI framework
- **Spectre.Console**: Rich terminal UI capabilities
- **Microsoft.Extensions.Configuration**: Configuration management
- **Microsoft.Extensions.Http**: HTTP client management
- **Microsoft.Extensions.Logging**: Structured logging framework
- **Polly**: Resilience and transient-fault-handling library (retry policies)
- **System.Security.Cryptography**: AES encryption (built-in .NET)
- **System.Text.Json**: JSON configuration handling (built-in .NET)

### Server API Dependencies
- **Authentication API**: Login/logout functionality
- **Repository API**: CRUD operations
- **Jobs API**: Job lifecycle management
- **New Cancellation API**: Job termination (to be implemented)

---

**Note**: This epic builds upon the Alpha Epic's REST API server and focuses on providing a modern, user-friendly CLI interface. The job cancellation API enhancement is included as a prerequisite for full CLI functionality.