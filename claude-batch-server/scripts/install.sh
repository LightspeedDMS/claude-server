#!/bin/bash

set -euo pipefail

# Claude Batch Server Installation Script
# Supports Rocky Linux 9.x and Ubuntu 22.04+

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_DIR="$(dirname "$SCRIPT_DIR")"
LOG_FILE="/tmp/claude-batch-install.log"

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Logging functions
log() {
    echo -e "${GREEN}[INFO]${NC} $1" | tee -a "$LOG_FILE"
}

warn() {
    echo -e "${YELLOW}[WARN]${NC} $1" | tee -a "$LOG_FILE"
}

error() {
    echo -e "${RED}[ERROR]${NC} $1" | tee -a "$LOG_FILE"
}

debug() {
    echo -e "${BLUE}[DEBUG]${NC} $1" | tee -a "$LOG_FILE"
}

# Check if running as root
check_root() {
    if [[ $EUID -ne 0 ]]; then
        error "This script must be run as root"
        exit 1
    fi
}

# Detect OS distribution
detect_os() {
    if [[ -f /etc/os-release ]]; then
        source /etc/os-release
        OS_ID="$ID"
        OS_VERSION="$VERSION_ID"
        log "Detected OS: $PRETTY_NAME"
    else
        error "Cannot detect OS distribution"
        exit 1
    fi
}

# Install .NET Core SDK
install_dotnet() {
    log "Installing .NET Core SDK 8.0..."
    
    case "$OS_ID" in
        "rocky"|"rhel"|"centos")
            # Add Microsoft repository
            rpm --import https://packages.microsoft.com/keys/microsoft.asc
            cat > /etc/yum.repos.d/microsoft-prod.repo << 'EOF'
[packages-microsoft-com-prod]
name=packages-microsoft-com-prod
baseurl=https://packages.microsoft.com/rhel/9/prod/
enabled=1
gpgcheck=1
gpgkey=https://packages.microsoft.com/keys/microsoft.asc
EOF
            dnf update -y
            dnf install -y dotnet-sdk-8.0
            ;;
        "ubuntu")
            # Install .NET using Microsoft's installation script
            curl -sSL https://dot.net/v1/dotnet-install.sh | bash /dev/stdin --channel 8.0
            
            # Add to system PATH
            echo 'export PATH="$HOME/.dotnet:$PATH"' >> /etc/environment
            echo 'export DOTNET_ROOT=$HOME/.dotnet' >> /etc/environment
            
            # Create symlink for system-wide access
            ln -sf "$HOME/.dotnet/dotnet" /usr/local/bin/dotnet
            ;;
        *)
            error "Unsupported OS for .NET installation: $OS_ID"
            exit 1
            ;;
    esac
    
    # Verify installation
    if command -v dotnet >/dev/null 2>&1; then
        log ".NET Core SDK installed successfully: $(dotnet --version)"
    else
        error "Failed to install .NET Core SDK"
        exit 1
    fi
}

# Install Docker and Docker Compose
install_docker() {
    log "Installing Docker and Docker Compose..."
    
    case "$OS_ID" in
        "rocky"|"rhel"|"centos")
            # Install Docker CE
            dnf config-manager --add-repo=https://download.docker.com/linux/centos/docker-ce.repo
            dnf install -y docker-ce docker-ce-cli containerd.io docker-compose-plugin
            
            # Start and enable Docker
            systemctl start docker
            systemctl enable docker
            ;;
        "ubuntu")
            # Update package index
            apt-get update
            
            # Install prerequisites
            apt-get install -y ca-certificates curl gnupg lsb-release
            
            # Add Docker's official GPG key
            mkdir -p /etc/apt/keyrings
            curl -fsSL https://download.docker.com/linux/ubuntu/gpg | gpg --dearmor -o /etc/apt/keyrings/docker.gpg
            
            # Set up repository
            echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.gpg] https://download.docker.com/linux/ubuntu $(lsb_release -cs) stable" | tee /etc/apt/sources.list.d/docker.list > /dev/null
            
            # Install Docker Engine
            apt-get update
            apt-get install -y docker-ce docker-ce-cli containerd.io docker-compose-plugin
            
            # Start and enable Docker
            systemctl start docker
            systemctl enable docker
            ;;
        *)
            error "Unsupported OS for Docker installation: $OS_ID"
            exit 1
            ;;
    esac
    
    # Verify installation
    if command -v docker >/dev/null 2>&1; then
        log "Docker installed successfully: $(docker --version)"
    else
        error "Failed to install Docker"
        exit 1
    fi
    
    if docker compose version >/dev/null 2>&1; then
        log "Docker Compose installed successfully: $(docker compose version)"
    else
        error "Failed to install Docker Compose"
        exit 1
    fi
}

