#!/bin/bash

set -euo pipefail

# Claude Server Management Script
# Provides unified interface for running all components

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$SCRIPT_DIR"

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
CYAN='\033[0;36m'
NC='\033[0m' # No Color

# Process tracking for proper cleanup
declare -a CHILD_PIDS=()
CLEANUP_DONE=false

# Signal handling for proper cleanup
cleanup() {
    if [[ "$CLEANUP_DONE" == "true" ]]; then
        return
    fi
    CLEANUP_DONE=true
    
    log "Received signal, cleaning up processes..."
    
    # Kill all tracked child processes
    for pid in "${CHILD_PIDS[@]}"; do
        if kill -0 "$pid" 2>/dev/null; then
            log "Stopping process $pid..."
            kill -TERM "$pid" 2>/dev/null || true
            
            # Give process time to clean up
            sleep 2
            
            # Force kill if still running
            if kill -0 "$pid" 2>/dev/null; then
                warn "Force killing process $pid..."
                kill -KILL "$pid" 2>/dev/null || true
            fi
        fi
    done
    
    # Additional cleanup for dotnet and npm processes
    log "Stopping any remaining server processes..."
    pkill -f "ClaudeBatchServer.Api" 2>/dev/null || true
    pkill -f "dotnet.*ClaudeBatchServer" 2>/dev/null || true
    pkill -f "vite.*dev" 2>/dev/null || true
    pkill -f "npm.*run.*dev" 2>/dev/null || true
    pkill -f "node.*vite" 2>/dev/null || true
    
    log "Cleanup completed"
}

# Set up signal traps
trap cleanup SIGINT SIGTERM EXIT

# Track child process
track_pid() {
    local pid=$1
    CHILD_PIDS+=("$pid")
    log "Tracking process $pid"
}

# Wait for process with signal handling
wait_with_signals() {
    local pid=$1
    local description="$2"
    
    log "Waiting for $description (PID: $pid)..."
    
    while kill -0 "$pid" 2>/dev/null; do
        if wait "$pid" 2>/dev/null; then
            log "$description completed normally"
            break
        else
            local exit_code=$?
            if [[ $exit_code -eq 130 ]]; then  # SIGINT
                log "$description interrupted by user"
                break
            elif [[ $exit_code -ne 0 ]]; then
                warn "$description exited with code $exit_code"
                break
            fi
        fi
    done
}

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

