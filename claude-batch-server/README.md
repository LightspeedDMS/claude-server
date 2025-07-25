# Claude Batch Server

A dockerized REST API server written in .NET Core that automates Claude Code execution in batch mode, supporting multi-user environments with proper user session isolation, Git integration, semantic search, and authentication.

## Features

- **🔐 Shadow File Authentication**: OS-level user authentication with JWT tokens
- **📁 Copy-on-Write Repository Management**: Efficient repository cloning using CoW filesystem features
- **🔄 Git Integration**: Automatic git pull before job execution with status tracking
- **🧠 Cidx Semantic Search**: Per-job semantic indexing with Docker container isolation
- **⚡ Smart System Prompts**: Dynamic Claude Code prompts based on cidx availability
- **📋 Job Queue System**: Configurable concurrent job processing with queue management
- **👤 User Impersonation**: Secure execution of Claude Code as authenticated users
- **🔒 Multi-User Support**: Complete isolation between user sessions
- **📄 File Staging System**: Upload files to staging area with automatic copy to job workspace
- **🔗 Placeholder Replacement**: {{filename}} placeholders in prompts replaced with uploaded filenames
- **🌐 RESTful API**: Comprehensive API for job management, repository management, file operations
- **🏠 Tilde Path Expansion**: Configuration paths support ~/home directory expansion
- **🐳 Docker Support**: Full containerization with privileged mode support

## Architecture

- **Backend**: .NET Core 8.0, ASP.NET Core Web API
- **Authentication**: JWT tokens with shadow file validation
- **Storage**: Btrfs/XFS Copy-on-Write support with hardlink fallback
- **Git Integration**: Automatic repository updates with failure handling
- **Semantic Search**: Cidx with Voyage-id embeddings and per-job Docker containers
- **Queue Management**: In-memory job queue with configurable concurrency
- **Logging**: Serilog with structured logging
- **Testing**: Comprehensive unit, integration, and E2E tests with real Claude Code execution

## Quick Start

### Prerequisites

- Rocky Linux 9.x or Ubuntu 22.04+
- Root access for installation
- .NET 8.0 SDK
- Docker and Docker Compose
- Claude Code CLI
- Git (for repository management)

### Installation

```bash
# Clone the repository
git clone <repository-url>
cd claude-batch-server

# Run the installation script
sudo ./scripts/install.sh
```

### Configuration

The application uses `appsettings.json` and `appsettings.Development.json` for configuration. All paths support tilde (`~`) expansion for user home directory.

Key configuration sections:

```json
{
  "Workspace": {
    "RepositoriesPath": "~/claude-code-server-workspace/repos",
    "JobsPath": "~/claude-code-server-workspace/jobs"
  },
  "Jobs": {
    "MaxConcurrent": "5",
    "TimeoutHours": "24",
    "UseNewWorkflow": "true"
  },
  "Jwt": {
    "Key": "YourSuperSecretJwtKeyThatShouldBe32CharactersOrLonger!",
    "ExpiryHours": "24"
  },
  "Auth": {
    "ShadowFilePath": "~/Dev/claude-server/claude-batch-server/claude-server-shadow",
    "PasswdFilePath": "~/Dev/claude-server/claude-batch-server/claude-server-passwd"
  }
}

### Running with Docker

```bash
# Start the services
docker compose -f docker/docker-compose.yml up -d

# View logs
docker compose -f docker/docker-compose.yml logs -f

# Stop the services
docker compose -f docker/docker-compose.yml down
```

### Running with Systemd

```bash
# Start the service
systemctl start claude-batch-server

# Check status
systemctl status claude-batch-server

