#!/bin/bash

# Claude Web UI Backup and Rollback Management Script
# Provides comprehensive backup and rollback functionality

set -euo pipefail

# Configuration
PROJECT_ROOT="/home/jsbattig/Dev/claude-server/claude-web-ui"
NGINX_DIR="/var/www/claude-web-ui"
BACKUP_DIR="/var/backups/claude-web-ui"
LOG_FILE="/var/log/claude-web-ui/backup-rollback.log"
NGINX_CONFIG_DIR="/etc/nginx/sites-available"
SSL_DIR="/etc/ssl"

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
    
    mkdir -p "$(dirname "$LOG_FILE")"
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
    log "ERROR" "Operation failed: $1"
    exit 1
}

# Check if running as root
check_permissions() {
    if [[ $EUID -ne 0 ]]; then
        log "ERROR" "This script must be run as root or with sudo"
        exit 1
    fi
}

# Create full system backup
create_full_backup() {
    local backup_name="${1:-full-backup-$(date +%Y%m%d-%H%M%S)}"
    local backup_path="$BACKUP_DIR/$backup_name"
    
    log "INFO" "Creating full system backup: $backup_name"
    
    # Create backup directory
    mkdir -p "$backup_path"
    
    # Backup web files
    if [[ -d "$NGINX_DIR/dist" ]]; then
        log "INFO" "Backing up web files..."
        cp -r "$NGINX_DIR/dist" "$backup_path/web-files"
        log "SUCCESS" "Web files backed up"
    else
        log "WARN" "No web files found to backup"
    fi
    
    # Backup NGINX configuration
    if [[ -f "$NGINX_CONFIG_DIR/claude-web-ui" ]]; then
        log "INFO" "Backing up NGINX configuration..."
        mkdir -p "$backup_path/nginx"
        cp "$NGINX_CONFIG_DIR/claude-web-ui" "$backup_path/nginx/"
        log "SUCCESS" "NGINX configuration backed up"
    else
        log "WARN" "No NGINX configuration found to backup"
    fi
    
    # Backup SSL certificates
    if [[ -f "/etc/ssl/certs/claude-web-ui.crt" ]]; then
        log "INFO" "Backing up SSL certificates..."
        mkdir -p "$backup_path/ssl"
        cp "/etc/ssl/certs/claude-web-ui.crt" "$backup_path/ssl/" 2>/dev/null || true
        cp "/etc/ssl/private/claude-web-ui.key" "$backup_path/ssl/" 2>/dev/null || true
        log "SUCCESS" "SSL certificates backed up"
    fi
    
    # Backup logs
    if [[ -d "/var/log/claude-web-ui" ]]; then
        log "INFO" "Backing up logs..."
        mkdir -p "$backup_path/logs"
        cp -r "/var/log/claude-web-ui"/* "$backup_path/logs/" 2>/dev/null || true
        log "SUCCESS" "Logs backed up"
    fi
    
    # Create backup manifest
    create_backup_manifest "$backup_path"
    
    # Compress backup
    log "INFO" "Compressing backup..."
    cd "$BACKUP_DIR"
    tar -czf "${backup_name}.tar.gz" "$backup_name"
    rm -rf "$backup_name"
    
    log "SUCCESS" "Full backup created: $BACKUP_DIR/${backup_name}.tar.gz"
    return 0
}

# Create backup manifest
create_backup_manifest() {
    local backup_path=$1
    local manifest_file="$backup_path/manifest.json"
    
    log "INFO" "Creating backup manifest..."
    
    cat > "$manifest_file" << EOF
{
    "backup_date": "$(date -Iseconds)",
    "backup_type": "full",
    "hostname": "$(hostname)",
    "created_by": "$SUDO_USER",
    "components": {
        "web_files": $([ -d "$backup_path/web-files" ] && echo "true" || echo "false"),
        "nginx_config": $([ -d "$backup_path/nginx" ] && echo "true" || echo "false"),
        "ssl_certificates": $([ -d "$backup_path/ssl" ] && echo "true" || echo "false"),
        "logs": $([ -d "$backup_path/logs" ] && echo "true" || echo "false")
    },
    "system_info": {
        "nginx_version": "$(nginx -v 2>&1 | cut -d' ' -f3)",
        "disk_usage": "$(df /var/www/claude-web-ui 2>/dev/null | awk 'NR==2 {print $5}' || echo 'N/A')",
        "backup_size": "$(du -sh "$backup_path" | cut -f1)"
    },
    "git_info": {
        "commit": "$(git -C "$PROJECT_ROOT" rev-parse HEAD 2>/dev/null || echo 'unknown')",
        "branch": "$(git -C "$PROJECT_ROOT" rev-parse --abbrev-ref HEAD 2>/dev/null || echo 'unknown')"
    }
}
EOF
    
    log "SUCCESS" "Backup manifest created"
}

# List available backups
list_backups() {
    log "INFO" "Available backups:"
    echo ""
    
    local backup_count=0
    
    # List compressed backups
    for backup in "$BACKUP_DIR"/*.tar.gz; do
        if [[ -f "$backup" ]]; then
            local filename=$(basename "$backup")
            local size=$(du -sh "$backup" | cut -f1)
            local date=$(stat -c %y "$backup" | cut -d' ' -f1,2 | cut -d'.' -f1)
            
            echo -e "${GREEN}$filename${NC}"
            echo -e "  Size: $size"
            echo -e "  Date: $date"
            echo ""
            ((backup_count++))
        fi
    done
    
    # List uncompressed backups
    for backup in "$BACKUP_DIR"/*/; do
        if [[ -d "$backup" && "$backup" != "$BACKUP_DIR/last-working/" ]]; then
            local dirname=$(basename "$backup")
            local size=$(du -sh "$backup" | cut -f1)
            local date=$(stat -c %y "$backup" | cut -d' ' -f1,2 | cut -d'.' -f1)
            
            echo -e "${BLUE}$dirname${NC} (uncompressed)"
            echo -e "  Size: $size"
            echo -e "  Date: $date"
            echo ""
            ((backup_count++))
        fi
    done
    
    # Check last-working backup
    if [[ -d "$BACKUP_DIR/last-working" ]]; then
        local size=$(du -sh "$BACKUP_DIR/last-working" | cut -f1)
        local date=$(stat -c %y "$BACKUP_DIR/last-working" | cut -d' ' -f1,2 | cut -d'.' -f1)
        
        echo -e "${YELLOW}last-working${NC} (automatic)"
        echo -e "  Size: $size"
        echo -e "  Date: $date"
        echo ""
        ((backup_count++))
    fi
    
    if [[ $backup_count -eq 0 ]]; then
        log "WARN" "No backups found"
    else
        log "INFO" "Total backups: $backup_count"
    fi
}

