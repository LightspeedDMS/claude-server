#!/bin/bash

# Claude Web UI Deployment Script
# Deploys the built application to NGINX web directory

set -euo pipefail

# Configuration
PROJECT_ROOT="/home/jsbattig/Dev/claude-server/claude-web-ui"
BUILD_DIR="$PROJECT_ROOT/dist"
NGINX_DIR="/var/www/claude-web-ui"
LOG_FILE="/var/log/claude-web-ui/deploy.log"
BACKUP_DIR="/var/backups/claude-web-ui"
NGINX_CONFIG_SOURCE="$PROJECT_ROOT/deployment/nginx"
NGINX_CONFIG_DIR="/etc/nginx/sites-available"
NGINX_ENABLED_DIR="/etc/nginx/sites-enabled"

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Environment (production or development)
ENVIRONMENT="${1:-production}"

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
    log "ERROR" "Deployment failed: $1"
    
    # Attempt rollback if backup exists
    if [[ -d "$BACKUP_DIR/last-working" ]]; then
        log "INFO" "Attempting automatic rollback..."
        rollback_deployment
    fi
    
    exit 1
}

# Check if running as root or with sudo
check_permissions() {
    if [[ $EUID -ne 0 ]]; then
        log "ERROR" "This script must be run as root or with sudo"
        exit 1
    fi
}

# Check prerequisites
check_prerequisites() {
    log "INFO" "Checking prerequisites..."
    
    # Check if NGINX is installed
    if ! command -v nginx &> /dev/null; then
        error_exit "NGINX is not installed"
    fi
    
    # Check if build directory exists
    if [[ ! -d "$BUILD_DIR" ]]; then
        error_exit "Build directory does not exist: $BUILD_DIR. Run build.sh first."
    fi
    
    # Check if index.html exists in build
    if [[ ! -f "$BUILD_DIR/index.html" ]]; then
        error_exit "index.html not found in build directory. Run build.sh first."
    fi
    
    log "SUCCESS" "Prerequisites check passed"
}

# Create necessary directories
create_directories() {
    log "INFO" "Creating necessary directories..."
    
    mkdir -p "$(dirname "$LOG_FILE")"
    mkdir -p "$BACKUP_DIR"
    mkdir -p "$NGINX_DIR"
    mkdir -p "$NGINX_DIR/dist"
    
    # Set proper ownership for web directory
    chown -R www-data:www-data "$NGINX_DIR"
}

# Backup current deployment
backup_current_deployment() {
    log "INFO" "Backing up current deployment..."
    
    if [[ -d "$NGINX_DIR/dist" ]] && [[ "$(ls -A "$NGINX_DIR/dist" 2>/dev/null)" ]]; then
        # Create timestamped backup
        local backup_name="deploy-backup-$(date +%Y%m%d-%H%M%S)"
        cp -r "$NGINX_DIR/dist" "$BACKUP_DIR/$backup_name"
        
        # Update last-working backup
        rm -rf "$BACKUP_DIR/last-working"
        cp -r "$NGINX_DIR/dist" "$BACKUP_DIR/last-working"
        
        log "SUCCESS" "Current deployment backed up to $BACKUP_DIR/$backup_name"
    else
        log "INFO" "No existing deployment to backup"
    fi
}

