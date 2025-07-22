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

### 1. Environment Setup
```bash
# Copy environment template
cp docker/.env.example docker/.env

# Edit configuration
nano docker/.env
```

### 2. Key Configuration Options

**Required Settings:**
```bash
# JWT secret (32+ characters)
JWT_KEY="YourSuperSecretJwtKeyThatShouldBe32CharactersOrLonger!"

# Workspace paths
WORKSPACE_REPOSITORIES_PATH="/workspace/repos"
WORKSPACE_JOBS_PATH="/workspace/jobs"

# Job limits  
MAX_CONCURRENT_JOBS=5
JOB_TIMEOUT_HOURS=24

# Claude configuration
CLAUDE_COMMAND="claude --dangerously-skip-permissions"
```

**Optional Settings:**
```bash
# Custom ports
HTTP_PORT=8080
HTTPS_PORT=8443

# Logging
LOG_LEVEL="Information"
LOG_PATH="/var/log/claude-batch-server"

# System prompts
CIDX_AVAILABLE_TEMPLATE_PATH="SystemPrompts/cidx-system-prompt-template.txt"
CIDX_UNAVAILABLE_TEMPLATE_PATH="SystemPrompts/cidx-unavailable-system-prompt-template.txt"
```

### 3. User Authentication Setup

**Create test user:**
```bash
# Add system user for testing
sudo useradd -m -s /bin/bash testuser

# Set password
sudo passwd testuser

# Generate password hash for API authentication
python3 -c "import crypt; print(crypt.crypt('your_password', crypt.mksalt(crypt.METHOD_SHA512)))"
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
```bash
sudo tee /etc/systemd/system/claude-batch-server.service << EOF
[Unit]
Description=Claude Batch Server
After=network.target

[Service]
Type=simple
User=root
WorkingDirectory=/opt/claude-batch-server
ExecStart=/usr/bin/dotnet ClaudeBatchServer.Api.dll
Restart=always
RestartSec=10
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=ASPNETCORE_URLS=http://localhost:5000

[Install]
WantedBy=multi-user.target
EOF
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
# Create workspace directories
sudo mkdir -p /workspace/{repos,jobs}
sudo chown -R root:root /workspace
sudo chmod 755 /workspace /workspace/repos /workspace/jobs
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

For detailed troubleshooting, see [README.md](README.md) and [API-REFERENCE.md](API-REFERENCE.md).