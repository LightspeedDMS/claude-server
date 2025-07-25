#!/bin/bash

# Claude Server Stack Startup Script
# Starts Claude Batch Server API, Web UI, and NGINX with configurable ports
#
# Usage: ./start-services.sh [API_PORT] [WEB_PORT] [NGINX_PORT]
# Example: ./start-services.sh 5185 5173 8080

set -e  # Exit on any error

# Color codes for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
PURPLE='\033[0;35m'
CYAN='\033[0;36m'
NC='\033[0m' # No Color

# Configuration with defaults
API_PORT=${1:-5185}
WEB_PORT=${2:-5173}
NGINX_PORT=${3:-8080}

# Paths
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
WEB_UI_DIR="$(dirname "$SCRIPT_DIR")"
API_DIR="$WEB_UI_DIR/../claude-batch-server"
NGINX_CONFIG_DIR="$WEB_UI_DIR/deployment/nginx"

# Dotnet path detection
DOTNET_CMD=""
if command -v dotnet >/dev/null 2>&1; then
    DOTNET_CMD="dotnet"
elif [ -f "$HOME/.dotnet/dotnet" ]; then
    DOTNET_CMD="$HOME/.dotnet/dotnet"
else
    echo -e "${RED}❌ .NET SDK not found. Please install .NET 8.0 SDK.${NC}"
    exit 1
fi

echo -e "${CYAN}🚀 Starting Claude Server Stack${NC}"
echo -e "${BLUE}📊 Configuration:${NC}"
echo -e "   API Port: ${YELLOW}$API_PORT${NC}"
echo -e "   Web UI Port: ${YELLOW}$WEB_PORT${NC}"
echo -e "   NGINX Port: ${YELLOW}$NGINX_PORT${NC}"
echo -e "   .NET Command: ${YELLOW}$DOTNET_CMD${NC}"
echo ""

# Function to check if port is in use
check_port() {
    local port=$1
    local service=$2
    if lsof -Pi :$port -sTCP:LISTEN -t >/dev/null 2>&1; then
        echo -e "${YELLOW}⚠️  Port $port is already in use (for $service)${NC}"
        echo -e "   You can kill existing processes with: ${CYAN}./scripts/stop-services.sh${NC}"
        return 1
    fi
    return 0
}

# Function to wait for service to be ready
wait_for_service() {
    local url=$1
    local service_name=$2
    local max_attempts=30
    local attempt=1
    
    echo -e "${BLUE}⏳ Waiting for $service_name to be ready...${NC}"
    
    while [ $attempt -le $max_attempts ]; do
        if curl -s -f "$url" >/dev/null 2>&1; then
            echo -e "${GREEN}✅ $service_name is ready!${NC}"
            return 0
        fi
        
        if [ $((attempt % 5)) -eq 0 ]; then
            echo -e "   Attempt $attempt/$max_attempts..."
        fi
        
        sleep 2
        attempt=$((attempt + 1))
    done
    
    echo -e "${RED}❌ $service_name failed to start within ${max_attempts}0 seconds${NC}"
    return 1
}

# Pre-flight checks
echo -e "${BLUE}🔍 Pre-flight checks...${NC}"

# Check if required directories exist
if [ ! -d "$API_DIR" ]; then
    echo -e "${RED}❌ Claude Batch Server directory not found: $API_DIR${NC}"
    exit 1
fi

if [ ! -f "$API_DIR/src/ClaudeBatchServer.Api/ClaudeBatchServer.Api.csproj" ]; then
    echo -e "${RED}❌ Claude Batch Server project not found${NC}"
    exit 1
fi

# Check ports
check_port $API_PORT "Claude Batch Server API" || exit 1
check_port $WEB_PORT "Web UI Development Server" || exit 1
check_port $NGINX_PORT "NGINX Reverse Proxy" || exit 1

echo -e "${GREEN}✅ Pre-flight checks passed${NC}"
echo ""

# Create PID file directory
PID_DIR="$WEB_UI_DIR/.pids"
mkdir -p "$PID_DIR"

# Create logs directory for all services
mkdir -p "$WEB_UI_DIR/logs"

