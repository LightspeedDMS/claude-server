#!/bin/bash

# Health Check Script for Claude Web UI
# Monitors application health and generates alerts

set -euo pipefail

# Configuration
HEALTH_URL="${1:-http://localhost/health}"
API_URL="${2:-http://localhost/api/health}"
LOG_FILE="/var/log/claude-web-ui/health-check.log"
STATUS_FILE="/var/log/claude-web-ui/health-status.json"
ALERT_EMAIL="${ALERT_EMAIL:-admin@localhost}"
MAX_RESPONSE_TIME=5  # seconds
RETRY_COUNT=3
RETRY_DELAY=2

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

# Check URL with retries
check_url() {
    local url=$1
    local name=$2
    local max_time=$3
    
    for ((i=1; i<=RETRY_COUNT; i++)); do
        log "INFO" "Checking $name (attempt $i/$RETRY_COUNT): $url"
        
        local start_time=$(date +%s.%N)
        local http_code
        local response_time
        
        if http_code=$(curl -k -s -o /dev/null -w "%{http_code}" --max-time "$max_time" "$url"); then
            local end_time=$(date +%s.%N)
            response_time=$(echo "$end_time - $start_time" | bc)
            
            if [[ "$http_code" -eq 200 ]]; then
                log "SUCCESS" "$name is healthy (HTTP $http_code, ${response_time}s)"
                return 0
            else
                log "WARN" "$name returned HTTP $http_code (${response_time}s)"
            fi
        else
            log "ERROR" "$name check failed (timeout or connection error)"
        fi
        
        if [[ $i -lt $RETRY_COUNT ]]; then
            log "INFO" "Retrying in ${RETRY_DELAY} seconds..."
            sleep "$RETRY_DELAY"
        fi
    done
    
    return 1
}

# Check NGINX status
check_nginx() {
    log "INFO" "Checking NGINX status..."
    
    if systemctl is-active --quiet nginx; then
        log "SUCCESS" "NGINX is running"
        return 0
    else
        log "ERROR" "NGINX is not running"
        return 1
    fi
}

# Check disk space
check_disk_space() {
    log "INFO" "Checking disk space..."
    
    local usage=$(df /var/www/claude-web-ui | awk 'NR==2 {print $5}' | sed 's/%//')
    
    if [[ $usage -lt 80 ]]; then
        log "SUCCESS" "Disk usage is acceptable: ${usage}%"
        return 0
    elif [[ $usage -lt 90 ]]; then
        log "WARN" "Disk usage is high: ${usage}%"
        return 0
    else
        log "ERROR" "Disk usage is critical: ${usage}%"
        return 1
    fi
}

# Check memory usage
check_memory() {
    log "INFO" "Checking memory usage..."
    
    local mem_info=$(free | grep '^Mem:')
    local total=$(echo "$mem_info" | awk '{print $2}')
    local used=$(echo "$mem_info" | awk '{print $3}')
    local usage=$((used * 100 / total))
    
    if [[ $usage -lt 80 ]]; then
        log "SUCCESS" "Memory usage is acceptable: ${usage}%"
        return 0
    elif [[ $usage -lt 90 ]]; then
        log "WARN" "Memory usage is high: ${usage}%"
        return 0
    else
        log "ERROR" "Memory usage is critical: ${usage}%"
        return 1
    fi
}