# Restore from backup
restore_backup() {
    local backup_name="$1"
    local backup_file="$BACKUP_DIR/${backup_name}"
    local temp_dir="/tmp/claude-restore-$$"
    
    log "INFO" "Starting restore from backup: $backup_name"
    
    # Check if backup exists
    if [[ -f "${backup_file}.tar.gz" ]]; then
        backup_file="${backup_file}.tar.gz"
        log "INFO" "Found compressed backup: $backup_file"
    elif [[ -d "$backup_file" ]]; then
        log "INFO" "Found uncompressed backup: $backup_file"
    elif [[ "$backup_name" == "last-working" && -d "$BACKUP_DIR/last-working" ]]; then
        backup_file="$BACKUP_DIR/last-working"
        log "INFO" "Found last-working backup: $backup_file"
    else
        error_exit "Backup not found: $backup_name"
    fi
    
    # Create temporary directory
    mkdir -p "$temp_dir"
    
    # Extract if compressed
    if [[ "$backup_file" == *.tar.gz ]]; then
        log "INFO" "Extracting compressed backup..."
        tar -xzf "$backup_file" -C "$temp_dir"
        backup_file="$temp_dir/$(basename "$backup_file" .tar.gz)"
    fi
    
    # Verify backup integrity
    if [[ ! -f "$backup_file/manifest.json" ]]; then
        log "WARN" "Backup manifest not found, proceeding anyway"
    else
        log "INFO" "Backup manifest found, verifying..."
        local backup_date=$(grep '"backup_date"' "$backup_file/manifest.json" | cut -d'"' -f4)
        log "INFO" "Backup date: $backup_date"
    fi
    
    # Create backup of current state before restore
    log "INFO" "Creating safety backup before restore..."
    create_full_backup "pre-restore-$(date +%Y%m%d-%H%M%S)"
    
    # Stop NGINX temporarily
    log "INFO" "Stopping NGINX for restore..."
    systemctl stop nginx
    
    # Restore web files
    if [[ -d "$backup_file/web-files" ]]; then
        log "INFO" "Restoring web files..."
        rm -rf "$NGINX_DIR/dist"/*
        cp -r "$backup_file/web-files"/* "$NGINX_DIR/dist/"
        chown -R www-data:www-data "$NGINX_DIR/dist"
        find "$NGINX_DIR/dist" -type f -exec chmod 644 {} \;
        find "$NGINX_DIR/dist" -type d -exec chmod 755 {} \;
        log "SUCCESS" "Web files restored"
    fi
    
    # Restore NGINX configuration
    if [[ -f "$backup_file/nginx/claude-web-ui" ]]; then
        log "INFO" "Restoring NGINX configuration..."
        cp "$backup_file/nginx/claude-web-ui" "$NGINX_CONFIG_DIR/"
        log "SUCCESS" "NGINX configuration restored"
    fi
    
    # Restore SSL certificates
    if [[ -d "$backup_file/ssl" ]]; then
        log "INFO" "Restoring SSL certificates..."
        cp "$backup_file/ssl/claude-web-ui.crt" "/etc/ssl/certs/" 2>/dev/null || true
        cp "$backup_file/ssl/claude-web-ui.key" "/etc/ssl/private/" 2>/dev/null || true
        chmod 644 "/etc/ssl/certs/claude-web-ui.crt" 2>/dev/null || true
        chmod 600 "/etc/ssl/private/claude-web-ui.key" 2>/dev/null || true
        log "SUCCESS" "SSL certificates restored"
    fi
    
    # Test NGINX configuration
    log "INFO" "Testing NGINX configuration..."
    if nginx -t; then
        log "SUCCESS" "NGINX configuration test passed"
    else
        error_exit "NGINX configuration test failed"
    fi
    
    # Start NGINX
    log "INFO" "Starting NGINX..."
    if systemctl start nginx; then
        log "SUCCESS" "NGINX started successfully"
    else
        error_exit "Failed to start NGINX"
    fi
    
    # Verify restoration
    log "INFO" "Verifying restoration..."
    sleep 2
    
    if curl -k -f -s "http://localhost/health" > /dev/null; then
        log "SUCCESS" "Health check passed after restoration"
    else
        log "WARN" "Health check failed, but restoration completed"
    fi
    
    # Cleanup
    rm -rf "$temp_dir"
    
    log "SUCCESS" "Restore completed successfully from: $backup_name"
}

# Quick rollback to last working deployment
quick_rollback() {
    log "INFO" "Performing quick rollback to last working deployment..."
    
    if [[ -d "$BACKUP_DIR/last-working" ]]; then
        restore_backup "last-working"
    else
        error_exit "No last-working backup found. Cannot perform quick rollback."
    fi
}

# Clean old backups
clean_old_backups() {
    local keep_count="${1:-10}"
    
    log "INFO" "Cleaning old backups, keeping latest $keep_count..."
    
    # Clean compressed backups
    local backup_count=$(find "$BACKUP_DIR" -name "*.tar.gz" -type f | wc -l)
    if [[ $backup_count -gt $keep_count ]]; then
        find "$BACKUP_DIR" -name "*.tar.gz" -type f -printf '%T@ %p\n' | sort -n | head -n $((backup_count - keep_count)) | cut -d' ' -f2- | xargs rm -f
        log "INFO" "Removed $((backup_count - keep_count)) old compressed backups"
    fi
    
    # Clean uncompressed backups (exclude last-working)
    local dir_count=$(find "$BACKUP_DIR" -maxdepth 1 -name "*-backup-*" -type d | wc -l)
    if [[ $dir_count -gt $keep_count ]]; then
        find "$BACKUP_DIR" -maxdepth 1 -name "*-backup-*" -type d -printf '%T@ %p\n' | sort -n | head -n $((dir_count - keep_count)) | cut -d' ' -f2- | xargs rm -rf
        log "INFO" "Removed $((dir_count - keep_count)) old uncompressed backups"
    fi
    
    log "SUCCESS" "Backup cleanup completed"
}

# Backup verification
verify_backup() {
    local backup_name="$1"
    local backup_file="$BACKUP_DIR/${backup_name}"
    
    log "INFO" "Verifying backup: $backup_name"
    
    # Check if backup exists
    if [[ -f "${backup_file}.tar.gz" ]]; then
        backup_file="${backup_file}.tar.gz"
        
        # Test extraction
        local temp_dir="/tmp/claude-verify-$$"
        mkdir -p "$temp_dir"
        
        if tar -tzf "$backup_file" > /dev/null 2>&1; then
            log "SUCCESS" "Compressed backup integrity verified"
        else
            log "ERROR" "Compressed backup is corrupted"
            rm -rf "$temp_dir"
            return 1
        fi
        
        # Extract and verify contents
        tar -xzf "$backup_file" -C "$temp_dir"
        local extracted_dir="$temp_dir/$(basename "$backup_file" .tar.gz)"
        
        # Check manifest
        if [[ -f "$extracted_dir/manifest.json" ]]; then
            log "SUCCESS" "Backup manifest found"
            
            # Parse manifest
            local backup_date=$(grep '"backup_date"' "$extracted_dir/manifest.json" | cut -d'"' -f4)
            local backup_size=$(grep '"backup_size"' "$extracted_dir/manifest.json" | cut -d'"' -f4)
            
            log "INFO" "Backup date: $backup_date"
            log "INFO" "Backup size: $backup_size"
        else
            log "WARN" "Backup manifest missing"
        fi
        
        # Check critical files
        if [[ -d "$extracted_dir/web-files" && -f "$extracted_dir/web-files/index.html" ]]; then
            log "SUCCESS" "Web files verified"
        else
            log "WARN" "Web files missing or incomplete"
        fi
        
        rm -rf "$temp_dir"
        
    elif [[ -d "$backup_file" ]]; then
        log "INFO" "Verifying uncompressed backup..."
        
        # Check manifest
        if [[ -f "$backup_file/manifest.json" ]]; then
            log "SUCCESS" "Backup manifest found"
        else
            log "WARN" "Backup manifest missing"
        fi
        
        # Check critical files
        if [[ -d "$backup_file/web-files" && -f "$backup_file/web-files/index.html" ]]; then
            log "SUCCESS" "Web files verified"
        else
            log "WARN" "Web files missing or incomplete"
        fi
        
    elif [[ "$backup_name" == "last-working" && -d "$BACKUP_DIR/last-working" ]]; then
        log "INFO" "Verifying last-working backup..."
        
        if [[ -f "$BACKUP_DIR/last-working/index.html" ]]; then
            log "SUCCESS" "Last-working backup verified"
        else
            log "ERROR" "Last-working backup is incomplete"
            return 1
        fi
        
    else
        log "ERROR" "Backup not found: $backup_name"
        return 1
    fi
    
    log "SUCCESS" "Backup verification completed"
    return 0
}

# Show backup statistics
show_backup_stats() {
    log "INFO" "Backup Statistics:"
    echo ""
    
    local total_size=0
    local backup_count=0
    
    # Count compressed backups
    for backup in "$BACKUP_DIR"/*.tar.gz; do
        if [[ -f "$backup" ]]; then
            local size_kb=$(du -k "$backup" | cut -f1)
            total_size=$((total_size + size_kb))
            ((backup_count++))
        fi
    done
    
    # Count uncompressed backups
    for backup in "$BACKUP_DIR"/*/; do
        if [[ -d "$backup" ]]; then
            local size_kb=$(du -sk "$backup" | cut -f1)
            total_size=$((total_size + size_kb))
            ((backup_count++))
        fi
    done
    
    local total_size_mb=$((total_size / 1024))
    local disk_usage=$(df "$BACKUP_DIR" | awk 'NR==2 {print $5}')
    local available_space=$(df -h "$BACKUP_DIR" | awk 'NR==2 {print $4}')
    
    echo -e "${BLUE}Total Backups:${NC} $backup_count"
    echo -e "${BLUE}Total Size:${NC} ${total_size_mb}MB"
    echo -e "${BLUE}Disk Usage:${NC} $disk_usage"
    echo -e "${BLUE}Available Space:${NC} $available_space"
    echo -e "${BLUE}Backup Directory:${NC} $BACKUP_DIR"
    echo ""
}