# Install Claude Code CLI
install_claude_cli() {
    log "Installing Claude Code CLI..."
    
    # Install using official installer
    curl -fsSL https://claude.ai/install.sh | bash
    
    # Add to system PATH
    if [[ -f "$HOME/.local/bin/claude" ]]; then
        ln -sf "$HOME/.local/bin/claude" /usr/local/bin/claude
        log "Claude Code CLI installed successfully"
    else
        warn "Claude Code CLI installation may have failed, but continuing..."
    fi
}

# Configure Copy-on-Write support
configure_cow() {
    log "Configuring Copy-on-Write filesystem support..."
    
    # Create workspace directories
    mkdir -p /workspace/repos /workspace/jobs
    
    # Detect filesystem type
    FS_TYPE=$(df -T /workspace | tail -1 | awk '{print $2}')
    log "Detected filesystem type: $FS_TYPE"
    
    case "$FS_TYPE" in
        "xfs")
            log "XFS filesystem detected - reflink support should be available"
            # Test reflink support
            if test_cow_support; then
                log "XFS reflink support confirmed"
            else
                warn "XFS reflink support not available"
            fi
            ;;
        "ext4")
            log "ext4 filesystem detected - checking reflink support"
            if test_cow_support; then
                log "ext4 reflink support confirmed"
            else
                warn "ext4 reflink support not available, will use fallback"
            fi
            ;;
        "btrfs")
            log "Btrfs filesystem detected - full CoW support available"
            # Install btrfs-progs if not already installed
            case "$OS_ID" in
                "rocky"|"rhel"|"centos")
                    dnf install -y btrfs-progs
                    ;;
                "ubuntu")
                    apt-get install -y btrfs-progs
                    ;;
            esac
            ;;
        *)
            warn "Unknown filesystem type: $FS_TYPE. Will use hardlink fallback."
            ;;
    esac
    
    # Install rsync for fallback support
    case "$OS_ID" in
        "rocky"|"rhel"|"centos")
            dnf install -y rsync
            ;;
        "ubuntu")
            apt-get install -y rsync
            ;;
    esac
    
    # Set proper permissions
    chmod 755 /workspace
    chmod 755 /workspace/repos /workspace/jobs
}

# Test Copy-on-Write support
test_cow_support() {
    local test_dir="/tmp/cow-test-$$"
    mkdir -p "$test_dir"
    
    echo "test content" > "$test_dir/source.txt"
    
    if cp --reflink=always "$test_dir/source.txt" "$test_dir/target.txt" 2>/dev/null; then
        rm -rf "$test_dir"
        return 0
    else
        rm -rf "$test_dir"
        return 1
    fi
}

# Build and deploy the application
build_and_deploy() {
    log "Building and deploying Claude Batch Server..."
    
    cd "$PROJECT_DIR"
    
    # Build the application
    export PATH="$HOME/.dotnet:$PATH"
    dotnet restore
    dotnet build -c Release
    
    # Create systemd service
    create_systemd_service
    
    # Create workspace directories
    mkdir -p /workspace/repos /workspace/jobs /var/log/claude-batch-server
    
    # Set proper permissions
    chown -R root:root /workspace
    chmod 755 /workspace /workspace/repos /workspace/jobs
    chmod 755 /var/log/claude-batch-server
    
    log "Application built and deployed successfully"
}

