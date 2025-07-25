#!/bin/bash

# Claude Web UI Log Analysis Script
# Analyzes logs for performance issues, errors, and security threats

set -euo pipefail

# Configuration
LOG_DIR="/var/log/claude-web-ui"
NGINX_LOG_DIR="/var/log/nginx"
REPORT_DIR="/var/log/claude-web-ui/reports"
REPORT_FILE="$REPORT_DIR/log-analysis-$(date +%Y%m%d-%H%M%S).json"
ALERT_EMAIL="${ALERT_EMAIL:-admin@localhost}"

# Log files to analyze
ACCESS_LOG="$NGINX_LOG_DIR/claude-web-ui-access.log"
ERROR_LOG="$NGINX_LOG_DIR/claude-web-ui-error.log"
APP_LOG="$LOG_DIR/deploy.log"
HEALTH_LOG="$LOG_DIR/health-check.log"

# Analysis parameters
ANALYSIS_HOURS="${1:-24}"  # Default: last 24 hours
ERROR_THRESHOLD=50         # Alert if more than 50 errors per hour
SLOW_REQUEST_THRESHOLD=5   # Alert if requests take more than 5 seconds
SUSPICIOUS_IP_THRESHOLD=100 # Alert if single IP makes more than 100 requests per hour

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
    
    echo -e "${timestamp} [${level}] ${message}"
    
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

# Initialize report structure
initialize_report() {
    mkdir -p "$REPORT_DIR"
    
    cat > "$REPORT_FILE" << 'EOF'
{
    "analysis_timestamp": "",
    "analysis_period_hours": 0,
    "summary": {
        "total_requests": 0,
        "error_count": 0,
        "error_rate_percent": 0,
        "slow_requests": 0,
        "unique_ips": 0,
        "suspicious_activity": false
    },
    "errors": [],
    "performance": {
        "avg_response_time": 0,
        "slowest_requests": [],
        "requests_per_hour": []
    },
    "security": {
        "suspicious_ips": [],
        "attack_patterns": [],
        "blocked_requests": 0
    },
    "recommendations": []
}
EOF
}

# Analyze HTTP access logs
analyze_access_logs() {
    log "INFO" "Analyzing access logs for the last $ANALYSIS_HOURS hours..."
    
    if [[ ! -f "$ACCESS_LOG" ]]; then
        log "WARN" "Access log not found: $ACCESS_LOG"
        return
    fi
    
    local cutoff_time=$(date -d "$ANALYSIS_HOURS hours ago" '+%d/%b/%Y:%H:%M:%S')
    local temp_log="/tmp/claude-access-filtered-$$"
    
    # Filter logs by time period
    awk -v cutoff="$cutoff_time" '
    function time_to_epoch(timestr) {
        gsub(/\//, " ", timestr)
        gsub(/:/, " ", timestr, 1)
        return mktime(timestr)
    }
    {
        if (match($0, /\[([^]]+)\]/, arr)) {
            log_time = arr[1]
            if (time_to_epoch(log_time) >= time_to_epoch(cutoff)) {
                print $0
            }
        }
    }' "$ACCESS_LOG" > "$temp_log"
    
    # Extract metrics
    local total_requests=$(wc -l < "$temp_log")
    local error_4xx=$(grep -c ' 4[0-9][0-9] ' "$temp_log" || echo 0)
    local error_5xx=$(grep -c ' 5[0-9][0-9] ' "$temp_log" || echo 0)
    local total_errors=$((error_4xx + error_5xx))
    local error_rate=0
    
    if [[ $total_requests -gt 0 ]]; then
        error_rate=$(echo "scale=2; $total_errors * 100 / $total_requests" | bc)
    fi
    
    # Analyze response times (if available in log format)
    local avg_response_time=0
    if grep -q 'rt=' "$temp_log"; then
        avg_response_time=$(grep -o 'rt=[0-9.]*' "$temp_log" | cut -d= -f2 | awk '{sum+=$1; count++} END {if(count>0) printf "%.3f", sum/count; else print "0"}')
    fi
    
    # Find slow requests
    local slow_requests=0
    if grep -q 'rt=' "$temp_log"; then
        slow_requests=$(grep -o 'rt=[0-9.]*' "$temp_log" | awk -v threshold="$SLOW_REQUEST_THRESHOLD" -F= '$2 > threshold' | wc -l)
    fi
    
    # Count unique IPs
    local unique_ips=$(awk '{print $1}' "$temp_log" | sort -u | wc -l)
    
    # Update report with access log analysis
    jq --arg timestamp "$(date -Iseconds)" \
       --arg period "$ANALYSIS_HOURS" \
       --arg total_requests "$total_requests" \
       --arg error_count "$total_errors" \
       --arg error_rate "$error_rate" \
       --arg slow_requests "$slow_requests" \
       --arg unique_ips "$unique_ips" \
       --arg avg_response_time "$avg_response_time" \
       '.analysis_timestamp = $timestamp |
        .analysis_period_hours = ($period | tonumber) |
        .summary.total_requests = ($total_requests | tonumber) |
        .summary.error_count = ($error_count | tonumber) |
        .summary.error_rate_percent = ($error_rate | tonumber) |
        .summary.slow_requests = ($slow_requests | tonumber) |
        .summary.unique_ips = ($unique_ips | tonumber) |
        .performance.avg_response_time = ($avg_response_time | tonumber)' \
       "$REPORT_FILE" > "$REPORT_FILE.tmp" && mv "$REPORT_FILE.tmp" "$REPORT_FILE"
    
    rm -f "$temp_log"
    
    log "SUCCESS" "Access log analysis completed"
    log "INFO" "Total requests: $total_requests, Errors: $total_errors ($error_rate%)"
}