# View logs
journalctl -u claude-batch-server -f
```

## API Documentation

### Authentication

#### POST /auth/login
Authenticate with system credentials using either plaintext passwords or pre-computed hashes.

**Option 1: Plaintext Password**
```json
{
  "username": "your-username",
  "password": "your-password"
}
```

**Option 2: Pre-computed Hash (Recommended for HTTP)**
```json
{
  "username": "your-username",
  "password": "$6$randomsalt$precomputedHashFromShadowFile..."
}
```

Response:
```json
{
  "token": "jwt-token",
  "user": "username",
  "expires": "2024-07-22T12:00:00Z"
}
```

**Generating Password Hashes:**
```bash
# Generate SHA-512 hash (recommended)
python3 -c "import crypt; print(crypt.crypt('your_password', crypt.mksalt(crypt.METHOD_SHA512)))"

# Or using openssl
openssl passwd -6 your_password

# Interactive (secure - doesn't show in bash history)
python3 -c "import crypt, getpass; print(crypt.crypt(getpass.getpass('Password: '), crypt.mksalt(crypt.METHOD_SHA512)))"
```

#### POST /auth/logout
Logout and revoke JWT token.

### Repository Management

#### GET /repositories
List all registered repositories.

Response:
```json
[
  {
    "name": "my-project",
    "path": "/workspace/repos/my-project",
    "description": "My awesome project",
    "gitUrl": "https://github.com/user/my-project.git",
    "registeredAt": "2024-07-22T10:00:00Z",
    "cloneStatus": "completed"
  }
]
```

#### POST /repositories/register
Register a new Git repository by cloning it.

```json
{
  "name": "repository-name",
  "gitUrl": "https://github.com/user/repo.git",
  "description": "Optional description"
}
```

Response:
```json
{
  "name": "repository-name",
  "path": "/workspace/repos/repository-name",
  "description": "Optional description",
  "gitUrl": "https://github.com/user/repo.git",
  "registeredAt": "2024-07-22T10:00:00Z",
  "cloneStatus": "completed"
}
```

#### DELETE /repositories/{repoName}
Unregister and remove repository from disk.

Response:
```json
{
  "success": true,
  "removed": true,
  "message": "Repository my-project successfully unregistered and removed from disk"
}
```

### Job Management

#### POST /jobs
Create a new job with Git and Cidx integration.

```json
{
  "prompt": "Explore this repository and find how many test files test TStringHashTrie",
  "repository": "repository-name",
  "images": ["screenshot1.png", "diagram.jpg"],
  "options": {
    "timeout": 600,
    "gitAware": true,
    "cidxAware": true
  }
}
```

**New Job Options:**
- `gitAware` (default: true): Enable automatic git pull before execution
- `cidxAware` (default: true): Enable semantic indexing and cidx integration
- `timeout`: Job timeout in seconds

#### POST /jobs/{jobId}/start
Queue and start job execution with Git + Cidx workflow.

#### GET /jobs/{jobId}
Get job status with Git and Cidx operation tracking.

Response:
```json
{
  "jobId": "12345678-1234-1234-1234-123456789abc",
  "status": "completed",
  "output": "...",
  "exitCode": 0,
  "cowPath": "/workspace/jobs/12345678-1234-1234-1234-123456789abc",
  "queuePosition": 0,
  "gitStatus": "pulled",
  "cidxStatus": "ready"
}
```

**Status Values:**
- **Job Status**: `created`, `queued`, `git_pulling`, `git_failed`, `cidx_indexing`, `cidx_ready`, `running`, `completed`, `failed`, `timeout`
- **Git Status**: `not_checked`, `checking`, `pulled`, `failed`, `not_git_repo`
- **Cidx Status**: `not_started`, `starting`, `indexing`, `ready`, `failed`, `stopped`

#### DELETE /jobs/{jobId}
Terminate and clean up job (including cidx containers).

#### GET /jobs
List all user jobs.

### File Operations

#### GET /jobs/{jobId}/files
List files in job workspace.

#### GET /jobs/{jobId}/files/download?path=/path/to/file
Download file from job workspace.

#### GET /jobs/{jobId}/files/content?path=/path/to/file
Get text file content.

#### POST /jobs/{jobId}/files
Upload files for job with support for {{filename}} placeholder replacement in prompts.

**File Upload with Staging:**
```bash
curl -X POST http://localhost:5000/jobs/{jobId}/files \
  -H "Authorization: Bearer $TOKEN" \
  -F "files=@screenshot.png" \
  -F "files=@data.csv"
