#!/bin/bash

set -euo pipefail

# Claude Batch Server Installation Script
# Supports Rocky Linux 9.x and Ubuntu 22.04+
# Production-ready installation with nginx, SSL, firewall, and monitoring

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_DIR="$(dirname "$SCRIPT_DIR")"
LOG_FILE="/tmp/claude-batch-install-$(date +%Y%m%d-%H%M%S).log"
BACKUP_BASE_DIR="/var/backups/claude-batch-server"
BACKUP_DIR=""

# Installation modes
PRODUCTION_MODE=false
DEVELOPMENT_MODE=false
DRY_RUN_MODE=false

# Colors for output - simplified detection that works with SSH
if [[ -t 1 ]] && [[ "${TERM:-}" != "dumb" ]] && [[ "${NO_COLOR:-}" != "1" ]]; then
    RED='\033[0;31m'
    GREEN='\033[0;32m'
    YELLOW='\033[1;33m'
    BLUE='\033[0;34m'
    PURPLE='\033[0;35m'
    CYAN='\033[0;36m'
    NC='\033[0m' # No Color
else
    # Disable colors for non-interactive terminals or when NO_COLOR is set
    RED=''
    GREEN=''
    YELLOW=''
    BLUE=''
    PURPLE=''
    CYAN=''
    NC=''
fi

# SSL Configuration (will be set interactively or via parameters)
SSL_COUNTRY=""
SSL_STATE=""
SSL_CITY=""
SSL_ORG=""
SSL_OU=""
SSL_CN=""

# Logging functions
log() {
    if [[ "$DRY_RUN_MODE" == "true" ]]; then
        echo -e "$(echo -e "${GREEN}[INFO]${NC}") $1"
    else
        echo -e "$(echo -e "${GREEN}[INFO]${NC}") $1" | tee -a "$LOG_FILE" 2>/dev/null || echo -e "$(echo -e "${GREEN}[INFO]${NC}") $1"
    fi
}

warn() {
    if [[ "$DRY_RUN_MODE" == "true" ]]; then
        echo -e "$(echo -e "${YELLOW}[WARN]${NC}") $1"
    else
        echo -e "$(echo -e "${YELLOW}[WARN]${NC}") $1" | tee -a "$LOG_FILE" 2>/dev/null || echo -e "$(echo -e "${YELLOW}[WARN]${NC}") $1"
    fi
}

error() {
    if [[ "$DRY_RUN_MODE" == "true" ]]; then
        echo -e "${RED}[ERROR]${NC} $1"
    else
        echo -e "${RED}[ERROR]${NC} $1" | tee -a "$LOG_FILE" 2>/dev/null || echo -e "${RED}[ERROR]${NC} $1"
    fi
}

debug() {
    if [[ "$DRY_RUN_MODE" == "true" ]]; then
        echo -e "$(echo -e "${BLUE}[DEBUG]${NC}") $1"
    else
        echo -e "$(echo -e "${BLUE}[DEBUG]${NC}") $1" | tee -a "$LOG_FILE" 2>/dev/null || echo -e "$(echo -e "${BLUE}[DEBUG]${NC}") $1"
    fi
}

# Dry-run specific logging functions
dry_run_action() {
    echo -e "$(echo -e "${YELLOW}[DRY-RUN]${NC}") Would execute: $1"
}

dry_run_check() {
    echo -e "$(echo -e "${BLUE}[CHECK]${NC}") $1"
}

dry_run_result() {
    echo -e "$(echo -e "${GREEN}[RESULT]${NC}") $1"
}

# Comprehensive dry-run analysis
perform_dry_run() {
    echo -e "$(echo -e "${GREEN}=== Claude Batch Server Installation Dry-Run Analysis ===${NC}")"
    echo -e "$(echo -e "${YELLOW}Mode:${NC}") $([ "$PRODUCTION_MODE" == "true" ] && echo "Production" || echo "Development")"
    echo -e "$(echo -e "${YELLOW}OS:${NC}") $OS_ID $VERSION_ID"
    echo ""
    
    # System analysis
    dry_run_analyze_system
    dry_run_analyze_dependencies
    dry_run_analyze_docker
    dry_run_analyze_dotnet
    dry_run_analyze_workspace
    dry_run_analyze_services
    
    if [[ "$PRODUCTION_MODE" == "true" ]]; then
        dry_run_analyze_production
    fi
    
    dry_run_analyze_cli_tool
    
    echo -e "\n$(echo -e "${GREEN}=== Summary ===${NC}")"
    echo "The installation would perform the above actions based on the current system state."
    echo "Run without --dry-run to execute the installation."
}

# Analyze system requirements
dry_run_analyze_system() {
    echo -e "$(echo -e "${BLUE}=== System Analysis ===${NC}")"
    
    dry_run_check "Checking sudo privileges"
    if [[ $EUID -eq 0 ]]; then
        dry_run_result "✓ Running as root"
    else
        dry_run_result "✗ Not running as root"
        dry_run_action "Request sudo privileges and re-execute as root"
    fi
    
    dry_run_check "Checking OS compatibility"
    case "$OS_ID" in
        "rocky"|"rhel"|"centos"|"ubuntu")
            dry_run_result "✓ Supported OS: $OS_ID $VERSION_ID"
            ;;
        *)
            dry_run_result "✗ Unsupported OS: $OS_ID"
            dry_run_action "Exit with error - unsupported operating system"
            ;;
    esac
    
    dry_run_check "Checking backup directory"
    if [[ -d "$BACKUP_BASE_DIR" ]]; then
        dry_run_result "✓ Backup directory exists: $BACKUP_BASE_DIR"
    else
        dry_run_result "✗ Backup directory missing"
        dry_run_action "Create backup directory: sudo mkdir -p $BACKUP_BASE_DIR"
    fi
    echo ""
}

# Analyze package dependencies
dry_run_analyze_dependencies() {
    echo -e "$(echo -e "${BLUE}=== Dependencies Analysis ===${NC}")"
    
    local packages=()
    case "$OS_ID" in
        "rocky"|"rhel"|"centos")
            packages=("curl" "wget" "git" "unzip" "tar" "openssl" "python3" "python3-pip" "python3-venv")
            ;;
        "ubuntu")
            packages=("curl" "wget" "git" "unzip" "tar" "openssl" "python3" "python3-pip" "python3-venv" "software-properties-common" "apt-transport-https" "ca-certificates" "gnupg" "lsb-release")
            ;;
    esac
    
    for package in "${packages[@]}"; do
        dry_run_check "Checking package: $package"
        if command -v "$package" &>/dev/null || dpkg -l "$package" &>/dev/null || rpm -q "$package" &>/dev/null; then
            dry_run_result "✓ Already installed: $package"
        else
            dry_run_result "✗ Missing package: $package"
            case "$OS_ID" in
                "rocky"|"rhel"|"centos")
                    dry_run_action "Install package: sudo dnf install -y $package"
                    ;;
                "ubuntu")
                    dry_run_action "Install package: sudo apt-get install -y $package"
                    ;;
            esac
        fi
    done
    
    # Check Claude CLI
    dry_run_check "Checking Claude Code CLI"
    if command -v claude &>/dev/null; then
        dry_run_result "✓ Claude Code CLI already installed"
    else
        dry_run_result "✗ Claude Code CLI not found"
        dry_run_action "Install Claude CLI: curl -fsSL https://claude.ai/install.sh | bash"
        dry_run_action "Create system-wide symlink: ln -sf ~/.local/bin/claude /usr/local/bin/claude"
    fi
    
    # Check pipx
    dry_run_check "Checking pipx"
    if command -v pipx &>/dev/null; then
        dry_run_result "✓ pipx already installed"
    else
        dry_run_result "✗ pipx not found"
        dry_run_action "Install pipx: python3 -m pip install --user pipx"
        dry_run_action "Add pipx to PATH: python3 -m pipx ensurepath"
    fi
    
    # Check code indexer
    dry_run_check "Checking cidx (code indexer)"
    if command -v cidx &>/dev/null; then
        dry_run_result "✓ cidx already installed"
    else
        dry_run_result "✗ cidx not found"
        dry_run_action "Install cidx: pipx install cidx"
    fi
    echo ""
}

# Analyze Docker installation
dry_run_analyze_docker() {
    echo -e "$(echo -e "${BLUE}=== Docker Analysis ===${NC}")"
    
    dry_run_check "Checking Docker Engine"
    if command -v docker &>/dev/null; then
        dry_run_result "✓ Docker already installed"
        local docker_version=$(docker --version 2>/dev/null || echo "unknown")
        dry_run_result "  Version: $docker_version"
    else
        dry_run_result "✗ Docker not found"
        case "$OS_ID" in
            "rocky"|"rhel"|"centos")
                dry_run_action "Add Docker repository: sudo dnf config-manager --add-repo https://download.docker.com/linux/centos/docker-ce.repo"
                dry_run_action "Install Docker: sudo dnf install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin"
                ;;
            "ubuntu")
                dry_run_action "Add Docker GPG key and repository"
                dry_run_action "Install Docker: sudo apt-get install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin"
                ;;
        esac
        dry_run_action "Start Docker service: sudo systemctl start docker && sudo systemctl enable docker"
    fi
    
    dry_run_check "Checking Docker Compose"
    if command -v docker-compose &>/dev/null || docker compose version &>/dev/null; then
        dry_run_result "✓ Docker Compose available"
    else
        dry_run_result "✗ Docker Compose not found"
        dry_run_action "Install Docker Compose plugin (included with Docker installation above)"
    fi
    
    dry_run_check "Checking Docker service status"
    if systemctl is-active --quiet docker 2>/dev/null; then
        dry_run_result "✓ Docker service is running"
    else
        dry_run_result "✗ Docker service not running"
        dry_run_action "Start Docker service: sudo systemctl start docker"
        dry_run_action "Enable Docker service: sudo systemctl enable docker"
    fi
    echo ""
}

