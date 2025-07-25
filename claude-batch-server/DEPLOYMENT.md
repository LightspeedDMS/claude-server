# Claude Batch Server - Deployment Guide

Quick deployment guide for Claude Batch Server with Git + Cidx integration.

## Prerequisites

### System Requirements
- **OS**: Rocky Linux 9.x or Ubuntu 22.04+
- **CPU**: 4+ cores recommended for concurrent jobs
- **RAM**: 8GB+ (4GB+ for cidx containers)
- **Storage**: Copy-on-Write filesystem (XFS/ext4/Btrfs)
- **Network**: Internet access for git cloning

### Software Dependencies
- .NET 8.0 SDK
- Docker & Docker Compose
- Claude Code CLI
- Git
- Python 3 (for password hashing)

## Quick Install

### Automatic Installation
```bash
# Clone repository
git clone <repository-url>
cd claude-batch-server

# Run installation script (Rocky Linux/Ubuntu)
sudo ./scripts/install.sh
```

### Manual Installation

#### 1. Install Dependencies

**Rocky Linux 9:**
```bash
# Install .NET 8.0
sudo dnf install -y dotnet-sdk-8.0

# Install Docker
sudo dnf install -y docker docker-compose
sudo systemctl enable --now docker
sudo usermod -aG docker $USER

# Install Claude Code
curl -fsSL https://claude.ai/install.sh | bash

# Install Git
sudo dnf install -y git
```

**Ubuntu 22.04+:**
```bash
# Install .NET 8.0
wget https://dot.net/v1/dotnet-install.sh
chmod +x dotnet-install.sh
sudo ./dotnet-install.sh --channel 8.0

# Install Docker
sudo apt update
sudo apt install -y docker.io docker-compose
sudo systemctl enable --now docker
sudo usermod -aG docker $USER

# Install Claude Code
curl -fsSL https://claude.ai/install.sh | bash

# Install Git
sudo apt install -y git
```

#### 2. Verify Copy-on-Write Support
```bash
# Test CoW on current filesystem
cp --reflink=always /etc/passwd /tmp/test-cow 2>/dev/null && echo "CoW supported" || echo "CoW not supported, will use fallback"
rm -f /tmp/test-cow
```

## Configuration

### 1. Configuration Files Setup

The application uses ASP.NET Core configuration files instead of environment variables:

```bash
# Main configuration file
nano src/ClaudeBatchServer.Api/appsettings.json

# Development overrides  
nano src/ClaudeBatchServer.Api/appsettings.Development.json
```

### 2. Key Configuration Sections

**Required Settings in appsettings.json:**
```json
{
  "Jwt": {
    "Key": "YourSuperSecretJwtKeyThatShouldBe32CharactersOrLonger!",
    "ExpiryHours": "24"
  },
  "Workspace": {
    "RepositoriesPath": "~/claude-code-server-workspace/repos",
    "JobsPath": "~/claude-code-server-workspace/jobs"
  },
  "Jobs": {
    "MaxConcurrent": "5",
    "TimeoutHours": "24",
    "UseNewWorkflow": "true"
  },
  "Claude": {
    "Command": "claude"
  }
}
```

**Authentication Settings (appsettings.Development.json):**
```json
{
  "Auth": {
    "ShadowFilePath": "~/Dev/claude-server/claude-batch-server/claude-server-shadow",
    "PasswdFilePath": "~/Dev/claude-server/claude-batch-server/claude-server-passwd"
  }
}
```

**Optional Logging Configuration:**
```json
{
  "Serilog": {
    "WriteTo": [
      {
        "Name": "File",
        "Args": {
          "path": "~/claude-batch-server-logs/app-.log",
          "rollingInterval": "Day",
          "retainedFileCountLimit": 7
        }
      }
    ]
  }
}
```

### 3. User Authentication Setup

**Create test user and shadow files:**
```bash
# Create custom shadow and passwd files for the application
# These are NOT system files - they're application-specific

# Create passwd file entry
echo "testuser:x:1000:1000:Test User:/home/testuser:/bin/bash" > claude-server-passwd

# Generate password hash
HASH=$(python3 -c "import crypt; print(crypt.crypt('testpassword', crypt.mksalt(crypt.METHOD_SHA512)))")

# Create shadow file entry  
echo "testuser:$HASH:19000:0:99999:7:::" > claude-server-shadow

# Secure the files
chmod 600 claude-server-shadow
chmod 644 claude-server-passwd
```

