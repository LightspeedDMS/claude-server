#!/bin/bash

# SSL Certificate Setup Script for Claude Web UI
# Supports both self-signed certificates and Let's Encrypt

set -euo pipefail

# Configuration
DOMAIN="${1:-localhost}"
EMAIL="${2:-admin@localhost}"
SSL_DIR="/etc/ssl/claude-web-ui"
CERT_FILE="/etc/ssl/certs/claude-web-ui.crt"
KEY_FILE="/etc/ssl/private/claude-web-ui.key"
LOG_FILE="/var/log/claude-web-ui/ssl-setup.log"

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
    log "ERROR" "SSL setup failed: $1"
    exit 1
}

# Check if running as root
check_permissions() {
    if [[ $EUID -ne 0 ]]; then
        log "ERROR" "This script must be run as root or with sudo"
        exit 1
    fi
}

# Install required packages
install_dependencies() {
    log "INFO" "Installing required packages..."
    
    # Update package list
    apt-get update
    
    # Install OpenSSL
    apt-get install -y openssl
    
    log "SUCCESS" "Dependencies installed"
}

# Create SSL directories
create_ssl_directories() {
    log "INFO" "Creating SSL directories..."
    
    mkdir -p "$SSL_DIR"
    mkdir -p "$(dirname "$CERT_FILE")"
    mkdir -p "$(dirname "$KEY_FILE")"
    
    # Set proper permissions
    chmod 700 "$SSL_DIR"
    chmod 700 "$(dirname "$KEY_FILE")"
    
    log "SUCCESS" "SSL directories created"
}

# Generate self-signed certificate
generate_self_signed_cert() {
    log "INFO" "Generating self-signed certificate for $DOMAIN..."
    
    # Create OpenSSL configuration
    local config_file="$SSL_DIR/openssl.conf"
    cat > "$config_file" << EOF
[req]
default_bits = 4096
prompt = no
default_md = sha256
distinguished_name = dn
req_extensions = v3_req

[dn]
C=US
ST=State
L=City
O=Organization
OU=IT Department
emailAddress=$EMAIL
CN=$DOMAIN

[v3_req]
basicConstraints = CA:FALSE
keyUsage = nonRepudiation, digitalSignature, keyEncipherment
subjectAltName = @alt_names

[alt_names]
DNS.1 = $DOMAIN
DNS.2 = *.$DOMAIN
DNS.3 = localhost
IP.1 = 127.0.0.1
IP.2 = ::1
EOF
    
    # Generate private key
    openssl genrsa -out "$KEY_FILE" 4096
    
    # Generate certificate
    openssl req -new -x509 -key "$KEY_FILE" -out "$CERT_FILE" -days 365 -config "$config_file" -extensions v3_req
    
    # Set proper permissions
    chmod 600 "$KEY_FILE"
    chmod 644 "$CERT_FILE"
    
    log "SUCCESS" "Self-signed certificate generated"
}

