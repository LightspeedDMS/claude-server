# Claude Batch Server Installation Script

This directory contains the installation script for the Claude Batch Server, which automates the complete setup process on supported Linux distributions.

## Overview

The `install.sh` script performs a comprehensive installation of the Claude Batch Server and all its dependencies. It's designed to work on production servers and includes enterprise-grade features like Copy-on-Write filesystem optimization, systemd service management, and comprehensive logging.

## What the Installation Script Does

### 1. System Prerequisites Check
- **Root Access Verification**: Ensures the script runs with root privileges for system-wide installation
- **OS Detection**: Automatically detects the operating system (Rocky Linux, RHEL, CentOS, or Ubuntu)
- **Compatibility Verification**: Confirms the OS version is supported

### 2. Core Dependencies Installation

#### .NET Core SDK 8.0
- **Rocky/RHEL/CentOS**: Adds Microsoft repository and installs via `dnf`
- **Ubuntu**: Uses Microsoft's official installation script
- **System Integration**: Creates system-wide dotnet command and environment variables
- **Verification**: Tests installation with version check

#### Docker and Docker Compose
- **Container Platform**: Installs Docker CE (Community Edition) with latest compose plugin
- **Service Configuration**: Enables Docker service for automatic startup
- **Repository Setup**: Adds official Docker repositories for each OS
- **Verification**: Tests both `docker` and `docker compose` commands

#### Claude Code CLI
- **Official Installation**: Uses Claude AI's official installer script
- **System Integration**: Creates system-wide symlink in `/usr/local/bin/`
- **Optional Component**: Continues installation even if Claude CLI fails (marked as optional)

#### Python and pipx
- **Package Manager**: Installs pipx for isolated Python application management
- **OS-Specific Installation**:
  - Rocky/RHEL/CentOS: Installs via `dnf` and pip
  - Ubuntu: Uses native `pipx` package
- **Path Configuration**: Ensures pipx is available system-wide

#### Code Indexer (cidx)
- **Semantic Search Tool**: Installs from GitHub repository using pipx
- **Force Update**: Uses `--force` flag to update existing installations
- **System Integration**: Creates system-wide symlink for global access

### 3. Copy-on-Write (CoW) Filesystem Optimization

#### Filesystem Detection and Configuration
- **Automatic Detection**: Identifies the underlying filesystem type (`xfs`, `ext4`, `btrfs`)
- **Reflink Testing**: Tests actual CoW support using `cp --reflink=always`

#### Filesystem-Specific Optimizations
- **XFS**: Configures reflink support for efficient file copying
- **ext4**: Checks for reflink feature availability (newer ext4 versions)
- **Btrfs**: Installs `btrfs-progs` tools for full CoW management
- **Fallback Support**: Installs `rsync` for filesystems without CoW support

#### Workspace Setup
- **Directory Creation**: Creates `/workspace/repos` and `/workspace/jobs` directories
- **Permission Management**: Sets appropriate permissions (755) for workspace access
- **Storage Optimization**: Enables efficient repository cloning and job workspace management

### 4. Application Build and Deployment

#### .NET Application Build
- **Source Compilation**: Builds the application in Release configuration
- **Dependency Resolution**: Restores all NuGet packages
- **Build Verification**: Ensures successful compilation

#### Systemd Service Creation
- **Service Definition**: Creates `/etc/systemd/system/claude-batch-server.service`
- **Auto-Start Configuration**: Enables service to start automatically on boot
- **Production Settings**: Configures ASP.NET Core for production environment
- **Network Binding**: Binds to all network interfaces on port 5000
- **Restart Policy**: Configures automatic restart on failures with 10-second delay

#### Directory and Permission Setup
- **Workspace Directories**: Creates production workspace structure
- **Log Directories**: Sets up `/var/log/claude-batch-server/` for application logs
- **Ownership**: Configures proper ownership and permissions for security

### 5. Logging and Monitoring Configuration

#### Log Management
- **Structured Logging**: Sets up application log directories
- **Logrotate Configuration**: Automatically rotates logs daily, keeps 7 days of history
- **Compression**: Compresses old logs to save disk space
- **Permission Management**: Ensures proper log file permissions

#### Installation Logging
- **Installation Log**: Creates `/tmp/claude-batch-install.log` with complete installation history
- **Colored Output**: Provides clear, colored console output for different message types
- **Error Tracking**: Logs all errors and warnings for troubleshooting

### 6. Installation Validation

#### Component Verification
- **Dependency Checks**: Verifies all installed components are accessible
- **Service Validation**: Confirms systemd service is properly configured
- **Workspace Verification**: Checks workspace directories exist and are accessible
- **CoW Testing**: Re-tests Copy-on-Write support after configuration