**User management scripts:**
```bash  
# Use provided scripts for user management
./scripts/add-user.sh testuser testpassword
./scripts/list-users.sh
./scripts/update-user.sh testuser newpassword
./scripts/remove-user.sh testuser
```

## Deployment Options

### Docker Deployment (Recommended)

#### 1. Build and Start
```bash
# Build and start services
docker compose -f docker/docker-compose.yml up -d

# Check status
docker compose -f docker/docker-compose.yml ps

# View logs
docker compose -f docker/docker-compose.yml logs -f
```

#### 2. Verify Deployment
```bash
# Health check
curl http://localhost:8080/health

# Test authentication
curl -X POST http://localhost:8080/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"testuser","password":"testpassword"}'
```

### Systemd Deployment

#### 1. Create Service File

**CRITICAL**: The service must include the Claude Code binary in PATH:

```bash
# First, determine where Claude Code is installed
CLAUDE_PATH=$(which claude 2>/dev/null || echo "$HOME/.local/bin/claude")
CLAUDE_DIR=$(dirname "$CLAUDE_PATH")

# Create the service file with correct PATH
sudo tee /etc/systemd/system/claude-batch-server.service << EOF
[Unit]
Description=Claude Batch Server
After=network.target docker.service
Requires=docker.service

[Service]
Type=simple
User=root
WorkingDirectory=/opt/claude-batch-server
ExecStart=/usr/bin/dotnet ClaudeBatchServer.Api.dll
Restart=always
RestartSec=10
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=ASPNETCORE_URLS=http://localhost:5000
Environment=PATH=$CLAUDE_DIR:/root/.dotnet:/root/.dotnet/bin:/usr/local/bin:/usr/bin:/bin
Environment=DOTNET_ROOT=/root/.dotnet

[Install]
WantedBy=multi-user.target
EOF
```

**Common Claude Code Installation Paths:**
- `$HOME/.local/bin/claude` (most common)
- `/usr/local/bin/claude` 
- `/opt/claude/bin/claude`

**Path Configuration Examples:**

For user installation (`~/.local/bin/claude`):
```bash
Environment=PATH=/root/.local/bin:/root/.dotnet:/root/.dotnet/bin:/usr/local/bin:/usr/bin:/bin
```

For system installation (`/usr/local/bin/claude`):
```bash
Environment=PATH=/usr/local/bin:/root/.dotnet:/root/.dotnet/bin:/usr/bin:/bin
```

For custom installation (`/opt/claude/bin/claude`):
```bash
Environment=PATH=/opt/claude/bin:/root/.dotnet:/root/.dotnet/bin:/usr/local/bin:/usr/bin:/bin
```

#### 2. Deploy Application
```bash
# Build application
dotnet publish src/ClaudeBatchServer.Api -c Release -o /opt/claude-batch-server

# Set permissions
sudo chown -R root:root /opt/claude-batch-server
sudo chmod +x /opt/claude-batch-server/ClaudeBatchServer.Api

# Start service
sudo systemctl enable claude-batch-server
sudo systemctl start claude-batch-server

# Check status
sudo systemctl status claude-batch-server
```

## Post-Deployment Setup

### 1. Create Workspace Directories
```bash
# Create workspace directories (using tilde expansion paths)
mkdir -p ~/claude-code-server-workspace/{repos,jobs}
mkdir -p ~/claude-code-server-workspace/jobs/staging
chmod 755 ~/claude-code-server-workspace ~/claude-code-server-workspace/repos ~/claude-code-server-workspace/jobs
```

### 2. Configure Log Rotation
```bash
sudo tee /etc/logrotate.d/claude-batch-server << EOF
/var/log/claude-batch-server/*.log {
    daily
    rotate 7
    missingok
    notifempty
    compress
    delaycompress
    copytruncate
}
EOF
```

