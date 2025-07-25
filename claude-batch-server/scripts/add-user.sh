#!/bin/bash

# Claude Server User Management - Add User
# Adds a user to the Claude Server authentication files
#
# Usage: ./add-user.sh <username> <password> [uid] [gid] [home_dir] [shell]

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

# Default values
DEFAULT_UID=1000
DEFAULT_GID=1000
DEFAULT_HOME="/home"
DEFAULT_SHELL="/bin/bash"

# Parse arguments
USERNAME="$1"
PASSWORD="$2"
UID="${3:-$DEFAULT_UID}"
GID="${4:-$DEFAULT_GID}"
HOME_DIR="${5:-$DEFAULT_HOME/$USERNAME}"
SHELL="${6:-$DEFAULT_SHELL}"

# Validation
if [ -z "$USERNAME" ] || [ -z "$PASSWORD" ]; then
    echo -e "${RED}❌ Usage: $0 <username> <password> [uid] [gid] [home_dir] [shell]${NC}"
    echo -e "${YELLOW}   Example: $0 alice mypassword123${NC}"
    echo -e "${YELLOW}   Example: $0 bob secret456 1001 1001 /home/bob /bin/bash${NC}"
    exit 1
fi

# Validate username format
if ! [[ "$USERNAME" =~ ^[a-zA-Z][a-zA-Z0-9_-]{2,31}$ ]]; then
    echo -e "${RED}❌ Invalid username format. Must start with letter, 3-32 chars, alphanumeric + underscore/dash only${NC}"
    exit 1
fi

# Check if user already exists
if [ -f "$PASSWD_FILE" ] && grep -q "^$USERNAME:" "$PASSWD_FILE"; then
    echo -e "${YELLOW}⚠️  User '$USERNAME' already exists. Use update-user.sh to modify.${NC}"
    exit 1
fi

echo -e "${CYAN}👤 Adding user to Claude Server authentication${NC}"
echo -e "${BLUE}📊 User Details:${NC}"
echo -e "   Username: ${YELLOW}$USERNAME${NC}"
echo -e "   UID: ${YELLOW}$UID${NC}"
echo -e "   GID: ${YELLOW}$GID${NC}"
echo -e "   Home: ${YELLOW}$HOME_DIR${NC}"
echo -e "   Shell: ${YELLOW}$SHELL${NC}"
echo ""

# Generate salt for password hashing
SALT=$(openssl rand -base64 12 | tr -d "=+/" | cut -c1-16)

echo -e "${BLUE}🔐 Generating password hash...${NC}"

# Generate SHA-512 hash using available tools
if command -v mkpasswd >/dev/null 2>&1; then
    # Use mkpasswd if available (most Linux distributions)
    HASH=$(mkpasswd -m sha-512 -S "$SALT" "$PASSWORD")
elif command -v python3 >/dev/null 2>&1; then
    # Fallback to Python (suppress deprecation warning)
    HASH=$(python3 -c "
import crypt, sys
try:
    result = crypt.crypt('$PASSWORD', '\$6\$${SALT}\$')
    print(result)
except:
    sys.exit(1)
" 2>/dev/null)
else
    # Manual SHA-512 implementation using OpenSSL (basic compatibility)
    echo -e "${YELLOW}⚠️  Neither mkpasswd nor python3 available. Using basic OpenSSL method.${NC}"
    echo -e "${YELLOW}⚠️  This may not be fully compatible with all systems.${NC}"
    
    # This is a simplified approach - may not work with all crypt implementations
    HASH_PART=$(echo -n "${PASSWORD}${SALT}" | openssl dgst -sha512 -binary | openssl base64 | tr -d '\n=+/' | cut -c1-86)
    HASH="\$6\$${SALT}\$${HASH_PART}"
fi

if [ $? -ne 0 ] || [ -z "$HASH" ]; then
    echo -e "${RED}❌ Failed to generate password hash${NC}"
    exit 1
fi

echo -e "${GREEN}✅ Password hash generated${NC}"

# Create backup copies if files exist
if [ -f "$PASSWD_FILE" ]; then
    cp "$PASSWD_FILE" "$PASSWD_FILE.backup.$(date +%Y%m%d_%H%M%S)"
    echo -e "${BLUE}📋 Created backup: $(basename "$PASSWD_FILE").backup.$(date +%Y%m%d_%H%M%S)${NC}"
fi

if [ -f "$SHADOW_FILE" ]; then
    cp "$SHADOW_FILE" "$SHADOW_FILE.backup.$(date +%Y%m%d_%H%M%S)"
    echo -e "${BLUE}📋 Created backup: $(basename "$SHADOW_FILE").backup.$(date +%Y%m%d_%H%M%S)${NC}"
fi

# Add user to passwd file
echo "$USERNAME:x:$UID:$GID:$USERNAME User:$HOME_DIR:$SHELL" >> "$PASSWD_FILE"
echo -e "${GREEN}✅ Added user to $(basename "$PASSWD_FILE")${NC}"

# Add user to shadow file with current date (days since epoch)
DAYS_SINCE_EPOCH=$(( $(date +%s) / 86400 ))
echo "$USERNAME:$HASH:$DAYS_SINCE_EPOCH:0:99999:7:::" >> "$SHADOW_FILE"
echo -e "${GREEN}✅ Added user to $(basename "$SHADOW_FILE")${NC}"

echo ""
echo -e "${GREEN}🎉 User '$USERNAME' successfully added to Claude Server authentication!${NC}"
echo ""
echo -e "${CYAN}📋 Next Steps:${NC}"
echo -e "   1. Restart Claude Server API: ${PURPLE}./scripts/restart-api.sh${NC}"
echo -e "   2. Test login: ${PURPLE}curl -X POST http://localhost:5185/auth/login -H 'Content-Type: application/json' -d '{\"username\":\"$USERNAME\",\"password\":\"$PASSWORD\"}'${NC}"
echo -e "   3. Or use Web UI: ${PURPLE}http://localhost:5173${NC}"
echo ""
echo -e "${BLUE}🔧 Management Commands:${NC}"
echo -e "   List users: ${PURPLE}./scripts/list-users.sh${NC}"
echo -e "   Remove user: ${PURPLE}./scripts/remove-user.sh $USERNAME${NC}"
echo -e "   Update password: ${PURPLE}./scripts/update-user.sh $USERNAME <new_password>${NC}"