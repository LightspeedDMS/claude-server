#!/bin/bash

# Claude Server - Restart API
# Restarts the Claude Batch Server API to pick up authentication changes
#
# Usage: ./restart-api.sh

# Color codes for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
PURPLE='\033[0;35m'
CYAN='\033[0;36m'
NC='\033[0m' # No Color

# Configuration
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
WEB_UI_DIR="$(dirname "$SCRIPT_DIR")/../claude-web-ui"

echo -e "${CYAN}🔄 Restarting Claude Server API${NC}"
echo ""

# Check if the Web UI scripts exist
if [ -f "$WEB_UI_DIR/scripts/stop-services.sh" ] && [ -f "$WEB_UI_DIR/scripts/start-services.sh" ]; then
    echo -e "${BLUE}📋 Using Web UI service management scripts...${NC}"
    
    # Stop services
    echo -e "${YELLOW}⏹️  Stopping services...${NC}"
    cd "$WEB_UI_DIR"
    ./scripts/stop-services.sh
    
    echo ""
    echo -e "${YELLOW}▶️  Starting services...${NC}"
    ./scripts/start-services.sh
    
else
    echo -e "${YELLOW}⚠️  Web UI service scripts not found, trying direct process management...${NC}"
    
    # Try to kill API processes directly
    API_PIDS=$(pgrep -f "ClaudeBatchServer.Api" || true)
    
    if [ -n "$API_PIDS" ]; then
        echo -e "${BLUE}🔹 Stopping API processes: $API_PIDS${NC}"
        kill $API_PIDS
        sleep 2
        
        # Force kill if still running
        REMAINING_PIDS=$(pgrep -f "ClaudeBatchServer.Api" || true)
        if [ -n "$REMAINING_PIDS" ]; then
            echo -e "${YELLOW}🔹 Force stopping remaining processes...${NC}"
            kill -9 $REMAINING_PIDS
        fi
    else
        echo -e "${YELLOW}⚠️  No API processes found running${NC}"
    fi
    
    echo -e "${BLUE}▶️  Starting API manually...${NC}"
    cd "$(dirname "$SCRIPT_DIR")"
    
    # Find dotnet command
    DOTNET_CMD=""
    if command -v dotnet >/dev/null 2>&1; then
        DOTNET_CMD="dotnet"
    elif [ -f "$HOME/.dotnet/dotnet" ]; then
        DOTNET_CMD="$HOME/.dotnet/dotnet"
    else
        echo -e "${RED}❌ .NET SDK not found${NC}"
        exit 1
    fi
    
    # Start API
    export ASPNETCORE_URLS="http://localhost:5000"
    export ASPNETCORE_ENVIRONMENT="Development"
    
    echo -e "${BLUE}🚀 Starting Claude Batch Server API on port 5000...${NC}"
    $DOTNET_CMD run --project src/ClaudeBatchServer.Api/ClaudeBatchServer.Api.csproj &
    
    # Wait a moment for startup
    sleep 3
    
    # Check if it's running
    if curl -s -f "http://localhost:5000/swagger/v1/swagger.json" >/dev/null 2>&1; then
        echo -e "${GREEN}✅ API started successfully${NC}"
    else
        echo -e "${YELLOW}⚠️  API may still be starting up...${NC}"
    fi
fi

echo ""
echo -e "${GREEN}🎉 Claude Server API restart complete!${NC}"
echo ""
echo -e "${CYAN}📡 Service URLs:${NC}"
echo -e "   API: ${YELLOW}http://localhost:5000${NC}"
echo -e "   Swagger: ${BLUE}http://localhost:5000/swagger${NC}"
echo -e "   Web UI: ${BLUE}http://localhost:5173${NC}"