# Analyze .NET installation
dry_run_analyze_dotnet() {
    echo -e "$(echo -e "${BLUE}=== .NET SDK Analysis ===${NC}")"
    
    dry_run_check "Checking .NET SDK"
    if command -v dotnet &>/dev/null; then
        local dotnet_version=$(dotnet --version 2>/dev/null || echo "unknown")
        dry_run_result "✓ .NET SDK already installed: $dotnet_version"
        
        # Check if it's .NET 8
        if dotnet --list-sdks | grep -q "8\."; then
            dry_run_result "✓ .NET 8 SDK available"
        else
            dry_run_result "⚠ .NET 8 SDK not found"
            dry_run_action "Install .NET 8 SDK"
        fi
    else
        dry_run_result "✗ .NET SDK not found"
        case "$OS_ID" in
            "rocky"|"rhel"|"centos")
                dry_run_action "Add Microsoft repository: rpm --import https://packages.microsoft.com/keys/microsoft.asc"
                dry_run_action "Install .NET 8 SDK: sudo dnf install -y dotnet-sdk-8.0"
                ;;
            "ubuntu")
                dry_run_action "Add Microsoft repository and install .NET 8 SDK"
                ;;
        esac
    fi
    
    dry_run_check "Checking .NET PATH configuration"
    if [[ ":$PATH:" == *":$HOME/.dotnet:"* ]]; then
        dry_run_result "✓ .NET already in PATH"
    else
        dry_run_result "✗ .NET not in PATH"
        dry_run_action "Add .NET to PATH in ~/.bashrc"
    fi
    echo ""
}

# Analyze workspace and application
dry_run_analyze_workspace() {
    echo -e "$(echo -e "${BLUE}=== Workspace & Application Analysis ===${NC}")"
    
    dry_run_check "Checking workspace directories"
    for dir in "/workspace" "/workspace/repos" "/workspace/jobs" "/var/log/claude-batch-server"; do
        if [[ -d "$dir" ]]; then
            dry_run_result "✓ Directory exists: $dir"
        else
            dry_run_result "✗ Directory missing: $dir"
            dry_run_action "Create directory: sudo mkdir -p $dir"
            dry_run_action "Set permissions: sudo chmod 755 $dir"
        fi
    done
    
    dry_run_check "Checking project build requirements"
    if [[ -f "$PROJECT_DIR/src/ClaudeBatchServer.Api/ClaudeBatchServer.Api.csproj" ]]; then
        dry_run_result "✓ API project file found"
        dry_run_action "Restore NuGet packages: dotnet restore"
        dry_run_action "Build application: dotnet build -c Release"
    else
        dry_run_result "✗ API project file not found"
        dry_run_action "Exit with error - project files missing"
    fi
    
    dry_run_check "Checking systemd service"
    if systemctl list-unit-files claude-batch-server.service &>/dev/null; then
        dry_run_result "✓ systemd service already exists"
        if systemctl is-active --quiet claude-batch-server; then
            dry_run_result "✓ Service is running"
        else
            dry_run_result "⚠ Service exists but not running"
            dry_run_action "Start service: sudo systemctl start claude-batch-server"
        fi
    else
        dry_run_result "✗ systemd service not found"
        dry_run_action "Create systemd service file: /etc/systemd/system/claude-batch-server.service"
        dry_run_action "Enable service: sudo systemctl enable claude-batch-server"
        dry_run_action "Start service: sudo systemctl start claude-batch-server"
    fi
    echo ""
}

# Analyze service dependencies
dry_run_analyze_services() {
    echo -e "$(echo -e "${BLUE}=== Service Dependencies Analysis ===${NC}")"
    
    dry_run_check "Checking Copy-on-Write filesystem support"
    if [[ -f "/proc/filesystems" ]] && grep -q "btrfs\|zfs\|xfs" /proc/filesystems; then
        dry_run_result "✓ CoW-capable filesystem detected"
    else
        dry_run_result "⚠ Limited CoW support - will use fallback methods"
    fi
    
    dry_run_check "Checking logging configuration"
    if [[ -d "/var/log/claude-batch-server" ]]; then
        dry_run_result "✓ Log directory exists"
    else
        dry_run_result "✗ Log directory missing"
        dry_run_action "Create log directory: sudo mkdir -p /var/log/claude-batch-server"
    fi
    echo ""
}

# Analyze production-specific components
dry_run_analyze_production() {
    echo -e "$(echo -e "${BLUE}=== Production Components Analysis ===${NC}")"
    
    dry_run_check "Checking nginx"
    if command -v nginx &>/dev/null; then
        dry_run_result "✓ nginx already installed"
        local nginx_version=$(nginx -v 2>&1 | cut -d' ' -f3 || echo "unknown")
        dry_run_result "  Version: $nginx_version"
    else
        dry_run_result "✗ nginx not found"
        case "$OS_ID" in
            "rocky"|"rhel"|"centos")
                dry_run_action "Install nginx: sudo dnf install -y nginx"
                ;;
            "ubuntu")
                dry_run_action "Install nginx: sudo apt-get install -y nginx"
                ;;
        esac
        dry_run_action "Start nginx: sudo systemctl start nginx && sudo systemctl enable nginx"
    fi
    
    dry_run_check "Checking SSL certificates"
    local ssl_dir="/etc/ssl/claude-batch-server"
    if [[ -f "$ssl_dir/server.crt" && -f "$ssl_dir/server.key" ]]; then
        dry_run_result "✓ SSL certificates exist"
        # Check if certificates are valid and not expired
        if openssl x509 -in "$ssl_dir/server.crt" -noout -checkend 86400 &>/dev/null; then
            dry_run_result "✓ SSL certificates are valid"
        else
            dry_run_result "⚠ SSL certificates may be expired"
            dry_run_action "Regenerate SSL certificates"
        fi
    else
        dry_run_result "✗ SSL certificates missing"
        dry_run_action "Create SSL directory: sudo mkdir -p $ssl_dir"
        if [[ -z "$SSL_CN" ]]; then
            dry_run_action "Prompt for SSL certificate information interactively"
        fi
        dry_run_action "Generate self-signed SSL certificate"
    fi
    
    dry_run_check "Checking nginx configuration"
    local nginx_conf="/etc/nginx/sites-available/claude-batch-server"
    if [[ -f "$nginx_conf" ]]; then
        dry_run_result "✓ nginx configuration exists"
    else
        dry_run_result "✗ nginx configuration missing"
        dry_run_action "Create nginx configuration: $nginx_conf"
        dry_run_action "Enable site: ln -s $nginx_conf /etc/nginx/sites-enabled/"
        dry_run_action "Test nginx configuration: nginx -t"
        dry_run_action "Reload nginx: sudo systemctl reload nginx"
    fi
    
    dry_run_check "Checking firewall configuration"
    case "$OS_ID" in
        "rocky"|"rhel"|"centos")
            if systemctl is-active --quiet firewalld; then
                dry_run_result "✓ firewalld is active"
                if sudo firewall-cmd --list-ports | grep -q "80/tcp\|443/tcp"; then
                    dry_run_result "✓ HTTP/HTTPS ports already open"
                else
                    dry_run_result "✗ HTTP/HTTPS ports not open"
                    dry_run_action "Open HTTP port: sudo firewall-cmd --permanent --add-port=80/tcp"
                    dry_run_action "Open HTTPS port: sudo firewall-cmd --permanent --add-port=443/tcp"
                    dry_run_action "Reload firewall: sudo firewall-cmd --reload"
                fi
            else
                dry_run_result "✗ firewalld not active"
                dry_run_action "Start firewalld: sudo systemctl start firewalld && sudo systemctl enable firewalld"
                dry_run_action "Configure firewall ports"
            fi
            ;;
        "ubuntu")
            if command -v ufw &>/dev/null; then
                if ufw status | grep -q "Status: active"; then
                    dry_run_result "✓ ufw is active"
                    if ufw status | grep -q "80/tcp\|443/tcp"; then
                        dry_run_result "✓ HTTP/HTTPS ports already open"
                    else
                        dry_run_result "✗ HTTP/HTTPS ports not open"
                        dry_run_action "Open HTTP port: sudo ufw allow 80/tcp"
                        dry_run_action "Open HTTPS port: sudo ufw allow 443/tcp"
                    fi
                else
                    dry_run_result "⚠ ufw installed but not active"
                    dry_run_action "Enable ufw: sudo ufw --force enable"
                    dry_run_action "Configure firewall ports"
                fi
            else
                dry_run_result "✗ ufw not installed"
                dry_run_action "Install ufw: sudo apt-get install -y ufw"
                dry_run_action "Configure and enable firewall"
            fi
            ;;
    esac
    echo ""
}

# Analyze CLI tool installation
dry_run_analyze_cli_tool() {
    echo -e "$(echo -e "${BLUE}=== CLI Tool Analysis ===${NC}")"
    
    dry_run_check "Checking claude-server CLI tool"
    if [[ -f "/usr/local/bin/claude-server" ]]; then
        dry_run_result "✓ claude-server CLI already installed"
        
        local cli_version_file="/usr/local/bin/.claude-server-version"
        local current_version=$(cd "$PROJECT_DIR" && git rev-parse HEAD 2>/dev/null || echo "unknown")
        
        if [[ -f "$cli_version_file" ]]; then
            local installed_version=$(cat "$cli_version_file" 2>/dev/null || echo "")
            if [[ "$installed_version" == "$current_version" ]]; then
                dry_run_result "✓ CLI tool is up-to-date"
            else
                dry_run_result "⚠ CLI tool needs update"
                dry_run_action "Rebuild and reinstall CLI tool"
            fi
        else
            dry_run_result "⚠ CLI tool version unknown"
            dry_run_action "Update CLI tool with version tracking"
        fi
    else
        dry_run_result "✗ claude-server CLI not found"
        if [[ -f "$PROJECT_DIR/src/ClaudeServerCLI/ClaudeServerCLI.csproj" ]]; then
            dry_run_result "✓ CLI project source found"
            dry_run_action "Build CLI tool: dotnet build -c Release"
            dry_run_action "Publish CLI tool: dotnet publish -c Release -r linux-x64 --self-contained"
            dry_run_action "Install to system: sudo cp publish/ClaudeServerCLI /usr/local/bin/claude-server"
            dry_run_action "Set executable permissions: sudo chmod +x /usr/local/bin/claude-server"
        else
            dry_run_result "✗ CLI project source not found"
            dry_run_action "Exit with error - CLI source files missing"
        fi
    fi
    
    dry_run_check "Checking CLI tool alias configuration"
    if grep -q "alias claude-server=" "$HOME/.bashrc" 2>/dev/null; then
        dry_run_result "✓ CLI alias already configured in .bashrc"
    else
        dry_run_result "✗ CLI alias not configured"
        dry_run_action "Add alias to ~/.bashrc: alias claude-server='/usr/local/bin/claude-server'"
    fi
    
    dry_run_check "Checking system-wide CLI access"
    if [[ -f "/etc/profile.d/claude-server.sh" ]]; then
        dry_run_result "✓ System-wide profile script exists"
    else
        dry_run_result "✗ System-wide profile script missing"
        dry_run_action "Create system profile script: /etc/profile.d/claude-server.sh"
    fi
    echo ""
}