# Help function
show_help() {
    echo -e "${CYAN}Claude Server Management Script${NC}"
    echo ""
    echo -e "${YELLOW}USAGE:${NC}"
    echo "    ./run.sh <COMPONENT> [MODE] [OPTIONS]"
    echo ""
    echo -e "${YELLOW}COMPONENTS:${NC}"
    echo -e "    ${GREEN}server${NC}          - Claude Batch Server (API)"
    echo -e "    ${GREEN}web${NC}             - Web UI (Vite dev server)"
    echo -e "    ${GREEN}both${NC}            - Server + Web UI together"
    echo -e "    ${GREEN}stop${NC}            - Stop running services"
    echo -e "    ${GREEN}test${NC}            - Run tests"
    echo -e "    ${GREEN}docker${NC}          - Docker operations"
    echo -e "    ${GREEN}install${NC}         - Install system dependencies (wraps install.sh)"
    echo ""
    echo -e "${YELLOW}MODES:${NC}"
    echo -e "    ${GREEN}dev${NC}             - Development mode (default)"
    echo -e "    ${GREEN}prod${NC}            - Production mode"
    echo -e "    ${GREEN}test${NC}            - Test mode"
    echo ""
    echo -e "${YELLOW}TEST OPTIONS:${NC}"
    echo -e "    ${GREEN}unit${NC}            - Run unit tests (as suites - fast)"
    echo -e "    ${GREEN}integration${NC}     - Run integration tests (as suites with timeout handling)"
    echo -e "    ${GREEN}e2e${NC}             - Run E2E tests (individually - systematic approach)"
    echo -e "    ${GREEN}all${NC}             - Run all tests (unit → integration → e2e)"
    echo -e "    ${GREEN}playwright${NC}      - Run Playwright E2E tests (separate web UI tests)"
    echo ""
    echo -e "${YELLOW}DOCKER OPTIONS:${NC}"
    echo -e "    ${GREEN}up${NC}              - Start Docker containers"
    echo -e "    ${GREEN}down${NC}            - Stop Docker containers"
    echo -e "    ${GREEN}build${NC}           - Build Docker images"
    echo -e "    ${GREEN}logs${NC}            - Show Docker logs"
    echo ""
    echo -e "${YELLOW}INSTALL OPTIONS:${NC}"
    echo -e "    ${GREEN}--production${NC}    - Install in production mode (nginx, SSL, firewall)"
    echo -e "    ${GREEN}--development${NC}   - Install in development mode (default)"
    echo -e "    ${GREEN}--dev${NC}           - Alias for --development"
    echo -e "    ${GREEN}--dry-run${NC}       - Show what would be installed without making changes"
    echo -e "    ${GREEN}--ssl-country${NC}   - SSL certificate country code (2 letters)"
    echo -e "    ${GREEN}--ssl-state${NC}     - SSL certificate state/province"
    echo -e "    ${GREEN}--ssl-city${NC}      - SSL certificate city"
    echo -e "    ${GREEN}--ssl-org${NC}       - SSL certificate organization"
    echo -e "    ${GREEN}--ssl-ou${NC}        - SSL certificate organizational unit"
    echo -e "    ${GREEN}--ssl-cn${NC}        - SSL certificate common name (hostname/FQDN)"
    echo -e "    ${GREEN}--help${NC}          - Show install help"
    echo ""
    echo -e "${YELLOW}EXAMPLES:${NC}"
    echo "    ./run.sh server dev              # Start server in development"
    echo "    ./run.sh web                     # Start web UI (dev mode default)"
    echo "    ./run.sh both prod               # Start both server and web in production"
    echo "    ./run.sh stop server             # Stop server processes"
    echo "    ./run.sh stop web                # Stop web UI processes"
    echo "    ./run.sh stop all                # Stop all services"
    echo "    ./run.sh test unit               # Run unit tests (fast, as suites)"
    echo "    ./run.sh test integration        # Run integration tests (with timeouts)"
    echo "    ./run.sh test e2e                # Run E2E tests (individually, systematic)"
    echo "    ./run.sh test all                # Run all tests in sequence"
    echo "    ./run.sh test playwright         # Run Playwright web UI tests"
    echo "    ./run.sh docker up               # Start with Docker"
    echo "    ./run.sh install                 # Install system dependencies (development mode)"
    echo "    ./run.sh install --production    # Install with nginx, SSL, and firewall"
    echo "    ./run.sh install --dry-run       # Show what would be installed"
    echo "    ./run.sh install --production --ssl-cn server.example.com  # Production with SSL"
    echo ""
    echo -e "${YELLOW}ENVIRONMENT VARIABLES:${NC}"
    echo -e "    ${GREEN}PORT${NC}            - Server port (default: 5185)"
    echo -e "    ${GREEN}WEB_PORT${NC}        - Web UI port (default: 5173)"
    echo -e "    ${GREEN}ASPNETCORE_ENVIRONMENT${NC} - ASP.NET environment"
    echo ""
    echo -e "${YELLOW}CONFIG FILES:${NC}"
    echo -e "    ${GREEN}.env${NC}            - Main environment variables"
    echo -e "    ${GREEN}.env.test${NC}       - Test environment variables"
    echo -e "    ${GREEN}appsettings.json${NC} - Server configuration"
    echo ""
    echo -e "${YELLOW}NOTES:${NC}"
    echo -e "    • Use Ctrl-C to stop running services (proper cleanup guaranteed)"
    echo -e "    • The 'install' command wraps ./claude-batch-server/scripts/install.sh"
    echo -e "    • Installation script handles sudo privileges automatically as needed"
    echo -e "    • Use --dry-run to see what would be installed before making changes"
    echo ""
    echo "For more information, see: https://github.com/jsbattig/claude-server"
}