# Create systemd service
create_systemd_service() {
    log "Creating systemd service..."
    
    cat > /etc/systemd/system/claude-batch-server.service << EOF
[Unit]
Description=Claude Batch Server
After=network.target
Wants=network.target

[Service]
Type=notify
User=root
Group=root
WorkingDirectory=$PROJECT_DIR/src/ClaudeBatchServer.Api
ExecStart=/usr/local/bin/dotnet ClaudeBatchServer.Api.dll
Restart=always
RestartSec=10
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=ASPNETCORE_URLS=http://0.0.0.0:5000
Environment=DOTNET_ROOT=/root/.dotnet
Environment=PATH=/root/.dotnet:/usr/local/bin:/usr/bin:/bin

[Install]
WantedBy=multi-user.target
EOF

    systemctl daemon-reload
    systemctl enable claude-batch-server
    
    log "Systemd service created and enabled"
}

# Setup logging
setup_logging() {
    log "Setting up logging..."
    
    # Create log directories
    mkdir -p /var/log/claude-batch-server
    
    # Create logrotate configuration
    cat > /etc/logrotate.d/claude-batch-server << EOF
/var/log/claude-batch-server/*.log {
    daily
    rotate 7
    compress
    delaycompress
    missingok
    notifempty
    create 644 root root
}
EOF

    log "Logging configured"
}

# Validate installation
validate_installation() {
    log "Validating installation..."
    
    local errors=0
    
    # Check .NET
    if ! command -v dotnet >/dev/null 2>&1; then
        error ".NET Core SDK not found"
        ((errors++))
    fi
    
    # Check Docker
    if ! command -v docker >/dev/null 2>&1; then
        error "Docker not found"
        ((errors++))
    fi
    
    # Check Claude CLI (optional)
    if ! command -v claude >/dev/null 2>&1; then
        warn "Claude Code CLI not found (optional)"
    fi
    
    # Check workspace directories
    if [[ ! -d /workspace/repos ]]; then
        error "Workspace repositories directory not found"
        ((errors++))
    fi
    
    if [[ ! -d /workspace/jobs ]]; then
        error "Workspace jobs directory not found"
        ((errors++))
    fi
    
    # Test CoW support
    if test_cow_support; then
        log "Copy-on-Write support: Available"
    else
        warn "Copy-on-Write support: Not available (will use fallback)"
    fi
    
    if [[ $errors -eq 0 ]]; then
        log "Installation validation completed successfully"
        return 0
    else
        error "Installation validation failed with $errors errors"
        return 1
    fi
}

# Print usage instructions
print_usage() {
    cat << EOF

${GREEN}Claude Batch Server Installation Complete!${NC}

${YELLOW}Next Steps:${NC}

1. Copy environment configuration:
   ${BLUE}cp $PROJECT_DIR/docker/.env.example /etc/claude-batch-server.env${NC}
   
2. Edit configuration:
   ${BLUE}nano /etc/claude-batch-server.env${NC}
   
3. Start the service:
   ${BLUE}systemctl start claude-batch-server${NC}
   
4. Check service status:
   ${BLUE}systemctl status claude-batch-server${NC}
   
5. View logs:
   ${BLUE}journalctl -u claude-batch-server -f${NC}

${YELLOW}Or run with Docker:${NC}

1. Navigate to project directory:
   ${BLUE}cd $PROJECT_DIR${NC}
   
2. Copy and configure environment:
   ${BLUE}cp docker/.env.example docker/.env${NC}
   ${BLUE}nano docker/.env${NC}
   
3. Start with Docker Compose:
   ${BLUE}docker compose -f docker/docker-compose.yml up -d${NC}

${YELLOW}API will be available at:${NC}
- HTTP: http://localhost:5000 (systemd) or http://localhost:8080 (docker)
- HTTPS: https://localhost:8443 (docker with nginx)

${YELLOW}Documentation:${NC}
- Swagger UI: http://localhost:5000/swagger (when running)
- Logs: /var/log/claude-batch-server/ or docker logs

EOF
}

# Main installation function
main() {
    log "Starting Claude Batch Server installation..."
    log "Log file: $LOG_FILE"
    
    # Check prerequisites
    check_root
    detect_os
    
    # Install components
    install_dotnet
    install_docker
    install_claude_cli
    configure_cow
    setup_logging
    build_and_deploy
    
    # Validate and finish
    if validate_installation; then
        log "Installation completed successfully!"
        print_usage
    else
        error "Installation completed with errors. Check $LOG_FILE for details."
        exit 1
    fi
}

# Check if script is being sourced or executed
if [[ "${BASH_SOURCE[0]}" == "${0}" ]]; then
    main "$@"
fi