# Show help
show_help() {
    echo "Claude Web UI Backup and Rollback Management"
    echo "============================================"
    echo ""
    echo "Usage: $0 [command] [options]"
    echo ""
    echo "Commands:"
    echo "  backup [name]           Create a full backup (optional custom name)"
    echo "  list                    List all available backups"
    echo "  restore <backup>        Restore from specified backup"
    echo "  rollback                Quick rollback to last working deployment"
    echo "  verify <backup>         Verify backup integrity"
    echo "  clean [count]           Clean old backups (default: keep 10)"
    echo "  stats                   Show backup statistics"
    echo "  --help, -h              Show this help message"
    echo ""
    echo "Examples:"
    echo "  sudo $0 backup                          # Create automatic backup"
    echo "  sudo $0 backup my-custom-backup         # Create named backup"
    echo "  sudo $0 list                            # List all backups"
    echo "  sudo $0 restore backup-20240101-120000  # Restore specific backup"
    echo "  sudo $0 rollback                        # Quick rollback"
    echo "  sudo $0 verify backup-20240101-120000   # Verify backup"
    echo "  sudo $0 clean 5                         # Keep only 5 recent backups"
    echo ""
    echo "Backup Location: $BACKUP_DIR"
    echo "Log File: $LOG_FILE"
}

# Main execution
main() {
    case "${1:-}" in
        backup)
            check_permissions
            create_full_backup "$2"
            ;;
        list)
            list_backups
            ;;
        restore)
            if [[ -z "${2:-}" ]]; then
                log "ERROR" "Backup name required for restore"
                exit 1
            fi
            check_permissions
            restore_backup "$2"
            ;;
        rollback)
            check_permissions
            quick_rollback
            ;;
        verify)
            if [[ -z "${2:-}" ]]; then
                log "ERROR" "Backup name required for verification"
                exit 1
            fi
            verify_backup "$2"
            ;;
        clean)
            check_permissions
            clean_old_backups "${2:-10}"
            ;;
        stats)
            show_backup_stats
            ;;
        --help|-h|help)
            show_help
            ;;
        "")
            log "ERROR" "No command specified"
            show_help
            exit 1
            ;;
        *)
            log "ERROR" "Unknown command: $1"
            show_help
            exit 1
            ;;
    esac
}

# Create backup directory if it doesn't exist
mkdir -p "$BACKUP_DIR"

# Run main function with all arguments
main "$@"