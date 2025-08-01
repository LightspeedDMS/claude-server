#!/bin/bash

# Claude Batch Server User Setup Script
# Creates/updates users with all required access rights for job execution
# Usage: ./setup-user.sh <username> <password> [--update]

set -euo pipefail

# Get script directory and source shared utilities
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$SCRIPT_DIR/shared-utils.sh"

# Alias shared functions to local names for compatibility
log() { shared_log "$@"; }
warn() { shared_warn "$@"; }
error() { shared_error "$@"; }
success() { shared_success "$@"; }

# Script configuration
CLAUDE_GROUP="claude-batch-users"
REQUIRED_GROUPS=("docker" "$CLAUDE_GROUP")




# Logging functions
log() {
    echo -e "${GREEN}[INFO]${NC} $1"
}

warn() {
    echo -e "${YELLOW}[WARN]${NC} $1"
}

error() {
    echo -e "${RED}[ERROR]${NC} $1"
}

debug() {
    echo -e "${BLUE}[DEBUG]${NC} $1"
}

success() {
    echo -e "${GREEN}[SUCCESS]${NC} $1"
}

# Help function
show_help() {
    echo -e "${CYAN}Claude Batch Server User Setup Script${NC}"
    echo ""
    echo -e "${YELLOW}USAGE:${NC}"
    echo "    ./setup-user.sh <username> <password> [--update]"
    echo ""
    echo -e "${YELLOW}PARAMETERS:${NC}"
    echo -e "    ${GREEN}username${NC}     - Username to create or update"
    echo -e "    ${GREEN}password${NC}     - Password for the user (any length/complexity allowed)"
    echo -e "    ${GREEN}--update${NC}     - Update existing user instead of creating new one"
    echo ""
    echo -e "${YELLOW}EXAMPLES:${NC}"
    echo "    ./setup-user.sh alice MySecurePass123!     # Create new user"
    echo "    ./setup-user.sh bob NewPassword456! --update  # Update existing user"
    echo ""
    echo -e "${YELLOW}WHAT THIS SCRIPT DOES:${NC}"
    echo "    • Creates/updates system user with proper shell and home directory"
    echo "    • Sets up required group memberships (docker, claude-batch-users)"
    echo "    • Configures workspace access permissions"
    echo "    • Installs fresh Claude Code and CIDX for the target user"
    echo "    • Sets up sudo impersonation rights for the service user"
    echo "    • Ensures Docker access for CIDX containers"
    echo ""
    echo -e "${YELLOW}REQUIREMENTS:${NC}"
    echo "    • Run with sudo privileges"
    echo "    • Docker must be installed"
    echo "    • Run './run.sh install' first to set up system dependencies"
    echo ""
}