### 3. Test Complete Workflow
```bash
# Get authentication token
TOKEN=$(curl -s -X POST http://localhost:8080/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"testuser","password":"testpassword"}' | jq -r '.token')

# Register test repository
curl -X POST http://localhost:8080/repositories/register \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "test-repo",
    "gitUrl": "https://github.com/jsbattig/tries.git",
    "description": "Test repository"
  }'

# Create and run test job
JOB_ID=$(curl -s -X POST http://localhost:8080/jobs \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "prompt": "List the main directories in this repository",
    "repository": "test-repo",
    "options": {"gitAware": true, "cidxAware": true, "timeout": 300}
  }' | jq -r '.jobId')

curl -X POST http://localhost:8080/jobs/$JOB_ID/start \
  -H "Authorization: Bearer $TOKEN"

# Monitor job
echo "Job ID: $JOB_ID"
echo "Monitor: curl http://localhost:8080/jobs/$JOB_ID -H 'Authorization: Bearer $TOKEN'"
```

## Security Configuration

### 1. Firewall Setup
```bash
# Allow HTTP/HTTPS traffic
sudo firewall-cmd --permanent --add-service=http
sudo firewall-cmd --permanent --add-service=https
sudo firewall-cmd --permanent --add-port=8080/tcp
sudo firewall-cmd --reload
```

### 2. SSL/TLS Setup (Production)
```bash
# Generate self-signed certificate (development)
sudo openssl req -x509 -nodes -days 365 -newkey rsa:2048 \
  -keyout /etc/ssl/private/claude-batch-server.key \
  -out /etc/ssl/certs/claude-batch-server.crt

# Update nginx configuration for HTTPS
# Edit docker/nginx.conf to include SSL configuration
```

### 3. User Permissions
```bash
# Create dedicated service user (optional)
sudo useradd -r -s /bin/false claude-batch-server

# Set appropriate permissions
sudo chown -R claude-batch-server:claude-batch-server /workspace
sudo chmod 750 /workspace
```

## Monitoring and Maintenance

### 1. Log Monitoring
```bash
# Docker logs
docker compose -f docker/docker-compose.yml logs -f

# Systemd logs  
sudo journalctl -u claude-batch-server -f

# File logs
sudo tail -f /var/log/claude-batch-server/app-*.log
```

### 2. Health Monitoring
```bash
# Basic health check
curl http://localhost:8080/health

# Detailed status
curl http://localhost:8080/admin/status -H "Authorization: Bearer $ADMIN_TOKEN"

# Git + Cidx status in jobs
curl http://localhost:8080/jobs -H "Authorization: Bearer $TOKEN" | \
  jq '.[] | {jobId, status, gitStatus, cidxStatus}'
```

### 3. Maintenance Tasks
```bash
# Clean up old jobs (if using systemd)
sudo systemctl stop claude-batch-server
sudo find /workspace/jobs -type d -mtime +7 -exec rm -rf {} +
sudo systemctl start claude-batch-server

# Docker cleanup
docker compose -f docker/docker-compose.yml down
docker system prune -f
docker compose -f docker/docker-compose.yml up -d

# Check disk usage
df -h /workspace
du -sh /workspace/repos/*
du -sh /workspace/jobs/*
```

## Troubleshooting

### Common Issues

#### Claude Code Command Not Found (Exit Code 127)

**Symptoms**: Jobs fail with "command not found" error, exit code 127

**Cause**: systemd service PATH doesn't include Claude Code binary location

**Solution**:
```bash
# 1. Find Claude Code installation
which claude
ls -la ~/.local/bin/claude
ls -la /usr/local/bin/claude

# 2. Update systemd service PATH
sudo systemctl edit claude-batch-server

# Add this override (replace /path/to/claude with actual path):
[Service]
Environment=PATH=/root/.local/bin:/root/.dotnet:/root/.dotnet/bin:/usr/local/bin:/usr/bin:/bin

# 3. Reload and restart
sudo systemctl daemon-reload
sudo systemctl restart claude-batch-server

# 4. Verify PATH in service
sudo systemctl show claude-batch-server --property=Environment
```