# Check if sudo is available and warn about individual command usage
check_sudo_available() {
    if ! command -v sudo >/dev/null 2>&1; then
        error "sudo is required for this installation script"
        exit 1
    fi
    
    # Test sudo access
    if ! sudo -n true 2>/dev/null; then
        warn "This script will prompt for sudo password when needed for system operations"
        warn "You may be prompted for your password multiple times during installation"
    fi
}

# Create backup directory only when needed
create_backup_dir() {
    if [[ -z "$BACKUP_DIR" ]]; then
        BACKUP_DIR="$BACKUP_BASE_DIR/$(date +%Y%m%d_%H%M%S)"
        sudo mkdir -p "$BACKUP_DIR"
        log "Created backup directory: $BACKUP_DIR"
    fi
}

# Backup configuration file
backup_config() {
    local file="$1"
    if [[ -f "$file" ]]; then
        create_backup_dir  # Ensure backup directory exists
        local backup_file="$BACKUP_DIR/$(basename "$file").backup"
        if [[ ! -f "$backup_file" ]]; then
            sudo cp "$file" "$backup_file"
            log "Backed up $file to $backup_file"
        else
            log "Backup already exists: $backup_file"
        fi
    fi
}

# Verify command exists and works
verify_command() {
    local cmd="$1"
    local test_arg="${2:---version}"
    
    if ! command -v "$cmd" >/dev/null 2>&1; then
        error "$cmd not found in PATH"
        return 1
    fi
    
    local test_output
    if ! test_output=$($cmd $test_arg 2>&1); then
        error "$cmd found but not working properly"
        debug "Command output: $test_output"
        return 1
    fi
    
    log "$cmd verified successfully"
    return 0
}

# Verify service is running
verify_service() {
    local service="$1"
    local max_wait="${2:-30}"
    local wait_time=0
    
    while [[ $wait_time -lt $max_wait ]]; do
        if sudo systemctl is-active --quiet "$service"; then
            log "Service $service is running"
            return 0
        fi
        sleep 2
        ((wait_time+=2))
    done
    
    error "Service $service failed to start within ${max_wait}s"
    return 1
}

# Verify port is listening
verify_port() {
    local port="$1"
    local max_wait="${2:-30}"
    local wait_time=0
    
    while [[ $wait_time -lt $max_wait ]]; do
        if ss -tlnp | grep -q ":$port "; then
            log "Port $port is listening"
            return 0
        fi
        sleep 2
        ((wait_time+=2))
    done
    
    error "Port $port is not listening after ${max_wait}s"
    return 1
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

# Install prerequisites
install_prerequisites() {
    log "Installing system prerequisites..."
    
    case "$OS_ID" in
        "rocky"|"rhel"|"centos")
            sudo dnf update -y
            sudo dnf install -y curl wget openssl ca-certificates
            ;;
        "ubuntu")
            sudo apt-get update
            sudo apt-get install -y curl wget openssl ca-certificates gnupg lsb-release
            ;;
        *)
            error "Unsupported OS for prerequisites: $OS_ID"
            exit 1
            ;;
    esac
    
    # Verify prerequisites
    verify_command "curl"
    # OpenSSL uses 'version' not '--version'
    verify_command "openssl" "version"
}

# Install .NET Core SDK
install_dotnet() {
    log "Installing .NET Core SDK 8.0..."
    
    # Check if .NET is already installed and functional
    if command -v dotnet >/dev/null 2>&1 && dotnet --version >/dev/null 2>&1; then
        local current_version=$(dotnet --version | cut -d'.' -f1)
        if [[ "$current_version" -ge "8" ]]; then
            log ".NET SDK 8.0+ already installed: $(dotnet --version)"
            return 0
        else
            log "Found older .NET version: $(dotnet --version), upgrading..."
        fi
    fi
    
    case "$OS_ID" in
        "rocky"|"rhel"|"centos")
            # Check if Microsoft repository already exists
            if [[ ! -f /etc/yum.repos.d/microsoft-prod.repo ]]; then
                # Add Microsoft repository
                sudo rpm --import https://packages.microsoft.com/keys/microsoft.asc
                sudo tee /etc/yum.repos.d/microsoft-prod.repo > /dev/null << 'EOF'
[packages-microsoft-com-prod]
name=packages-microsoft-com-prod
baseurl=https://packages.microsoft.com/rhel/9/prod/
enabled=1
gpgcheck=1
gpgkey=https://packages.microsoft.com/keys/microsoft.asc
EOF
                log "Added Microsoft repository"
            else
                log "Microsoft repository already configured"
            fi
            
            sudo dnf update -y
            sudo dnf install -y dotnet-sdk-8.0
            ;;
        "ubuntu")
            # Check if .NET is already installed via the script
            if [[ -d "$HOME/.dotnet" && -f "$HOME/.dotnet/dotnet" ]]; then
                log ".NET already installed in $HOME/.dotnet"
            else
                # Install .NET using Microsoft's installation script
                curl -sSL https://dot.net/v1/dotnet-install.sh | bash /dev/stdin --channel 8.0
            fi
            
            # Add to current user's PATH and environment
            if ! echo "$PATH" | grep -q "$HOME/.dotnet"; then
                export PATH="$HOME/.dotnet:$PATH"
                export DOTNET_ROOT="$HOME/.dotnet"
            fi
            
            # Add to .bashrc for permanent PATH setting (idempotent)
            if ! grep -q 'export PATH="$HOME/.dotnet:$PATH"' "$HOME/.bashrc" 2>/dev/null; then
                echo "" >> "$HOME/.bashrc"
                echo "# .NET SDK PATH (added by install script)" >> "$HOME/.bashrc"
                echo 'export PATH="$HOME/.dotnet:$PATH"' >> "$HOME/.bashrc"
                echo 'export DOTNET_ROOT="$HOME/.dotnet"' >> "$HOME/.bashrc"
                log "Added .NET SDK to PATH in ~/.bashrc"
            fi
            ;;
        *)
            error "Unsupported OS for .NET installation: $OS_ID"
            exit 1
            ;;
    esac
    
    # Verify installation
    verify_command "dotnet"
}

# Install nginx web server
install_nginx() {
    log "Installing nginx web server..."
    
    # Check if nginx is already installed
    if command -v nginx >/dev/null 2>&1; then
        log "nginx already installed: $(nginx -v 2>&1)"
    else
        case "$OS_ID" in
            "rocky"|"rhel"|"centos")
                sudo dnf install -y nginx
                ;;
            "ubuntu")
                sudo apt-get update
                sudo apt-get install -y nginx
                ;;
            *)
                error "Unsupported OS for nginx installation: $OS_ID"
                exit 1
                ;;
        esac
    fi
    
    # Start and enable nginx (idempotent)
    if ! sudo systemctl is-active --quiet nginx; then
        sudo systemctl start nginx
        log "Started nginx service"
    else
        log "nginx service already running"
    fi
    
    if ! sudo systemctl is-enabled --quiet nginx; then
        sudo systemctl enable nginx
        log "Enabled nginx service"
    else
        log "nginx service already enabled"
    fi
    
    # Verify installation
    verify_command "nginx" "-t"
    verify_service "nginx"
    verify_port "80"
}

# Configure firewall
configure_firewall() {
    log "Configuring firewall..."
    
    case "$OS_ID" in
        "rocky"|"rhel"|"centos")
            # Rocky/RHEL uses firewalld
            if ! sudo systemctl is-active --quiet firewalld; then
                sudo systemctl start firewalld
                sudo systemctl enable firewalld
                log "Started and enabled firewalld"
            else
                log "Firewalld already running"
            fi
            
            # Add HTTP and HTTPS services (idempotent)
            if ! sudo firewall-cmd --list-services | grep -q "http "; then
                sudo firewall-cmd --permanent --add-service=http
                log "Added HTTP service to firewall"
            else
                log "HTTP service already allowed in firewall"
            fi
            
            if ! sudo firewall-cmd --list-services | grep -q "https"; then
                sudo firewall-cmd --permanent --add-service=https
                log "Added HTTPS service to firewall"
            else
                log "HTTPS service already allowed in firewall"
            fi
            
            # Add custom ports for the application (idempotent)
            local ports=("5000/tcp" "8080/tcp" "8443/tcp")
            for port in "${ports[@]}"; do
                if ! sudo firewall-cmd --list-ports | grep -q "$port"; then
                    sudo firewall-cmd --permanent --add-port="$port"
                    log "Added port $port to firewall"
                else
                    log "Port $port already allowed in firewall"
                fi
            done
            
            # Reload firewall
            sudo firewall-cmd --reload
            
            log "Firewalld configured successfully"
            ;;
        "ubuntu")
            # Ubuntu uses ufw
            if ! sudo ufw status | grep -q "Status: active"; then
                sudo ufw --force enable
                log "Enabled UFW firewall"
            else
                log "UFW firewall already active"
            fi
            
            # Allow SSH (important to not lock out) - idempotent
            if ! sudo ufw status | grep -q "22/tcp.*ALLOW"; then
                sudo ufw allow ssh
                log "Added SSH rule to firewall"
            else
                log "SSH already allowed in firewall"
            fi
            
            # Allow HTTP and HTTPS - idempotent
            if ! sudo ufw status | grep -q "80/tcp.*ALLOW"; then
                sudo ufw allow http
                log "Added HTTP rule to firewall"
            else
                log "HTTP already allowed in firewall"
            fi
            
            if ! sudo ufw status | grep -q "443/tcp.*ALLOW"; then
                sudo ufw allow https
                log "Added HTTPS rule to firewall"
            else
                log "HTTPS already allowed in firewall"
            fi
            
            # Allow custom ports - idempotent
            local ports=("5000" "8080" "8443")
            for port in "${ports[@]}"; do
                if ! sudo ufw status | grep -q "${port}/tcp.*ALLOW"; then
                    sudo ufw allow "${port}/tcp"
                    log "Added port $port/tcp to firewall"
                else
                    log "Port $port/tcp already allowed in firewall"
                fi
            done
            
            log "UFW configured successfully"
            ;;
        *)
            warn "Unknown OS for firewall configuration: $OS_ID"
            ;;
    esac
}

