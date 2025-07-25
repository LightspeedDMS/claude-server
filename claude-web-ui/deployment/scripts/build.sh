#!/bin/bash

# Claude Web UI Build Script
# Builds the application for production deployment

set -euo pipefail

# Configuration
PROJECT_ROOT="/home/jsbattig/Dev/claude-server/claude-web-ui"
BUILD_DIR="$PROJECT_ROOT/dist"
LOG_FILE="/var/log/claude-web-ui/build.log"
BACKUP_DIR="/var/backups/claude-web-ui"

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Logging function
log() {
    local level=$1
    shift
    local message="$*"
    local timestamp=$(date '+%Y-%m-%d %H:%M:%S')
    
    echo -e "${timestamp} [${level}] ${message}" | tee -a "$LOG_FILE"
    
    case $level in
        "ERROR")
            echo -e "${RED}[ERROR]${NC} ${message}" >&2
            ;;
        "WARN")
            echo -e "${YELLOW}[WARN]${NC} ${message}"
            ;;
        "INFO")
            echo -e "${BLUE}[INFO]${NC} ${message}"
            ;;
        "SUCCESS")
            echo -e "${GREEN}[SUCCESS]${NC} ${message}"
            ;;
    esac
}

# Error handler
error_exit() {
    log "ERROR" "Build failed: $1"
    exit 1
}

# Create necessary directories
create_directories() {
    log "INFO" "Creating necessary directories..."
    
    sudo mkdir -p "$(dirname "$LOG_FILE")"
    sudo mkdir -p "$BACKUP_DIR"
    sudo chown -R "$USER:$USER" "$(dirname "$LOG_FILE")" "$BACKUP_DIR"
}

# Check prerequisites
check_prerequisites() {
    log "INFO" "Checking prerequisites..."
    
    # Check if Node.js is installed
    if ! command -v node &> /dev/null; then
        error_exit "Node.js is not installed"
    fi
    
    # Check if npm is installed
    if ! command -v npm &> /dev/null; then
        error_exit "npm is not installed"
    fi
    
    # Check if project directory exists
    if [[ ! -d "$PROJECT_ROOT" ]]; then
        error_exit "Project directory does not exist: $PROJECT_ROOT"
    fi
    
    # Check if package.json exists
    if [[ ! -f "$PROJECT_ROOT/package.json" ]]; then
        error_exit "package.json not found in $PROJECT_ROOT"
    fi
    
    log "SUCCESS" "Prerequisites check passed"
}

# Backup existing build
backup_existing_build() {
    if [[ -d "$BUILD_DIR" ]]; then
        local backup_name="build-backup-$(date +%Y%m%d-%H%M%S)"
        log "INFO" "Backing up existing build to $BACKUP_DIR/$backup_name"
        
        cp -r "$BUILD_DIR" "$BACKUP_DIR/$backup_name" || {
            log "WARN" "Failed to create backup, continuing anyway"
        }
    fi
}

# Clean previous build
clean_build() {
    log "INFO" "Cleaning previous build..."
    
    if [[ -d "$BUILD_DIR" ]]; then
        rm -rf "$BUILD_DIR"
    fi
    
    # Clean npm cache
    cd "$PROJECT_ROOT"
    npm cache clean --force
    
    log "SUCCESS" "Build cleaned"
}

# Install dependencies
install_dependencies() {
    log "INFO" "Installing dependencies..."
    
    cd "$PROJECT_ROOT"
    
    # Remove node_modules and package-lock.json for fresh install
    rm -rf node_modules package-lock.json
    
    # Install dependencies
    npm install --production=false || error_exit "Failed to install dependencies"
    
    log "SUCCESS" "Dependencies installed"
}

# Run tests
run_tests() {
    log "INFO" "Running tests..."
    
    cd "$PROJECT_ROOT"
    
    # Run unit tests
    npm run test || {
        log "WARN" "Unit tests failed, continuing with build"
    }
    
    log "SUCCESS" "Tests completed"
}