# Check SSL certificate
check_ssl_certificate() {
    if [[ "$HEALTH_URL" =~ ^https:// ]]; then
        log "INFO" "Checking SSL certificate..."
        
        local cert_file="/etc/ssl/certs/claude-web-ui.crt"
        
        if [[ -f "$cert_file" ]]; then
            if openssl x509 -checkend 86400 -noout -in "$cert_file" >/dev/null; then
                local days_left=$(openssl x509 -in "$cert_file" -checkend 0 -noout 2>/dev/null && echo $(($(date -d "$(openssl x509 -in "$cert_file" -dates -noout | grep "notAfter" | sed 's/notAfter=//')" +%s) - $(date +%s))) / 86400 || echo "0")
                
                if [[ $days_left -gt 30 ]]; then
                    log "SUCCESS" "SSL certificate is valid ($days_left days remaining)"
                    return 0
                elif [[ $days_left -gt 7 ]]; then
                    log "WARN" "SSL certificate expires soon ($days_left days remaining)"
                    return 0
                else
                    log "ERROR" "SSL certificate expires very soon ($days_left days remaining)"
                    return 1
                fi
            else
                log "ERROR" "SSL certificate is expired or invalid"
                return 1
            fi
        else
            log "WARN" "SSL certificate file not found"
            return 0
        fi
    else
        log "INFO" "Skipping SSL check (HTTP URL)"
        return 0
    fi
}

# Check API connectivity
check_api() {
    log "INFO" "Checking API connectivity..."
    
    # Try to get API status
    if curl -k -s --max-time 10 "$API_URL" | grep -q "healthy\|ok\|status"; then
        log "SUCCESS" "API is responding"
        return 0
    else
        log "ERROR" "API is not responding properly"
        return 1
    fi
}

# Generate status report
generate_status_report() {
    local overall_status=$1
    local timestamp=$(date -Iseconds)
    local uptime=$(uptime -p)
    
    cat > "$STATUS_FILE" << EOF
{
    "timestamp": "$timestamp",
    "overall_status": "$overall_status",
    "uptime": "$uptime",
    "checks": {
        "web_frontend": $web_status,
        "api_backend": $api_status,
        "nginx_service": $nginx_status,
        "disk_space": $disk_status,
        "memory_usage": $memory_status,
        "ssl_certificate": $ssl_status
    },
    "urls": {
        "health_url": "$HEALTH_URL",
        "api_url": "$API_URL"
    },
    "system_info": {
        "hostname": "$(hostname)",
        "load_average": "$(uptime | awk -F'load average:' '{print $2}')",
        "disk_usage": "$(df /var/www/claude-web-ui | awk 'NR==2 {print $5}')",
        "memory_usage": "$(free | grep '^Mem:' | awk '{printf "%.1f%%", $3/$2 * 100.0}')"
    }
}
EOF
    
    log "INFO" "Status report generated: $STATUS_FILE"
}

# Send alert email
send_alert() {
    local status=$1
    local message=$2
    
    if command -v mail &> /dev/null && [[ "$ALERT_EMAIL" != "admin@localhost" ]]; then
        local subject="Claude Web UI Health Alert - $status"
        
        {
            echo "Claude Web UI Health Check Alert"
            echo "================================"
            echo ""
            echo "Status: $status"
            echo "Time: $(date)"
            echo "Host: $(hostname)"
            echo ""
            echo "Details:"
            echo "$message"
            echo ""
            echo "Please check the application immediately."
            echo ""
            echo "Log file: $LOG_FILE"
            echo "Status file: $STATUS_FILE"
        } | mail -s "$subject" "$ALERT_EMAIL"
        
        log "INFO" "Alert email sent to $ALERT_EMAIL"
    else
        log "INFO" "Email alerts not configured or mail command not available"
    fi
}

# Main health check
main() {
    log "INFO" "Starting health check..."
    
    local overall_status="HEALTHY"
    local failed_checks=""
    
    # Initialize status variables
    web_status="false"
    api_status="false"
    nginx_status="false"
    disk_status="false"
    memory_status="false"
    ssl_status="false"
    
    # Check web frontend
    if check_url "$HEALTH_URL" "Web Frontend" "$MAX_RESPONSE_TIME"; then
        web_status="true"
    else
        overall_status="UNHEALTHY"
        failed_checks="$failed_checks Web Frontend,"
    fi
    
    # Check API backend
    if check_api; then
        api_status="true"
    else
        overall_status="UNHEALTHY"
        failed_checks="$failed_checks API Backend,"
    fi
    
    # Check NGINX
    if check_nginx; then
        nginx_status="true"
    else
        overall_status="CRITICAL"
        failed_checks="$failed_checks NGINX Service,"
    fi
    
    # Check disk space
    if check_disk_space; then
        disk_status="true"
    else
        if [[ "$overall_status" != "CRITICAL" ]]; then
            overall_status="WARNING"
        fi
        failed_checks="$failed_checks Disk Space,"
    fi
    
    # Check memory
    if check_memory; then
        memory_status="true"
    else
        if [[ "$overall_status" != "CRITICAL" ]]; then
            overall_status="WARNING"
        fi
        failed_checks="$failed_checks Memory Usage,"
    fi
    
    # Check SSL certificate
    if check_ssl_certificate; then
        ssl_status="true"
    else
        if [[ "$overall_status" != "CRITICAL" ]]; then
            overall_status="WARNING"
        fi
        failed_checks="$failed_checks SSL Certificate,"
    fi
    
    # Generate status report
    generate_status_report "$overall_status"
    
    # Log overall status
    case $overall_status in
        "HEALTHY")
            log "SUCCESS" "All health checks passed"
            ;;
        "WARNING")
            log "WARN" "Some health checks failed: ${failed_checks%, }"
            send_alert "$overall_status" "Failed checks: ${failed_checks%, }"
            ;;
        "UNHEALTHY")
            log "ERROR" "Critical health checks failed: ${failed_checks%, }"
            send_alert "$overall_status" "Failed checks: ${failed_checks%, }"
            ;;
        "CRITICAL")
            log "ERROR" "System is in critical state: ${failed_checks%, }"
            send_alert "$overall_status" "Failed checks: ${failed_checks%, }"
            ;;
    esac
    
    # Exit with appropriate code
    case $overall_status in
        "HEALTHY") exit 0 ;;
        "WARNING") exit 1 ;;
        "UNHEALTHY") exit 2 ;;
        "CRITICAL") exit 3 ;;
    esac
}

# Handle script arguments
case "${1:-}" in
    --help|-h)
        echo "Usage: $0 [health-url] [api-url]"
        echo ""
        echo "Arguments:"
        echo "  health-url  URL to check for web frontend health (default: http://localhost/health)"
        echo "  api-url     URL to check for API health (default: http://localhost/api/health)"
        echo ""
        echo "Environment Variables:"
        echo "  ALERT_EMAIL    Email address for alerts (default: admin@localhost)"
        echo ""
        echo "Exit Codes:"
        echo "  0 - Healthy"
        echo "  1 - Warning"
        echo "  2 - Unhealthy"
        echo "  3 - Critical"
        echo ""
        echo "Examples:"
        echo "  $0"
        echo "  $0 https://example.com/health https://example.com/api/health"
        echo "  ALERT_EMAIL=admin@example.com $0"
        exit 0
        ;;
esac

# Install bc if not present (for floating point arithmetic)
if ! command -v bc &> /dev/null; then
    if command -v apt-get &> /dev/null; then
        apt-get update && apt-get install -y bc
    fi
fi

# Run main function
main