# Prompt for SSL certificate information
prompt_ssl_info() {
    if [[ "$PRODUCTION_MODE" != "true" ]]; then
        # Use defaults for development
        SSL_COUNTRY="US"
        SSL_STATE="CA"
        SSL_CITY="San Francisco"
        SSL_ORG="Claude Batch Server"
        SSL_OU="IT Department"
        SSL_CN="localhost"
        return
    fi
    
    log "SSL Certificate Information Required"
    echo ""
    
    # Only prompt if not already set (via parameters)
    if [[ -z "$SSL_COUNTRY" ]]; then
        read -p "Country Code (2 letters) [US]: " SSL_COUNTRY
        SSL_COUNTRY="${SSL_COUNTRY:-US}"
    fi
    
    if [[ -z "$SSL_STATE" ]]; then
        read -p "State/Province [CA]: " SSL_STATE
        SSL_STATE="${SSL_STATE:-CA}"
    fi
    
    if [[ -z "$SSL_CITY" ]]; then
        read -p "City [San Francisco]: " SSL_CITY
        SSL_CITY="${SSL_CITY:-San Francisco}"
    fi
    
    if [[ -z "$SSL_ORG" ]]; then
        read -p "Organization [Claude Batch Server]: " SSL_ORG
        SSL_ORG="${SSL_ORG:-Claude Batch Server}"
    fi
    
    if [[ -z "$SSL_OU" ]]; then
        read -p "Organizational Unit [IT Department]: " SSL_OU
        SSL_OU="${SSL_OU:-IT Department}"
    fi
    
    if [[ -z "$SSL_CN" ]]; then
        read -p "Common Name (hostname/FQDN) [localhost]: " SSL_CN
        SSL_CN="${SSL_CN:-localhost}"
    fi
    
    echo ""
    log "SSL Certificate will be generated with:"
    log "  Country: $SSL_COUNTRY"
    log "  State: $SSL_STATE"
    log "  City: $SSL_CITY"
    log "  Organization: $SSL_ORG"
    log "  Organizational Unit: $SSL_OU"
    log "  Common Name: $SSL_CN"
    echo ""
}

# Check if SSL certificate is valid and not expired
is_ssl_cert_valid() {
    local cert_file="$1"
    local cn_expected="$2"
    
    if [[ ! -f "$cert_file" ]]; then
        return 1
    fi
    
    # Check if certificate is not expired
    if ! sudo openssl x509 -in "$cert_file" -checkend 86400 >/dev/null 2>&1; then
        log "Certificate is expired or expires within 24 hours"
        return 1
    fi
    
    # Check if the CN matches (if provided)
    if [[ -n "$cn_expected" ]]; then
        local cert_cn=$(sudo openssl x509 -in "$cert_file" -noout -subject | sed -n 's/.*CN=\([^,]*\).*/\1/p')
        if [[ "$cert_cn" != "$cn_expected" ]]; then
            log "Certificate CN '$cert_cn' doesn't match expected '$cn_expected'"
            return 1
        fi
    fi
    
    return 0
}

# Generate self-signed SSL certificates
generate_ssl_cert() {
    log "Generating self-signed SSL certificates..."
    
    local ssl_dir="/etc/ssl/claude-batch-server"
    local key_file="$ssl_dir/server.key"
    local cert_file="$ssl_dir/server.crt"
    
    # Ensure SSL info is available
    if [[ -z "$SSL_COUNTRY" || -z "$SSL_CN" ]]; then
        prompt_ssl_info
    fi
    
    # Create SSL directory
    sudo mkdir -p "$ssl_dir"
    
    # Check if valid certificates already exist
    if [[ -f "$key_file" && -f "$cert_file" ]]; then
        if is_ssl_cert_valid "$cert_file" "$SSL_CN"; then
            log "Valid SSL certificates already exist"
            log "Certificate details:"
            sudo openssl x509 -in "$cert_file" -text -noout | grep -E "(Subject:|Issuer:|Not Before:|Not After:)" | sed 's/^/  /'
            return 0
        else
            log "Existing certificates are invalid/expired, regenerating..."
            # Backup existing certificates before regeneration
            backup_config "$key_file"
            backup_config "$cert_file"
        fi
    fi
    
    # Generate private key
    sudo openssl genrsa -out "$key_file" 2048
    
    # Generate certificate signing request and certificate
    sudo openssl req -new -x509 -key "$key_file" -out "$cert_file" -days 365 -subj \
        "/C=$SSL_COUNTRY/ST=$SSL_STATE/L=$SSL_CITY/O=$SSL_ORG/OU=$SSL_OU/CN=$SSL_CN"
    
    # Set proper permissions
    sudo chmod 600 "$key_file"
    sudo chmod 644 "$cert_file"
    
    log "SSL certificates generated at $ssl_dir"
    
    # Verify certificates
    if sudo openssl x509 -in "$cert_file" -text -noout >/dev/null 2>&1; then
        log "SSL certificate verification successful"
        log "Certificate details:"
        sudo openssl x509 -in "$cert_file" -text -noout | grep -E "(Subject:|Issuer:|Not Before:|Not After:)" | sed 's/^/  /'
    else
        error "SSL certificate verification failed"
        return 1
    fi
}

# Install Docker and Docker Compose
install_docker() {
    log "Installing Docker and Docker Compose..."
    
    # Check if Docker is already installed
    if command -v docker >/dev/null 2>&1 && docker --version >/dev/null 2>&1; then
        log "Docker already installed: $(docker --version)"
    else
        case "$OS_ID" in
            "rocky"|"rhel"|"centos")
                # Add Docker repository if not exists
                if [[ ! -f /etc/yum.repos.d/docker-ce.repo ]]; then
                    sudo dnf config-manager --add-repo=https://download.docker.com/linux/centos/docker-ce.repo
                    log "Added Docker repository"
                fi
                
                # Install Docker CE
                sudo dnf install -y docker-ce docker-ce-cli containerd.io docker-compose-plugin
                ;;
            "ubuntu")
                # Update package index
                sudo apt-get update
                
                # Install prerequisites
                sudo apt-get install -y ca-certificates curl gnupg lsb-release
                
                # Add Docker's official GPG key
                sudo mkdir -p /etc/apt/keyrings
                if [[ ! -f /etc/apt/keyrings/docker.gpg ]]; then
                    curl -fsSL https://download.docker.com/linux/ubuntu/gpg | sudo gpg --dearmor -o /etc/apt/keyrings/docker.gpg
                    log "Added Docker GPG key"
                fi
                
                # Set up repository
                if [[ ! -f /etc/apt/sources.list.d/docker.list ]]; then
                    echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.gpg] https://download.docker.com/linux/ubuntu $(lsb_release -cs) stable" | sudo tee /etc/apt/sources.list.d/docker.list > /dev/null
                    log "Added Docker repository"
                fi
                
                # Install Docker Engine
                sudo apt-get update
                sudo apt-get install -y docker-ce docker-ce-cli containerd.io docker-compose-plugin
                ;;
            *)
                error "Unsupported OS for Docker installation: $OS_ID"
                exit 1
                ;;
        esac
    fi
    
    # Start and enable Docker (idempotent)
    if ! sudo systemctl is-active --quiet docker; then
        sudo systemctl start docker
        log "Started Docker service"
    else
        log "Docker service already running"
    fi
    
    if ! sudo systemctl is-enabled --quiet docker; then
        sudo systemctl enable docker
        log "Enabled Docker service"
    else
        log "Docker service already enabled"
    fi
    
    # Verify installation
    verify_command "docker"
    verify_command "docker" "compose version"
    verify_service "docker"
}

# Install Claude Code CLI
install_claude_cli() {
    log "Installing Claude Code CLI..."
    
    # Check if Claude CLI is already installed and accessible
    local claude_was_already_installed=false
    if command -v claude >/dev/null 2>&1; then
        log "Claude CLI already installed: $(claude --version 2>/dev/null || echo 'version unknown')"
        claude_was_already_installed=true
    elif [[ -f "$HOME/.local/bin/claude" ]]; then
        log "Claude CLI binary exists at $HOME/.local/bin/claude but not in PATH"
        claude_was_already_installed=true
    fi
    
    # Install using official installer as regular user
    curl -fsSL https://claude.ai/install.sh | bash
    
    # Ensure .local/bin is in PATH for the current user
    if [[ -f "$HOME/.local/bin/claude" ]]; then
        local path_setup_needed=false
        
        # Add to current user's PATH if not already there
        if ! echo "$PATH" | grep -q "$HOME/.local/bin"; then
            export PATH="$HOME/.local/bin:$PATH"
            path_setup_needed=true
        fi
        
        # Add to .bashrc for permanent PATH setting
        if ! grep -q "$HOME/.local/bin" "$HOME/.bashrc" 2>/dev/null; then
            echo "" >> "$HOME/.bashrc"
            echo "# Claude CLI PATH (added by install script)" >> "$HOME/.bashrc"
            echo 'export PATH="$HOME/.local/bin:$PATH"' >> "$HOME/.bashrc"
            log "Added Claude CLI to PATH in ~/.bashrc"
            path_setup_needed=true
        fi
        
        log "Claude Code CLI installed successfully at $HOME/.local/bin/claude"
        
        # Test if claude is now accessible
        if command -v claude >/dev/null 2>&1; then
            log "✅ 'claude' command is now available (no restart needed)"
            # Claude is available, no PATH setup script needed
            path_setup_needed=false
        else
            warn "⚠️  You need to run 'source ~/.bashrc' or restart your shell to use 'claude' command"
            warn "Or you can run: export PATH=\"\$HOME/.local/bin:\$PATH\""
            path_setup_needed=true
        fi
        
        # Only create the PATH setup script if Claude was NOT already installed 
        # and PATH setup is needed
        if [[ "$claude_was_already_installed" == "false" && "$path_setup_needed" == "true" ]]; then
            cat > /tmp/claude-path-setup.sh << 'EOF'
#!/bin/bash
# Claude CLI PATH setup - Source this file to make claude available immediately
export PATH="$HOME/.local/bin:$PATH"
echo "✅ Claude CLI is now available in your current session"
echo "Run 'claude --version' to test"
EOF
            chmod +x /tmp/claude-path-setup.sh
            
            log "💡 To use 'claude' immediately after this script finishes:"
            log "   Run: $(echo -e "${BLUE}source /tmp/claude-path-setup.sh${NC}")"
        fi
    else
        warn "Claude Code CLI installation may have failed, but continuing..."
    fi
}