# Start Claude Batch Server API
echo -e "${PURPLE}🔧 Starting Claude Batch Server API...${NC}"
cd "$API_DIR"

# Set environment variables for the API
export ASPNETCORE_URLS="http://localhost:$API_PORT"
export ASPNETCORE_ENVIRONMENT="Development"
export Serilog__WriteTo__1__Args__path="$WEB_UI_DIR/logs/api-.log"

# Start API server in background
$DOTNET_CMD run --project src/ClaudeBatchServer.Api/ClaudeBatchServer.Api.csproj > "$WEB_UI_DIR/logs/api.log" 2>&1 &
API_PID=$!
echo $API_PID > "$PID_DIR/api.pid"

echo -e "   API PID: ${YELLOW}$API_PID${NC}"
echo -e "   Log file: ${CYAN}$WEB_UI_DIR/logs/api.log${NC}"

# Wait for API to be ready
wait_for_service "http://localhost:$API_PORT/swagger/v1/swagger.json" "Claude Batch Server API"

# Start Web UI Development Server
echo -e "${PURPLE}🌐 Starting Web UI Development Server...${NC}"
cd "$WEB_UI_DIR"

# Create logs directory
mkdir -p logs

# Update vite.config.js with correct API port if needed
if [ -f "vite.config.js" ]; then
    # Backup original
    cp vite.config.js vite.config.js.backup
    
    # Update proxy target
    sed -i.tmp "s|target: 'http://localhost:[0-9]*'|target: 'http://localhost:$API_PORT'|g" vite.config.js
    rm -f vite.config.js.tmp
fi

# Start Vite dev server
npm run dev -- --port $WEB_PORT --host > "$WEB_UI_DIR/logs/web.log" 2>&1 &
WEB_PID=$!
echo $WEB_PID > "$PID_DIR/web.pid"

echo -e "   Web UI PID: ${YELLOW}$WEB_PID${NC}"
echo -e "   Log file: ${CYAN}$WEB_UI_DIR/logs/web.log${NC}"

# Wait for Web UI to be ready
wait_for_service "http://localhost:$WEB_PORT" "Web UI Development Server"

# Start NGINX Reverse Proxy
echo -e "${PURPLE}🔀 Starting NGINX Reverse Proxy...${NC}"

# Create dynamic NGINX config
NGINX_CONFIG="$NGINX_CONFIG_DIR/claude-services-$NGINX_PORT.conf"
cat > "$NGINX_CONFIG" << EOF
# Claude Server Stack NGINX Configuration
# Generated automatically by start-services.sh

upstream claude_api {
    server localhost:$API_PORT;
}

upstream claude_web {
    server localhost:$WEB_PORT;
}

server {
    listen $NGINX_PORT;
    server_name localhost;
    
    # Security headers
    add_header X-Frame-Options DENY;
    add_header X-Content-Type-Options nosniff;
    add_header X-XSS-Protection "1; mode=block";
    
    # API routes
    location /api/ {
        proxy_pass http://claude_api/;
        proxy_set_header Host \$host;
        proxy_set_header X-Real-IP \$remote_addr;
        proxy_set_header X-Forwarded-For \$proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto \$scheme;
        
        # CORS headers for development
        add_header 'Access-Control-Allow-Origin' 'http://localhost:$WEB_PORT' always;
        add_header 'Access-Control-Allow-Methods' 'GET, POST, PUT, DELETE, OPTIONS' always;
        add_header 'Access-Control-Allow-Headers' 'Authorization, Content-Type, Accept' always;
        add_header 'Access-Control-Allow-Credentials' 'true' always;
        
        if (\$request_method = 'OPTIONS') {
            return 204;
        }
    }
    
    # Web UI routes
    location / {
        proxy_pass http://claude_web/;
        proxy_set_header Host \$host;
        proxy_set_header X-Real-IP \$remote_addr;
        proxy_set_header X-Forwarded-For \$proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto \$scheme;
        
        # WebSocket support for Vite HMR
        proxy_http_version 1.1;
        proxy_set_header Upgrade \$http_upgrade;
        proxy_set_header Connection "upgrade";
    }
    
    # Health check endpoint
    location /health {
        access_log off;
        return 200 "OK";
        add_header Content-Type text/plain;
    }
}
EOF