```

Response:
```json
[
  {
    "filename": "screenshot.png",
    "path": "/staging/screenshot.png",
    "serverPath": "/full/server/path/screenshot.png",
    "fileType": "image/png",
    "fileSize": 12345,
    "overwritten": false
  }
]
```

## Git + Cidx Integration

### Git Integration Workflow

1. **Repository Registration**: Clone Git repositories via API to configured repositories path
2. **Job Creation**: Create CoW clone of repository to job workspace  
3. **File Upload Staging**: Files uploaded to staging area with hash-based naming to prevent conflicts
4. **Pre-Execution Setup**: 
   - Automatic `git pull` to get latest changes
   - Copy staged files to job workspace (removing hash from filenames)
   - Replace {{filename}} placeholders in prompts with actual uploaded filenames
5. **Failure Handling**: Jobs fail with "git_failed" status if git operations fail

### Cidx Semantic Search Workflow

1. **Container Start**: Each job gets isolated cidx Docker container
2. **Post-Git Indexing**: Run `cidx index --reconcile` after successful git pull
3. **Status Validation**: Check Docker Services, Voyage-AI Provider, Ollama, Qdrant
4. **Dynamic System Prompts**: Claude receives cidx-aware instructions
5. **Automatic Cleanup**: Cidx container stopped after Claude execution

### System Prompt Integration

The system generates dynamic Claude Code prompts based on cidx availability:

**When Cidx is Ready:**
```
CIDX SEMANTIC SEARCH AVAILABLE

Your primary code exploration tool is cidx (semantic search). Always prefer cidx over grep/find/rg when available.

USAGE PRIORITY:
1. FIRST: Check cidx status with: cidx status
2. IF all services show "Running/Ready/Not needed/Ready": Use cidx for all code searches
3. IF any service shows failures: Fall back to grep/find/rg