# Install Python and pipx
install_pipx() {
    log "Installing Python and pipx..."
    
    case "$OS_ID" in
        "rocky"|"rhel"|"centos")
            # Install Python 3 and pip
            sudo dnf install -y python3 python3-pip python3-venv
            
            # Install pipx using pip
            python3 -m pip install --user pipx
            python3 -m pipx ensurepath
            ;;
        "ubuntu")
            # Install Python 3 and pipx
            sudo apt-get update
            sudo apt-get install -y python3 python3-pip python3-venv pipx
            
            # Ensure pipx is in PATH
            pipx ensurepath
            ;;
        *)
            error "Unsupported OS for pipx installation: $OS_ID"
            exit 1
            ;;
    esac
    
    # Add pipx to current session PATH
    export PATH="$HOME/.local/bin:$PATH"
    
    # Verify installation
    if command -v pipx >/dev/null 2>&1; then
        log "pipx installed successfully: $(pipx --version)"
    else
        error "Failed to install pipx"
        exit 1
    fi
}

# Install Code Indexer (cidx)
install_code_indexer() {
    log "Installing Code Indexer (cidx)..."
    
    # Install code-indexer using pipx with force flag to update if already installed
    if pipx install --force git+https://github.com/jsbattig/code-indexer.git; then
        log "Code Indexer installed successfully"
        
        # Create symlink for system-wide access
        if [[ -f "$HOME/.local/bin/cidx" ]]; then
            sudo ln -sf "$HOME/.local/bin/cidx" /usr/local/bin/cidx
            log "Created system-wide symlink for cidx"
        fi
    else
        warn "Code Indexer installation may have failed, but continuing..."
    fi
}

# Configure Copy-on-Write support
configure_cow() {
    log "Configuring Copy-on-Write filesystem support..."
    
    # Create workspace directories (idempotent)
    sudo mkdir -p /workspace/repos /workspace/jobs
    
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
                    sudo dnf install -y btrfs-progs
                    ;;
                "ubuntu")
                    sudo apt-get install -y btrfs-progs
                    ;;
            esac
            ;;
        *)
            warn "Unknown filesystem type: $FS_TYPE. Will use hardlink fallback."
            ;;
    esac
    
    # Install rsync for fallback support (idempotent)
    if ! command -v rsync >/dev/null 2>&1; then
        case "$OS_ID" in
            "rocky"|"rhel"|"centos")
                sudo dnf install -y rsync
                ;;
            "ubuntu")
                sudo apt-get install -y rsync
                ;;
        esac
        log "Installed rsync for fallback support"
    else
        log "rsync already installed"
    fi
    
    # Set proper permissions (idempotent)
    sudo chmod 755 /workspace
    sudo chmod 755 /workspace/repos /workspace/jobs
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
    
    # Verify the API project built successfully
    local api_dll="$PROJECT_DIR/src/ClaudeBatchServer.Api/bin/Release/net8.0/ClaudeBatchServer.Api.dll"
    local api_runtime_config="$PROJECT_DIR/src/ClaudeBatchServer.Api/bin/Release/net8.0/ClaudeBatchServer.Api.runtimeconfig.json"
    
    if [[ ! -f "$api_dll" ]]; then
        error "API DLL not found at $api_dll after build"
        error "Build may have failed. Check build output above."
        exit 1
    fi
    
    if [[ ! -f "$api_runtime_config" ]]; then
        error "API runtime config not found at $api_runtime_config after build"
        error "Build may have failed. Check build output above."
        exit 1
    fi
    
    log "API build verified - DLL and runtime config found in build output directory"
    
    # Create systemd service
    create_systemd_service
    
    # Create workspace directories (idempotent)
    sudo mkdir -p /workspace/repos /workspace/jobs /var/log/claude-batch-server
    
    # Set proper permissions for current user (idempotent)
    local current_user=$(whoami)
    local current_group=$(id -gn)
    sudo chown -R "$current_user:$current_group" /workspace
    sudo chown -R "$current_user:$current_group" /var/log/claude-batch-server
    sudo chmod 755 /workspace /workspace/repos /workspace/jobs
    sudo chmod 755 /var/log/claude-batch-server
    
    log "Application built and deployed successfully"
}

# Build and install claude-server CLI tool
install_claude_server_cli() {
    log "Building and installing claude-server CLI tool..."
    
    cd "$PROJECT_DIR/src/ClaudeServerCLI"
    
    # Check if CLI tool is already installed and up-to-date
    local cli_version_file="/usr/local/bin/.claude-server-version"
    local current_version=$(git rev-parse HEAD 2>/dev/null || echo "unknown")
    
    if [[ -f "/usr/local/bin/claude-server" && -f "$cli_version_file" ]]; then
        local installed_version=$(cat "$cli_version_file" 2>/dev/null || echo "")
        if [[ "$installed_version" == "$current_version" ]]; then
            log "claude-server CLI tool is already up-to-date (version: $current_version)"
            return 0
        fi
    fi
    
    # Build the CLI tool
    export PATH="$HOME/.dotnet:$PATH"
    dotnet restore
    dotnet build -c Release
    
    # Publish self-contained executable
    local runtime=""
    case "$OS_ID" in
        "rocky"|"centos"|"rhel")
            runtime="linux-x64"
            ;;
        "ubuntu"|"debian")
            runtime="linux-x64"
            ;;
        *)
            runtime="linux-x64"
            ;;
    esac
    
    dotnet publish -c Release -r "$runtime" --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=true -o ./publish
    
    # Verify the published executable exists
    if [[ ! -f "./publish/claude-server" ]]; then
        error "Published executable not found at ./publish/claude-server"
        log "Contents of publish directory:"
        ls -la ./publish/ || true
        exit 1
    fi
    
    # Install to system location with backup
    if [[ -f "/usr/local/bin/claude-server" ]]; then
        backup_config "/usr/local/bin/claude-server"
    fi
    
    sudo cp ./publish/claude-server /usr/local/bin/claude-server
    sudo chmod +x /usr/local/bin/claude-server
    
    # Store version information
    echo "$current_version" | sudo tee "$cli_version_file" > /dev/null
    
    # Create global alias (idempotent)
    local bashrc_file="$HOME/.bashrc"
    local alias_line="alias claude-server='/usr/local/bin/claude-server'"
    
    if ! grep -Fxq "$alias_line" "$bashrc_file" 2>/dev/null; then
        echo "# Claude Server CLI alias (added by install script)" >> "$bashrc_file"
        echo "$alias_line" >> "$bashrc_file"
        log "Added claude-server alias to $bashrc_file"
    else
        log "claude-server alias already exists in $bashrc_file"
    fi
    
    # Also add to /etc/profile.d for system-wide access
    local profile_script="/etc/profile.d/claude-server.sh"
    if [[ ! -f "$profile_script" ]]; then
        sudo tee "$profile_script" > /dev/null << EOF
#!/bin/bash
# Claude Server CLI global configuration
export PATH="/usr/local/bin:\$PATH"
EOF
        sudo chmod +x "$profile_script"
        log "Created system-wide profile script: $profile_script"
    fi
    
    # Test CLI installation
    if /usr/local/bin/claude-server --help &>/dev/null; then
        log "claude-server CLI tool installed successfully"
        log "CLI tool location: /usr/local/bin/claude-server"
        log "Use 'claude-server --help' to see available commands"
    else
        error "Failed to install claude-server CLI tool"
        return 1
    fi
    
    cd "$PROJECT_DIR"
}