# Analyze error logs
analyze_error_logs() {
    log "INFO" "Analyzing error logs..."
    
    if [[ ! -f "$ERROR_LOG" ]]; then
        log "WARN" "Error log not found: $ERROR_LOG"
        return
    fi
    
    local cutoff_time=$(date -d "$ANALYSIS_HOURS hours ago" '+%Y/%m/%d %H:%M:%S')
    local temp_log="/tmp/claude-error-filtered-$$"
    
    # Filter error logs by time
    awk -v cutoff="$cutoff_time" '
    {
        if (match($0, /([0-9]{4}\/[0-9]{2}\/[0-9]{2} [0-9]{2}:[0-9]{2}:[0-9]{2})/, arr)) {
            log_time = arr[1]
            if (log_time >= cutoff) {
                print $0
            }
        }
    }' "$ERROR_LOG" > "$temp_log"
    
    # Categorize errors
    local critical_errors=$(grep -ci "critical\|emergency\|alert" "$temp_log" || echo 0)
    local php_errors=$(grep -ci "php\|fatal" "$temp_log" || echo 0)
    local connection_errors=$(grep -ci "connection\|timeout\|refused" "$temp_log" || echo 0)
    local permission_errors=$(grep -ci "permission\|denied\|forbidden" "$temp_log" || echo 0)
    
    # Extract unique error patterns
    local error_patterns=$(grep -o '\[error\][^,]*' "$temp_log" | sort | uniq -c | sort -nr | head -10)
    
    # Create error array for report
    local errors_json='[]'
    while IFS= read -r line; do
        if [[ -n "$line" ]]; then
            local count=$(echo "$line" | awk '{print $1}')
            local message=$(echo "$line" | sed 's/^[[:space:]]*[0-9][0-9]* //')
            errors_json=$(echo "$errors_json" | jq --arg count "$count" --arg message "$message" '. += [{"count": ($count | tonumber), "message": $message}]')
        fi
    done <<< "$error_patterns"
    
    # Update report with error analysis
    jq --argjson errors "$errors_json" \
       --arg critical "$critical_errors" \
       --arg php "$php_errors" \
       --arg connection "$connection_errors" \
       --arg permission "$permission_errors" \
       '.errors = $errors |
        .summary.critical_errors = ($critical | tonumber) |
        .summary.php_errors = ($php | tonumber) |
        .summary.connection_errors = ($connection | tonumber) |
        .summary.permission_errors = ($permission | tonumber)' \
       "$REPORT_FILE" > "$REPORT_FILE.tmp" && mv "$REPORT_FILE.tmp" "$REPORT_FILE"
    
    rm -f "$temp_log"
    
    log "SUCCESS" "Error log analysis completed"
    log "INFO" "Critical errors: $critical_errors, Connection errors: $connection_errors"
}

