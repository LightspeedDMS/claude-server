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
    echo -e "    ${GREEN}unit${NC}            - Run unit tests"
    echo -e "    ${GREEN}integration${NC}     - Run integration tests"
    echo -e "    ${GREEN}e2e${NC}             - Run E2E tests (Playwright)"
    echo -e "    ${GREEN}all${NC}             - Run all tests"
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
    echo "    ./run.sh test unit               # Run unit tests"
    echo "    ./run.sh test e2e                # Run E2E tests"
    echo "    ./run.sh test all                # Run all tests"
    echo "    ./run.sh docker up               # Start with Docker"
    echo "    ./run.sh install                 # Install system dependencies (development mode)"
    echo "    ./run.sh install --production    # Install with nginx, SSL, and firewall"
    echo "    ./run.sh install --dry-run       # Show what would be installed"
    echo "    ./run.sh install --production --ssl-cn server.example.com  # Production with SSL"
    echo ""
    echo -e "${YELLOW}ENVIRONMENT VARIABLES:${NC}"
    echo -e "    ${GREEN}PORT${NC}            - Server port (default: 5000)"
    echo -e "    ${GREEN}WEB_PORT${NC}        - Web UI port (default: 5173)"
    echo -e "    ${GREEN}ASPNETCORE_ENVIRONMENT${NC} - ASP.NET environment"
    echo ""
    echo -e "${YELLOW}CONFIG FILES:${NC}"
    echo -e "    ${GREEN}.env${NC}            - Main environment variables"
    echo -e "    ${GREEN}.env.test${NC}       - Test environment variables"
    echo -e "    ${GREEN}appsettings.json${NC} - Server configuration"
    echo ""
    echo -e "${YELLOW}NOTES:${NC}"
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
    local port="${PORT:-5000}"
    
    log "Starting Claude Batch Server in $mode mode on port $port..."
    
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
            dotnet run --project src/ClaudeBatchServer.Api/ClaudeBatchServer.Api.csproj --no-build
            ;;
        "prod")
            export ASPNETCORE_ENVIRONMENT=Production
            export ASPNETCORE_URLS="http://0.0.0.0:$port"
            dotnet run --project src/ClaudeBatchServer.Api/ClaudeBatchServer.Api.csproj --configuration Release
            ;;
        "test")
            export ASPNETCORE_ENVIRONMENT=Testing
            export ASPNETCORE_URLS="http://0.0.0.0:$port"
            dotnet run --project src/ClaudeBatchServer.Api/ClaudeBatchServer.Api.csproj --configuration Debug
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
    
    cd "$PROJECT_ROOT/claude-web-ui"
    
    case "$mode" in
        "dev")
            npm run dev -- --port "$port"
            ;;
        "prod")
            npm run build
            npm run preview -- --port "$port"
            ;;
        "test")
            npm run dev -- --port "$port" --mode test
            ;;
        *)
            error "Unknown web mode: $mode"
            exit 1
            ;;
    esac
}

# Start both server and web UI
start_both() {
    local mode="${1:-dev}"
    
    log "Starting both server and web UI in $mode mode..."
    
    # Start server in background
    log "Starting server in background..."
    (start_server "$mode") &
    SERVER_PID=$!
    
    # Wait a bit for server to start
    sleep 3
    
    # Start web UI in foreground
    log "Starting web UI..."
    start_web "$mode"
    
    # Clean up server process on exit
    trap "kill $SERVER_PID 2>/dev/null || true" EXIT
}

# Run tests
run_tests() {
    local test_type="${1:-all}"
    
    log "Running $test_type tests..."
    
    case "$test_type" in
        "unit")
            cd "$PROJECT_ROOT/claude-batch-server"
            export PATH="$HOME/.dotnet:$PATH"
            dotnet test tests/ClaudeBatchServer.Tests/ClaudeBatchServer.Tests.csproj --configuration Release --verbosity normal
            ;;
        "integration")
            cd "$PROJECT_ROOT/claude-batch-server"
            export PATH="$HOME/.dotnet:$PATH"
            
            # Set test environment variables
            export TEST_USERNAME="${TEST_USERNAME:-jsbattig}"
            export TEST_PASSWORD="${TEST_PASSWORD:-TestPassword123!}"
            
            dotnet test tests/ClaudeBatchServer.IntegrationTests/ClaudeBatchServer.IntegrationTests.csproj --configuration Release --verbosity normal
            ;;
        "e2e")
            cd "$PROJECT_ROOT/claude-web-ui"
            
            # Ensure Playwright is installed
            if [[ ! -d "node_modules/@playwright" ]]; then
                log "Installing Playwright..."
                npx playwright install
            fi
            
            npm run test:e2e
            ;;
        "all")
            log "Running all tests in sequence..."
            run_tests "unit"
            run_tests "integration"
            run_tests "e2e"
            ;;
        *)
            error "Unknown test type: $test_type"
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
            docker compose logs -f
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