# Configure nginx for production
configure_nginx() {
    log "Configuring nginx for Claude Batch Server..."
    
    local nginx_conf="/etc/nginx/sites-available/claude-batch-server"
    local nginx_link="/etc/nginx/sites-enabled/claude-batch-server"
    local ssl_dir="/etc/ssl/claude-batch-server"
    
    # Backup existing nginx configuration
    backup_config "/etc/nginx/nginx.conf"
    
    # Create sites-available directory if it doesn't exist (mainly for Rocky/RHEL)
    sudo mkdir -p /etc/nginx/sites-available
    sudo mkdir -p /etc/nginx/sites-enabled
    
    # Check if configuration already exists and is current
    local config_hash=""
    if [[ -f "$nginx_conf" ]]; then
        config_hash=$(sudo md5sum "$nginx_conf" 2>/dev/null | cut -d' ' -f1)
        log "Existing nginx configuration found"
    fi
    
    # Backup existing configuration if it exists
    [[ -f "$nginx_conf" ]] && backup_config "$nginx_conf"
    
    # Create nginx configuration for Claude Batch Server
    sudo tee "$nginx_conf" > /dev/null << EOF
server {
    listen 80;
    server_name localhost _;
    
    # Redirect HTTP to HTTPS in production
    return 301 https://\$server_name\$request_uri;
}

server {
    listen 443 ssl http2;
    server_name localhost _;
    
    # SSL Configuration
    ssl_certificate ${ssl_dir}/server.crt;
    ssl_certificate_key ${ssl_dir}/server.key;
    ssl_protocols TLSv1.2 TLSv1.3;
    ssl_ciphers ECDHE-RSA-AES128-GCM-SHA256:ECDHE-RSA-AES256-GCM-SHA384:ECDHE-RSA-AES128-SHA256:ECDHE-RSA-AES256-SHA384;
    ssl_prefer_server_ciphers on;
    ssl_session_cache shared:SSL:10m;
    ssl_session_timeout 10m;
    
    # Security headers
    add_header X-Frame-Options DENY;
    add_header X-Content-Type-Options nosniff;
    add_header X-XSS-Protection "1; mode=block";
    add_header Strict-Transport-Security "max-age=63072000; includeSubDomains; preload";
    
    # Proxy to the application
    location / {
        proxy_pass http://127.0.0.1:5000;
        proxy_http_version 1.1;
        proxy_set_header Upgrade \$http_upgrade;
        proxy_set_header Connection keep-alive;
        proxy_set_header Host \$host;
        proxy_set_header X-Real-IP \$remote_addr;
        proxy_set_header X-Forwarded-For \$proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto \$scheme;
        proxy_cache_bypass \$http_upgrade;
        
        # Timeouts
        proxy_connect_timeout 60s;
        proxy_send_timeout 60s;
        proxy_read_timeout 60s;
    }
    
    # Health check endpoint
    location /health {
        proxy_pass http://127.0.0.1:5000/health;
        access_log off;
    }
    
    # Static files caching
    location ~* \.(js|css|png|jpg|jpeg|gif|ico|svg)$ {
        proxy_pass http://127.0.0.1:5000;
        expires 1y;
        add_header Cache-Control "public, immutable";
    }
}
EOF
    
    # Check if configuration changed
    local new_hash=$(sudo md5sum "$nginx_conf" 2>/dev/null | cut -d' ' -f1)
    local config_changed=true
    
    if [[ -n "$config_hash" && "$config_hash" == "$new_hash" ]]; then
        config_changed=false
        log "Nginx configuration unchanged"
    else
        log "Nginx configuration updated"
    fi
    
    # Enable the site (idempotent)
    if [[ ! -L "$nginx_link" || "$(readlink "$nginx_link")" != "$nginx_conf" ]]; then
        sudo ln -sf "$nginx_conf" "$nginx_link"
        log "Enabled nginx site configuration"
    else
        log "Nginx site already enabled"
    fi
    
    # Ensure include directive exists in main nginx.conf (idempotent)
    if ! sudo grep -q "include.*sites-enabled" /etc/nginx/nginx.conf; then
        sudo sed -i '/http {/a\    include /etc/nginx/sites-enabled/*;' /etc/nginx/nginx.conf
        log "Added sites-enabled include to nginx.conf"
        config_changed=true
    fi
    
    # Test nginx configuration
    if sudo nginx -t; then
        log "Nginx configuration test passed"
        
        # Only reload if configuration changed
        if [[ "$config_changed" == "true" ]]; then
            sudo systemctl reload nginx
            log "Reloaded nginx with new configuration"
        else
            log "Nginx configuration unchanged, skipping reload"
        fi
        
        verify_service "nginx"
        verify_port "443"
    else
        error "Nginx configuration test failed"
        return 1
    fi
}

# Create systemd service
create_systemd_service() {
    log "Creating systemd service..."
    
    # Detect actual dotnet path
    local dotnet_path
    if command -v dotnet >/dev/null 2>&1; then
        dotnet_path=$(which dotnet)
        log "Detected dotnet at: $dotnet_path"
    else
        error "dotnet not found in PATH. Please install .NET 8.0 SDK first."
        exit 1
    fi
    
    # Determine DOTNET_ROOT from dotnet path
    local dotnet_root
    if [[ "$dotnet_path" == */bin/dotnet ]]; then
        # Standard system installation (e.g., /usr/bin/dotnet)
        dotnet_root="${dotnet_path%/bin/dotnet}"
    elif [[ "$dotnet_path" == */.dotnet/dotnet ]]; then
        # User installation (e.g., /home/user/.dotnet/dotnet)
        dotnet_root="${dotnet_path%/dotnet}"
    else
        # Fallback - assume parent directory
        dotnet_root="$(dirname "$dotnet_path")"
    fi
    
    log "Using DOTNET_ROOT: $dotnet_root"
    
    local service_file="/etc/systemd/system/claude-batch-server.service"
    local service_changed=true
    local service_hash=""
    
    # Check if service file already exists and get its hash
    if [[ -f "$service_file" ]]; then
        service_hash=$(sudo md5sum "$service_file" 2>/dev/null | cut -d' ' -f1)
        log "Existing systemd service found"
    fi
    
    # Backup existing service file if it exists
    [[ -f "$service_file" ]] && backup_config "$service_file"
    
    # Get current user and group for the service
    local current_user=$(whoami)
    local current_group=$(id -gn)
    
    sudo tee "$service_file" > /dev/null << EOF
[Unit]
Description=Claude Batch Server
After=network.target
Wants=network.target

[Service]
Type=exec
User=$current_user
Group=$current_group
WorkingDirectory=$PROJECT_DIR/src/ClaudeBatchServer.Api/bin/Release/net8.0
ExecStart=$dotnet_path ClaudeBatchServer.Api.dll
Restart=always
RestartSec=10
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=ASPNETCORE_URLS=http://0.0.0.0:5000
Environment=DOTNET_ROOT=$dotnet_root
Environment=PATH=$dotnet_root:$dotnet_root/bin:/usr/local/bin:/usr/bin:/bin

# Security settings
NoNewPrivileges=true
PrivateTmp=true

[Install]
WantedBy=multi-user.target
EOF

    # Check if service file changed
    local new_service_hash=$(sudo md5sum "$service_file" 2>/dev/null | cut -d' ' -f1)
    
    if [[ -n "$service_hash" && "$service_hash" == "$new_service_hash" ]]; then
        service_changed=false
        log "Systemd service unchanged"
    else
        log "Systemd service updated"
    fi
    
    # Only reload daemon if service changed
    if [[ "$service_changed" == "true" ]]; then
        sudo systemctl daemon-reload
        log "Reloaded systemd daemon"
    fi
    
    # Enable service (idempotent)
    if ! sudo systemctl is-enabled --quiet claude-batch-server; then
        sudo systemctl enable claude-batch-server
        log "Enabled claude-batch-server service"
    else
        log "Claude-batch-server service already enabled"
    fi
}

# Setup logging
setup_logging() {
    log "Setting up logging..."
    
    # Create log directories (idempotent)
    sudo mkdir -p /var/log/claude-batch-server
    
    # Create logrotate configuration (idempotent)
    if [[ ! -f /etc/logrotate.d/claude-batch-server ]]; then
        sudo tee /etc/logrotate.d/claude-batch-server > /dev/null << EOF
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
        log "Created logrotate configuration"
    else
        log "Logrotate configuration already exists"
    fi

    log "Logging configured"
}

# Validate installation
validate_installation() {
    log "Validating installation..."
    
    local errors=0
    
    # Check .NET
    if ! verify_command "dotnet"; then
        ((errors++))
    fi
    
    # Check Docker
    if ! verify_command "docker"; then
        ((errors++))
    fi
    
    # Check Docker Compose
    if ! verify_command "docker" "compose version"; then
        ((errors++))
    fi
    
    # Check Claude CLI (optional)
    if ! command -v claude >/dev/null 2>&1; then
        warn "Claude Code CLI not found (optional)"
    fi
    
    # Check pipx (optional)
    if ! command -v pipx >/dev/null 2>&1; then
        warn "pipx not found (optional)"
    fi
    
    # Check code indexer (optional)
    if ! command -v cidx >/dev/null 2>&1; then
        warn "Code Indexer (cidx) not found (optional)"
    else
        log "Code Indexer (cidx) found: $(cidx --version 2>/dev/null || echo 'version unknown')"
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
    
    # Production-specific validations
    if [[ "$PRODUCTION_MODE" == "true" ]]; then
        # Check nginx
        if ! verify_command "nginx" "-t"; then
            ((errors++))
        fi
        
        # Check SSL certificates
        local ssl_dir="/etc/ssl/claude-batch-server"
        if [[ ! -f "$ssl_dir/server.crt" || ! -f "$ssl_dir/server.key" ]]; then
            error "SSL certificates not found"
            ((errors++))
        fi
        
        # Check firewall
        case "$OS_ID" in
            "rocky"|"rhel"|"centos")
                if ! sudo systemctl is-active --quiet firewalld; then
                    error "Firewalld is not running"
                    ((errors++))
                fi
                ;;
            "ubuntu")
                if ! sudo ufw status | grep -q "Status: active"; then
                    error "UFW is not active"
                    ((errors++))
                fi
                ;;
        esac
        
        # Check HTTPS port
        if ! verify_port "443" 10; then
            warn "HTTPS port 443 not listening (may be normal if service not started)"
        fi
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

# Get network information for access instructions
get_network_info() {
    # Get primary IP address
    local primary_ip=$(ip route get 1.1.1.1 2>/dev/null | grep -oP 'src \K\S+' || echo "127.0.0.1")
    
    # Get external IP if possible (with timeout)
    local external_ip=""
    if command -v curl >/dev/null 2>&1; then
        external_ip=$(timeout 5 curl -s ifconfig.me 2>/dev/null || echo "")
    fi
    
    # Get all network interfaces
    local interfaces=$(ip -4 addr show 2>/dev/null | grep -oP '(?<=inet\s)\d+(\.\d+){3}' | grep -v '127.0.0.1' | head -3)
    
    echo "PRIMARY_IP=$primary_ip"
    echo "EXTERNAL_IP=$external_ip"
    echo "ALL_IPS=$interfaces"
}

