# Claude Batch Server - Installation Script Improvements

## Overview

The `scripts/install.sh` script has been significantly enhanced to provide a smart installer for environments with comprehensive security and monitoring features.

## New Features Added

### 1. Production vs Development Modes
- **`--production`**: Full setup with nginx, SSL, firewall configuration
- **`--development`**: Basic installation without security components (default)

### 2. Interactive SSL Certificate Generation
- Prompts for SSL certificate details in production mode
- Supports command-line parameters to avoid interactive prompts
- Self-signed certificate generation with proper permissions
- Certificate validation and details display

### 3. Comprehensive Verification System
- **Service verification**: Checks if services start and run properly
- **Port verification**: Ensures ports are listening
- **Command verification**: Tests that installed tools work correctly
- **SSL verification**: Validates generated certificates
- **Firewall verification**: Confirms firewall rules are active

### 4. Intelligent Firewall Configuration

#### Rocky Linux/RHEL/CentOS (firewalld)
```bash
# Opens required ports
- HTTP (80) and HTTPS (443)
- Application port (5000)
- Docker ports (8080, 8443)
```

#### Ubuntu (ufw)
```bash
# Enables firewall and opens ports
- SSH (22) - prevents lockout
- HTTP (80) and HTTPS (443)  
- Application port (5000)
- Docker ports (8080, 8443)
```

### 5. Nginx Integration
- Automatic nginx installation for both Rocky and Ubuntu
- HTTPS-first configuration with HTTP → HTTPS redirect
- Reverse proxy setup for the application
- Security headers configuration
- SSL/TLS termination with modern ciphers
- Static file caching optimization

### 6. Configuration Backup System
- Automatic backup of all modified configuration files
- Timestamped backup directory: `/var/backups/claude-batch-server/YYYYMMDD_HHMMSS/`
- Backs up: nginx configs, systemd services, SSL certificates, firewall rules

### 7. Enhanced Sudo Handling
- Automatic re-execution with sudo if needed
- Proper sudo usage throughout all operations
- No hardcoded assumptions about user privileges

### 8. Systemd Service Hardening
- Security settings added to service file:
  - `NoNewPrivileges=true`
  - `PrivateTmp=true`
  - `ProtectSystem=strict`
  - `ReadWritePaths` for required directories
  - `ProtectHome=true`

## Usage Examples

### Development Installation (Default)
```bash
sudo ./scripts/install.sh --development
# or simply
sudo ./scripts/install.sh
```

### Production Installation with Interactive SSL Setup
```bash
sudo ./scripts/install.sh --production
```

### Production Installation with SSL Parameters
```bash
sudo ./scripts/install.sh --production \
  --ssl-cn "server.example.com" \
  --ssl-org "My Company Inc" \
  --ssl-country "US" \
  --ssl-state "California"
```

## SSL Certificate Parameters

| Parameter | Description | Example |
|-----------|-------------|---------|
| `--ssl-country` | 2-letter country code | US, CA, GB |
| `--ssl-state` | State or province | California, Ontario |
| `--ssl-city` | City name | San Francisco |
| `--ssl-org` | Organization name | My Company Inc |
| `--ssl-ou` | Organizational unit | IT Department |
| `--ssl-cn` | Common name (hostname) | server.example.com |

## Installation Verification

The script performs comprehensive validation:

### Base Components
- ✅ .NET Core SDK 8.0 installation and functionality
- ✅ Docker and Docker Compose installation
- ✅ Workspace directory creation (`/workspace/repos`, `/workspace/jobs`)
- ✅ Copy-on-Write filesystem support detection
- ✅ Optional: Claude CLI, pipx, code-indexer (cidx)

### Production Components (--production mode only)
- ✅ nginx installation and configuration test
- ✅ SSL certificate generation and validation
- ✅ Firewall service status and rule verification
- ✅ HTTPS port (443) listening check

## File Locations

### Configuration Files
- nginx site config: `/etc/nginx/sites-available/claude-batch-server`
- SSL certificates: `/etc/ssl/claude-batch-server/`
- systemd service: `/etc/systemd/system/claude-batch-server.service`
- Log rotation: `/etc/logrotate.d/claude-batch-server`

### Runtime Directories
- Application logs: `/var/log/claude-batch-server/`
- Workspace: `/workspace/repos`, `/workspace/jobs`
- Backups: `/var/backups/claude-batch-server/`

## Access URLs

### Production Mode
- **HTTPS (recommended)**: https://localhost/
- **HTTP (redirects)**: http://localhost/
- **Direct API**: http://localhost:5000/

### Development Mode
- **Direct API**: http://localhost:5000/
- **Docker**: http://localhost:8080/ (if using docker-compose)

## Security Features

### SSL/TLS Configuration
- TLS 1.2 and 1.3 support
- Modern cipher suites
- HSTS headers
- Security headers (X-Frame-Options, X-Content-Type-Options, etc.)

### Firewall Rules
- Minimal required ports only
- SSH access preserved (Ubuntu)
- Service-specific port access

### Service Hardening
- systemd security restrictions
- Non-privileged execution where possible
- Protected filesystem access

## Troubleshooting

### Check Installation Status
```bash
sudo systemctl status claude-batch-server
sudo systemctl status nginx        # Production mode only
sudo journalctl -u claude-batch-server -f
```

### Verify Firewall Rules
```bash
# Rocky/RHEL/CentOS
sudo firewall-cmd --list-all

# Ubuntu
sudo ufw status verbose
```

### Test SSL Certificate
```bash
openssl x509 -in /etc/ssl/claude-batch-server/server.crt -text -noout
```

### Check nginx Configuration
```bash
sudo nginx -t
```

## Rollback Instructions

Configuration backups are stored in `/var/backups/claude-batch-server/TIMESTAMP/`. To rollback:

1. Stop services:
   ```bash
   sudo systemctl stop claude-batch-server
   sudo systemctl stop nginx  # if production mode
   ```

2. Restore configurations from backup directory

3. Restart services:
   ```bash
   sudo systemctl start nginx
   sudo systemctl start claude-batch-server
   ```

## Future Enhancements

- [ ] Let's Encrypt integration for real SSL certificates
- [ ] Database backup configuration
- [ ] Monitoring and alerting setup (Prometheus/Grafana)
- [ ] Log aggregation configuration
- [ ] Docker Swarm/Kubernetes deployment options
- [ ] Health check endpoints and monitoring scripts