**Quick Fix Script**:
```bash
#!/bin/bash
# Fix Claude Code PATH for systemd service

CLAUDE_PATH=$(which claude 2>/dev/null)
if [ -z "$CLAUDE_PATH" ]; then
    echo "❌ Claude Code not found in PATH"
    echo "Install Claude Code first: curl -fsSL https://claude.ai/install.sh | bash"
    exit 1
fi

CLAUDE_DIR=$(dirname "$CLAUDE_PATH")
echo "✅ Found Claude Code at: $CLAUDE_PATH"

# Create systemd override
sudo mkdir -p /etc/systemd/system/claude-batch-server.service.d
sudo tee /etc/systemd/system/claude-batch-server.service.d/claude-path.conf << EOF
[Service]
Environment=PATH=$CLAUDE_DIR:/root/.dotnet:/root/.dotnet/bin:/usr/local/bin:/usr/bin:/bin
EOF

sudo systemctl daemon-reload
sudo systemctl restart claude-batch-server

echo "✅ Updated systemd service PATH"
echo "✅ Service restarted"
```

#### Repository Not CIDX-Aware Error

**Symptoms**: "Repository 'name' is not cidx-aware or was not properly indexed during registration"

**Cause**: Repository CidxAware flag not set correctly during registration

**Solution**:
```bash
# 1. Check repository status
curl -X GET http://localhost:8080/repositories/REPO_NAME \
  -H "Authorization: Bearer $TOKEN"

# 2. Look for CidxAware field in response
# If false or missing, re-register repository:

# 3. Delete and re-register repository
curl -X DELETE http://localhost:8080/repositories/REPO_NAME \
  -H "Authorization: Bearer $TOKEN"

curl -X POST http://localhost:8080/repositories/register \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "REPO_NAME",
    "gitUrl": "https://github.com/user/repo.git",
    "description": "Repository with CIDX indexing"
  }'

# 4. Wait for indexing to complete
curl -X GET http://localhost:8080/repositories/REPO_NAME \
  -H "Authorization: Bearer $TOKEN" | jq '.cloneStatus'
```

**Check CIDX Indexing Status**:
```bash
# Monitor repository registration progress
watch 'curl -s -X GET http://localhost:8080/repositories/REPO_NAME -H "Authorization: Bearer $TOKEN" | jq "{name: .name, status: .cloneStatus, cidxAware: .cidxAware}"'
```

#### CoW Not Working
```bash
# Check filesystem support
lsblk -f
sudo tune2fs -l /dev/sda1 | grep features

# Test CoW manually
cp --reflink=always /etc/passwd /tmp/test-cow
ls -li /etc/passwd /tmp/test-cow  # Should show same inode
```

#### Docker Issues
```bash
# Check Docker status
sudo systemctl status docker
docker ps
docker compose -f docker/docker-compose.yml ps

# Restart Docker services
docker compose -f docker/docker-compose.yml restart
```

#### Authentication Problems
```bash
# Check shadow file access
sudo ls -la /etc/shadow
sudo test -r /etc/shadow && echo "Readable" || echo "Not readable"

# Test password hash
python3 -c "
import crypt
hash = crypt.crypt('testpassword', crypt.mksalt(crypt.METHOD_SHA512))
print(f'Generated hash: {hash}')
print(f'Verification: {crypt.crypt(\"testpassword\", hash) == hash}')
"
```

#### Git Clone Issues
```bash
# Test git access
git clone https://github.com/jsbattig/tries.git /tmp/test-clone
rm -rf /tmp/test-clone

# Check network connectivity
curl -I https://github.com
```

#### Cidx Container Issues
```bash
# Check Docker resources
docker stats

# Check available disk space
df -h

# Test cidx manually
docker run --rm cidx/cidx:latest cidx --version
```

### Performance Tuning

#### Resource Limits
```bash
# Set Docker memory limits in docker-compose.yml
services:
  claude-batch-server:
    mem_limit: 2g
    memswap_limit: 2g
    
# Monitor resource usage
docker stats
top -p $(pgrep -f ClaudeBatchServer)
```

#### Concurrent Job Limits
```bash
# Adjust based on system capacity
# 4-core system: MAX_CONCURRENT_JOBS=2-3
# 8-core system: MAX_CONCURRENT_JOBS=4-5
# 16-core system: MAX_CONCURRENT_JOBS=8-10
```

**Queue Behavior**: The system automatically queues jobs exceeding `MaxConcurrent` rather than rejecting them:
- Jobs are processed in FIFO order as slots become available
- No memory overhead for queued jobs (only job metadata stored)
- Queue survives server restarts and maintains job order
- Monitor queue length via API: `GET /jobs` shows all user jobs with queue positions

For detailed troubleshooting, see [README.md](README.md) and [API-REFERENCE.md](API-REFERENCE.md).