# Install Let's Encrypt certificate
install_letsencrypt_cert() {
    log "INFO" "Installing Let's Encrypt certificate for $DOMAIN..."
    
    # Check if domain is not localhost
    if [[ "$DOMAIN" == "localhost" ]] || [[ "$DOMAIN" =~ ^[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
        log "WARN" "Cannot use Let's Encrypt for localhost or IP addresses. Using self-signed certificate instead."
        generate_self_signed_cert
        return
    fi
    
    # Install certbot
    if ! command -v certbot &> /dev/null; then
        log "INFO" "Installing certbot..."
        apt-get install -y certbot python3-certbot-nginx
    fi
    
    # Create webroot directory
    mkdir -p /var/www/letsencrypt
    chown -R www-data:www-data /var/www/letsencrypt
    
    # Generate certificate
    if certbot certonly \
        --webroot \
        --webroot-path=/var/www/letsencrypt \
        --email "$EMAIL" \
        --agree-tos \
        --no-eff-email \
        --domains "$DOMAIN"; then
        
        # Copy certificates to our standard location
        cp "/etc/letsencrypt/live/$DOMAIN/fullchain.pem" "$CERT_FILE"
        cp "/etc/letsencrypt/live/$DOMAIN/privkey.pem" "$KEY_FILE"
        
        # Set proper permissions
        chmod 600 "$KEY_FILE"
        chmod 644 "$CERT_FILE"
        
        # Set up auto-renewal
        setup_cert_renewal
        
        log "SUCCESS" "Let's Encrypt certificate installed"
    else
        log "WARN" "Let's Encrypt certificate generation failed. Falling back to self-signed certificate."
        generate_self_signed_cert
    fi
}

# Setup certificate auto-renewal
setup_cert_renewal() {
    log "INFO" "Setting up certificate auto-renewal..."
    
    # Create renewal hook script
    local hook_script="/etc/letsencrypt/renewal-hooks/deploy/claude-web-ui-renewal.sh"
    mkdir -p "$(dirname "$hook_script")"
    
    cat > "$hook_script" << EOF
#!/bin/bash

# Copy renewed certificates to Claude Web UI location
cp "/etc/letsencrypt/live/$DOMAIN/fullchain.pem" "$CERT_FILE"
cp "/etc/letsencrypt/live/$DOMAIN/privkey.pem" "$KEY_FILE"

# Set proper permissions
chmod 600 "$KEY_FILE"
chmod 644 "$CERT_FILE"

# Reload NGINX
systemctl reload nginx

# Log the renewal
echo "\$(date): Certificate renewed for $DOMAIN" >> /var/log/claude-web-ui/cert-renewal.log
EOF
    
    chmod +x "$hook_script"
    
    # Test auto-renewal
    certbot renew --dry-run
    
    log "SUCCESS" "Certificate auto-renewal configured"
}

# Verify certificate
verify_certificate() {
    log "INFO" "Verifying certificate..."
    
    if [[ -f "$CERT_FILE" ]] && [[ -f "$KEY_FILE" ]]; then
        # Check certificate validity
        local cert_info=$(openssl x509 -in "$CERT_FILE" -text -noout)
        local subject=$(echo "$cert_info" | grep "Subject:" | head -1)
        local validity=$(echo "$cert_info" | grep "Not After" | head -1)
        
        log "INFO" "Certificate subject: $subject"
        log "INFO" "Certificate validity: $validity"
        
        # Verify private key matches certificate
        local cert_modulus=$(openssl x509 -noout -modulus -in "$CERT_FILE" | openssl md5)
        local key_modulus=$(openssl rsa -noout -modulus -in "$KEY_FILE" | openssl md5)
        
        if [[ "$cert_modulus" == "$key_modulus" ]]; then
            log "SUCCESS" "Certificate and private key match"
        else
            error_exit "Certificate and private key do not match"
        fi
        
        log "SUCCESS" "Certificate verification completed"
    else
        error_exit "Certificate or private key file missing"
    fi
}

# Update NGINX configuration for SSL
update_nginx_ssl_config() {
    log "INFO" "Updating NGINX SSL configuration..."
    
    local nginx_config="/etc/nginx/sites-available/claude-web-ui"
    
    if [[ -f "$nginx_config" ]]; then
        # Update certificate paths in NGINX config
        sed -i "s|ssl_certificate .*|ssl_certificate $CERT_FILE;|" "$nginx_config"
        sed -i "s|ssl_certificate_key .*|ssl_certificate_key $KEY_FILE;|" "$nginx_config"
        
        # Test NGINX configuration
        if nginx -t; then
            systemctl reload nginx
            log "SUCCESS" "NGINX SSL configuration updated"
        else
            error_exit "NGINX configuration test failed"
        fi
    else
        log "WARN" "NGINX configuration file not found. SSL configured but NGINX not updated."
    fi
}

# Create certificate status script
create_status_script() {
    log "INFO" "Creating certificate status script..."
    
    local status_script="/usr/local/bin/claude-ssl-status"
    cat > "$status_script" << 'EOF'
#!/bin/bash

# Certificate status script for Claude Web UI

CERT_FILE="/etc/ssl/certs/claude-web-ui.crt"
KEY_FILE="/etc/ssl/private/claude-web-ui.key"

# Colors
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
NC='\033[0m'

echo "Claude Web UI SSL Certificate Status"
echo "==================================="

if [[ -f "$CERT_FILE" ]]; then
    echo -ne "${GREEN}Certificate:${NC} "
    echo "Present"
    
    # Get certificate info
    SUBJECT=$(openssl x509 -in "$CERT_FILE" -subject -noout | sed 's/subject=//')
    ISSUER=$(openssl x509 -in "$CERT_FILE" -issuer -noout | sed 's/issuer=//')
    NOT_AFTER=$(openssl x509 -in "$CERT_FILE" -dates -noout | grep "notAfter" | sed 's/notAfter=//')
    
    echo "Subject: $SUBJECT"
    echo "Issuer: $ISSUER"
    echo "Expires: $NOT_AFTER"
    
    # Check if certificate is expired
    if openssl x509 -checkend 86400 -noout -in "$CERT_FILE" >/dev/null; then
        echo -e "${GREEN}Status: Valid${NC}"
    else
        echo -e "${RED}Status: Expires within 24 hours!${NC}"
    fi
    
    # Check days until expiration
    DAYS_LEFT=$(openssl x509 -in "$CERT_FILE" -checkend 0 -noout 2>/dev/null && echo $(($(date -d "$(openssl x509 -in "$CERT_FILE" -dates -noout | grep "notAfter" | sed 's/notAfter=//')" +%s) - $(date +%s))) / 86400 || echo "0")
    
    if [[ $DAYS_LEFT -gt 30 ]]; then
        echo -e "${GREEN}Days remaining: $DAYS_LEFT${NC}"
    elif [[ $DAYS_LEFT -gt 7 ]]; then
        echo -e "${YELLOW}Days remaining: $DAYS_LEFT${NC}"
    else
        echo -e "${RED}Days remaining: $DAYS_LEFT${NC}"
    fi
else
    echo -e "${RED}Certificate: Not found${NC}"
fi

if [[ -f "$KEY_FILE" ]]; then
    echo -e "${GREEN}Private Key: Present${NC}"
else
    echo -e "${RED}Private Key: Not found${NC}"
fi

# Check NGINX status
if systemctl is-active --quiet nginx; then
    echo -e "${GREEN}NGINX: Running${NC}"
else
    echo -e "${RED}NGINX: Not running${NC}"
fi
EOF
    
    chmod +x "$status_script"
    log "SUCCESS" "Certificate status script created: $status_script"
}

# Show SSL setup summary
show_summary() {
    log "INFO" "SSL Setup Summary:"
    echo -e "${BLUE}Domain:${NC} $DOMAIN"
    echo -e "${BLUE}Certificate:${NC} $CERT_FILE"
    echo -e "${BLUE}Private Key:${NC} $KEY_FILE"
    echo -e "${BLUE}SSL Directory:${NC} $SSL_DIR"
    echo -e "${BLUE}Log File:${NC} $LOG_FILE"
    echo ""
    echo "To check certificate status, run: /usr/local/bin/claude-ssl-status"
    echo "To renew Let's Encrypt certificate: certbot renew"
}

# Main execution
main() {
    local cert_type="${3:-self-signed}"
    
    log "INFO" "Starting SSL certificate setup..."
    log "INFO" "Domain: $DOMAIN"
    log "INFO" "Email: $EMAIL"
    log "INFO" "Certificate type: $cert_type"
    
    check_permissions
    install_dependencies
    create_ssl_directories
    
    if [[ "$cert_type" == "letsencrypt" ]]; then
        install_letsencrypt_cert
    else
        generate_self_signed_cert
    fi
    
    verify_certificate
    update_nginx_ssl_config
    create_status_script
    show_summary
    
    log "SUCCESS" "SSL certificate setup completed!"
}

# Handle script arguments
case "${1:-}" in
    --help|-h)
        echo "Usage: $0 [domain] [email] [cert-type]"
        echo ""
        echo "Arguments:"
        echo "  domain      Domain name (default: localhost)"
        echo "  email       Email address for Let's Encrypt (default: admin@localhost)"
        echo "  cert-type   Certificate type: 'self-signed' or 'letsencrypt' (default: self-signed)"
        echo ""
        echo "Examples:"
        echo "  $0 example.com admin@example.com letsencrypt"
        echo "  $0 localhost admin@localhost self-signed"
        echo "  $0 mysite.com"
        exit 0
        ;;
esac

# Run main function
main