# Check dependencies
check_dependencies() {
    local missing_deps=()
    
    # Check .NET
    if ! command -v dotnet >/dev/null 2>&1; then
        if [[ -f "$HOME/.dotnet/dotnet" ]]; then
            export PATH="$HOME/.dotnet:$PATH"
            export DOTNET_ROOT="$HOME/.dotnet"
        else
            missing_deps+=("dotnet")
        fi
    fi
    
    # Check Node.js for web UI
    if [[ "$1" == "web" || "$1" == "both" || "$1" == "test" ]]; then
        if ! command -v npm >/dev/null 2>&1; then
            missing_deps+=("npm")
        fi
    fi
    
    # Check Docker if needed
    if [[ "$1" == "docker" ]]; then
        if ! command -v docker >/dev/null 2>&1; then
            missing_deps+=("docker")
        fi
    fi
    
    if [[ ${#missing_deps[@]} -gt 0 ]]; then
        error "Missing dependencies: ${missing_deps[*]}"
        log "Run './run.sh install' to install system dependencies"
        exit 1
    fi
}

# Install dependencies - wrapper for install.sh script
install_dependencies() {
    log "Running Claude Batch Server installation script..."
    
    # Get all arguments after 'install' command
    # The first argument is 'install', so we skip it
    local install_args=("${@:2}")
    
    # Pass all remaining arguments to the actual install script
    "$PROJECT_ROOT/claude-batch-server/scripts/install.sh" "${install_args[@]}"
}

# Start Claude Batch Server
start_server() {
    local mode="${1:-dev}"
    local port="${PORT:-5185}"
    
    log "Starting Claude Batch Server in $mode mode on port $port..."
    log "Use Ctrl-C to stop the server properly"
    
    cd "$PROJECT_ROOT/claude-batch-server"
    
    # Ensure .NET is in PATH
    export PATH="$HOME/.dotnet:$PATH"
    export DOTNET_ROOT="$HOME/.dotnet"
    
    case "$mode" in
        "dev")
            export ASPNETCORE_ENVIRONMENT=Development
            export ASPNETCORE_URLS="http://0.0.0.0:$port"
            log "Forcing clean build to ensure changes are picked up..."
            log "VOYAGE_API_KEY configured in appsettings.json for CIDX voyage-ai embedding"
            dotnet clean src/ClaudeBatchServer.Api/ClaudeBatchServer.Api.csproj
            dotnet restore src/ClaudeBatchServer.Api/ClaudeBatchServer.Api.csproj
            dotnet build src/ClaudeBatchServer.Api/ClaudeBatchServer.Api.csproj --no-restore
            
            # Start server and track PID
            dotnet run --project src/ClaudeBatchServer.Api/ClaudeBatchServer.Api.csproj --no-build &
            track_pid $!
            wait_with_signals $! "Claude Batch Server"
            ;;
        "prod")
            export ASPNETCORE_ENVIRONMENT=Production
            export ASPNETCORE_URLS="http://0.0.0.0:$port"
            dotnet run --project src/ClaudeBatchServer.Api/ClaudeBatchServer.Api.csproj --configuration Release &
            track_pid $!
            wait_with_signals $! "Claude Batch Server"
            ;;
        "test")
            export ASPNETCORE_ENVIRONMENT=Testing
            export ASPNETCORE_URLS="http://0.0.0.0:$port"
            dotnet run --project src/ClaudeBatchServer.Api/ClaudeBatchServer.Api.csproj --configuration Debug &
            track_pid $!
            wait_with_signals $! "Claude Batch Server"
            ;;
        *)
            error "Unknown server mode: $mode"
            exit 1
            ;;
    esac
}