# Validation functions
validate_username() {
    local username="$1"
    if [[ ! "$username" =~ ^[a-z_][a-z0-9_-]*$ ]]; then
        error "Invalid username format. Use lowercase letters, numbers, underscores, and hyphens only."
        return 1
    fi
    if [[ ${#username} -gt 32 ]]; then
        error "Username too long. Maximum 32 characters."
        return 1
    fi
    return 0
}

validate_password() {
    local password="$1"
    # Always return success - we don't enforce password requirements
    return 0
}

assess_password_strength() {
    local password="$1"
    local score=0
    local feedback=()
    
    # Length check
    if [[ ${#password} -ge 12 ]]; then
        score=$((score + 2))
        feedback+=("✓ Length: Strong (${#password} characters)")
    elif [[ ${#password} -ge 8 ]]; then
        score=$((score + 1))
        feedback+=("~ Length: Moderate (${#password} characters)")
    else
        feedback+=("⚠ Length: Weak (${#password} characters, recommend 8+)")
    fi
    
    # Character variety checks
    if [[ "$password" =~ [a-z] ]]; then
        score=$((score + 1))
        feedback+=("✓ Contains lowercase letters")
    else
        feedback+=("⚠ Missing lowercase letters")
    fi
    
    if [[ "$password" =~ [A-Z] ]]; then
        score=$((score + 1))
        feedback+=("✓ Contains uppercase letters")
    else
        feedback+=("⚠ Missing uppercase letters")
    fi
    
    if [[ "$password" =~ [0-9] ]]; then
        score=$((score + 1))
        feedback+=("✓ Contains numbers")
    else
        feedback+=("⚠ Missing numbers")
    fi
    
    if [[ "$password" =~ [^a-zA-Z0-9] ]]; then
        score=$((score + 1))
        feedback+=("✓ Contains special characters")
    else
        feedback+=("⚠ Missing special characters")
    fi
    
    # Overall assessment
    local strength=""
    if [[ $score -ge 5 ]]; then
        strength="${GREEN}Strong${NC}"
    elif [[ $score -ge 3 ]]; then
        strength="${YELLOW}Moderate${NC}"
    else
        strength="${RED}Weak${NC}"
    fi
    
    echo ""
    echo -e "${CYAN}Password Strength Assessment:${NC}"
    echo -e "  Overall: $strength (Score: $score/6)"
    for item in "${feedback[@]}"; do
        echo "    $item"
    done
    echo ""
}

check_prerequisites() {
    log "Checking prerequisites..."
    
    # Check if running as root
    if [[ $EUID -ne 0 ]]; then
        error "This script must be run as root (use sudo)"
        exit 1
    fi
    
    # Check required commands
    local missing_commands=()
    for cmd in useradd usermod chown chmod docker; do
        if ! command -v "$cmd" >/dev/null 2>&1; then
            missing_commands+=("$cmd")
        fi
    done
    
    if [[ ${#missing_commands[@]} -gt 0 ]]; then
        error "Missing required commands: ${missing_commands[*]}"
        error "Please install the missing packages and try again"
        exit 1
    fi
    
    # Check if Docker is running
    if ! systemctl is-active --quiet docker; then
        warn "Docker service is not running. Starting Docker..."
        systemctl start docker || {
            error "Failed to start Docker service"
            exit 1
        }
    fi
    
    success "Prerequisites check passed"
}

# Ensure required groups exist
ensure_groups_exist() {
    log "Ensuring required groups exist..."
    
    for group in "${REQUIRED_GROUPS[@]}"; do
        if ! getent group "$group" >/dev/null 2>&1; then
            log "Creating group: $group"
            groupadd "$group"
            success "Created group: $group"
        else
            debug "Group already exists: $group"
        fi
    done
}

# Get current service user (the one running Claude Batch Server)
get_service_user() {
    # Try to detect service user from common patterns
    local service_user=""
    
    # Check for claude-batch-server user first
    if id "claude-batch-server" >/dev/null 2>&1; then
        service_user="claude-batch-server"
    # Check if there's a non-root user in the claude-batch-users group
    elif getent group "$CLAUDE_GROUP" >/dev/null 2>&1; then
        local group_members
        group_members=$(getent group "$CLAUDE_GROUP" | cut -d: -f4)
        for member in ${group_members//,/ }; do
            if [[ "$member" != "root" ]] && id "$member" >/dev/null 2>&1; then
                service_user="$member"
                break
            fi
        done
    fi
    
    # Fallback: ask for service user
    if [[ -z "$service_user" ]]; then
        warn "Could not automatically detect service user."
        read -p "Enter the username that runs Claude Batch Server: " service_user
        if ! id "$service_user" >/dev/null 2>&1; then
            error "User '$service_user' does not exist"
            exit 1
        fi
    fi
    
    echo "$service_user"
}

# Create or update user
manage_user() {
    local username="$1"
    local password="$2"
    local update_mode="$3"
    
    if id "$username" >/dev/null 2>&1; then
        if [[ "$update_mode" != "true" ]]; then
            error "User '$username' already exists. Use --update to modify existing user."
            exit 1
        fi
        log "Updating existing user: $username"
        update_user "$username" "$password"
    else
        if [[ "$update_mode" == "true" ]]; then
            error "User '$username' does not exist. Remove --update to create new user."
            exit 1
        fi
        log "Creating new user: $username"
        create_user "$username" "$password"
    fi
}

create_user() {
    local username="$1"
    local password="$2"
    
    log "Creating user '$username' with home directory and bash shell..."
    
    useradd -m -s /bin/bash -c "Claude Batch Server User - $username" "$username"
    
    # Set password
    echo "$username:$password" | chpasswd
    
    success "User '$username' created successfully"
}

update_user() {
    local username="$1"
    local password="$2"
    
    log "Updating user '$username'..."
    
    # Update password
    echo "$username:$password" | chpasswd
    
    # Ensure user has proper shell and home directory
    usermod -s /bin/bash "$username"
    
    # Create home directory if it doesn't exist
    if [[ ! -d "/home/$username" ]]; then
        log "Creating home directory for $username"
        mkhomedir_helper "$username" 2>/dev/null || {
            mkdir -p "/home/$username"
            chown "$username:$username" "/home/$username"
            chmod 755 "/home/$username"
        }
    fi
    
    success "User '$username' updated successfully"
}

# Setup group memberships
setup_group_memberships() {
    local username="$1"
    
    log "Setting up group memberships for $username..."
    
    for group in "${REQUIRED_GROUPS[@]}"; do
        if groups "$username" | grep -q "\\b$group\\b"; then
            debug "$username is already in group: $group"
        else
            log "Adding $username to group: $group"
            usermod -a -G "$group" "$username"
        fi
    done
    
    success "Group memberships configured for $username"
}

# Setup workspace permissions
setup_workspace_permissions() {
    local username="$1"
    local service_user="$2"
    
    log "Setting up workspace permissions..."
    
    # Get workspace path from config or use default
    local workspace_base=""
    local config_files=(
        "$SCRIPT_DIR/claude-batch-server/src/ClaudeBatchServer.Api/appsettings.Development.json"
        "$SCRIPT_DIR/src/ClaudeBatchServer.Api/appsettings.Development.json"
        "$SCRIPT_DIR/appsettings.Development.json"
    )
    
    for config_file in "${config_files[@]}"; do
        if [[ -f "$config_file" ]]; then
            local workspace_jobs
            workspace_jobs=$(grep -o '"JobsPath"[[:space:]]*:[[:space:]]*"[^"]*"' "$config_file" | cut -d'"' -f4 | head -1)
            if [[ -n "$workspace_jobs" ]]; then
                # Expand ~ to home directory
                workspace_jobs="${workspace_jobs/#\~/$HOME}"
                workspace_base="$(dirname "$workspace_jobs")"
                break
            fi
        fi
    done
    
    # Fallback to default workspace location
    if [[ -z "$workspace_base" ]]; then
        workspace_base="/home/$service_user/claude-code-server-workspace"
        warn "Could not find workspace config, using default: $workspace_base"
    fi
    
    log "Workspace base directory: $workspace_base"
    
    # Ensure workspace directories exist with proper permissions
    for subdir in "" "jobs" "repos" "staging"; do
        local dir_path="$workspace_base/$subdir"
        if [[ ! -d "$dir_path" ]]; then
            log "Creating workspace directory: $dir_path"
            mkdir -p "$dir_path"
        fi
        
        # Set ownership and permissions
        chown -R "$service_user:$CLAUDE_GROUP" "$dir_path"
        chmod -R g+rwX "$dir_path"
        chmod g+s "$dir_path"  # SetGID for new files
    done
    
    success "Workspace permissions configured"
}

# Setup sudo impersonation rights
setup_sudo_impersonation() {
    local service_user="$1"
    local sudoers_file="/etc/sudoers.d/claude-batch-server-impersonation"
    
    log "Setting up sudo impersonation rights..."
    
    # Create sudoers file for service user impersonation
    cat > "$sudoers_file" <<EOF
# Claude Batch Server user impersonation rules
# Allow service user to impersonate any user for job execution
$service_user ALL=(ALL) NOPASSWD: ALL

# Allow user existence checks
$service_user ALL=(root) NOPASSWD: /usr/bin/id *

# Security settings
Defaults!ALL !rootpw, !runaspw, !targetpw
EOF
    
    # Set correct permissions and validate
    chmod 440 "$sudoers_file"
    if visudo -cf "$sudoers_file"; then
        success "Sudo impersonation rights configured for '$service_user'"
    else
        error "Invalid sudo configuration"
        rm -f "$sudoers_file"
        exit 1
    fi
}

# Verify user access
verify_user_access() {
    local username="$1"
    
    log "Verifying access for $username..."
    
    # Test Docker access
    if sudo -u "$username" docker --version >/dev/null 2>&1; then
        success "✓ $username can access Docker"
    else
        warn "⚠ $username cannot access Docker (may need to re-login for group changes)"
    fi
    
    # Show final user configuration
    log "Final user configuration for $username:"
    echo "  User ID: $(id "$username")"
    echo "  Groups: $(groups "$username" | cut -d: -f2-)"
    echo "  Home: $(eval echo ~"$username")"
    echo "  Shell: $(getent passwd "$username" | cut -d: -f7)"
}

# Main function
main() {
    local username=""
    local password=""
    local update_mode="false"
    
    # Parse arguments
    case $# in
        0)
            show_help
            exit 0
            ;;
        1)
            if [[ "$1" == "--help" || "$1" == "-h" ]]; then
                show_help
                exit 0
            else
                error "Missing required arguments"
                show_help
                exit 1
            fi
            ;;
        2)
            username="$1"
            password="$2"
            ;;
        3)
            username="$1"
            password="$2"
            if [[ "$3" == "--update" ]]; then
                update_mode="true"
            else
                error "Invalid argument: $3"
                show_help
                exit 1
            fi
            ;;
        *)
            error "Too many arguments"
            show_help
            exit 1
            ;;
    esac
    
    # Validate inputs
    validate_username "$username" || exit 1
    validate_password "$password" || exit 1
    
    # Assess password strength (informational only)
    assess_password_strength "$password"
    
    log "Starting Claude Batch Server user setup for: $username"
    
    # Run setup steps
    check_prerequisites
    ensure_groups_exist
    
    local service_user
    service_user=$(get_service_user)
    log "Detected service user: $service_user"
    
    manage_user "$username" "$password" "$update_mode"
    setup_group_memberships "$username"
    setup_workspace_permissions "$username" "$service_user"
    setup_sudo_impersonation "$service_user"
    setup_python_for_user "$username" "/home/$username" "/home/jsbattig"
    setup_nodejs_for_user "$username" "/home/$username" "/home/jsbattig"
    install_claude_code_for_user "$username" "/home/$username"
    install_cidx_for_user "$username" "/home/$username"
    verify_claude_access "$username" "/home/$username"
    verify_cidx_access "$username" "/home/$username"
    verify_user_access "$username"
    
    echo ""
    success "🎉 User setup completed successfully!"
    echo ""
    echo -e "${CYAN}Next Steps:${NC}"
    echo "  1. User '$username' can now be used for job execution"
    echo "  2. Test authentication: claude auth login --username $username --password ****"
    echo "  3. The user may need to log out and back in for Docker group changes to take effect"
    echo ""
    echo -e "${YELLOW}Binary Access:${NC}"
    echo "  • Claude Code: Fresh installation via npm (shared utilities)"
    echo "  • CIDX: Fresh installation via pip --user (shared utilities)"
    echo "  • Node.js: Copied from service user NVM installation"
    echo ""
    echo -e "${YELLOW}Security Notes:${NC}"
    echo "  • User has access to Docker (required for CIDX containers)"
    echo "  • User can read/write workspace files via group permissions"
    echo "  • Service user can impersonate this user via sudo (required for job execution)"
    echo "  • Uses shared utilities with run.sh for consistent installations"
    echo ""
}

# Run main function with all arguments
main "$@"