# Deploy files
deploy_files() {
    log "INFO" "Deploying files to $NGINX_DIR/dist..."
    
    # Remove existing files
    rm -rf "$NGINX_DIR/dist"/*
    
    # Copy new build
    cp -r "$BUILD_DIR"/* "$NGINX_DIR/dist/"
    
    # Set proper permissions
    chown -R www-data:www-data "$NGINX_DIR/dist"
    find "$NGINX_DIR/dist" -type f -exec chmod 644 {} \;
    find "$NGINX_DIR/dist" -type d -exec chmod 755 {} \;
    
    log "SUCCESS" "Files deployed successfully"
}

# Configure NGINX
configure_nginx() {
    log "INFO" "Configuring NGINX for $ENVIRONMENT environment..."
    
    local config_file
    if [[ "$ENVIRONMENT" == "development" ]]; then
        config_file="claude-web-ui-dev.conf"
    else
        config_file="claude-web-ui.conf"
    fi
    
    # Copy NGINX configuration
    if [[ -f "$NGINX_CONFIG_SOURCE/$config_file" ]]; then
        cp "$NGINX_CONFIG_SOURCE/$config_file" "$NGINX_CONFIG_DIR/claude-web-ui"
        
        # Enable the site
        ln -sf "$NGINX_CONFIG_DIR/claude-web-ui" "$NGINX_ENABLED_DIR/claude-web-ui"
        
        log "SUCCESS" "NGINX configuration deployed: $config_file"
    else
        error_exit "NGINX configuration file not found: $NGINX_CONFIG_SOURCE/$config_file"
    fi
}

# Test NGINX configuration
test_nginx_config() {
    log "INFO" "Testing NGINX configuration..."
    
    if nginx -t; then
        log "SUCCESS" "NGINX configuration test passed"
    else
        error_exit "NGINX configuration test failed"
    fi
}

# Reload NGINX
reload_nginx() {
    log "INFO" "Reloading NGINX..."
    
    if systemctl reload nginx; then
        log "SUCCESS" "NGINX reloaded successfully"
    else
        error_exit "Failed to reload NGINX"
    fi
}

# Verify deployment
verify_deployment() {
    log "INFO" "Verifying deployment..."
    
    # Check if NGINX is running
    if ! systemctl is-active --quiet nginx; then
        error_exit "NGINX is not running"
    fi
    
    # Check if site is accessible
    local test_url="http://localhost/health"
    if [[ "$ENVIRONMENT" == "production" ]]; then
        test_url="https://localhost/health"
    fi
    
    if curl -k -f -s "$test_url" > /dev/null; then
        log "SUCCESS" "Health check passed"
    else
        log "WARN" "Health check failed, but continuing deployment"
    fi
    
    # Verify main files exist
    if [[ -f "$NGINX_DIR/dist/index.html" ]]; then
        log "SUCCESS" "Main files verified"
    else
        error_exit "Main files missing after deployment"
    fi
}

# Rollback deployment
rollback_deployment() {
    log "INFO" "Rolling back deployment..."
    
    if [[ -d "$BACKUP_DIR/last-working" ]]; then
        rm -rf "$NGINX_DIR/dist"/*
        cp -r "$BACKUP_DIR/last-working"/* "$NGINX_DIR/dist/"
        
        # Set proper permissions
        chown -R www-data:www-data "$NGINX_DIR/dist"
        find "$NGINX_DIR/dist" -type f -exec chmod 644 {} \;
        find "$NGINX_DIR/dist" -type d -exec chmod 755 {} \;
        
        # Reload NGINX
        systemctl reload nginx
        
        log "SUCCESS" "Rollback completed"
    else
        log "ERROR" "No backup found for rollback"
    fi
}

# Clean old backups
clean_old_backups() {
    log "INFO" "Cleaning old backups..."
    
    # Keep only the last 10 backups
    local backup_count=$(find "$BACKUP_DIR" -name "deploy-backup-*" -type d | wc -l)
    if [[ $backup_count -gt 10 ]]; then
        find "$BACKUP_DIR" -name "deploy-backup-*" -type d | sort | head -n $((backup_count - 10)) | xargs rm -rf
        log "INFO" "Cleaned old backups, kept latest 10"
    fi
}

# Generate deployment report
generate_deployment_report() {
    log "INFO" "Generating deployment report..."
    
    local report_file="$NGINX_DIR/deployment-report.json"
    local deployment_time=$(date -Iseconds)
    local deployed_size=$(du -sh "$NGINX_DIR/dist" | cut -f1)
    
    cat > "$report_file" << EOF
{
    "deploymentTime": "$deployment_time",
    "environment": "$ENVIRONMENT",
    "deployedSize": "$deployed_size",
    "nginxVersion": "$(nginx -v 2>&1 | cut -d' ' -f3)",
    "deploymentPath": "$NGINX_DIR/dist",
    "gitCommit": "$(git -C "$PROJECT_ROOT" rev-parse HEAD 2>/dev/null || echo 'unknown')",
    "gitBranch": "$(git -C "$PROJECT_ROOT" rev-parse --abbrev-ref HEAD 2>/dev/null || echo 'unknown')",
    "deployedBy": "$SUDO_USER",
    "status": "success"
}
EOF
    
    chown www-data:www-data "$report_file"
    log "SUCCESS" "Deployment report generated: $report_file"
}

# Show deployment status
show_status() {
    log "INFO" "Deployment Status:"
    echo -e "${BLUE}Environment:${NC} $ENVIRONMENT"
    echo -e "${BLUE}NGINX Status:${NC} $(systemctl is-active nginx)"
    echo -e "${BLUE}Site Directory:${NC} $NGINX_DIR/dist"
    echo -e "${BLUE}Config File:${NC} $NGINX_ENABLED_DIR/claude-web-ui"
    
    if [[ "$ENVIRONMENT" == "development" ]]; then
        echo -e "${BLUE}URL:${NC} http://localhost"
    else
        echo -e "${BLUE}URL:${NC} https://localhost"
    fi
    
    echo -e "${BLUE}Log File:${NC} $LOG_FILE"
}

# Main execution
main() {
    log "INFO" "Starting Claude Web UI deployment process..."
    log "INFO" "Environment: $ENVIRONMENT"
    
    check_permissions
    check_prerequisites
    create_directories
    backup_current_deployment
    deploy_files
    configure_nginx
    test_nginx_config
    reload_nginx
    verify_deployment
    clean_old_backups
    generate_deployment_report
    show_status
    
    log "SUCCESS" "Deployment completed successfully!"
}

# Handle script arguments
case "${1:-production}" in
    production|prod)
        ENVIRONMENT="production"
        ;;
    development|dev)
        ENVIRONMENT="development"
        ;;
    rollback)
        check_permissions
        rollback_deployment
        exit 0
        ;;
    status)
        show_status
        exit 0
        ;;
    --help|-h)
        echo "Usage: $0 [environment|command]"
        echo ""
        echo "Environments:"
        echo "  production     Deploy for production (default)"
        echo "  development    Deploy for development"
        echo ""
        echo "Commands:"
        echo "  rollback       Rollback to last working deployment"
        echo "  status         Show deployment status"
        echo "  --help, -h     Show this help message"
        echo ""
        echo "Examples:"
        echo "  sudo $0 production"
        echo "  sudo $0 development"
        echo "  sudo $0 rollback"
        exit 0
        ;;
    *)
        log "ERROR" "Invalid environment or command: $1"
        echo "Use --help for usage information"
        exit 1
        ;;
esac

# Run main function
main