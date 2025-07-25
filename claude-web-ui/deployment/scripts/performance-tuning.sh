#!/bin/bash

# Claude Web UI Performance Tuning Script
# Optimizes NGINX, system settings, and application performance

set -euo pipefail

# Configuration
LOG_FILE="/var/log/claude-web-ui/performance-tuning.log"
NGINX_CONFIG="/etc/nginx/sites-available/claude-web-ui"
MAIN_NGINX_CONFIG="/etc/nginx/nginx.conf"

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
    log "ERROR" "Performance tuning failed: $1"
    exit 1
}

# Check if running as root
check_permissions() {
    if [[ $EUID -ne 0 ]]; then
        log "ERROR" "This script must be run as root or with sudo"
        exit 1
    fi
}

# Detect system resources
detect_system_resources() {
    log "INFO" "Detecting system resources..."
    
    # CPU cores
    local cpu_cores=$(nproc)
    log "INFO" "CPU cores: $cpu_cores"
    
    # Memory in GB
    local memory_gb=$(free -g | awk '/^Mem:/{print $2}')
    log "INFO" "Memory: ${memory_gb}GB"
    
    # Disk type (SSD vs HDD)
    local disk_type="unknown"
    if lsblk -d -o name,rota | grep -q "0$"; then
        disk_type="SSD"
    else
        disk_type="HDD"
    fi
    log "INFO" "Storage type: $disk_type"
    
    # Export for use in optimization functions
    export DETECTED_CPU_CORES="$cpu_cores"
    export DETECTED_MEMORY_GB="$memory_gb"
    export DETECTED_DISK_TYPE="$disk_type"
}