# Analyze security patterns
analyze_security() {
    log "INFO" "Analyzing security patterns..."
    
    if [[ ! -f "$ACCESS_LOG" ]]; then
        log "WARN" "Access log not found for security analysis"
        return
    fi
    
    local cutoff_time=$(date -d "$ANALYSIS_HOURS hours ago" '+%d/%b/%Y:%H:%M:%S')
    local temp_log="/tmp/claude-security-$$"
    
    # Filter recent logs
    awk -v cutoff="$cutoff_time" '
    function time_to_epoch(timestr) {
        gsub(/\//, " ", timestr)
        gsub(/:/, " ", timestr, 1)
        return mktime(timestr)
    }
    {
        if (match($0, /\[([^]]+)\]/, arr)) {
            log_time = arr[1]
            if (time_to_epoch(log_time) >= time_to_epoch(cutoff)) {
                print $0
            }
        }
    }' "$ACCESS_LOG" > "$temp_log"
    
    # Detect suspicious IPs (high request volume)
    local suspicious_ips=$(awk '{print $1}' "$temp_log" | sort | uniq -c | awk -v threshold="$SUSPICIOUS_IP_THRESHOLD" '$1 > threshold {print $2 ":" $1}' | head -10)
    
    # Detect attack patterns
    local sql_injection=$(grep -ci "union\|select\|drop\|insert\|update\|delete" "$temp_log" || echo 0)
    local xss_attempts=$(grep -ci "script\|javascript\|onload\|onerror" "$temp_log" || echo 0)
    local path_traversal=$(grep -ci "\.\.\/" "$temp_log" || echo 0)
    local bot_requests=$(grep -ci "bot\|crawler\|spider\|scraper" "$temp_log" || echo 0)
    
    # Count blocked requests (4xx responses)
    local blocked_requests=$(grep -c ' 4[0-9][0-9] ' "$temp_log" || echo 0)
    
    # Create suspicious IPs array
    local suspicious_ips_json='[]'
    while IFS= read -r line; do
        if [[ -n "$line" ]]; then
            local ip=$(echo "$line" | cut -d: -f1)
            local count=$(echo "$line" | cut -d: -f2)
            suspicious_ips_json=$(echo "$suspicious_ips_json" | jq --arg ip "$ip" --arg count "$count" '. += [{"ip": $ip, "request_count": ($count | tonumber)}]')
        fi
    done <<< "$suspicious_ips"
    
    # Create attack patterns array
    local attack_patterns_json=$(jq -n --arg sql "$sql_injection" --arg xss "$xss_attempts" --arg path "$path_traversal" --arg bot "$bot_requests" '[
        {"type": "sql_injection", "count": ($sql | tonumber)},
        {"type": "xss_attempts", "count": ($xss | tonumber)},
        {"type": "path_traversal", "count": ($path | tonumber)},
        {"type": "bot_requests", "count": ($bot | tonumber)}
    ]')
    
    # Determine if there's suspicious activity
    local suspicious_activity="false"
    if [[ -n "$suspicious_ips" ]] || [[ $sql_injection -gt 0 ]] || [[ $xss_attempts -gt 0 ]] || [[ $path_traversal -gt 0 ]]; then
        suspicious_activity="true"
    fi
    
    # Update report with security analysis
    jq --argjson suspicious_ips "$suspicious_ips_json" \
       --argjson attack_patterns "$attack_patterns_json" \
       --arg blocked_requests "$blocked_requests" \
       --arg suspicious_activity "$suspicious_activity" \
       '.security.suspicious_ips = $suspicious_ips |
        .security.attack_patterns = $attack_patterns |
        .security.blocked_requests = ($blocked_requests | tonumber) |
        .summary.suspicious_activity = ($suspicious_activity == "true")' \
       "$REPORT_FILE" > "$REPORT_FILE.tmp" && mv "$REPORT_FILE.tmp" "$REPORT_FILE"
    
    rm -f "$temp_log"
    
    log "SUCCESS" "Security analysis completed"
    log "INFO" "Suspicious IPs found: $(echo "$suspicious_ips" | wc -l), Attack attempts: $((sql_injection + xss_attempts + path_traversal))"
}

