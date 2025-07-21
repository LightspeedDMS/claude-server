# Claude Batch Server

A dockerized REST API server written in .NET Core that automates Claude Code execution in batch mode, supporting multi-user environments with proper user session isolation and authentication.

## Features

- **Shadow File Authentication**: OS-level user authentication with JWT tokens
- **Copy-on-Write Repository Management**: Efficient repository cloning using CoW filesystem features
- **Job Queue System**: Configurable concurrent job processing with queue management
- **User Impersonation**: Secure execution of Claude Code as authenticated users
- **Multi-User Support**: Complete isolation between user sessions
- **RESTful API**: Comprehensive API for job management, file operations, and monitoring
- **Docker Support**: Full containerization with privileged mode support

## Architecture

- **Backend**: .NET Core 8.0, ASP.NET Core Web API
- **Authentication**: JWT tokens with shadow file validation
- **Storage**: Btrfs/XFS Copy-on-Write support with hardlink fallback
- **Queue Management**: In-memory job queue with configurable concurrency
- **Logging**: Serilog with structured logging
- **Testing**: Comprehensive unit, integration, and E2E tests

## Quick Start

### Prerequisites

- Rocky Linux 9.x or Ubuntu 22.04+
- Root access for installation
- .NET 8.0 SDK
- Docker and Docker Compose
- Claude Code CLI

### Installation

```bash
# Clone the repository
git clone <repository-url>
cd claude-batch-server

# Run the installation script
sudo ./scripts/install.sh
```

### Configuration

1. Copy the environment template:
```bash
cp docker/.env.example docker/.env
```

2. Edit the configuration:
```bash
nano docker/.env
```

3. Key configuration options:
- `JWT_KEY`: Secret key for JWT token signing (32+ characters)
- `MAX_CONCURRENT_JOBS`: Maximum number of concurrent jobs (default: 5)
- `JOB_TIMEOUT_HOURS`: Job timeout in hours (default: 24)

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

**Security Benefits of Hash Authentication:**
- No plaintext passwords transmitted (even over HTTP)
- Compatible with shadow file format ($1$, $5$, $6$ algorithms)
- Client never needs to store plaintext passwords
- Maintains salt-based security from system password hashes

#### POST /auth/logout
Logout and revoke JWT token.

### Job Management

#### POST /jobs
Create a new job.

```json
{
  "prompt": "Your Claude Code prompt",
  "repository": "repository-name",
  "images": ["image1.png", "image2.jpg"],
  "options": {
    "timeout": 300
  }
}
```

#### POST /jobs/{jobId}/start
Queue and start job execution.

#### GET /jobs/{jobId}
Get job status and output.

#### DELETE /jobs/{jobId}
Terminate and clean up job.

#### GET /jobs
List all user jobs.

### Repository Management

#### GET /repositories
List available repositories.

### File Operations

#### GET /jobs/{jobId}/files
List files in job workspace.

#### GET /jobs/{jobId}/files/download?path=/path/to/file
Download file from job workspace.

#### GET /jobs/{jobId}/files/content?path=/path/to/file
Get text file content.

#### POST /jobs/{jobId}/images
Upload images for job.

## Development

### Building

```bash
# Restore packages
dotnet restore

# Build solution
dotnet build

# Run tests
dotnet test
```

### Project Structure

```
claude-batch-server/
├── src/
│   ├── ClaudeBatchServer.Api/          # Web API project
│   └── ClaudeBatchServer.Core/         # Core business logic
├── tests/
│   ├── ClaudeBatchServer.Tests/        # Unit tests
│   └── ClaudeBatchServer.IntegrationTests/ # Integration tests
├── docker/                             # Docker configuration
├── scripts/                            # Installation scripts
└── README.md
```

### Key Components

- **Authentication Service**: Shadow file-based authentication with JWT
- **Repository Service**: Copy-on-Write repository management
- **Job Service**: Job lifecycle and queue management
- **Claude Code Executor**: User impersonation and process execution

## Copy-on-Write Support

The system automatically detects and uses the most efficient CoW method:

1. **XFS Reflinks** (Rocky Linux) - `cp --reflink=always`
2. **ext4 Reflinks** (Ubuntu 22.04+) - `cp --reflink=always`
3. **Btrfs Snapshots** - `btrfs subvolume snapshot`
4. **Hardlink Fallback** - `rsync --link-dest`

## Security

- **User Isolation**: Jobs run under authenticated user context
- **JWT Authentication**: Secure token-based authentication
- **Input Validation**: All inputs are sanitized and validated
- **Audit Logging**: Comprehensive logging of all operations
- **Container Security**: Runs with minimal required privileges

## Monitoring

- **Structured Logging**: JSON-formatted logs with Serilog
- **Health Checks**: Built-in health monitoring
- **Metrics**: Job queue metrics and performance tracking
- **Log Rotation**: Automatic log rotation and cleanup

## Usage Examples

### Authentication with Hash (Recommended for HTTP)

```bash
# Step 1: Generate password hash
HASH=$(python3 -c "import crypt; print(crypt.crypt('mypassword123', crypt.mksalt(crypt.METHOD_SHA512)))")
echo "Generated hash: $HASH"

# Step 2: Login with hash
curl -X POST http://localhost:8080/auth/login \
  -H "Content-Type: application/json" \
  -d "{\"username\":\"testuser\",\"password\":\"$HASH\"}"

# Step 3: Use JWT token for authenticated requests
TOKEN="your-jwt-token-here"
curl -X POST http://localhost:8080/jobs \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN" \
  -d '{"prompt":"List files in this repository","repository":"test-repo"}'
```

### Authentication with Plaintext (HTTPS recommended)

```bash
# Login with plaintext password
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
2. **CoW Not Working**: Check filesystem support with the validation endpoint
3. **Jobs Stuck**: Check queue status and concurrent job limits
4. **Authentication Fails**: Verify user exists in system password database

### Log Locations

- **Systemd**: `journalctl -u claude-batch-server`
- **Docker**: `docker logs claude-batch-server`
- **Files**: `/var/log/claude-batch-server/`

## License

MIT License - see LICENSE file for details.

## Contributing

1. Fork the repository
2. Create a feature branch
3. Write tests for your changes
4. Ensure all tests pass
5. Submit a pull request

## Support

For issues and questions:
- Create an issue in the GitHub repository
- Check the troubleshooting section
- Review the API documentation