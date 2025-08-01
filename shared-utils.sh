#!/bin/bash

# Shared utilities for Claude Server scripts
# Common functions for Node.js and Claude Code installation

# Colors for output (reusable)
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
CYAN='\033[0;36m'
NC='\033[0m' # No Color

# Shared logging functions
shared_log() {
    echo -e "${GREEN}[INFO]${NC} $1"
}

shared_warn() {
    echo -e "${YELLOW}[WARN]${NC} $1"
}

shared_error() {
    echo -e "${RED}[ERROR]${NC} $1"
}

shared_success() {
    echo -e "${GREEN}[SUCCESS]${NC} $1"
}

# Install Claude Code for a specific user
install_claude_code_for_user() {
    local username="$1"
    local user_home="${2:-/home/$username}"
    
    shared_log "Installing Claude Code for user: $username"
    
    # Ensure Node.js is available
    if ! command -v node >/dev/null 2>&1 || ! command -v npm >/dev/null 2>&1; then
        shared_error "Node.js and npm are required but not found"
        shared_log "Please run './run.sh install' first to set up the system dependencies"
        return 1
    fi
    
    # Install Claude Code
    if sudo -u "$username" bash -c "cd '$user_home' && source ~/.bashrc 2>/dev/null && npm install -g @anthropic-ai/claude-code" 2>/dev/null; then
        shared_success "✓ Claude Code installed globally for $username"
    else
        shared_log "Global install failed, trying local install..."
        if sudo -u "$username" bash -c "cd '$user_home' && source ~/.bashrc 2>/dev/null && npm install @anthropic-ai/claude-code" 2>/dev/null; then
            shared_success "✓ Claude Code installed locally for $username"
            # Add local node_modules/.bin to PATH
            local bashrc="$user_home/.bashrc"
            if ! sudo -u "$username" grep -q "node_modules/.bin" "$bashrc" 2>/dev/null; then
                echo 'export PATH="$HOME/node_modules/.bin:$PATH"' | sudo -u "$username" tee -a "$bashrc" >/dev/null
                shared_log "Added local node_modules/.bin to PATH"
            fi
        else
            shared_warn "⚠ Failed to install Claude Code for $username"
            return 1
        fi
    fi
    
    # Test installation
    if sudo -u "$username" bash -c "cd '$user_home' && source ~/.bashrc 2>/dev/null && claude --version" >/dev/null 2>&1; then
        shared_success "✓ Claude Code verified working for $username"
        return 0
    else
        shared_warn "⚠ Claude Code installation verification failed for $username"
        return 1
    fi
}

# Setup Python environment for a user (copying from service user)
setup_python_for_user() {
    local username="$1"
    local user_home="${2:-/home/$username}"
    local service_user_home="${3:-/home/jsbattig}"
    
    shared_log "Setting up Python environment for $username..."
    
    # Copy pyenv from service user if available
    if [[ -d "$service_user_home/.pyenv" ]]; then
        shared_log "Copying pyenv installation from service user..."
        if [[ ! -d "$user_home/.pyenv" ]]; then
            sudo cp -r "$service_user_home/.pyenv" "$user_home/"
            sudo chown -R "$username:$username" "$user_home/.pyenv"
        fi
        
        # Add pyenv to bashrc
        local bashrc="$user_home/.bashrc"
        if ! sudo -u "$username" grep -q ".pyenv/bin" "$bashrc" 2>/dev/null; then
            {
                echo ""
                echo "# pyenv setup"
                echo 'export PYENV_ROOT="$HOME/.pyenv"'
                echo 'export PATH="$PYENV_ROOT/bin:$PATH"'
                echo 'eval "$(pyenv init -)"'
            } | sudo -u "$username" tee -a "$bashrc" >/dev/null
        fi
        
        # Use existing Python version from service user (don't install new versions)
        local service_python_version
        service_python_version=$(sudo -u "${service_user_home##*/}" bash -c "source ~/.bashrc && python3 --version" 2>/dev/null | cut -d' ' -f2)
        if [[ -n "$service_python_version" ]]; then
            shared_log "Service user uses Python $service_python_version"
            # Check if this version is available in the copied pyenv
            if sudo -u "$username" bash -c "cd '$user_home' && source ~/.bashrc && pyenv versions | grep -q '$service_python_version'" 2>/dev/null; then
                sudo -u "$username" bash -c "cd '$user_home' && source ~/.bashrc && pyenv global $service_python_version" 2>/dev/null || true
                shared_log "Set Python version to $service_python_version for $username"
            else
                shared_log "Python $service_python_version not available in pyenv, using default"
            fi
        fi
        
        return 0
    else
        shared_log "No pyenv installation found in service user home, using system Python"
        return 1
    fi
}

