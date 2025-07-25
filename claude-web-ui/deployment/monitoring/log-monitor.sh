#!/bin/bash

# Log Monitor Script for Claude Web UI
# Monitors NGINX and application logs for errors and anomalies

set -euo pipefail

# Configuration
NGINX_ACCESS_LOG="/var/log/nginx/claude-web-ui-access.log"
NGINX_ERROR_LOG="/var/log/nginx/claude-web-ui-error.log"
APP_LOG="/var/log/claude-web-ui"
MONITOR_LOG="/var/log/claude-web-ui/log-monitor.log"
ALERT_EMAIL="${ALERT_EMAIL:-admin@localhost}"
CHECK_INTERVAL="${CHECK_INTERVAL:-300}"  # 5 minutes
ERROR_THRESHOLD="${ERROR_THRESHOLD:-10}"  # errors per check interval
WARNING_THRESHOLD="${WARNING_THRESHOLD:-5}"  # warnings per check interval

# State files for tracking
STATE_DIR="/var/lib/claude-web-ui/log-monitor"
LAST_CHECK_FILE="$STATE_DIR/last-check"
ERROR_COUNT_FILE="$STATE_DIR/error-count"
WARNING_COUNT_FILE="$STATE_DIR/warning-count"

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Create necessary directories
mkdir -p "$STATE_DIR"
mkdir -p "$(dirname "$MONITOR_LOG")"