# Start Web UI
start_web() {
    local mode="${1:-dev}"
    local port="${WEB_PORT:-5173}"
    
    log "Starting Web UI in $mode mode on port $port..."
    log "Use Ctrl-C to stop the web UI properly"
    
    cd "$PROJECT_ROOT/claude-web-ui"
    
    case "$mode" in
        "dev")
            VITE_API_URL=http://localhost:${PORT:-5185} npm run dev -- --port "$port" &
            track_pid $!
            wait_with_signals $! "Web UI"
            ;;
        "prod")
            npm run build
            npm run preview -- --port "$port" &
            track_pid $!
            wait_with_signals $! "Web UI"
            ;;
        "test")
            VITE_API_URL=http://localhost:${PORT:-5185} npm run dev -- --port "$port" --mode test &
            track_pid $!
            wait_with_signals $! "Web UI"
            ;;
        *)
            error "Unknown web mode: $mode"
            exit 1
            ;;
    esac
}

# Ensure test user exists as real system user for dev mode testing
ensure_test_user() {
    local current_user=$(whoami)
    
    log "Ensuring test_user exists as real system user for dev mode testing..."
    
    # Check if test_user already exists
    if id "test_user" &>/dev/null; then
        log "test_user already exists as system user"
        return 0
    fi
    
    # Get current user's password hash to copy to test_user
    local current_user_hash
    current_user_hash=$(sudo grep "^$current_user:" /etc/shadow | cut -d: -f2 2>/dev/null || echo "")
    
    if [[ -z "$current_user_hash" ]]; then
        warn "Could not get password hash for current user, setting standard test password"
        # Create user and set known test password for E2E tests
        sudo useradd -m -s /bin/bash -c "Test User for Claude Server" test_user
        echo 'test_user:TestPassword123!' | sudo chpasswd
        log "test_user created with standard test password: TestPassword123!"
    else
        # Create user and copy password hash
        sudo useradd -m -s /bin/bash -c "Test User for Claude Server" test_user
        # Copy password hash from current user
        sudo usermod --password "$current_user_hash" test_user
        log "test_user created with same password as $current_user"
        log "For E2E tests, you may need to set known password: echo 'test_user:TestPassword123!' | sudo chpasswd"
    fi
    
    # Ensure test_user can read workspace files (add to service user's group if needed)
    local service_group=$(id -gn "$current_user")
    sudo usermod -a -G "$service_group" test_user
    
    log "test_user created as real system user"
}

# Clean up test user (for dev cleanup)
cleanup_test_user() {
    log "Cleaning up test_user..."
    
    if id "test_user" &>/dev/null; then
        # Kill any processes owned by test_user
        sudo pkill -u test_user 2>/dev/null || true
        sleep 1
        
        # Remove the user and home directory
        sudo userdel -r test_user 2>/dev/null || true
        log "test_user removed from system"
    else
        log "test_user does not exist"
    fi
}

# Setup impersonation rights for current user
setup_impersonation_rights() {
    local current_user=$(whoami)
    local sudoers_file="/etc/sudoers.d/$current_user-impersonation"
    
    log "Setting up impersonation rights for current user '$current_user'..."
    
    # Check if sudoers file already exists
    if [[ -f "$sudoers_file" ]]; then
        log "Impersonation rights already configured"
        return 0
    fi
    
    # Create sudoers file for current user impersonation
    log "Creating sudo impersonation rules..."
    if sudo tee "$sudoers_file" > /dev/null 2>&1 << EOF; then
# Claude Batch Server user impersonation rules for $current_user  
# Allow current user to impersonate any user
$current_user ALL=(ALL) NOPASSWD: ALL

# Allow user existence checks
$current_user ALL=(root) NOPASSWD: /usr/bin/id *

# Prevent impersonation of system users (security via application logic)
Defaults!ALL !rootpw, !runaspw, !targetpw
EOF
        # Set correct permissions and validate
        sudo chmod 440 "$sudoers_file"
        if sudo visudo -cf "$sudoers_file"; then
            log "Impersonation rights configured successfully for '$current_user'"
        else
            error "Invalid sudo impersonation configuration"
            sudo rm -f "$sudoers_file"
            return 1
        fi
    else
        error "Failed to create sudo impersonation rules"
        return 1
    fi
}