CIDX EXAMPLES:
- Find authentication: cidx query "authentication function" --quiet
- Find error handling: cidx query "error handling patterns" --language python --quiet
- Find database code: cidx query "database connection" --path */services/* --quiet
```

**When Cidx is Unavailable:**
```
CIDX SEMANTIC SEARCH UNAVAILABLE

Cidx services are not ready. Use traditional search tools for code exploration.

USE TRADITIONAL TOOLS:
- grep -r "pattern" .
- find . -name "*.ext" -exec grep "pattern" {} \;
- rg "pattern" --type language
```

## Development

### Building

```bash
# Restore packages
dotnet restore

# Build solution
dotnet build

# Run tests
dotnet test

# Run specific test category
dotnet test --filter Category=E2E
dotnet test --filter Category=Integration
```

### Project Structure

```
claude-batch-server/
├── src/
│   ├── ClaudeBatchServer.Api/          # Web API project
│   │   ├── Controllers/
│   │   │   ├── AuthController.cs       # Authentication endpoints
│   │   │   ├── JobsController.cs       # Job management endpoints
│   │   │   └── RepositoriesController.cs # Repository management endpoints
│   │   └── Program.cs
│   └── ClaudeBatchServer.Core/         # Core business logic
│       ├── Services/
│       │   ├── AuthService.cs          # Shadow file authentication
│       │   ├── JobService.cs           # Job lifecycle management
│       │   ├── CowRepositoryService.cs # Repository + CoW management
│       │   └── ClaudeCodeExecutor.cs   # Git + Cidx + Claude execution
│       ├── Models/                     # Domain models
│       ├── DTOs/                       # API data transfer objects
│       └── SystemPrompts/              # Externalized prompt templates
│           ├── cidx-system-prompt-template.txt
│           └── cidx-unavailable-system-prompt-template.txt
├── tests/
│   ├── ClaudeBatchServer.Tests/        # Unit tests
│   └── ClaudeBatchServer.IntegrationTests/ # Integration + E2E tests
│       └── GitCidxIntegrationTests.cs  # End-to-end Git + Cidx tests
├── docker/                             # Docker configuration
├── scripts/                            # Installation scripts
└── README.md
```

### Key Components

- **Authentication Service**: Shadow file-based authentication with JWT
- **Repository Service**: Git repository registration and Copy-on-Write management
- **Job Service**: Job lifecycle, queue management, and status tracking
- **Claude Code Executor**: User impersonation, Git integration, Cidx management, and process execution

### Testing Strategy

- **Unit Tests**: Mock external dependencies (filesystem, processes, auth)
- **Integration Tests**: Real filesystem operations, mocked Claude Code execution
- **E2E Tests**: Real Claude Code execution with Git + Cidx integration

**E2E Test Coverage:**
- Repository registration and git cloning
- Git pull integration and failure handling
- Cidx container lifecycle and semantic indexing
- Dynamic system prompt generation based on cidx status
- Complete workflow from repository registration to Claude execution
- Automatic cleanup and resource management

## Copy-on-Write Support

The system automatically detects and uses the most efficient CoW method:

1. **XFS Reflinks** (Rocky Linux) - `cp --reflink=always`
2. **ext4 Reflinks** (Ubuntu 22.04+) - `cp --reflink=always`
3. **Btrfs Snapshots** - `btrfs subvolume snapshot`
4. **Hardlink Fallback** - `rsync --link-dest`

## Configuration

### Workspace Paths (with Tilde Expansion)

```json
{
  "Workspace": {
    "RepositoriesPath": "~/claude-code-server-workspace/repos",
    "JobsPath": "~/claude-code-server-workspace/jobs"
  }
}
```

### System Prompts

```json
{
  "SystemPrompts": {
    "CidxAvailableTemplatePath": "SystemPrompts/cidx-system-prompt-template.txt",
    "CidxUnavailableTemplatePath": "SystemPrompts/cidx-unavailable-system-prompt-template.txt"
  }
}
```

### Job Configuration

```json
{
  "Jobs": {
    "MaxConcurrent": "5",
    "TimeoutHours": "24"
  }
}
```

## Security

- **User Isolation**: Jobs run under authenticated user context
- **JWT Authentication**: Secure token-based authentication with manual validation
- **Input Validation**: All inputs are sanitized and validated
- **Git Security**: Safe repository cloning and git operations
- **Container Isolation**: Cidx runs in isolated Docker containers
- **Audit Logging**: Comprehensive logging of all operations
- **Container Security**: Runs with minimal required privileges

## Monitoring

- **Structured Logging**: JSON-formatted logs with Serilog
- **Git Operation Tracking**: Real-time status of git pull operations
- **Cidx Status Monitoring**: Container health and service readiness
- **Job Queue Metrics**: Queue position and processing statistics
- **Health Checks**: Built-in health monitoring
- **Log Rotation**: Automatic log rotation and cleanup

## Usage Examples

### Complete Workflow: Repository to Code Exploration

```bash
# Step 1: Authenticate
TOKEN=$(curl -s -X POST http://localhost:8080/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"testuser","password":"mypassword"}' | jq -r '.token')

# Step 2: Register a Git repository
curl -X POST http://localhost:8080/repositories/register \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN" \
  -d '{
    "name": "tries-repo",
    "gitUrl": "https://github.com/jsbattig/tries.git",
    "description": "Test repository for semantic search"
  }'

# Step 3: Create job with Git + Cidx integration
JOB_ID=$(curl -s -X POST http://localhost:8080/jobs \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN" \
  -d '{
    "prompt": "Explore this repository and find how many test files are testing TStringHashTrie. Explain your methodology.",
    "repository": "tries-repo",
    "options": {
      "gitAware": true,
      "cidxAware": true,
      "timeout": 600
    }
  }' | jq -r '.jobId')

# Step 4: Start the job
curl -X POST http://localhost:8080/jobs/$JOB_ID/start \
  -H "Authorization: Bearer $TOKEN"

# Step 5: Monitor job progress
while true; do
  STATUS=$(curl -s http://localhost:8080/jobs/$JOB_ID \
    -H "Authorization: Bearer $TOKEN" | jq -r '.status')
  echo "Job status: $STATUS"
  if [[ "$STATUS" == "completed" || "$STATUS" == "failed" ]]; then
    break
  fi
  sleep 2
done

# Step 6: Get final results
curl -s http://localhost:8080/jobs/$JOB_ID \
  -H "Authorization: Bearer $TOKEN" | jq '.output'

# Step 7: Cleanup repository
curl -X DELETE http://localhost:8080/repositories/tries-repo \
  -H "Authorization: Bearer $TOKEN"
```

### Authentication Examples

**Hash Authentication (Recommended for HTTP):**
```bash
# Generate password hash
HASH=$(python3 -c "import crypt; print(crypt.crypt('mypassword123', crypt.mksalt(crypt.METHOD_SHA512)))")

# Login with hash
curl -X POST http://localhost:8080/auth/login \
  -H "Content-Type: application/json" \
  -d "{\"username\":\"testuser\",\"password\":\"$HASH\"}"
```

**Plaintext Authentication (HTTPS recommended):**
```bash
curl -X POST http://localhost:8080/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"testuser","password":"myplaintextpassword"}'
```

## API Access

- **HTTP**: http://localhost:5000 (systemd) or http://localhost:8080 (docker)
- **HTTPS**: https://localhost:8443 (docker with nginx)
- **Swagger UI**: http://localhost:5000/swagger (development)

## Troubleshooting

### Common Issues

1. **Permission Denied**: Ensure the service runs with appropriate privileges
2. **Git Clone Fails**: Check network connectivity and repository URLs
3. **Cidx Services Down**: Verify Docker is running and has sufficient resources
4. **CoW Not Working**: Check filesystem support with the validation endpoint
5. **Jobs Stuck in git_pulling**: Check git remote accessibility
6. **Cidx Indexing Fails**: Check available disk space and Docker container limits
7. **Authentication Fails**: Verify user exists in system password database

### Debug Git + Cidx Issues

```bash
# Check job status with Git and Cidx details
curl -s http://localhost:8080/jobs/$JOB_ID \
  -H "Authorization: Bearer $TOKEN" | jq '{status, gitStatus, cidxStatus, output}'

# Check repository registration status
curl -s http://localhost:8080/repositories \
  -H "Authorization: Bearer $TOKEN" | jq '.[] | {name, cloneStatus, gitUrl}'

# Monitor cidx container (if accessible)
docker ps | grep cidx
docker logs <cidx-container-id>
```

### Log Locations

- **Systemd**: `journalctl -u claude-batch-server`
- **Docker**: `docker logs claude-batch-server`
- **Files**: `/var/log/claude-batch-server/`

### Technical Debt

**Known Issues:**
- **JWT Middleware**: Currently using manual JWT validation as workaround for middleware authentication issues
- **Planned Fix**: Debug and resolve middleware configuration for consistent authentication across all controllers

## Performance

- **Git Operations**: Repositories are cloned once and reused via CoW clones
- **Cidx Efficiency**: Per-job semantic indexing with automatic container cleanup
- **Concurrent Processing**: Configurable job concurrency with queue management
- **Resource Management**: Automatic cleanup of workspaces and cidx containers

## License

MIT License - see LICENSE file for details.

## Contributing

1. Fork the repository
2. Create a feature branch
3. Write tests for your changes (including E2E tests for Git + Cidx integration)
4. Ensure all tests pass
5. Submit a pull request

## Support

For issues and questions:
- Create an issue in the GitHub repository
- Check the troubleshooting section
- Review the API documentation
- Consult the Git + Cidx integration test examples