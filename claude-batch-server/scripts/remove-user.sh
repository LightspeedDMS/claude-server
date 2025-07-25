#!/bin/bash

# Claude Server User Management - Remove User
# Removes a user from the Claude Server authentication files
#
# Usage: ./remove-user.sh <username>

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

# Validation
if [ -z "$USERNAME" ]; then
    echo -e "${RED}❌ Usage: $0 <username>${NC}"
    echo -e "${YELLOW}   Example: $0 alice${NC}"
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
    echo -e "${YELLOW}⚠️  User '$USERNAME' does not exist in Claude Server authentication${NC}"
    exit 1
fi

echo -e "${CYAN}🗑️  Removing user from Claude Server authentication${NC}"
echo -e "${BLUE}📊 User: ${YELLOW}$USERNAME${NC}"
echo ""

# Create backup copies
cp "$PASSWD_FILE" "$PASSWD_FILE.backup.$(date +%Y%m%d_%H%M%S)"
cp "$SHADOW_FILE" "$SHADOW_FILE.backup.$(date +%Y%m%d_%H%M%S)"
echo -e "${BLUE}📋 Created backups with timestamp${NC}"

# Remove user from passwd file
sed -i "/^$USERNAME:/d" "$PASSWD_FILE"
echo -e "${GREEN}✅ Removed user from $(basename "$PASSWD_FILE")${NC}"

# Remove user from shadow file
sed -i "/^$USERNAME:/d" "$SHADOW_FILE"
echo -e "${GREEN}✅ Removed user from $(basename "$SHADOW_FILE")${NC}"

echo ""
echo -e "${GREEN}🎉 User '$USERNAME' successfully removed from Claude Server authentication!${NC}"
echo ""
echo -e "${CYAN}📋 Next Steps:${NC}"
echo -e "   1. Restart Claude Server API: ${PURPLE}./scripts/restart-api.sh${NC}"
echo -e "   2. Verify removal: ${PURPLE}./scripts/list-users.sh${NC}"
echo ""
echo -e "${BLUE}🔧 Management Commands:${NC}"
echo -e "   List users: ${PURPLE}./scripts/list-users.sh${NC}"
echo -e "   Add user: ${PURPLE}./scripts/add-user.sh <username> <password>${NC}"