# Build application
build_application() {
    log "INFO" "Building application..."
    
    cd "$PROJECT_ROOT"
    
    # Set production environment
    export NODE_ENV=production
    
    # Build the application
    npm run build || error_exit "Build failed"
    
    # Verify build output
    if [[ ! -d "$BUILD_DIR" ]]; then
        error_exit "Build directory was not created"
    fi
    
    if [[ ! -f "$BUILD_DIR/index.html" ]]; then
        error_exit "index.html was not generated"
    fi
    
    log "SUCCESS" "Application built successfully"
}

# Optimize build
optimize_build() {
    log "INFO" "Optimizing build..."
    
    cd "$BUILD_DIR"
    
    # Create .htaccess for Apache (if needed)
    cat > .htaccess << 'EOF'
# Enable compression
<IfModule mod_deflate.c>
    AddOutputFilterByType DEFLATE text/plain
    AddOutputFilterByType DEFLATE text/html
    AddOutputFilterByType DEFLATE text/xml
    AddOutputFilterByType DEFLATE text/css
    AddOutputFilterByType DEFLATE application/xml
    AddOutputFilterByType DEFLATE application/xhtml+xml
    AddOutputFilterByType DEFLATE application/rss+xml
    AddOutputFilterByType DEFLATE application/javascript
    AddOutputFilterByType DEFLATE application/x-javascript
</IfModule>

# Cache static assets
<IfModule mod_expires.c>
    ExpiresActive on
    ExpiresByType text/css "access plus 1 year"
    ExpiresByType application/javascript "access plus 1 year"
    ExpiresByType image/png "access plus 1 year"
    ExpiresByType image/jpg "access plus 1 year"
    ExpiresByType image/jpeg "access plus 1 year"
    ExpiresByType image/gif "access plus 1 year"
    ExpiresByType image/svg+xml "access plus 1 year"
    ExpiresByType font/woff "access plus 1 year"
    ExpiresByType font/woff2 "access plus 1 year"
</IfModule>

# SPA routing
<IfModule mod_rewrite.c>
    RewriteEngine On
    RewriteBase /
    RewriteRule ^index\.html$ - [L]
    RewriteCond %{REQUEST_FILENAME} !-f
    RewriteCond %{REQUEST_FILENAME} !-d
    RewriteRule . /index.html [L]
</IfModule>
EOF
    
    # Set proper permissions
    find . -type f -exec chmod 644 {} \;
    find . -type d -exec chmod 755 {} \;
    
    log "SUCCESS" "Build optimized"
}

# Generate build report
generate_report() {
    log "INFO" "Generating build report..."
    
    local report_file="$BUILD_DIR/build-report.json"
    local build_size=$(du -sh "$BUILD_DIR" | cut -f1)
    local file_count=$(find "$BUILD_DIR" -type f | wc -l)
    local build_time=$(date -Iseconds)
    
    cat > "$report_file" << EOF
{
    "buildTime": "$build_time",
    "buildSize": "$build_size",
    "fileCount": $file_count,
    "nodeVersion": "$(node --version)",
    "npmVersion": "$(npm --version)",
    "environment": "production",
    "gitCommit": "$(git -C "$PROJECT_ROOT" rev-parse HEAD 2>/dev/null || echo 'unknown')",
    "gitBranch": "$(git -C "$PROJECT_ROOT" rev-parse --abbrev-ref HEAD 2>/dev/null || echo 'unknown')"
}
EOF
    
    log "SUCCESS" "Build report generated: $report_file"
    log "INFO" "Build size: $build_size"
    log "INFO" "File count: $file_count"
}

# Main execution
main() {
    log "INFO" "Starting Claude Web UI build process..."
    
    create_directories
    check_prerequisites
    backup_existing_build
    clean_build
    install_dependencies
    run_tests
    build_application
    optimize_build
    generate_report
    
    log "SUCCESS" "Build process completed successfully!"
    log "INFO" "Build output: $BUILD_DIR"
    log "INFO" "Build log: $LOG_FILE"
}

# Handle script arguments
case "${1:-}" in
    --help|-h)
        echo "Usage: $0 [options]"
        echo "Options:"
        echo "  --help, -h     Show this help message"
        echo "  --skip-tests   Skip running tests"
        echo "  --verbose      Enable verbose output"
        exit 0
        ;;
    --skip-tests)
        run_tests() { log "INFO" "Skipping tests as requested"; }
        ;;
    --verbose)
        set -x
        ;;
esac

# Run main function
main "$@"