# Print usage instructions
print_usage() {
    # Get network information
    local network_info=$(get_network_info)
    local primary_ip=$(echo "$network_info" | grep "PRIMARY_IP=" | cut -d'=' -f2)
    local external_ip=$(echo "$network_info" | grep "EXTERNAL_IP=" | cut -d'=' -f2)
    local all_ips=$(echo "$network_info" | grep "ALL_IPS=" | cut -d'=' -f2)
    
    printf "\n%b🎉 Claude Batch Server Installation Complete!%b\n\n" "$GREEN" "$NC"
    printf "%bInstallation Details:%b\n" "$YELLOW" "$NC"
    printf -- "- Mode: %s\n" "$([ "$PRODUCTION_MODE" == "true" ] && echo "Production" || echo "Development")"
    printf -- "- OS: %s\n" "$(lsb_release -d 2>/dev/null | cut -f2 || echo "$OS_ID $VERSION_ID")"
    printf -- "- Backup Directory: %s\n\n" "${BACKUP_DIR:-"(none created)"}"
    printf "%b📡 Server Access Information:%b\n" "$YELLOW" "$NC"
    printf -- "- Primary IP: %b%s%b\n" "$BLUE" "$primary_ip" "$NC"
    [ -n "$external_ip" ] && printf -- "- External IP: %b%s%b\n" "$BLUE" "$external_ip" "$NC"
    printf -- "- Local Access: %bhttp://localhost%s%b\n\n" "$BLUE" "$([ "$PRODUCTION_MODE" == "true" ] && echo " (redirects to HTTPS)")" "$NC"
    
    printf "\n%b🚀 Essential Setup Steps:%b\n\n" "$YELLOW" "$NC"
    printf "1. %bSet up Claude CLI Authentication:%b\n" "$BLUE" "$NC"
    printf "   %bclaude /login%b\n" "$BLUE" "$NC"
    printf "   %b# Complete Claude AI authentication in your browser%b\n\n" "$GREEN" "$NC"
    
    printf "2. %bConfigure Claude CLI permissions:%b\n" "$BLUE" "$NC"
    printf "   %bclaude --dangerously-skip-permissions%b\n" "$BLUE" "$NC"
    printf "   %b# This allows Claude CLI to work in server environments%b\n\n" "$GREEN" "$NC"
    
    printf "3. %bCopy and configure server environment:%b\n" "$BLUE" "$NC"
    printf "   %bcp %s/docker/.env.example /etc/claude-batch-server.env%b\n" "$BLUE" "$PROJECT_DIR" "$NC"
    printf "   %bsudo nano /etc/claude-batch-server.env%b\n\n" "$BLUE" "$NC"
    
    printf "4. %bStart the Claude Batch Server:%b\n" "$BLUE" "$NC"
    printf "   %bsudo systemctl start claude-batch-server%b\n\n" "$BLUE" "$NC"
    
    printf "5. %bVerify service is running:%b\n" "$BLUE" "$NC"
    printf "   %bsudo systemctl status claude-batch-server%b\n\n" "$BLUE" "$NC"

    printf "%b🏃 How to Run Claude Batch Server:%b\n\n" "$YELLOW" "$NC"
    printf "%bOption 1: Systemd Service (Recommended for Production)%b\n" "$BLUE" "$NC"
    printf "The installation has created a systemd service for you:\n\n"
    
    printf "• Start the service:\n"
    printf "  %bsudo systemctl start claude-batch-server%b\n\n" "$BLUE" "$NC"
    
    printf "• Enable auto-start on boot:\n"
    printf "  %bsudo systemctl enable claude-batch-server%b\n\n" "$BLUE" "$NC"
    
    printf "• Check service status:\n"
    printf "  %bsudo systemctl status claude-batch-server%b\n\n" "$BLUE" "$NC"
    
    printf "• View service logs:\n"
    printf "  %bsudo journalctl -u claude-batch-server -f%b\n\n" "$BLUE" "$NC"
    
    printf "• Stop the service:\n"
    printf "  %bsudo systemctl stop claude-batch-server%b\n\n" "$BLUE" "$NC"
    
    printf "• Restart the service:\n"
    printf "  %bsudo systemctl restart claude-batch-server%b\n\n" "$BLUE" "$NC"

    cat << EOF
$(echo -e "$(echo -e "${BLUE}Option 2: Docker Compose (Alternative)${NC}")")
If you prefer containerized deployment:

• Navigate to project directory:
  $(echo -e "${BLUE}cd $PROJECT_DIR${NC}")

• Configure Docker environment:
  $(echo -e "$(echo -e "${BLUE}cp docker/.env.example docker/.env${NC}")")
  $(echo -e "$(echo -e "${BLUE}nano docker/.env${NC}")")

• Start with Docker Compose:
  $(echo -e "$(echo -e "${BLUE}docker compose -f docker/docker-compose.yml up -d${NC}")")

• View container logs:
  $(echo -e "$(echo -e "${BLUE}docker logs claude-batch-server -f${NC}")")

• Stop containers:
  $(echo -e "$(echo -e "${BLUE}docker compose -f docker/docker-compose.yml down${NC}")")

$(echo -e "$(echo -e "${BLUE}Option 3: Manual Development Mode${NC}")")
For development and testing:

• Navigate to API directory:
  $(echo -e "${BLUE}cd $PROJECT_DIR/src/ClaudeBatchServer.Api${NC}")

• Run directly with .NET:
  $(echo -e "$(echo -e "${BLUE}dotnet run${NC}")")

• Or build and run:
  $(echo -e "$(echo -e "${BLUE}dotnet build && dotnet run --project ClaudeBatchServer.Api${NC}")")

$(echo -e "$(echo -e "${YELLOW}⚙️ Configuration:${NC}")")

• System service config: $(echo -e "$(echo -e "${BLUE}/etc/claude-batch-server.env${NC}")")
• Docker config: $(echo -e "${BLUE}$PROJECT_DIR/docker/.env${NC}")
• Development config: $(echo -e "${BLUE}$PROJECT_DIR/src/ClaudeBatchServer.Api/appsettings.Development.json${NC}")

$(echo -e "$(echo -e "${YELLOW}🛠️ Server Management:${NC}")")

• View server logs:
  $(echo -e "$(echo -e "${BLUE}sudo journalctl -u claude-batch-server -f${NC}")")

• Use the CLI tool:
  $(echo -e "$(echo -e "${BLUE}claude-server --help${NC}")")
  $(echo -e "${BLUE}claude-server auth login --server-url http://$primary_ip$([ "$PRODUCTION_MODE" == "true" ] && echo "s")${NC}")

• Add users (development mode):
  $(echo -e "$(echo -e "${BLUE}claude-server user add <username> <password>${NC}")")

$(echo -e "$(echo -e "${YELLOW}👥 User Authentication Management:${NC}")")

• Add a new user:
  $(echo -e "$(echo -e "${BLUE}claude-server user add myuser mypassword123${NC}")")

• List all users:
  $(echo -e "$(echo -e "${BLUE}claude-server user list${NC}")")
  $(echo -e "$(echo -e "${BLUE}claude-server user list --detailed${NC}")")

• Update user password:
  $(echo -e "$(echo -e "${BLUE}claude-server user update myuser newpassword456${NC}")")

• Remove a user:
  $(echo -e "$(echo -e "${BLUE}claude-server user remove myuser${NC}")")

• Login via CLI:
  $(echo -e "${BLUE}claude-server auth login --server-url http://$primary_ip$([ "$PRODUCTION_MODE" == "true" ] && echo "s"):$([ "$PRODUCTION_MODE" == "true" ] && echo "443" || echo "5000")${NC}")

$(echo -e "$(echo -e "${YELLOW}🧪 Testing Your Installation:${NC}")")

• Test API health:
  ${BLUE}curl -f http://$primary_ip:5000/health${NC}

• Test authentication (after adding user):
  ${BLUE}curl -X POST http://$primary_ip:5000/auth/login \\
    -H 'Content-Type: application/json' \\
    -d '{"username":"myuser","password":"mypassword123"}'${NC}

• Access Swagger UI:
  ${BLUE}Open: http://$primary_ip$([ "$PRODUCTION_MODE" == "true" ] && echo "s"):$([ "$PRODUCTION_MODE" == "true" ] && echo "443" || echo "5000")/swagger${NC}

EOF

if [[ "$PRODUCTION_MODE" == "true" ]]; then
    cat << EOF
$(echo -e "${YELLOW}🔒 Production Mode - Additional Information:${NC}")

$(echo -e "${BLUE}Access URLs:${NC}")
• HTTPS (recommended): ${BLUE}https://$primary_ip/${NC}$([ -n "$external_ip" ] && echo "
• External HTTPS: ${BLUE}https://$external_ip/${NC}")
• HTTP (redirects to HTTPS): ${BLUE}http://$primary_ip/${NC}
• Direct API: ${BLUE}http://$primary_ip:5000/${NC}

$(echo -e "${BLUE}SSL Certificate:${NC}")
• Certificate: $(echo -e "${BLUE}/etc/ssl/claude-batch-server/server.crt${NC}")
• Private Key: $(echo -e "${BLUE}/etc/ssl/claude-batch-server/server.key${NC}")
• Valid for: ${BLUE}$SSL_CN${NC}

$(echo -e "${BLUE}Security Status:${NC}")
EOF
    case "$OS_ID" in
        "rocky"|"rhel"|"centos")
            echo "• Firewalld: $(sudo systemctl is-active firewalld)"
            echo "• Open ports: HTTP (80), HTTPS (443), API (5000), Docker (8080, 8443)"
            ;;
        "ubuntu")
            echo "• UFW: $(sudo ufw status | head -1)"
            echo "• Open ports: SSH, HTTP, HTTPS, API (5000), Docker (8080, 8443)"
            ;;
    esac
    cat << EOF

$(echo -e "${YELLOW}⚠️  Production Checklist:${NC}")
□ Update SSL certificate with proper domain name
□ Configure firewall for your specific network
□ Set up proper backup procedures
□ Review security settings in configuration
□ Test external access and SSL certificate

EOF
else
    cat << EOF
$(echo -e "${YELLOW}🔧 Development Mode - Additional Options:${NC}")

$(echo -e "${BLUE}Alternative Access URLs:${NC}")
• Direct API: ${BLUE}http://$primary_ip:5000/${NC}
• Docker (if used): ${BLUE}http://$primary_ip:8080/${NC}
• All local IPs: ${BLUE}$(echo "$all_ips" | tr '\n' ' ')${NC}

$(echo -e "${BLUE}Docker Development Alternative:${NC}")
If you prefer Docker development setup:

1. Navigate to project directory:
   ${BLUE}cd $PROJECT_DIR${NC}
   
2. Copy and configure environment:
   $(echo -e "${BLUE}cp docker/.env.example docker/.env${NC}")
   $(echo -e "${BLUE}nano docker/.env${NC}")
   
3. Start with Docker Compose:
   $(echo -e "${BLUE}docker compose -f docker/docker-compose.yml up -d${NC}")

$(echo -e "${YELLOW}💡 Development Tips:${NC}")
• Use local authentication files for testing users
• API server runs without SSL in development mode
• Direct port access (5000) bypasses nginx proxy