# Start both server and web UI
start_both() {
    local mode="${1:-dev}"
    
    log "Starting both server and web UI in $mode mode..."
    log "Use Ctrl-C to stop both services properly"
    
    # Ensure test_user exists for dev mode testing
    if [[ "$mode" == "dev" ]]; then
        ensure_test_user
        setup_impersonation_rights
    fi
    
    # Use current user as the service account (simplified architecture)
    local current_user=$(whoami)
    log "Running as service user '$current_user' - simplified architecture"
    
    # Start server in background
    log "Starting server in background..."
    cd "$PROJECT_ROOT/claude-batch-server"
    
    # Ensure .NET is in PATH
    export PATH="$HOME/.dotnet:$PATH"
    export DOTNET_ROOT="$HOME/.dotnet"
    
    case "$mode" in
        "dev")
            export ASPNETCORE_ENVIRONMENT=Development
            export ASPNETCORE_URLS="http://0.0.0.0:${PORT:-5185}"
            log "Building server..."
            dotnet clean src/ClaudeBatchServer.Api/ClaudeBatchServer.Api.csproj >/dev/null 2>&1
            dotnet restore src/ClaudeBatchServer.Api/ClaudeBatchServer.Api.csproj >/dev/null 2>&1
            dotnet build src/ClaudeBatchServer.Api/ClaudeBatchServer.Api.csproj --no-restore >/dev/null 2>&1
            
            dotnet run --project src/ClaudeBatchServer.Api/ClaudeBatchServer.Api.csproj --no-build &
            track_pid $!
            SERVER_PID=$!
            ;;
        "prod")
            export ASPNETCORE_ENVIRONMENT=Production
            export ASPNETCORE_URLS="http://0.0.0.0:${PORT:-5185}"
            dotnet run --project src/ClaudeBatchServer.Api/ClaudeBatchServer.Api.csproj --configuration Release &
            track_pid $!
            SERVER_PID=$!
            ;;
        *)
            error "Unknown mode for both: $mode"
            exit 1
            ;;
    esac
    
    # Wait a bit for server to start
    log "Waiting for server to initialize..."
    sleep 5
    
    # Start web UI in background
    log "Starting web UI in background..."
    cd "$PROJECT_ROOT/claude-web-ui"
    
    case "$mode" in
        "dev")
            VITE_API_URL=http://localhost:${PORT:-5185} npm run dev -- --port "${WEB_PORT:-5173}" &
            track_pid $!
            WEB_PID=$!
            ;;
        "prod")
            npm run build >/dev/null 2>&1
            npm run preview -- --port "${WEB_PORT:-5173}" &
            track_pid $!
            WEB_PID=$!
            ;;
    esac
    
    log "Both services started!"
    log "Server: http://localhost:${PORT:-5185}"
    log "Web UI: http://localhost:${WEB_PORT:-5173}"
    log "Press Ctrl-C to stop both services"
    
    # Wait for either process to exit
    while kill -0 "$SERVER_PID" 2>/dev/null && kill -0 "$WEB_PID" 2>/dev/null; do
        sleep 1
    done
    
    log "One of the services stopped, initiating cleanup..."
}