# Analyze performance trends
analyze_performance() {
    log "INFO" "Analyzing performance trends..."
    
    if [[ ! -f "$ACCESS_LOG" ]]; then
        log "WARN" "Access log not found for performance analysis"
        return
    fi
    
    # Analyze requests per hour
    local requests_per_hour=$(awk '
    {
        if (match($0, /\[([^]]+)\]/, arr)) {
            log_time = arr[1]
            gsub(/\//, " ", log_time)
            gsub(/:/, " ", log_time, 1)
            epoch = mktime(log_time)
            hour = strftime("%Y-%m-%d %H:00", epoch)
            hours[hour]++
        }
    }
    END {
        for (hour in hours) {
            print hour ":" hours[hour]
        }
    }' "$ACCESS_LOG" | sort | tail -24)
    
    # Find slowest requests (if response time is available)
    local slowest_requests='[]'
    if grep -q 'rt=' "$ACCESS_LOG"; then
        local slow_requests_data=$(grep 'rt=' "$ACCESS_LOG" | sort -t= -k2 -nr | head -10)
        
        while IFS= read -r line; do
            if [[ -n "$line" ]]; then
                local url=$(echo "$line" | awk '{print $7}')
                local response_time=$(echo "$line" | grep -o 'rt=[0-9.]*' | cut -d= -f2)
                local status=$(echo "$line" | awk '{print $9}')
                slowest_requests=$(echo "$slowest_requests" | jq --arg url "$url" --arg time "$response_time" --arg status "$status" '. += [{"url": $url, "response_time": ($time | tonumber), "status": $status}]')
            fi
        done <<< "$slow_requests_data"
    fi
    
    # Create requests per hour array
    local requests_per_hour_json='[]'
    while IFS= read -r line; do
        if [[ -n "$line" ]]; then
            local hour=$(echo "$line" | cut -d: -f1-2)
            local count=$(echo "$line" | cut -d: -f3)
            requests_per_hour_json=$(echo "$requests_per_hour_json" | jq --arg hour "$hour" --arg count "$count" '. += [{"hour": $hour, "requests": ($count | tonumber)}]')
        fi
    done <<< "$requests_per_hour"
    
    # Update report with performance analysis
    jq --argjson requests_per_hour "$requests_per_hour_json" \
       --argjson slowest_requests "$slowest_requests" \
       '.performance.requests_per_hour = $requests_per_hour |
        .performance.slowest_requests = $slowest_requests' \
       "$REPORT_FILE" > "$REPORT_FILE.tmp" && mv "$REPORT_FILE.tmp" "$REPORT_FILE"
    
    log "SUCCESS" "Performance analysis completed"
}

# Generate recommendations
generate_recommendations() {
    log "INFO" "Generating recommendations..."
    
    local recommendations='[]'
    
    # Read current metrics from report
    local error_rate=$(jq -r '.summary.error_rate_percent' "$REPORT_FILE")
    local slow_requests=$(jq -r '.summary.slow_requests' "$REPORT_FILE")
    local suspicious_activity=$(jq -r '.summary.suspicious_activity' "$REPORT_FILE")
    local total_requests=$(jq -r '.summary.total_requests' "$REPORT_FILE")
    
    # High error rate recommendation
    if (( $(echo "$error_rate > 5" | bc -l) )); then
        recommendations=$(echo "$recommendations" | jq '. += [{"priority": "high", "category": "errors", "message": "High error rate detected ('$error_rate'%). Investigate application errors and server configuration."}]')
    fi
    
    # Slow requests recommendation
    if [[ $slow_requests -gt 0 ]]; then
        recommendations=$(echo "$recommendations" | jq '. += [{"priority": "medium", "category": "performance", "message": "'$slow_requests' slow requests detected. Consider optimizing database queries and enabling caching."}]')
    fi
    
    # Security recommendation
    if [[ "$suspicious_activity" == "true" ]]; then
        recommendations=$(echo "$recommendations" | jq '. += [{"priority": "high", "category": "security", "message": "Suspicious activity detected. Review security logs and consider implementing additional security measures."}]')
    fi
    
    # Traffic volume recommendation
    if [[ $total_requests -gt 10000 ]]; then
        recommendations=$(echo "$recommendations" | jq '. += [{"priority": "low", "category": "scaling", "message": "High traffic volume ('$total_requests' requests). Consider implementing load balancing and CDN."}]')
    fi
    
    # Low traffic recommendation
    if [[ $total_requests -lt 100 ]]; then
        recommendations=$(echo "$recommendations" | jq '. += [{"priority": "info", "category": "monitoring", "message": "Low traffic volume detected. Verify monitoring and log collection is working correctly."}]')
    fi
    
    # Update report with recommendations
    jq --argjson recommendations "$recommendations" \
       '.recommendations = $recommendations' \
       "$REPORT_FILE" > "$REPORT_FILE.tmp" && mv "$REPORT_FILE.tmp" "$REPORT_FILE"
    
    log "SUCCESS" "Recommendations generated"
}

# Send alert email if needed
send_alert_email() {
    local error_rate=$(jq -r '.summary.error_rate_percent' "$REPORT_FILE")
    local suspicious_activity=$(jq -r '.summary.suspicious_activity' "$REPORT_FILE")
    local slow_requests=$(jq -r '.summary.slow_requests' "$REPORT_FILE")
    
    local should_alert=false
    local alert_reasons=""
    
    # Check alert conditions
    if (( $(echo "$error_rate > 10" | bc -l) )); then
        should_alert=true
        alert_reasons="$alert_reasons High error rate: ${error_rate}%\n"
    fi
    
    if [[ "$suspicious_activity" == "true" ]]; then
        should_alert=true
        alert_reasons="$alert_reasons Suspicious security activity detected\n"
    fi
    
    if [[ $slow_requests -gt 20 ]]; then
        should_alert=true
        alert_reasons="$alert_reasons High number of slow requests: $slow_requests\n"
    fi
    
    # Send alert if conditions are met
    if [[ "$should_alert" == "true" ]] && command -v mail &> /dev/null && [[ "$ALERT_EMAIL" != "admin@localhost" ]]; then
        local subject="Claude Web UI Log Analysis Alert"
        
        {
            echo "Claude Web UI Log Analysis Alert"
            echo "================================"
            echo ""
            echo "Analysis Period: Last $ANALYSIS_HOURS hours"
            echo "Timestamp: $(date)"
            echo "Host: $(hostname)"
            echo ""
            echo "Alert Reasons:"
            echo -e "$alert_reasons"
            echo ""
            echo "Full report available at: $REPORT_FILE"
            echo ""
            echo "Summary:"
            jq -r '.summary | to_entries[] | "\(.key): \(.value)"' "$REPORT_FILE"
        } | mail -s "$subject" "$ALERT_EMAIL"
        
        log "INFO" "Alert email sent to $ALERT_EMAIL"
    else
        log "INFO" "No alerts triggered or email not configured"
    fi
}

# Display summary report
display_summary() {
    log "INFO" "Log Analysis Summary:"
    echo ""
    
    echo -e "${BLUE}Analysis Period:${NC} Last $ANALYSIS_HOURS hours"
    echo -e "${BLUE}Report File:${NC} $REPORT_FILE"
    echo ""
    
    # Display key metrics
    local total_requests=$(jq -r '.summary.total_requests' "$REPORT_FILE")
    local error_count=$(jq -r '.summary.error_count' "$REPORT_FILE")
    local error_rate=$(jq -r '.summary.error_rate_percent' "$REPORT_FILE")
    local slow_requests=$(jq -r '.summary.slow_requests' "$REPORT_FILE")
    local unique_ips=$(jq -r '.summary.unique_ips' "$REPORT_FILE")
    local suspicious_activity=$(jq -r '.summary.suspicious_activity' "$REPORT_FILE")
    
    echo -e "${BLUE}Traffic:${NC}"
    echo -e "  Total requests: $total_requests"
    echo -e "  Unique IPs: $unique_ips"
    echo ""
    
    echo -e "${BLUE}Errors:${NC}"
    if [[ $error_count -gt 0 ]]; then
        echo -e "  Error count: ${RED}$error_count${NC}"
        echo -e "  Error rate: ${RED}${error_rate}%${NC}"
    else
        echo -e "  Error count: ${GREEN}$error_count${NC}"
        echo -e "  Error rate: ${GREEN}${error_rate}%${NC}"
    fi
    echo ""
    
    echo -e "${BLUE}Performance:${NC}"
    if [[ $slow_requests -gt 0 ]]; then
        echo -e "  Slow requests: ${YELLOW}$slow_requests${NC}"
    else
        echo -e "  Slow requests: ${GREEN}$slow_requests${NC}"
    fi
    echo ""
    
    echo -e "${BLUE}Security:${NC}"
    if [[ "$suspicious_activity" == "true" ]]; then
        echo -e "  Suspicious activity: ${RED}Yes${NC}"
    else
        echo -e "  Suspicious activity: ${GREEN}No${NC}"
    fi
    echo ""
    
    # Display top recommendations
    local high_priority_recs=$(jq -r '.recommendations[] | select(.priority == "high") | .message' "$REPORT_FILE")
    if [[ -n "$high_priority_recs" ]]; then
        echo -e "${RED}High Priority Recommendations:${NC}"
        while IFS= read -r rec; do
            if [[ -n "$rec" ]]; then
                echo -e "  • $rec"
            fi
        done <<< "$high_priority_recs"
        echo ""
    fi
}

# Clean old reports
clean_old_reports() {
    log "INFO" "Cleaning old reports..."
    
    # Keep only last 30 reports
    if [[ -d "$REPORT_DIR" ]]; then
        find "$REPORT_DIR" -name "log-analysis-*.json" -type f -mtime +30 -delete
        log "SUCCESS" "Old reports cleaned"
    fi
}

# Main execution
main() {
    log "INFO" "Starting log analysis for the last $ANALYSIS_HOURS hours..."
    
    # Install jq if not available
    if ! command -v jq &> /dev/null; then
        if command -v apt-get &> /dev/null; then
            apt-get update && apt-get install -y jq bc
        else
            log "ERROR" "jq and bc are required but not installed"
            exit 1
        fi
    fi
    
    initialize_report
    analyze_access_logs
    analyze_error_logs
    analyze_security
    analyze_performance
    generate_recommendations
    send_alert_email
    display_summary
    clean_old_reports
    
    log "SUCCESS" "Log analysis completed successfully!"
    log "INFO" "Full report available at: $REPORT_FILE"
}

# Handle script arguments
case "${1:-24}" in
    --help|-h)
        echo "Usage: $0 [hours]"
        echo ""
        echo "Arguments:"
        echo "  hours           Number of hours to analyze (default: 24)"
        echo ""
        echo "Environment Variables:"
        echo "  ALERT_EMAIL     Email address for alerts (default: admin@localhost)"
        echo ""
        echo "Examples:"
        echo "  $0              # Analyze last 24 hours"
        echo "  $0 12           # Analyze last 12 hours"
        echo "  ALERT_EMAIL=admin@example.com $0 48"
        exit 0
        ;;
    *[!0-9]*)
        log "ERROR" "Invalid hours parameter: $1"
        exit 1
        ;;
esac

# Run main function
main