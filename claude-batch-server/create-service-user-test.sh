#!/bin/bash

# Test script for service user creation
set -euo pipefail

SERVICE_USER="claude-batch-server"
SERVICE_GROUP="claude-batch-server"
SERVICE_HOME="/var/lib/claude-batch-server"

log() {
    echo "[INFO] $1"
}

error() {
    echo "[ERROR] $1" >&2
}

# Create service user
create_service_user() {
    log "Creating service user '$SERVICE_USER'..."
    
    if id "$SERVICE_USER" &>/dev/null; then
        log "Service user '$SERVICE_USER' already exists"
        return 0
    fi
    
    # Create system user with home directory
    sudo useradd --system \
        --home-dir "$SERVICE_HOME" \
        --create-home \
        --shell /bin/bash \
        --comment "Claude Batch Server Service User" \
        "$SERVICE_USER"
        
    # Set appropriate permissions
    sudo chmod 750 "$SERVICE_HOME"
    
    log "Service user '$SERVICE_USER' created successfully"
}

# Configure service user groups
configure_service_user_groups() {
    log "Configuring service user groups..."
    
    # Add to shadow group for authentication
    if ! groups "$SERVICE_USER" | grep -q "\bshadow\b"; then
        log "Adding service user '$SERVICE_USER' to shadow group for authentication access..."
        sudo usermod -a -G shadow "$SERVICE_USER"
        log "Service user '$SERVICE_USER' added to shadow group"
    else
        log "Service user '$SERVICE_USER' already in shadow group"
    fi
    
    # Add to docker group for cidx (if docker group exists)
    if getent group docker >/dev/null 2>&1; then
        if ! groups "$SERVICE_USER" | grep -q "\bdocker\b"; then
            log "Adding service user '$SERVICE_USER' to docker group for cidx access..."
            sudo usermod -aG docker "$SERVICE_USER"
            log "Service user '$SERVICE_USER' added to docker group"
        else
            log "Service user '$SERVICE_USER' already in docker group"
        fi
    else
        log "Docker group not found, skipping docker group assignment"
    fi
    
    log "Service user groups configured"
}

# Configure sudo impersonation
configure_sudo_impersonation() {
    log "Configuring sudo rules for user impersonation..."
    
    local sudoers_file="/etc/sudoers.d/claude-batch-server"
    
    sudo tee "$sudoers_file" > /dev/null << 'EOF'
# Claude Batch Server user impersonation rules
# Allow service user to impersonate regular users (UID >= 1000)
claude-batch-server ALL=(#>=1000) NOPASSWD: ALL

# Allow user existence checks
claude-batch-server ALL=(root) NOPASSWD: /usr/bin/id *

# Prevent impersonation of system users
Defaults!ALL !rootpw, !runaspw, !targetpw
EOF

    sudo chmod 440 "$sudoers_file"
    
    # Validate sudoers configuration
    if sudo visudo -cf "$sudoers_file"; then
        log "Sudo configuration created successfully"
    else
        error "Invalid sudo configuration"
        sudo rm -f "$sudoers_file"
        exit 1
    fi
}

# Main execution
main() {
    log "Creating service user for testing..."
    
    create_service_user
    configure_service_user_groups
    configure_sudo_impersonation
    
    log "Service user setup completed"
    log "Verifying service user configuration..."
    
    # Verify user exists
    if id "$SERVICE_USER" &>/dev/null; then
        log "✓ Service user exists"
        log "User info: $(id "$SERVICE_USER")"
        log "Groups: $(groups "$SERVICE_USER")"
    else
        error "✗ Service user creation failed"
        exit 1
    fi
    
    # Verify sudo configuration
    if [[ -f "/etc/sudoers.d/claude-batch-server" ]]; then
        log "✓ Sudo configuration exists"
    else
        error "✗ Sudo configuration missing"
        exit 1
    fi
}

main "$@"