# Optimize NGINX main configuration
optimize_nginx_main() {
    log "INFO" "Optimizing NGINX main configuration..."
    
    # Backup original config
    cp "$MAIN_NGINX_CONFIG" "$MAIN_NGINX_CONFIG.backup-$(date +%Y%m%d-%H%M%S)"
    
    # Calculate optimal worker processes and connections
    local worker_processes="$DETECTED_CPU_CORES"
    local worker_connections=$((1024 * DETECTED_CPU_CORES))
    
    # Adjust based on memory
    if [[ $DETECTED_MEMORY_GB -ge 8 ]]; then
        worker_connections=$((2048 * DETECTED_CPU_CORES))
    elif [[ $DETECTED_MEMORY_GB -ge 4 ]]; then
        worker_connections=$((1536 * DETECTED_CPU_CORES))
    fi
    
    # Create optimized nginx.conf
    cat > "$MAIN_NGINX_CONFIG" << EOF
# Optimized NGINX configuration for Claude Web UI
user www-data;
worker_processes $worker_processes;
worker_rlimit_nofile 65535;
pid /run/nginx.pid;

events {
    worker_connections $worker_connections;
    use epoll;
    multi_accept on;
}

http {
    # Basic Settings
    sendfile on;
    tcp_nopush on;
    tcp_nodelay on;
    keepalive_timeout 30;
    keepalive_requests 100;
    types_hash_max_size 2048;
    server_tokens off;
    
    # File Upload Settings
    client_max_body_size 50M;
    client_body_buffer_size 128k;
    client_header_buffer_size 1k;
    large_client_header_buffers 4 4k;
    client_body_timeout 12;
    client_header_timeout 12;
    send_timeout 10;
    
    # MIME Types
    include /etc/nginx/mime.types;
    default_type application/octet-stream;
    
    # Logging Format
    log_format main '\$remote_addr - \$remote_user [\$time_local] "\$request" '
                    '\$status \$body_bytes_sent "\$http_referer" '
                    '"\$http_user_agent" "\$http_x_forwarded_for" '
                    'rt=\$request_time uct="\$upstream_connect_time" '
                    'uht="\$upstream_header_time" urt="\$upstream_response_time"';
    
    # Gzip Compression
    gzip on;
    gzip_vary on;
    gzip_proxied any;
    gzip_comp_level 6;
    gzip_min_length 1024;
    gzip_types
        text/plain
        text/css
        text/xml
        text/javascript
        application/javascript
        application/xml+rss
        application/json
        application/xml
        image/svg+xml
        font/truetype
        font/opentype
        application/vnd.ms-fontobject;
    
    # Brotli Compression (if available)
    # brotli on;
    # brotli_comp_level 6;
    # brotli_types text/plain text/css application/json application/javascript text/xml application/xml application/xml+rss text/javascript;
    
    # Rate Limiting
    limit_req_status 429;
    limit_conn_status 429;
    
    # SSL Optimization
    ssl_protocols TLSv1.2 TLSv1.3;
    ssl_ciphers ECDHE-RSA-AES256-GCM-SHA512:DHE-RSA-AES256-GCM-SHA512:ECDHE-RSA-AES256-GCM-SHA384:DHE-RSA-AES256-GCM-SHA384:ECDHE-RSA-AES256-SHA384;
    ssl_prefer_server_ciphers off;
    ssl_session_cache shared:SSL:10m;
    ssl_session_timeout 10m;
    ssl_session_tickets off;
    ssl_buffer_size 4k;
    
    # Connection Pooling
    upstream_keepalive_connections 32;
    upstream_keepalive_requests 100;
    upstream_keepalive_timeout 60s;
    
    # File Cache
    open_file_cache max=1000 inactive=20s;
    open_file_cache_valid 30s;
    open_file_cache_min_uses 2;
    open_file_cache_errors on;
    
    # Include Virtual Host Configs
    include /etc/nginx/conf.d/*.conf;
    include /etc/nginx/sites-enabled/*;
}
EOF
    
    log "SUCCESS" "NGINX main configuration optimized"
}

# Optimize site-specific NGINX configuration
optimize_nginx_site() {
    log "INFO" "Optimizing site-specific NGINX configuration..."
    
    if [[ ! -f "$NGINX_CONFIG" ]]; then
        log "WARN" "Site configuration not found, skipping site optimization"
        return
    fi
    
    # Backup original config
    cp "$NGINX_CONFIG" "$NGINX_CONFIG.backup-$(date +%Y%m%d-%H%M%S)"
    
    # Add performance optimizations to existing config
    # This assumes the current config structure and adds optimizations
    
    # Create temporary file with optimizations
    local temp_config="/tmp/nginx-optimized-$$"
    
    # Read existing config and add optimizations
    awk '
    /^upstream claude_api/ {
        print $0
        getline
        print $0
        print "    keepalive 32;"
        print "    keepalive_requests 100;"
        print "    keepalive_timeout 60s;"
        next
    }
    /location ~\* \\.\(js\|css\|png\|jpg\|jpeg\|gif\|ico\|svg\|woff\|woff2\|ttf\|eot\)\$/ {
        print $0
        while (getline && $0 !~ /^    }/) {
            if ($0 ~ /expires/) {
                print "        expires 1y;"
            } else if ($0 ~ /add_header Cache-Control/) {
                print "        add_header Cache-Control \"public, immutable\";"
            } else {
                print $0
            }
        }
        print "        # Additional performance headers"
        print "        add_header X-Cache-Status \"HIT\";"
        print "        add_header Vary \"Accept-Encoding\";"
        print "        # Enable sendfile for static assets"
        print "        sendfile on;"
        print "        tcp_nopush on;"
        print $0
        next
    }
    /location \/api\// {
        print $0
        while (getline && $0 !~ /^    }/) {
            if ($0 ~ /proxy_pass/) {
                print $0
                print "        # Connection pooling"
                print "        proxy_http_version 1.1;"
                print "        proxy_set_header Connection \"\";"
            } else if ($0 ~ /proxy_connect_timeout/) {
                print "        proxy_connect_timeout 5s;"
            } else if ($0 ~ /proxy_send_timeout/) {
                print "        proxy_send_timeout 10s;"
            } else if ($0 ~ /proxy_read_timeout/) {
                print "        proxy_read_timeout 10s;"
            } else {
                print $0
            }
        }
        print "        # Additional proxy optimizations"
        print "        proxy_buffering on;"
        print "        proxy_buffer_size 4k;"
        print "        proxy_buffers 8 4k;"
        print "        proxy_busy_buffers_size 8k;"
        print $0
        next
    }
    { print $0 }
    ' "$NGINX_CONFIG" > "$temp_config"
    
    # Replace original with optimized version
    mv "$temp_config" "$NGINX_CONFIG"
    
    log "SUCCESS" "Site-specific NGINX configuration optimized"
}

# Optimize system settings
optimize_system_settings() {
    log "INFO" "Optimizing system settings..."
    
    # Backup original sysctl settings
    cp /etc/sysctl.conf /etc/sysctl.conf.backup-$(date +%Y%m%d-%H%M%S) 2>/dev/null || true
    
    # Network optimizations
    cat >> /etc/sysctl.conf << 'EOF'

# Claude Web UI Performance Optimizations
# Network settings
net.core.somaxconn = 65535
net.core.netdev_max_backlog = 5000
net.ipv4.tcp_max_syn_backlog = 8192
net.ipv4.tcp_congestion_control = bbr
net.ipv4.tcp_slow_start_after_idle = 0
net.ipv4.tcp_tw_reuse = 1
net.ipv4.ip_local_port_range = 10240 65535

# File system settings
fs.file-max = 65535

# Memory settings
vm.swappiness = 10
vm.dirty_ratio = 15
vm.dirty_background_ratio = 5
EOF
    
    # Apply settings
    sysctl -p
    
    # Optimize file limits
    cat >> /etc/security/limits.conf << 'EOF'

# Claude Web UI file limits
www-data soft nofile 65535
www-data hard nofile 65535
nginx soft nofile 65535
nginx hard nofile 65535
EOF
    
    # Create systemd override for nginx
    mkdir -p /etc/systemd/system/nginx.service.d
    cat > /etc/systemd/system/nginx.service.d/override.conf << 'EOF'
[Service]
LimitNOFILE=65535
EOF
    
    systemctl daemon-reload
    
    log "SUCCESS" "System settings optimized"
}

# Install and configure Redis for caching (optional)
install_redis_cache() {
    log "INFO" "Installing Redis for caching..."
    
    if command -v redis-server &> /dev/null; then
        log "INFO" "Redis already installed, configuring..."
    else
        apt-get update
        apt-get install -y redis-server
    fi
    
    # Configure Redis for optimal performance
    cat > /etc/redis/redis.conf << 'EOF'
# Claude Web UI Redis Configuration
bind 127.0.0.1
port 6379
timeout 300
keepalive 60
tcp-backlog 511

# Memory configuration
maxmemory 128mb
maxmemory-policy allkeys-lru

# Persistence (for caching, we can disable)
save ""
appendonly no

# Performance
tcp-nodelay yes
databases 1
EOF
    
    # Enable and start Redis
    systemctl enable redis-server
    systemctl restart redis-server
    
    log "SUCCESS" "Redis cache configured"
}

# Configure NGINX caching
configure_nginx_caching() {
    log "INFO" "Configuring NGINX caching..."
    
    # Create cache directories
    mkdir -p /var/cache/nginx/claude-web-ui
    chown -R www-data:www-data /var/cache/nginx/claude-web-ui
    
    # Add cache configuration to main nginx.conf
    if ! grep -q "proxy_cache_path" "$MAIN_NGINX_CONFIG"; then
        sed -i '/http {/a\
    # Proxy Cache Configuration\
    proxy_cache_path /var/cache/nginx/claude-web-ui levels=1:2 keys_zone=claude_cache:10m max_size=100m inactive=60m use_temp_path=off;\
    proxy_cache_key $scheme$request_method$host$request_uri;\
    proxy_cache_valid 200 302 10m;\
    proxy_cache_valid 404 1m;\
    proxy_cache_use_stale error timeout updating http_500 http_502 http_503 http_504;' "$MAIN_NGINX_CONFIG"
    fi
    
    log "SUCCESS" "NGINX caching configured"
}

# Optimize log rotation
optimize_log_rotation() {
    log "INFO" "Optimizing log rotation..."
    
    # Enhanced logrotate configuration
    cat > /etc/logrotate.d/claude-web-ui-optimized << 'EOF'
/var/log/claude-web-ui/*.log {
    daily
    missingok
    rotate 14
    compress
    delaycompress
    notifempty
    create 644 root root
    sharedscripts
    prerotate
        /usr/bin/find /var/log/claude-web-ui -name "*.log" -size +100M -exec truncate -s 50M {} \;
    endscript
    postrotate
        systemctl reload nginx > /dev/null 2>&1 || true
    endscript
}

/var/log/nginx/claude-web-ui-*.log {
    daily
    missingok
    rotate 14
    compress
    delaycompress
    notifempty
    create 644 www-data www-data
    sharedscripts
    prerotate
        /usr/bin/find /var/log/nginx -name "claude-web-ui-*.log" -size +100M -exec truncate -s 50M {} \;
    endscript
    postrotate
        systemctl reload nginx > /dev/null 2>&1 || true
    endscript
}
EOF
    
    log "SUCCESS" "Log rotation optimized"
}

# Install monitoring tools
install_monitoring_tools() {
    log "INFO" "Installing monitoring tools..."
    
    # Install htop, iotop, and other monitoring tools
    apt-get update
    apt-get install -y htop iotop nethogs iftop ncdu

    # Install nginx-extras for additional modules if available
    if apt-cache search nginx-extras | grep -q nginx-extras; then
        apt-get install -y nginx-extras
        log "SUCCESS" "NGINX extras installed"
    fi
    
    log "SUCCESS" "Monitoring tools installed"
}

# Create performance monitoring script
create_performance_monitor() {
    log "INFO" "Creating performance monitoring script..."
    
    cat > /usr/local/bin/claude-performance-monitor << 'EOF'
#!/bin/bash

# Claude Web UI Performance Monitor
# Collects and reports performance metrics

LOG_FILE="/var/log/claude-web-ui/performance-monitor.log"
METRICS_FILE="/var/log/claude-web-ui/performance-metrics.json"

# Collect system metrics
collect_metrics() {
    local timestamp=$(date -Iseconds)
    local load_avg=$(uptime | awk -F'load average:' '{print $2}' | tr -d ' ')
    local memory_usage=$(free | grep '^Mem:' | awk '{printf "%.1f", $3/$2 * 100.0}')
    local disk_usage=$(df /var/www/claude-web-ui | awk 'NR==2 {print $5}' | sed 's/%//')
    local nginx_connections=$(ss -tuln | grep :80 | wc -l)
    local nginx_processes=$(pgrep nginx | wc -l)
    
    # NGINX status (if stub_status is available)
    local nginx_active_connections="0"
    local nginx_requests_per_second="0"
    
    if curl -s http://localhost/nginx_status >/dev/null 2>&1; then
        nginx_active_connections=$(curl -s http://localhost/nginx_status | grep "Active connections" | awk '{print $3}')
    fi
    
    # Create metrics JSON
    cat > "$METRICS_FILE" << EOF_METRICS
{
    "timestamp": "$timestamp",
    "system": {
        "load_average": "$load_avg",
        "memory_usage_percent": $memory_usage,
        "disk_usage_percent": $disk_usage
    },
    "nginx": {
        "active_connections": $nginx_active_connections,
        "listening_connections": $nginx_connections,
        "worker_processes": $nginx_processes
    },
    "application": {
        "health_check": $(curl -k -s -o /dev/null -w "%{http_code}" http://localhost/health 2>/dev/null || echo "0")
    }
}
EOF_METRICS
    
    echo "$(date): Metrics collected - Load: $load_avg, Memory: ${memory_usage}%, Disk: ${disk_usage}%" >> "$LOG_FILE"
}

# Check for performance issues
check_performance() {
    if [[ -f "$METRICS_FILE" ]]; then
        local memory_usage=$(grep '"memory_usage_percent"' "$METRICS_FILE" | cut -d: -f2 | tr -d ' ,')
        local disk_usage=$(grep '"disk_usage_percent"' "$METRICS_FILE" | cut -d: -f2 | tr -d ' ,')
        
        if (( $(echo "$memory_usage > 85" | bc -l) )); then
            echo "$(date): WARNING - High memory usage: ${memory_usage}%" >> "$LOG_FILE"
        fi
        
        if (( disk_usage > 85 )); then
            echo "$(date): WARNING - High disk usage: ${disk_usage}%" >> "$LOG_FILE"
        fi
    fi
}

# Main execution
collect_metrics
check_performance
EOF
    
    chmod +x /usr/local/bin/claude-performance-monitor
    
    # Create systemd timer for regular monitoring
    cat > /etc/systemd/system/claude-performance-monitor.service << 'EOF'
[Unit]
Description=Claude Web UI Performance Monitor
After=network.target

[Service]
Type=oneshot
ExecStart=/usr/local/bin/claude-performance-monitor
User=root
EOF
    
    cat > /etc/systemd/system/claude-performance-monitor.timer << 'EOF'
[Unit]
Description=Run Claude Performance Monitor every 5 minutes
Requires=claude-performance-monitor.service

[Timer]
OnCalendar=*:0/5
Persistent=true

[Install]
WantedBy=timers.target
EOF
    
    systemctl daemon-reload
    systemctl enable claude-performance-monitor.timer
    systemctl start claude-performance-monitor.timer
    
    log "SUCCESS" "Performance monitoring configured"
}

# Test configuration
test_optimizations() {
    log "INFO" "Testing optimized configuration..."
    
    # Test NGINX configuration
    if nginx -t; then
        log "SUCCESS" "NGINX configuration test passed"
    else
        error_exit "NGINX configuration test failed"
    fi
    
    # Test system limits
    local nginx_limit=$(su -s /bin/bash -c 'ulimit -n' www-data)
    if [[ $nginx_limit -ge 65535 ]]; then
        log "SUCCESS" "File limits configured correctly"
    else
        log "WARN" "File limits may not be optimal: $nginx_limit"
    fi
    
    # Test Redis connection (if installed)
    if command -v redis-cli &> /dev/null; then
        if redis-cli ping | grep -q PONG; then
            log "SUCCESS" "Redis is responding"
        else
            log "WARN" "Redis is not responding"
        fi
    fi
    
    log "SUCCESS" "Configuration tests completed"
}

# Restart services
restart_services() {
    log "INFO" "Restarting services to apply optimizations..."
    
    # Reload systemd daemon
    systemctl daemon-reload
    
    # Restart NGINX
    if systemctl restart nginx; then
        log "SUCCESS" "NGINX restarted successfully"
    else
        error_exit "Failed to restart NGINX"
    fi
    
    # Restart Redis if installed
    if systemctl is-active --quiet redis-server; then
        systemctl restart redis-server
        log "SUCCESS" "Redis restarted successfully"
    fi
    
    log "SUCCESS" "Services restarted"
}

# Show optimization summary
show_summary() {
    log "INFO" "Performance Optimization Summary:"
    echo ""
    echo -e "${BLUE}NGINX Configuration:${NC}"
    echo -e "  Worker processes: $DETECTED_CPU_CORES"
    echo -e "  Worker connections: $((1024 * DETECTED_CPU_CORES * (DETECTED_MEMORY_GB >= 4 ? 2 : 1)))"
    echo -e "  Keepalive timeout: 30s"
    echo -e "  Gzip compression: Enabled"
    echo ""
    echo -e "${BLUE}System Optimizations:${NC}"
    echo -e "  File limits: 65535"
    echo -e "  TCP optimization: Enabled"
    echo -e "  Memory management: Optimized"
    echo ""
    echo -e "${BLUE}Monitoring:${NC}"
    echo -e "  Performance monitor: /usr/local/bin/claude-performance-monitor"
    echo -e "  Metrics file: /var/log/claude-web-ui/performance-metrics.json"
    echo -e "  Log file: $LOG_FILE"
    echo ""
    echo -e "${BLUE}Cache:${NC}"
    echo -e "  NGINX cache: /var/cache/nginx/claude-web-ui"
    if command -v redis-server &> /dev/null; then
        echo -e "  Redis cache: Enabled"
    else
        echo -e "  Redis cache: Not installed"
    fi
}

# Main execution
main() {
    log "INFO" "Starting performance optimization for Claude Web UI..."
    
    check_permissions
    detect_system_resources
    optimize_nginx_main
    optimize_nginx_site
    optimize_system_settings
    configure_nginx_caching
    optimize_log_rotation
    install_monitoring_tools
    create_performance_monitor
    
    # Optional optimizations
    if [[ "${1:-}" == "--with-redis" ]]; then
        install_redis_cache
    fi
    
    test_optimizations
    restart_services
    show_summary
    
    log "SUCCESS" "Performance optimization completed successfully!"
}

# Handle script arguments
case "${1:-}" in
    --help|-h)
        echo "Usage: $0 [options]"
        echo ""
        echo "Options:"
        echo "  --with-redis    Install and configure Redis cache"
        echo "  --help, -h      Show this help message"
        echo ""
        echo "This script optimizes:"
        echo "  - NGINX configuration for better performance"
        echo "  - System kernel parameters"
        echo "  - File limits and network settings"
        echo "  - Caching and compression"
        echo "  - Log rotation and monitoring"
        exit 0
        ;;
esac

# Run main function
main "$@"