# Start NGINX
if command -v nginx >/dev/null 2>&1; then
    nginx -c "$NGINX_CONFIG" -p "$NGINX_CONFIG_DIR" > "$WEB_UI_DIR/logs/nginx.log" 2>&1 &
    NGINX_PID=$!
    echo $NGINX_PID > "$PID_DIR/nginx.pid"
    
    echo -e "   NGINX PID: ${YELLOW}$NGINX_PID${NC}"
    echo -e "   Config: ${CYAN}$NGINX_CONFIG${NC}"
    echo -e "   Log file: ${CYAN}$WEB_UI_DIR/logs/nginx.log${NC}"
    
    # Wait for NGINX to be ready
    wait_for_service "http://localhost:$NGINX_PORT/health" "NGINX Reverse Proxy"
    
    NGINX_ENABLED=true
else
    echo -e "${YELLOW}⚠️  NGINX not found, skipping reverse proxy setup${NC}"
    NGINX_ENABLED=false
fi

# Create status file
cat > "$WEB_UI_DIR/.service-status" << EOF
# Claude Server Stack Status
# Generated: $(date)

API_PORT=$API_PORT
WEB_PORT=$WEB_PORT
NGINX_PORT=$NGINX_PORT
API_PID=$API_PID
WEB_PID=$WEB_PID
NGINX_PID=${NGINX_PID:-""}
NGINX_ENABLED=$NGINX_ENABLED
EOF

echo ""
echo -e "${GREEN}🎉 Claude Server Stack started successfully!${NC}"
echo ""
echo -e "${CYAN}📡 Service URLs:${NC}"
echo -e "   Claude Batch Server API: ${YELLOW}http://localhost:$API_PORT${NC}"
echo -e "   └─ Swagger UI: ${BLUE}http://localhost:$API_PORT/swagger${NC}"
echo -e "   └─ Health: ${BLUE}http://localhost:$API_PORT/swagger/v1/swagger.json${NC}"
echo ""
echo -e "   Web UI (Development): ${YELLOW}http://localhost:$WEB_PORT${NC}"
echo -e "   └─ Login Page: ${BLUE}http://localhost:$WEB_PORT${NC}"
echo ""

if [ "$NGINX_ENABLED" = true ]; then
    echo -e "   NGINX Reverse Proxy: ${YELLOW}http://localhost:$NGINX_PORT${NC}"
    echo -e "   └─ Complete App: ${BLUE}http://localhost:$NGINX_PORT${NC}"
    echo -e "   └─ API via Proxy: ${BLUE}http://localhost:$NGINX_PORT/api${NC}"
    echo -e "   └─ Health Check: ${BLUE}http://localhost:$NGINX_PORT/health${NC}"
    echo ""
    echo -e "${GREEN}🌟 Recommended: Use NGINX URL for full-stack testing${NC}"
else
    echo -e "${YELLOW}⚠️  NGINX not available - use individual service URLs${NC}"
fi

echo ""
echo -e "${CYAN}📋 Management Commands:${NC}"
echo -e "   Stop services: ${PURPLE}./scripts/stop-services.sh${NC}"
echo -e "   View logs: ${PURPLE}tail -f logs/*.log${NC}"
echo -e "   Check status: ${PURPLE}./scripts/check-status.sh${NC}"
echo ""
echo -e "${BLUE}🔧 Test Credentials (from server .env):${NC}"

# Read test credentials from server .env
if [ -f "$API_DIR/.env" ]; then
    TEST_USER=$(grep "TEST_USERNAME" "$API_DIR/.env" | cut -d'=' -f2)
    echo -e "   Username: ${YELLOW}$TEST_USER${NC}"
    echo -e "   Password: ${YELLOW}[Configured in server .env]${NC}"
else
    echo -e "   ${YELLOW}Configure in: $API_DIR/.env${NC}"
fi

echo ""
echo -e "${GREEN}✅ All services are ready for development and testing!${NC}"