#### Performance Testing
- **Filesystem Performance**: Tests reflink/CoW functionality
- **Version Reporting**: Reports versions of all installed components
- **Error Reporting**: Provides detailed error counts and descriptions

### 7. Post-Installation Guidance

#### Service Management Instructions
- **Manual Start**: Commands to start/stop/status the service
- **Log Monitoring**: Instructions for viewing real-time and historical logs
- **Configuration**: Guidance for environment file setup and editing

#### Docker Alternative
- **Docker Compose Setup**: Instructions for running with Docker instead of systemd
- **Environment Configuration**: Docker-specific environment setup
- **HTTPS Configuration**: Instructions for nginx reverse proxy with SSL

#### Access Information
- **API Endpoints**: URLs for HTTP and HTTPS access
- **Swagger Documentation**: Link to interactive API documentation
- **Tool Usage**: Instructions for using installed tools (Claude CLI, cidx)

## Usage

### Basic Installation
```bash
sudo ./install.sh
```

### Prerequisites
- **Root Access**: Must be run as root or with sudo
- **Supported OS**: Rocky Linux 9.x, RHEL 9.x, CentOS 9.x, or Ubuntu 22.04+
- **Internet Access**: Required for downloading packages and dependencies
- **Disk Space**: Minimum 2GB free space for all components

### Installation Process
1. **Preparation**: The script checks system compatibility and logs all actions
2. **Dependencies**: Installs all required software packages and tools
3. **Optimization**: Configures filesystem optimizations and workspace
4. **Deployment**: Builds and configures the application service
5. **Validation**: Verifies all components are working correctly
6. **Documentation**: Provides next steps and usage instructions

## Post-Installation

### Starting the Service
```bash
# Using systemd (recommended)
sudo systemctl start claude-batch-server
sudo systemctl status claude-batch-server

# Or using Docker
cd /path/to/project
sudo docker compose -f docker/docker-compose.yml up -d
```

### Configuration
1. **Environment Setup**: Copy and edit `/etc/claude-batch-server.env` for systemd deployment
2. **Docker Environment**: Copy and edit `docker/.env` for Docker deployment
3. **API Keys**: Configure Claude AI API credentials in environment file
4. **Network Settings**: Adjust port bindings if needed (default: 5000 for systemd, 8080 for Docker)

### Monitoring
- **Service Status**: `sudo systemctl status claude-batch-server`
- **Real-time Logs**: `sudo journalctl -u claude-batch-server -f`
- **Application Logs**: Check `/var/log/claude-batch-server/`
- **Docker Logs**: `sudo docker logs claude-batch-server`

### API Access
- **HTTP**: http://localhost:5000 (systemd) or http://localhost:8080 (Docker)
- **HTTPS**: https://localhost:8443 (Docker with nginx)
- **Documentation**: http://localhost:5000/swagger (interactive API documentation)

## Troubleshooting

### Installation Issues
- **Check Logs**: Review `/tmp/claude-batch-install.log` for detailed error information
- **OS Support**: Ensure you're using a supported operating system version
- **Network Access**: Verify internet connectivity for package downloads
- **Disk Space**: Ensure sufficient disk space for all components

### Service Issues
- **Service Status**: Use `systemctl status claude-batch-server` to check service health
- **Port Conflicts**: Check if port 5000 is already in use by another service
- **Permissions**: Verify workspace directory permissions are correct
- **Dependencies**: Ensure all required services (especially .NET runtime) are available

### Performance Optimization
- **CoW Support**: Check filesystem type and reflink support for optimal performance
- **Memory**: Monitor memory usage for large repository operations
- **Storage**: Consider using XFS or Btrfs filesystems for better CoW performance
- **Network**: Ensure good network connectivity for Claude AI API calls

## Advanced Configuration

### Filesystem Optimization
- **XFS**: Use `mkfs.xfs -m reflink=1` when creating new filesystems
- **Btrfs**: Natural CoW support, consider using subvolumes for workspace isolation
- **ext4**: Requires kernel 4.2+ and `tune2fs -O reflink` for reflink support

### Security Considerations
- **API Keys**: Never commit API keys to version control
- **Network Access**: Consider firewall rules for production deployments
- **User Permissions**: The service runs as root - consider creating dedicated user for production
- **HTTPS**: Use the Docker nginx configuration for SSL/TLS in production

### Scaling Considerations
- **Workspace Storage**: Monitor `/workspace` disk usage for large repository operations
- **Concurrent Jobs**: Configure appropriate limits based on system resources
- **Log Rotation**: Adjust log retention based on disk space and compliance requirements

This installation script provides a production-ready deployment of the Claude Batch Server with enterprise-grade features and optimizations.