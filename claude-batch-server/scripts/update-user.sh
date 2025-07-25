#!/bin/bash

# Claude Server User Management - Update User Password
# Updates a user's password in the Claude Server authentication files
#
# Usage: ./update-user.sh <username> <new_password>

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
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SERVER_DIR="$(dirname "$SCRIPT_DIR")"
PASSWD_FILE="$SERVER_DIR/claude-server-passwd"
SHADOW_FILE="$SERVER_DIR/claude-server-shadow"

# Parse arguments
USERNAME="$1"
NEW_PASSWORD="$2"

# Validation
if [ -z "$USERNAME" ] || [ -z "$NEW_PASSWORD" ]; then
    echo -e "${RED}❌ Usage: $0 <username> <new_password>${NC}"
    echo -e "${YELLOW}   Example: $0 alice newpassword123${NC}"
    exit 1
fi

# Check if files exist
if [ ! -f "$PASSWD_FILE" ]; then
    echo -e "${RED}❌ Password file not found: $PASSWD_FILE${NC}"
    exit 1
fi

if [ ! -f "$SHADOW_FILE" ]; then
    echo -e "${RED}❌ Shadow file not found: $SHADOW_FILE${NC}"
    exit 1
fi

# Check if user exists
if ! grep -q "^$USERNAME:" "$PASSWD_FILE"; then
    echo -e "${RED}❌ User '$USERNAME' does not exist in Claude Server authentication${NC}"
    echo -e "${YELLOW}   Use ./scripts/add-user.sh to create the user first${NC}"
    exit 1
fi

echo -e "${CYAN}🔐 Updating password for user '$USERNAME'${NC}"
echo ""

# Generate salt for password hashing
SALT=$(openssl rand -base64 12 | tr -d "=+/" | cut -c1-16)

echo -e "${BLUE}🔐 Generating new password hash...${NC}"

# Generate SHA-512 hash using available tools
if command -v mkpasswd >/dev/null 2>&1; then
    # Use mkpasswd if available (most Linux distributions)
    HASH=$(mkpasswd -m sha-512 -S "$SALT" "$NEW_PASSWORD")
elif command -v python3 >/dev/null 2>&1; then
    # Fallback to Python (suppress deprecation warning)
    HASH=$(python3 -c "
import crypt, sys
try:
    result = crypt.crypt('$NEW_PASSWORD', '\$6\$${SALT}\$')
    print(result)
except:
    sys.exit(1)
" 2>/dev/null)
else
    # Manual SHA-512 implementation using OpenSSL (basic compatibility)
    echo -e "${YELLOW}⚠️  Neither mkpasswd nor python3 available. Using basic OpenSSL method.${NC}"
    echo -e "${YELLOW}⚠️  This may not be fully compatible with all systems.${NC}"
    
    # This is a simplified approach - may not work with all crypt implementations
    HASH_PART=$(echo -n "${NEW_PASSWORD}${SALT}" | openssl dgst -sha512 -binary | openssl base64 | tr -d '\n=+/' | cut -c1-86)
    HASH="\$6\$${SALT}\$${HASH_PART}"
fi

if [ $? -ne 0 ] || [ -z "$HASH" ]; then
    echo -e "${RED}❌ Failed to generate password hash${NC}"
    exit 1
fi

echo -e "${GREEN}✅ New password hash generated${NC}"

# Create backup copy
cp "$SHADOW_FILE" "$SHADOW_FILE.backup.$(date +%Y%m%d_%H%M%S)"
echo -e "${BLUE}📋 Created backup: $(basename "$SHADOW_FILE").backup.$(date +%Y%m%d_%H%M%S)${NC}"

# Update password in shadow file
DAYS_SINCE_EPOCH=$(( $(date +%s) / 86400 ))

# Read the existing shadow entry to preserve other fields
EXISTING_LINE=$(grep "^$USERNAME:" "$SHADOW_FILE")
if [ -n "$EXISTING_LINE" ]; then
    # Parse existing fields
    IFS=: read -r user _ old_last_change min_age max_age warn_period inactive_period expire_date reserved <<< "$EXISTING_LINE"
    
    # Create new shadow entry with updated password and last change date
    NEW_SHADOW_LINE="$USERNAME:$HASH:$DAYS_SINCE_EPOCH:${min_age:-0}:${max_age:-99999}:${warn_period:-7}:${inactive_period:-}:${expire_date:-}:${reserved:-}"
else
    # Create new shadow entry with defaults
    NEW_SHADOW_LINE="$USERNAME:$HASH:$DAYS_SINCE_EPOCH:0:99999:7:::"
fi

# Replace the user's line in shadow file
sed -i "/^$USERNAME:/c\\$NEW_SHADOW_LINE" "$SHADOW_FILE"

echo -e "${GREEN}✅ Password updated in $(basename "$SHADOW_FILE")${NC}"

echo ""
echo -e "${GREEN}🎉 Password for user '$USERNAME' successfully updated!${NC}"
echo ""
echo -e "${CYAN}📋 Next Steps:${NC}"
echo -e "   1. Restart Claude Server API: ${PURPLE}./scripts/restart-api.sh${NC}"
echo -e "   2. Test new password: ${PURPLE}curl -X POST http://localhost:5185/auth/login -H 'Content-Type: application/json' -d '{\"username\":\"$USERNAME\",\"password\":\"$NEW_PASSWORD\"}'${NC}"
echo -e "   3. Or use Web UI: ${PURPLE}http://localhost:5173${NC}"
echo ""
echo -e "${BLUE}🔧 Management Commands:${NC}"
echo -e "   List users: ${PURPLE}./scripts/list-users.sh${NC}"
echo -e "   Add user: ${PURPLE}./scripts/add-user.sh <username> <password>${NC}"
echo -e "   Remove user: ${PURPLE}./scripts/remove-user.sh $USERNAME${NC}"