# Run tests using the comprehensive test-runner.sh script
run_tests() {
    local test_type="${1:-all}"
    
    log "Running $test_type tests using systematic test runner..."
    
    # Check if the test-runner.sh script exists
    local test_runner_script="$PROJECT_ROOT/claude-batch-server/test-runner.sh"
    if [[ ! -f "$test_runner_script" ]]; then
        error "Test runner script not found: $test_runner_script"
        log "Please ensure the test-runner.sh script is present in the claude-batch-server directory"
        exit 1
    fi
    
    # Make sure the script is executable
    chmod +x "$test_runner_script"
    
    # Set required environment variables for the test runner
    export PATH="$HOME/.dotnet:$PATH"
    export TEST_USERNAME="${TEST_USERNAME:-jsbattig}"
    export TEST_PASSWORD="${TEST_PASSWORD:-TestPassword123!}"
    
    case "$test_type" in
        "unit"|"integration"|"e2e"|"all")
            # Use the systematic test runner
            cd "$PROJECT_ROOT/claude-batch-server"
            "$test_runner_script" "$test_type"
            ;;
        "playwright")
            # Keep original Playwright E2E tests as a separate option
            cd "$PROJECT_ROOT/claude-web-ui"
            
            # Ensure Playwright is installed
            if [[ ! -d "node_modules/@playwright" ]]; then
                log "Installing Playwright..."
                npx playwright install
            fi
            
            npm run test:e2e
            ;;
        *)
            error "Unknown test type: $test_type"
            log "Available options: unit, integration, e2e, all, playwright"
            exit 1
            ;;
    esac
}

# Docker operations
docker_operations() {
    local operation="${1:-up}"
    
    cd "$PROJECT_ROOT/claude-batch-server/docker"
    
    case "$operation" in
        "up")
            log "Starting Docker containers..."
            docker compose up -d
            log "Containers started. Web UI: http://localhost:8080, API: http://localhost:8080/api"
            ;;
        "down")
            log "Stopping Docker containers..."
            docker compose down
            ;;
        "build")
            log "Building Docker images..."
            docker compose build
            ;;
        "logs")
            log "Showing Docker logs..."
            docker compose logs -f &
            track_pid $!
            wait_with_signals $! "Docker logs"
            ;;
        *)
            error "Unknown Docker operation: $operation"
            exit 1
            ;;
    esac
}

# Stop services
stop_services() {
    local service="${1:-all}"
    
    log "Stopping $service services..."
    
    case "$service" in
        "server")
            log "Stopping Claude Batch Server processes..."
            pkill -f "ClaudeBatchServer.Api" || true
            pkill -f "dotnet.*ClaudeBatchServer" || true
            log "Server processes stopped"
            ;;
        "web")
            log "Stopping Web UI processes..."
            pkill -f "vite.*dev" || true
            pkill -f "npm.*run.*dev" || true
            pkill -f "node.*vite" || true
            log "Web UI processes stopped"
            ;;
        "all")
            log "Stopping all services..."
            stop_services "server"
            stop_services "web"
            log "All services stopped"
            ;;
        *)
            error "Unknown service to stop: $service"
            log "Available options: server, web, all"
            exit 1
            ;;
    esac
}

# Main script logic
main() {
    if [[ $# -eq 0 ]]; then
        show_help
        exit 0
    fi
    
    local component="$1"
    local mode="${2:-dev}"
    local option="${3:-}"
    
    case "$component" in
        "--help"|"-h"|"help")
            show_help
            ;;
        "install")
            install_dependencies "$@"
            ;;
        "server")
            check_dependencies "$component"
            start_server "$mode"
            ;;
        "web")
            check_dependencies "$component"
            start_web "$mode"
            ;;
        "both")
            check_dependencies "$component"
            start_both "$mode"
            ;;
        "stop")
            stop_services "$mode"
            ;;
        "test")
            check_dependencies "$component"
            run_tests "$mode"
            ;;
        "docker")
            check_dependencies "$component"
            docker_operations "$mode"
            ;;
        *)
            error "Unknown component: $component"
            log "Run './run.sh --help' for usage information"
            exit 1
            ;;
    esac
}

# Run main function with all arguments
main "$@"