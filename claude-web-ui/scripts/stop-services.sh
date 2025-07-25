#!/bin/bash

# Claude Server Stack Shutdown Script
# Automatically detects and stops all Claude services (API, Web UI, NGINX)
#
# Usage: ./stop-services.sh [--force]

set -e  # Exit on any error

# Color codes for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
PURPLE='\033[0;35m'
CYAN='\033[0;36m'
NC='\033[0m' # No Color

# Configuration
FORCE_KILL=${1:-""}
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
WEB_UI_DIR="$(dirname "$SCRIPT_DIR")"
PID_DIR="$WEB_UI_DIR/.pids"

echo -e "${CYAN}🛑 Stopping Claude Server Stack${NC}"
echo ""

# Function to safely kill a process
kill_process() {
    local pid=$1
    local name=$2
    local force=$3
    
    if [ -z "$pid" ] || [ "$pid" = "" ]; then
        echo -e "${YELLOW}⚠️  No PID found for $name${NC}"
        return 0
    fi
    
    # Check if process exists
    if ! kill -0 "$pid" 2>/dev/null; then
        echo -e "${YELLOW}⚠️  $name (PID: $pid) is not running${NC}"
        return 0
    fi
    
    echo -e "${BLUE}🔹 Stopping $name (PID: $pid)...${NC}"
    
    if [ "$force" = "--force" ]; then
        # Force kill
        kill -9 "$pid" 2>/dev/null || true
        echo -e "${YELLOW}   Force killed $name${NC}"
    else
        # Graceful shutdown
        kill -TERM "$pid" 2>/dev/null || true
        
        # Wait up to 10 seconds for graceful shutdown
        local count=0
        while kill -0 "$pid" 2>/dev/null && [ $count -lt 10 ]; do
            sleep 1
            count=$((count + 1))
        done
        
        # Force kill if still running
        if kill -0 "$pid" 2>/dev/null; then
            echo -e "${YELLOW}   Graceful shutdown failed, force killing...${NC}"
            kill -9 "$pid" 2>/dev/null || true
        fi
        
        echo -e "${GREEN}   ✅ Stopped $name${NC}"
    fi
}

# Function to kill processes by name pattern
kill_by_pattern() {
    local pattern=$1
    local name=$2
    
    local pids=$(pgrep -f "$pattern" 2>/dev/null || true)
    
    if [ -z "$pids" ]; then
        echo -e "${YELLOW}⚠️  No $name processes found${NC}"
        return 0
    fi
    
    echo -e "${BLUE}🔹 Found $name processes: $pids${NC}"
    
    for pid in $pids; do
        kill_process "$pid" "$name" "$FORCE_KILL"
    done
}

# Function to kill processes using specific ports
kill_by_port() {
    local port=$1
    local name=$2
    
    local pids=$(lsof -ti :$port 2>/dev/null || true)
    
    if [ -z "$pids" ]; then
        echo -e "${YELLOW}⚠️  No processes found on port $port ($name)${NC}"
        return 0
    fi
    
    echo -e "${BLUE}🔹 Found processes on port $port ($name): $pids${NC}"
    
    for pid in $pids; do
        # Get process name for better logging
        local proc_name=$(ps -p $pid -o comm= 2>/dev/null || echo "Unknown")
        kill_process "$pid" "$name ($proc_name)" "$FORCE_KILL"
    done
}

# Read service status if available
API_PID=""
WEB_PID=""
NGINX_PID=""
API_PORT="5185"
WEB_PORT="5173"
NGINX_PORT="8080"

if [ -f "$WEB_UI_DIR/.service-status" ]; then
    echo -e "${BLUE}📋 Reading service status...${NC}"
    source "$WEB_UI_DIR/.service-status"
    echo -e "   API Port: $API_PORT, Web Port: $WEB_PORT, NGINX Port: $NGINX_PORT"
else
    echo -e "${YELLOW}⚠️  Service status file not found, using auto-detection${NC}"
fi

# Read PIDs from PID files
if [ -f "$PID_DIR/api.pid" ]; then
    API_PID=$(cat "$PID_DIR/api.pid" 2>/dev/null || true)
fi

if [ -f "$PID_DIR/web.pid" ]; then
    WEB_PID=$(cat "$PID_DIR/web.pid" 2>/dev/null || true)
fi

if [ -f "$PID_DIR/nginx.pid" ]; then
    NGINX_PID=$(cat "$PID_DIR/nginx.pid" 2>/dev/null || true)
fi

echo ""
echo -e "${PURPLE}🔍 Stopping services by PID files...${NC}"

# Stop services using PID files
kill_process "$API_PID" "Claude Batch Server API" "$FORCE_KILL"
kill_process "$WEB_PID" "Web UI Development Server" "$FORCE_KILL"
kill_process "$NGINX_PID" "NGINX Reverse Proxy" "$FORCE_KILL"