# Logging function
log() {
    local level=$1
    shift
    local message="$*"
    local timestamp=$(date '+%Y-%m-%d %H:%M:%S')
    
    echo -e "${timestamp} [${level}] ${message}" | tee -a "$MONITOR_LOG"
    
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

# Get last check timestamp
get_last_check() {
    if [[ -f "$LAST_CHECK_FILE" ]]; then
        cat "$LAST_CHECK_FILE"
    else
        echo "$(date -d "5 minutes ago" +%s)"
    fi
}

# Update last check timestamp
update_last_check() {
    echo "$(date +%s)" > "$LAST_CHECK_FILE"
}

# Check NGINX access log
check_access_log() {
    log "INFO" "Checking NGINX access log..."
    
    if [[ ! -f "$NGINX_ACCESS_LOG" ]]; then
        log "WARN" "NGINX access log not found: $NGINX_ACCESS_LOG"
        return
    fi
    
    local last_check=$(get_last_check)
    local current_time=$(date +%s)
    local since_time=$(date -d "@$last_check" "+%d/%b/%Y:%H:%M:%S")
    
    # Count different types of responses
    local total_requests=$(awk -v since="$since_time" '$4 >= "["since {count++} END {print count+0}' "$NGINX_ACCESS_LOG")
    local error_4xx=$(awk -v since="$since_time" '$4 >= "["since && $9 ~ /^4/ {count++} END {print count+0}' "$NGINX_ACCESS_LOG")
    local error_5xx=$(awk -v since="$since_time" '$4 >= "["since && $9 ~ /^5/ {count++} END {print count+0}' "$NGINX_ACCESS_LOG")
    local slow_requests=$(awk -v since="$since_time" '$4 >= "["since && $NF > 1 {count++} END {print count+0}' "$NGINX_ACCESS_LOG" 2>/dev/null || echo "0")
    
    log "INFO" "Access log summary (last $((current_time - last_check)) seconds):"
    log "INFO" "  Total requests: $total_requests"
    log "INFO" "  4xx errors: $error_4xx"
    log "INFO" "  5xx errors: $error_5xx"
    log "INFO" "  Slow requests (>1s): $slow_requests"
    
    # Check for suspicious patterns
    local suspicious_ips=$(awk -v since="$since_time" '$4 >= "["since {print $1}' "$NGINX_ACCESS_LOG" | sort | uniq -c | awk '$1 > 100 {print $2": "$1" requests"}')
    
    if [[ -n "$suspicious_ips" ]]; then
        log "WARN" "Suspicious IPs with high request counts:"
        while IFS= read -r line; do
            log "WARN" "  $line"
        done <<< "$suspicious_ips"
    fi
    
    # Alert if too many errors
    local total_errors=$((error_4xx + error_5xx))
    if [[ $total_errors -gt $ERROR_THRESHOLD ]]; then
        log "ERROR" "High error rate: $total_errors errors in $((current_time - last_check)) seconds"
        send_alert "High Error Rate" "NGINX access log shows $total_errors errors (4xx: $error_4xx, 5xx: $error_5xx) in the last $((current_time - last_check)) seconds."
    elif [[ $total_errors -gt $WARNING_THRESHOLD ]]; then
        log "WARN" "Elevated error rate: $total_errors errors in $((current_time - last_check)) seconds"
    fi
}

# Check NGINX error log
check_error_log() {
    log "INFO" "Checking NGINX error log..."
    
    if [[ ! -f "$NGINX_ERROR_LOG" ]]; then
        log "WARN" "NGINX error log not found: $NGINX_ERROR_LOG"
        return
    fi
    
    local last_check=$(get_last_check)
    local since_date=$(date -d "@$last_check" "+%Y/%m/%d %H:%M:%S")
    
    # Get recent error entries
    local recent_errors=$(awk -v since="$since_date" '$1" "$2 >= since' "$NGINX_ERROR_LOG" | wc -l)
    
    if [[ $recent_errors -gt 0 ]]; then
        log "WARN" "Found $recent_errors error entries in NGINX error log"
        
        # Show critical errors
        local critical_errors=$(awk -v since="$since_date" '$1" "$2 >= since && /\[crit\]|\[alert\]|\[emerg\]/' "$NGINX_ERROR_LOG")
        
        if [[ -n "$critical_errors" ]]; then
            log "ERROR" "Critical NGINX errors found:"
            echo "$critical_errors" | tail -5 | while IFS= read -r line; do
                log "ERROR" "  $line"
            done
            
            send_alert "Critical NGINX Errors" "Critical errors found in NGINX error log:\n\n$critical_errors"
        fi
        
        # Count different error types
        local connection_errors=$(awk -v since="$since_date" '$1" "$2 >= since && /connection.*failed|upstream.*failed/' "$NGINX_ERROR_LOG" | wc -l)
        local ssl_errors=$(awk -v since="$since_date" '$1" "$2 >= since && /SSL|ssl/' "$NGINX_ERROR_LOG" | wc -l)
        local permission_errors=$(awk -v since="$since_date" '$1" "$2 >= since && /permission denied|forbidden/' "$NGINX_ERROR_LOG" | wc -l)
        
        if [[ $connection_errors -gt 0 ]]; then
            log "WARN" "Connection errors: $connection_errors"
        fi
        if [[ $ssl_errors -gt 0 ]]; then
            log "WARN" "SSL errors: $ssl_errors"
        fi
        if [[ $permission_errors -gt 0 ]]; then
            log "WARN" "Permission errors: $permission_errors"
        fi
    else
        log "SUCCESS" "No recent errors in NGINX error log"
    fi
}

# Check application logs
check_app_logs() {
    log "INFO" "Checking application logs..."
    
    if [[ ! -d "$APP_LOG" ]]; then
        log "INFO" "Application log directory not found: $APP_LOG"
        return
    fi
    
    local last_check=$(get_last_check)
    
    # Check for recent log files
    local recent_logs=$(find "$APP_LOG" -name "*.log" -newer "$LAST_CHECK_FILE" 2>/dev/null || true)
    
    if [[ -n "$recent_logs" ]]; then
        while IFS= read -r logfile; do
            if [[ -f "$logfile" ]]; then
                local errors=$(grep -i "error\|exception\|fatal" "$logfile" | wc -l)
                local warnings=$(grep -i "warn" "$logfile" | wc -l)
                
                if [[ $errors -gt 0 ]]; then
                    log "WARN" "Found $errors errors in $logfile"
                    # Show recent errors
                    grep -i "error\|exception\|fatal" "$logfile" | tail -3 | while IFS= read -r line; do
                        log "WARN" "  $line"
                    done
                fi
                
                if [[ $warnings -gt $WARNING_THRESHOLD ]]; then
                    log "WARN" "High warning count in $logfile: $warnings"
                fi
            fi
        done <<< "$recent_logs"
    else
        log "INFO" "No recent application log entries"
    fi
}

# Check log rotation and disk space
check_log_maintenance() {
    log "INFO" "Checking log maintenance..."
    
    # Check log sizes
    local large_logs=""
    
    for logfile in "$NGINX_ACCESS_LOG" "$NGINX_ERROR_LOG" "$MONITOR_LOG"; do
        if [[ -f "$logfile" ]]; then
            local size=$(stat -c%s "$logfile" 2>/dev/null || echo "0")
            local size_mb=$((size / 1024 / 1024))
            
            if [[ $size_mb -gt 100 ]]; then
                large_logs="$large_logs\n  $logfile: ${size_mb}MB"
                log "WARN" "Large log file: $logfile (${size_mb}MB)"
            fi
        fi
    done
    
    if [[ -n "$large_logs" ]]; then
        log "WARN" "Large log files detected. Consider log rotation:"
        echo -e "$large_logs"
    fi
    
    # Check log directory disk usage
    local log_disk_usage=$(df "$APP_LOG" | awk 'NR==2 {print $5}' | sed 's/%//' 2>/dev/null || echo "0")
    
    if [[ $log_disk_usage -gt 80 ]]; then
        log "ERROR" "Log directory disk usage is high: ${log_disk_usage}%"
        send_alert "High Log Disk Usage" "Log directory disk usage is at ${log_disk_usage}%. Consider cleaning old logs."
    elif [[ $log_disk_usage -gt 70 ]]; then
        log "WARN" "Log directory disk usage is elevated: ${log_disk_usage}%"
    fi
}

# Analyze traffic patterns
analyze_traffic_patterns() {
    log "INFO" "Analyzing traffic patterns..."
    
    if [[ ! -f "$NGINX_ACCESS_LOG" ]]; then
        return
    fi
    
    local last_check=$(get_last_check)
    local since_time=$(date -d "@$last_check" "+%d/%b/%Y:%H:%M:%S")
    
    # Top requested URLs
    local top_urls=$(awk -v since="$since_time" '$4 >= "["since {print $7}' "$NGINX_ACCESS_LOG" | sort | uniq -c | sort -nr | head -5)
    
    if [[ -n "$top_urls" ]]; then
        log "INFO" "Top requested URLs:"
        while IFS= read -r line; do
            log "INFO" "  $line"
        done <<< "$top_urls"
    fi
    
    # Top user agents
    local suspicious_agents=$(awk -v since="$since_time" '$4 >= "["since {ua = ""; for(i=12; i<=NF; i++) ua = ua $i " "; print ua}' "$NGINX_ACCESS_LOG" | sort | uniq -c | sort -nr | head -3 | grep -E "bot|crawler|spider|scan" || true)
    
    if [[ -n "$suspicious_agents" ]]; then
        log "WARN" "Suspicious user agents detected:"
        while IFS= read -r line; do
            log "WARN" "  $line"
        done <<< "$suspicious_agents"
    fi
    
    # Calculate average response time
    local avg_response_time=$(awk -v since="$since_time" '$4 >= "["since && $NF ~ /^[0-9.]+$/ {sum += $NF; count++} END {if(count > 0) printf "%.2f", sum/count; else print "0"}' "$NGINX_ACCESS_LOG" 2>/dev/null || echo "0")
    
    if [[ $(echo "$avg_response_time > 2" | bc -l 2>/dev/null) == 1 ]]; then
        log "WARN" "Average response time is high: ${avg_response_time}s"
    else
        log "INFO" "Average response time: ${avg_response_time}s"
    fi
}

# Send alert email
send_alert() {
    local subject=$1
    local message=$2
    
    if command -v mail &> /dev/null && [[ "$ALERT_EMAIL" != "admin@localhost" ]]; then
        {
            echo "Claude Web UI Log Monitor Alert"
            echo "==============================="
            echo ""
            echo "Alert: $subject"
            echo "Time: $(date)"
            echo "Host: $(hostname)"
            echo ""
            echo "Details:"
            echo -e "$message"
            echo ""
            echo "Please investigate immediately."
            echo ""
            echo "Monitor log: $MONITOR_LOG"
        } | mail -s "Claude Web UI Alert: $subject" "$ALERT_EMAIL"
        
        log "INFO" "Alert email sent to $ALERT_EMAIL"
    else
        log "INFO" "Email alerts not configured or mail command not available"
    fi
}

# Generate monitoring report
generate_report() {
    local report_file="/var/log/claude-web-ui/log-monitor-report.json"
    local timestamp=$(date -Iseconds)
    local last_check=$(get_last_check)
    local check_duration=$(($(date +%s) - last_check))
    
    # Get basic stats
    local total_requests=$(tail -1000 "$NGINX_ACCESS_LOG" 2>/dev/null | wc -l || echo "0")
    local recent_errors=$(tail -100 "$NGINX_ERROR_LOG" 2>/dev/null | wc -l || echo "0")
    
    cat > "$report_file" << EOF
{
    "timestamp": "$timestamp",
    "check_duration": $check_duration,
    "logs_checked": {
        "nginx_access": "$(test -f "$NGINX_ACCESS_LOG" && echo "true" || echo "false")",
        "nginx_error": "$(test -f "$NGINX_ERROR_LOG" && echo "true" || echo "false")",
        "application": "$(test -d "$APP_LOG" && echo "true" || echo "false")"
    },
    "stats": {
        "total_requests_sample": $total_requests,
        "recent_error_entries": $recent_errors,
        "log_files_size": {
            "access_log_mb": $(stat -c%s "$NGINX_ACCESS_LOG" 2>/dev/null | awk '{print int($1/1024/1024)}' || echo "0"),
            "error_log_mb": $(stat -c%s "$NGINX_ERROR_LOG" 2>/dev/null | awk '{print int($1/1024/1024)}' || echo "0")
        }
    },
    "alerts_sent": $(grep -c "Alert email sent" "$MONITOR_LOG" 2>/dev/null || echo "0"),
    "next_check": "$(date -d "+$CHECK_INTERVAL seconds" -Iseconds)"
}
EOF
    
    log "INFO" "Monitoring report generated: $report_file"
}

# Main monitoring function
main() {
    log "INFO" "Starting log monitoring check..."
    
    check_access_log
    check_error_log
    check_app_logs
    check_log_maintenance
    analyze_traffic_patterns
    generate_report
    
    update_last_check
    
    log "SUCCESS" "Log monitoring check completed"
}

# Daemon mode
run_daemon() {
    log "INFO" "Starting log monitor in daemon mode (check interval: ${CHECK_INTERVAL}s)"
    
    while true; do
        main
        log "INFO" "Sleeping for $CHECK_INTERVAL seconds..."
        sleep "$CHECK_INTERVAL"
    done
}

# Handle script arguments
case "${1:-}" in
    --daemon|-d)
        run_daemon
        ;;
    --help|-h)
        echo "Usage: $0 [options]"
        echo ""
        echo "Options:"
        echo "  --daemon, -d   Run in daemon mode (continuous monitoring)"
        echo "  --help, -h     Show this help message"
        echo ""
        echo "Environment Variables:"
        echo "  ALERT_EMAIL         Email address for alerts (default: admin@localhost)"
        echo "  CHECK_INTERVAL      Check interval in seconds (default: 300)"
        echo "  ERROR_THRESHOLD     Error threshold per interval (default: 10)"
        echo "  WARNING_THRESHOLD   Warning threshold per interval (default: 5)"
        echo ""
        echo "Examples:"
        echo "  $0                              # Run single check"
        echo "  $0 --daemon                     # Run in daemon mode"
        echo "  ALERT_EMAIL=admin@example.com CHECK_INTERVAL=60 $0 --daemon"
        exit 0
        ;;
    *)
        main
        ;;
esac