# Setup Node.js environment for a user (copying from service user)
setup_nodejs_for_user() {
    local username="$1"
    local user_home="${2:-/home/$username}"
    local service_user_home="${3:-/home/jsbattig}"
    
    shared_log "Setting up Node.js environment for $username..."
    
    # Copy NVM from service user if available
    if [[ -d "$service_user_home/.nvm" ]]; then
        shared_log "Copying NVM installation from service user..."
        if [[ ! -d "$user_home/.nvm" ]]; then
            sudo cp -r "$service_user_home/.nvm" "$user_home/"
            sudo chown -R "$username:$username" "$user_home/.nvm"
        fi
        
        # Add NVM to bashrc
        local bashrc="$user_home/.bashrc"
        if ! sudo -u "$username" grep -q ".nvm/nvm.sh" "$bashrc" 2>/dev/null; then
            {
                echo ""
                echo "# NVM setup"
                echo 'export NVM_DIR="$HOME/.nvm"'
                echo '[ -s "$NVM_DIR/nvm.sh" ] && \. "$NVM_DIR/nvm.sh"'
                echo '[ -s "$NVM_DIR/bash_completion" ] && \. "$NVM_DIR/bash_completion"'
            } | sudo -u "$username" tee -a "$bashrc" >/dev/null
        fi
        
        # Set default Node.js version to match service user
        local service_node_version
        service_node_version=$(sudo -u "${service_user_home##*/}" bash -c "source ~/.nvm/nvm.sh && node --version" 2>/dev/null || node --version)
        shared_log "Setting Node.js version to $service_node_version for $username"
        sudo -u "$username" bash -c "cd '$user_home' && source ~/.bashrc && nvm install $service_node_version && nvm use $service_node_version && nvm alias default $service_node_version" 2>/dev/null || true
        
        return 0
    else
        shared_warn "No NVM installation found in service user home: $service_user_home"
        return 1
    fi
}

# Create a user with Claude Code access (full setup)
create_claude_user() {
    local username="$1"
    local password="$2"
    local service_user_home="${3:-/home/jsbattig}"
    
    shared_log "Creating user '$username' with Claude Code access..."
    
    # Create user if doesn't exist
    if ! id "$username" >/dev/null 2>&1; then
        sudo useradd -m -s /bin/bash -c "Claude Code User - $username" "$username"
        echo "$username:$password" | sudo chpasswd
        shared_success "User '$username' created"
    else
        shared_log "User '$username' already exists, updating password"
        echo "$username:$password" | sudo chpasswd
    fi
    
    # Setup Node.js environment
    setup_nodejs_for_user "$username" "/home/$username" "$service_user_home"
    
    # Install Claude Code
    install_claude_code_for_user "$username" "/home/$username"
    
    shared_success "User '$username' setup completed with Claude Code access"
}

# Install CIDX for a specific user (using pyenv Python if available, like service user)
install_cidx_for_user() {
    local username="$1"
    local user_home="${2:-/home/$username}"
    
    shared_log "Installing CIDX for user: $username"
    
    # Check if user has Python available (pyenv or system)
    if ! sudo -u "$username" bash -c "cd '$user_home' && source ~/.bashrc 2>/dev/null && command -v python3" >/dev/null 2>&1; then
        shared_error "Python3 not found for user $username. Please ensure Python environment is set up."
        return 1
    fi
    
    # Install pipx for the user using their Python environment
    if ! sudo -u "$username" bash -c "cd '$user_home' && source ~/.bashrc 2>/dev/null && command -v pipx" >/dev/null 2>&1; then
        shared_log "Installing pipx for $username using their Python environment..."
        if sudo -u "$username" bash -c "cd '$user_home' && source ~/.bashrc 2>/dev/null && python3 -m pip install --user pipx" 2>/dev/null; then
            sudo -u "$username" bash -c "cd '$user_home' && source ~/.bashrc 2>/dev/null && python3 -m pipx ensurepath" 2>/dev/null || true
            shared_log "pipx installed for $username"
        else
            shared_warn "Failed to install pipx for $username"
            return 1
        fi
    fi
    
    # Add ~/.local/bin to PATH in bashrc
    local bashrc="$user_home/.bashrc"
    if ! sudo -u "$username" grep -q ".local/bin" "$bashrc" 2>/dev/null; then
        echo 'export PATH="$HOME/.local/bin:$PATH"' | sudo -u "$username" tee -a "$bashrc" >/dev/null
        shared_log "Added ~/.local/bin to PATH for pipx/CIDX access"
    fi
    
    # Install CIDX using pipx (same as install.sh)
    if sudo -u "$username" bash -c "cd '$user_home' && source ~/.bashrc 2>/dev/null && pipx install --force git+https://github.com/jsbattig/code-indexer.git" 2>/dev/null; then
        shared_success "✓ CIDX installed successfully for $username"
        return 0
    else
        shared_warn "⚠ Failed to install CIDX for $username"
        return 1
    fi
}

# Verify Claude Code access for a user
verify_claude_access() {
    local username="$1"
    local user_home="${2:-/home/$username}"
    
    shared_log "Verifying Claude Code access for $username..."
    
    if sudo -u "$username" bash -c "cd '$user_home' && source ~/.bashrc 2>/dev/null && claude --version" >/dev/null 2>&1; then
        local version
        version=$(sudo -u "$username" bash -c "cd '$user_home' && source ~/.bashrc 2>/dev/null && claude --version" 2>/dev/null)
        shared_success "✓ $username can access Claude Code ($version)"
        return 0
    else
        shared_warn "⚠ $username cannot access Claude Code"
        return 1
    fi
}

# Verify CIDX access for a user
verify_cidx_access() {
    local username="$1"
    local user_home="${2:-/home/$username}"
    
    shared_log "Verifying CIDX access for $username..."
    
    if sudo -u "$username" bash -c "cd '$user_home' && source ~/.bashrc 2>/dev/null && cidx --version" >/dev/null 2>&1; then
        local version
        version=$(sudo -u "$username" bash -c "cd '$user_home' && source ~/.bashrc 2>/dev/null && cidx --version" 2>/dev/null | head -1)
        shared_success "✓ $username can access CIDX ($version)"
        return 0
    else
        shared_warn "⚠ $username cannot access CIDX"
        return 1
    fi
}