echo ""
echo -e "${PURPLE}🔍 Stopping services by process patterns...${NC}"

# Stop services by process patterns (fallback)
kill_by_pattern "ClaudeBatchServer" "Claude Batch Server"
kill_by_pattern "vite.*--port.*$WEB_PORT" "Vite Development Server"
kill_by_pattern "nginx.*claude-services" "NGINX (Claude Config)"

echo ""
echo -e "${PURPLE}🔍 Stopping services by port usage...${NC}"

# Stop services by port (ultimate fallback)
kill_by_port "$API_PORT" "Claude API"
kill_by_port "$WEB_PORT" "Web UI"
kill_by_port "$NGINX_PORT" "NGINX"

echo ""
echo -e "${PURPLE}🧹 Cleaning up resources...${NC}"

# Remove PID files
if [ -d "$PID_DIR" ]; then
    rm -f "$PID_DIR"/*.pid
    echo -e "${GREEN}   ✅ Cleaned up PID files${NC}"
fi

# Remove service status file
if [ -f "$WEB_UI_DIR/.service-status" ]; then
    rm -f "$WEB_UI_DIR/.service-status"
    echo -e "${GREEN}   ✅ Cleaned up service status${NC}"
fi

# Remove temporary NGINX configs
if [ -d "$WEB_UI_DIR/deployment/nginx" ]; then
    rm -f "$WEB_UI_DIR/deployment/nginx"/claude-services-*.conf
    echo -e "${GREEN}   ✅ Cleaned up temporary NGINX configs${NC}"
fi

# Restore vite.config.js if backup exists
if [ -f "$WEB_UI_DIR/vite.config.js.backup" ]; then
    mv "$WEB_UI_DIR/vite.config.js.backup" "$WEB_UI_DIR/vite.config.js"
    echo -e "${GREEN}   ✅ Restored original vite.config.js${NC}"
fi

# Check for any remaining processes
echo ""
echo -e "${PURPLE}🔍 Final verification...${NC}"

REMAINING_PROCESSES=""

# Check for remaining Claude processes
if pgrep -f "ClaudeBatchServer" >/dev/null 2>&1; then
    REMAINING_PROCESSES="$REMAINING_PROCESSES Claude Batch Server"
fi

if lsof -Pi :$API_PORT -sTCP:LISTEN >/dev/null 2>&1; then
    REMAINING_PROCESSES="$REMAINING_PROCESSES API-Port-$API_PORT"
fi

if lsof -Pi :$WEB_PORT -sTCP:LISTEN >/dev/null 2>&1; then
    REMAINING_PROCESSES="$REMAINING_PROCESSES Web-Port-$WEB_PORT"
fi

if lsof -Pi :$NGINX_PORT -sTCP:LISTEN >/dev/null 2>&1; then
    REMAINING_PROCESSES="$REMAINING_PROCESSES NGINX-Port-$NGINX_PORT"
fi

if [ -n "$REMAINING_PROCESSES" ]; then
    echo -e "${YELLOW}⚠️  Some processes may still be running:$REMAINING_PROCESSES${NC}"
    echo -e "${YELLOW}   Use --force flag for immediate termination: ${CYAN}./scripts/stop-services.sh --force${NC}"
    
    if [ "$FORCE_KILL" != "--force" ]; then
        echo ""
        echo -e "${BLUE}🔍 Detailed process information:${NC}"
        echo -e "${CYAN}   Processes on API port ($API_PORT):${NC}"
        lsof -Pi :$API_PORT -sTCP:LISTEN 2>/dev/null || echo "   None"
        
        echo -e "${CYAN}   Processes on Web port ($WEB_PORT):${NC}"
        lsof -Pi :$WEB_PORT -sTCP:LISTEN 2>/dev/null || echo "   None"
        
        echo -e "${CYAN}   Processes on NGINX port ($NGINX_PORT):${NC}"
        lsof -Pi :$NGINX_PORT -sTCP:LISTEN 2>/dev/null || echo "   None"
        
        echo -e "${CYAN}   Claude-related processes:${NC}"
        pgrep -f -l "ClaudeBatchServer\|claude.*server" 2>/dev/null || echo "   None"
    fi
else
    echo -e "${GREEN}✅ All Claude services have been stopped successfully${NC}"
fi

echo ""
echo -e "${CYAN}📋 Available Commands:${NC}"
echo -e "   Start services: ${PURPLE}./scripts/start-services.sh${NC}"
echo -e "   Check status: ${PURPLE}./scripts/check-status.sh${NC}"
echo -e "   View logs: ${PURPLE}ls -la logs/#{NC}"

echo ""
echo -e "${GREEN}🎯 Claude Server Stack shutdown complete!${NC}"