EOF
fi

    printf "%b📚 Additional Resources:%b\n\n" "$YELLOW" "$NC"
    printf "%bAPI Documentation:%b\n" "$BLUE" "$NC"
    printf "• Swagger UI: %bhttp://%s%s:%s/swagger%b\n" "$BLUE" "$primary_ip" "$([ "$PRODUCTION_MODE" == "true" ] && echo "s")" "$([ "$PRODUCTION_MODE" == "true" ] && echo "443" || echo "5000")" "$NC"
    printf "• Health Check: %bhttp://%s%s:%s/health%b\n\n" "$BLUE" "$primary_ip" "$([ "$PRODUCTION_MODE" == "true" ] && echo "s")" "$([ "$PRODUCTION_MODE" == "true" ] && echo "443" || echo "5000")" "$NC"
    
    printf "%bSystem Logs:%b\n" "$BLUE" "$NC"
    printf "• Application logs: %b/var/log/claude-batch-server/%b\n" "$BLUE" "$NC"
    printf "• System service: %bsudo journalctl -u claude-batch-server%b\n" "$BLUE" "$NC"
    printf "• Docker logs: %bdocker logs claude-batch-server%b (if using Docker)\n\n" "$BLUE" "$NC"
    
    printf "%bService Commands:%b\n" "$BLUE" "$NC"
    printf "• Start: %bsudo systemctl start claude-batch-server%b\n" "$BLUE" "$NC"
    printf "• Stop: %bsudo systemctl stop claude-batch-server%b\n" "$BLUE" "$NC"
    printf "• Restart: %bsudo systemctl restart claude-batch-server%b\n" "$BLUE" "$NC"
    printf "• Enable auto-start: %bsudo systemctl enable claude-batch-server%b\n" "$BLUE" "$NC"
    printf "• View status: %bsudo systemctl status claude-batch-server%b\n\n" "$BLUE" "$NC"
    
    printf "%bTroubleshooting:%b\n" "$BLUE" "$NC"
    printf "• Test Claude CLI: %bclaude --version%b\n" "$BLUE" "$NC"
    printf "• Test claude-server CLI: %bclaude-server --help%b\n" "$BLUE" "$NC"
    printf "• Check API connectivity: %bcurl -f http://%s:5000/health%b\n" "$BLUE" "$primary_ip" "$NC"
    printf "• Check port usage: %bsudo netstat -tlnp | grep -E ':(80|443|5000|8080|8443)'%b\n\n" "$BLUE" "$NC"
    
    printf "%b🎯 Quick Start Summary:%b\n" "$GREEN" "$NC"
    [ -f "/tmp/claude-path-setup.sh" ] && printf "%bIf 'claude' command is not found, first run:%b %bsource /tmp/claude-path-setup.sh%b\n\n" "$YELLOW" "$NC" "$BLUE" "$NC"
    
    printf "%bEssential Steps:%b\n" "$YELLOW" "$NC"
    printf "1. %bclaude /login%b %b# Authenticate with Claude AI%b\n" "$BLUE" "$NC" "$GREEN" "$NC"
    printf "2. %bclaude --dangerously-skip-permissions%b %b# Allow server usage%b\n" "$BLUE" "$NC" "$GREEN" "$NC"
    printf "3. %bsudo systemctl start claude-batch-server%b %b# Start the service%b\n" "$BLUE" "$NC" "$GREEN" "$NC"
    printf "4. %bclaude-server user add myuser mypassword%b %b# Create your first user%b\n" "$BLUE" "$NC" "$GREEN" "$NC"
    printf "5. %bcurl -f http://%s:5000/health%b %b# Verify it's running%b\n\n" "$BLUE" "$primary_ip" "$NC" "$GREEN" "$NC"
    
    printf "%bAccess Your Server:%b\n" "$YELLOW" "$NC"
    printf "• API: %bhttp://%s%s:%s%b\n" "$BLUE" "$primary_ip" "$([ "$PRODUCTION_MODE" == "true" ] && echo "s")" "$([ "$PRODUCTION_MODE" == "true" ] && echo "443" || echo "5000")" "$NC"
    printf "• Swagger UI: %bhttp://%s%s:%s/swagger%b\n" "$BLUE" "$primary_ip" "$([ "$PRODUCTION_MODE" == "true" ] && echo "s")" "$([ "$PRODUCTION_MODE" == "true" ] && echo "443" || echo "5000")" "$NC"
    printf "• CLI Login: %bclaude-server auth login --server-url http://%s%s:%s%b\n\n" "$BLUE" "$primary_ip" "$([ "$PRODUCTION_MODE" == "true" ] && echo "s")" "$([ "$PRODUCTION_MODE" == "true" ] && echo "443" || echo "5000")" "$NC"
    
    printf "%bService Management:%b\n" "$YELLOW" "$NC"
    printf "• Status: %bsudo systemctl status claude-batch-server%b\n" "$BLUE" "$NC"
    printf "• Logs: %bsudo journalctl -u claude-batch-server -f%b\n" "$BLUE" "$NC"
    printf "• Restart: %bsudo systemctl restart claude-batch-server%b\n\n" "$BLUE" "$NC"
    
    printf "%b🆘 Need Help?%b\n" "$YELLOW" "$NC"
    printf "• Check the installation log: %b%s%b\n" "$BLUE" "$LOG_FILE" "$NC"
    printf "• Review configuration: %b/etc/claude-batch-server.env%b\n" "$BLUE" "$NC"
    printf "• Visit project documentation for detailed setup guides\n\n"
}

# Parse command line arguments
parse_arguments() {
    while [[ $# -gt 0 ]]; do
        case $1 in
            --production)
                PRODUCTION_MODE=true
                log "Production mode enabled"
                shift
                ;;
            --development|--dev)
                DEVELOPMENT_MODE=true
                log "Development mode enabled"
                shift
                ;;
            --ssl-country)
                SSL_COUNTRY="$2"
                shift 2
                ;;
            --ssl-state)
                SSL_STATE="$2"
                shift 2
                ;;
            --ssl-city)
                SSL_CITY="$2"
                shift 2
                ;;
            --ssl-org)
                SSL_ORG="$2"
                shift 2
                ;;
            --ssl-ou)
                SSL_OU="$2"
                shift 2
                ;;
            --ssl-cn)
                SSL_CN="$2"
                shift 2
                ;;
            --dry-run)
                DRY_RUN_MODE=true
                log "Dry-run mode enabled - no changes will be made"
                shift
                ;;
            --help|-h)
                print_help
                exit 0
                ;;
            *)
                error "Unknown option: $1"
                print_help
                exit 1
                ;;
        esac
    done
    
    # Default to development mode if neither is specified
    if [[ "$PRODUCTION_MODE" != "true" && "$DEVELOPMENT_MODE" != "true" ]]; then
        DEVELOPMENT_MODE=true
        log "Defaulting to development mode"
    fi
}

# Print help information
print_help() {
    printf "\n%bClaude Batch Server Installation Script%b\n\n" "$GREEN" "$NC"
    printf "%b⚠️  IMPORTANT: Do NOT run this script with sudo!%b\n" "$RED" "$NC"
    printf "%bRun as a regular user. The script will prompt for sudo when needed.%b\n\n" "$YELLOW" "$NC"
    printf "%bUsage:%b\n" "$YELLOW" "$NC"
    printf "  %s [OPTIONS]\n\n" "$0"
    printf "%bOptions:%b\n" "$YELLOW" "$NC"
    cat << EOF
  --production              Install in production mode with nginx, SSL, and firewall
  --development, --dev      Install in development mode (default)
  
  --ssl-country CODE        SSL certificate country code (2 letters)
  --ssl-state STATE         SSL certificate state/province
  --ssl-city CITY           SSL certificate city
  --ssl-org ORGANIZATION    SSL certificate organization
  --ssl-ou UNIT            SSL certificate organizational unit
  --ssl-cn HOSTNAME        SSL certificate common name (hostname/FQDN)
  
  --dry-run                Analyze current environment and show actions that would be taken
  --help, -h               Show this help message

$(echo -e "${YELLOW}Examples:${NC}")
  # Development installation
  $0 --development
  
  # Production installation with interactive SSL setup
  $0 --production
  
  # Production installation with SSL parameters
  $0 --production --ssl-cn server.example.com --ssl-org "My Company"
  
  # Dry-run analysis without making changes
  $0 --dry-run --production

$(echo -e "${YELLOW}Modes:${NC}")
  $(echo -e "${GREEN}Development:${NC}") Installs basic components without nginx/SSL/firewall
  $(echo -e "${GREEN}Production:${NC}")  Full installation with nginx, SSL certificates, and firewall

EOF
}

# Main installation function
main() {
    # Check if running with sudo (which we don't want)
    if [[ $EUID -eq 0 ]]; then
        error "This script should NOT be run with sudo!"
        error "Please run as a regular user: ./install.sh $*"
        error "The script will prompt for sudo when needed for specific operations."
        exit 1
    fi
    
    log "Starting Claude Batch Server installation..."
    
    # Parse command line arguments first to set DRY_RUN_MODE
    parse_arguments "$@"
    
    # Only show log file in non-dry-run mode and ensure it's writable
    if [[ "$DRY_RUN_MODE" != "true" ]]; then
        # Create log file with proper permissions
        touch "$LOG_FILE" 2>/dev/null || LOG_FILE="/tmp/claude-batch-install-fallback.log"
        log "Log file: $LOG_FILE"
    fi
    
    # Check prerequisites
    detect_os
    
    # Handle dry-run mode
    if [[ "$DRY_RUN_MODE" == "true" ]]; then
        perform_dry_run
        exit 0
    fi
    
    # Check sudo availability (skip in dry-run mode)
    check_sudo_available
    
    # Install base components
    log "Installing base components..."
    install_prerequisites
    install_dotnet
    install_docker
    install_claude_cli
    install_pipx
    install_code_indexer
    configure_cow
    setup_logging
    build_and_deploy
    install_claude_server_cli
    
    # Production-specific components
    if [[ "$PRODUCTION_MODE" == "true" ]]; then
        log "Installing production components..."
        install_nginx
        generate_ssl_cert
        configure_nginx
        configure_firewall
    fi
    
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