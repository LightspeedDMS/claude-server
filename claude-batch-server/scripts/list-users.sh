#!/bin/bash

# Claude Server User Management - List Users
# Lists all users in the Claude Server authentication files
#
# Usage: ./list-users.sh

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
SERVER_DIR="$(dirname "$SCRIPT_DIR")"
PASSWD_FILE="$SERVER_DIR/claude-server-passwd"
SHADOW_FILE="$SERVER_DIR/claude-server-shadow"

echo -e "${CYAN}👥 Claude Server Authentication Users${NC}"
echo ""

# Check if files exist
if [ ! -f "$PASSWD_FILE" ]; then
    echo -e "${RED}❌ Password file not found: $PASSWD_FILE${NC}"
    echo -e "${YELLOW}   Run ./scripts/add-user.sh to create your first user${NC}"
    exit 1
fi

if [ ! -f "$SHADOW_FILE" ]; then
    echo -e "${RED}❌ Shadow file not found: $SHADOW_FILE${NC}"
    echo -e "${YELLOW}   Run ./scripts/add-user.sh to create your first user${NC}"
    exit 1
fi

# Count users
USER_COUNT=$(wc -l < "$PASSWD_FILE")

if [ "$USER_COUNT" -eq 0 ]; then
    echo -e "${YELLOW}⚠️  No users found in Claude Server authentication${NC}"
    echo -e "${BLUE}   Add a user: ${PURPLE}./scripts/add-user.sh <username> <password>${NC}"
    exit 0
fi

echo -e "${BLUE}📊 Total Users: ${YELLOW}$USER_COUNT${NC}"
echo ""

# Display header
printf "%-15s %-6s %-6s %-25s %-15s %-15s\n" "USERNAME" "UID" "GID" "HOME" "SHELL" "LAST_CHANGE"
printf "%-15s %-6s %-6s %-25s %-15s %-15s\n" "---------------" "------" "------" "-------------------------" "---------------" "---------------"

# Parse and display users
while IFS=: read -r username _ uid gid gecos home shell; do
    # Get password info from shadow file
    shadow_line=$(grep "^$username:" "$SHADOW_FILE" 2>/dev/null || echo "")
    
    if [ -n "$shadow_line" ]; then
        # Parse shadow file fields
        IFS=: read -r _ hash last_change _ _ _ _ _ _ <<< "$shadow_line"
        
        # Convert days since epoch to readable date
        if [ -n "$last_change" ] && [ "$last_change" != "0" ]; then
            if command -v date >/dev/null 2>&1; then
                # Calculate seconds since epoch and convert to date
                last_change_seconds=$((last_change * 86400))
                last_change_date=$(date -d "@$last_change_seconds" "+%Y-%m-%d" 2>/dev/null || echo "$last_change")
            else
                last_change_date="$last_change"
            fi
        else
            last_change_date="Never"
        fi
        
        # Check if password is set
        if [ -z "$hash" ] || [ "$hash" = "*" ] || [ "$hash" = "!" ]; then
            status="🔒 No Password"
        else
            status="✅ Active"
        fi
    else
        last_change_date="Not Found"
        status="❌ No Shadow"
    fi
    
    # Truncate long paths for display
    short_home=$(echo "$home" | sed 's|.*/\([^/]*/[^/]*\)$|\1|' | cut -c1-23)
    short_shell=$(basename "$shell")
    
    printf "%-15s %-6s %-6s %-25s %-15s %-15s %s\n" \
        "$username" "$uid" "$gid" "$short_home" "$short_shell" "$last_change_date" "$status"
        
done < "$PASSWD_FILE"

echo ""
echo -e "${BLUE}🔧 Management Commands:${NC}"
echo -e "   Add user: ${PURPLE}./scripts/add-user.sh <username> <password>${NC}"
echo -e "   Remove user: ${PURPLE}./scripts/remove-user.sh <username>${NC}"
echo -e "   Update password: ${PURPLE}./scripts/update-user.sh <username> <new_password>${NC}"
echo -e "   Test login: ${PURPLE}curl -X POST http://localhost:5185/auth/login -H 'Content-Type: application/json' -d '{\"username\":\"<user>\",\"password\":\"<pass>\"}'${NC}"