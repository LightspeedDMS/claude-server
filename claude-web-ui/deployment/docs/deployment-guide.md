# Claude Web UI Deployment Guide

## Overview

This guide provides comprehensive instructions for deploying the Claude Web UI behind NGINX with HTTPS support, monitoring, and production optimizations.

## Prerequisites

- Ubuntu/Debian Linux server
- Root or sudo access
- NGINX installed
- Node.js 18+ installed
- Claude Batch Server running on port 5000

## Quick Start

### 1. Build the Application

```bash
cd /home/jsbattig/Dev/claude-server/claude-web-ui
chmod +x deployment/scripts/build.sh
./deployment/scripts/build.sh
```

### 2. Deploy to NGINX

For production deployment:
```bash
sudo chmod +x deployment/scripts/deploy.sh
sudo ./deployment/scripts/deploy.sh production
```

For development deployment:
```bash
sudo ./deployment/scripts/deploy.sh development
```

### 3. Setup SSL Certificate

For production with Let's Encrypt:
```bash
sudo chmod +x deployment/scripts/ssl-setup.sh
sudo ./deployment/scripts/ssl-setup.sh your-domain.com admin@your-domain.com letsencrypt
```

For development with self-signed certificate:
```bash
sudo ./deployment/scripts/ssl-setup.sh localhost admin@localhost self-signed
```

### 4. Start Monitoring

```bash
sudo chmod +x deployment/monitoring/health-check.sh
sudo chmod +x deployment/monitoring/log-monitor.sh

# Run health check
sudo ./deployment/monitoring/health-check.sh

# Start log monitoring daemon
sudo ./deployment/monitoring/log-monitor.sh --daemon &
```

## Detailed Installation Steps

### Step 1: Prepare the Environment

1. **Update system packages:**
   ```bash
   sudo apt update && sudo apt upgrade -y
   ```

2. **Install required packages:**
   ```bash
   sudo apt install -y nginx nodejs npm curl bc mailutils
   ```

3. **Verify installations:**
   ```bash
   node --version  # Should be 18+
   npm --version
   nginx -v
   ```

### Step 2: Configure NGINX

1. **Copy NGINX configuration:**
   ```bash
   sudo cp deployment/nginx/claude-web-ui.conf /etc/nginx/sites-available/claude-web-ui
   ```

2. **Enable the site:**
   ```bash
   sudo ln -sf /etc/nginx/sites-available/claude-web-ui /etc/nginx/sites-enabled/
   ```

3. **Remove default site (optional):**
   ```bash
   sudo rm -f /etc/nginx/sites-enabled/default
   ```

4. **Test NGINX configuration:**
   ```bash
   sudo nginx -t
   ```

### Step 3: Build and Deploy

1. **Make scripts executable:**
   ```bash
   chmod +x deployment/scripts/*.sh
   chmod +x deployment/monitoring/*.sh
   ```

2. **Build the application:**
   ```bash
   ./deployment/scripts/build.sh
   ```

3. **Deploy to NGINX:**
   ```bash
   sudo ./deployment/scripts/deploy.sh production
   ```

### Step 4: SSL Configuration

#### Option A: Let's Encrypt (Recommended for Production)

1. **Install Let's Encrypt certificate:**
   ```bash
   sudo ./deployment/scripts/ssl-setup.sh your-domain.com admin@your-domain.com letsencrypt
   ```

2. **Verify certificate:**
   ```bash
   sudo /usr/local/bin/claude-ssl-status
   ```

#### Option B: Self-Signed Certificate (Development)

1. **Generate self-signed certificate:**
   ```bash
   sudo ./deployment/scripts/ssl-setup.sh localhost admin@localhost self-signed
   ```

### Step 5: Monitoring Setup

1. **Configure health monitoring:**
   ```bash
   # Create systemd service for health monitoring
   sudo tee /etc/systemd/system/claude-health-monitor.service << 'EOF'
   [Unit]
   Description=Claude Web UI Health Monitor
   After=network.target nginx.service
   
   [Service]
   Type=simple
   User=root
   ExecStart=/home/jsbattig/Dev/claude-server/claude-web-ui/deployment/monitoring/health-check.sh
   Restart=always
   RestartSec=300
   
   [Install]
   WantedBy=multi-user.target
   EOF
   
   sudo systemctl enable claude-health-monitor
   sudo systemctl start claude-health-monitor
   ```

2. **Configure log monitoring:**
   ```bash
   # Create systemd service for log monitoring
   sudo tee /etc/systemd/system/claude-log-monitor.service << 'EOF'
   [Unit]
   Description=Claude Web UI Log Monitor
   After=network.target nginx.service
   
   [Service]
   Type=simple
   User=root
   ExecStart=/home/jsbattig/Dev/claude-server/claude-web-ui/deployment/monitoring/log-monitor.sh --daemon
   Restart=always
   RestartSec=60
   Environment=CHECK_INTERVAL=300
   Environment=ALERT_EMAIL=admin@localhost
   
   [Install]
   WantedBy=multi-user.target
   EOF
   
   sudo systemctl enable claude-log-monitor
   sudo systemctl start claude-log-monitor
   ```

3. **Set up log rotation:**
   ```bash
   sudo tee /etc/logrotate.d/claude-web-ui << 'EOF'
   /var/log/claude-web-ui/*.log {
       daily
       missingok
       rotate 30
       compress
       delaycompress
       notifempty
       create 644 root root
       postrotate
           systemctl reload nginx > /dev/null 2>&1 || true
       endscript
   }
   
   /var/log/nginx/claude-web-ui-*.log {
       daily
       missingok
       rotate 30
       compress
       delaycompress
       notifempty
       create 644 www-data www-data
       postrotate
           systemctl reload nginx > /dev/null 2>&1 || true
       endscript
   }
   EOF
   ```

## Configuration Files

### NGINX Configuration Features

The NGINX configuration includes:

- **SSL/TLS termination** with modern security settings
- **Rate limiting** to prevent abuse
- **Gzip compression** for static assets
- **Cache headers** for optimal performance
- **Security headers** (HSTS, CSP, etc.)
- **API proxy** to Claude Batch Server
- **CORS support** for cross-origin requests
- **Health check endpoint**
- **SPA routing support**

### Environment-Specific Configurations

- **Production** (`claude-web-ui.conf`): Full security, SSL enforcement, rate limiting
- **Development** (`claude-web-ui-dev.conf`): Relaxed security, HTTP only, verbose logging

## Deployment Scripts

### build.sh

Features:
- Dependency installation and validation
- Test execution
- Production build with optimizations
- Build verification and reporting
- Automatic backup of previous builds

Usage:
```bash
./deployment/scripts/build.sh [--skip-tests] [--verbose]
```

### deploy.sh

Features:
- Automated deployment to NGINX directory
- Configuration management
- Backup and rollback support
- Health verification
- Environment-specific deployment

Usage:
```bash
sudo ./deployment/scripts/deploy.sh [production|development|rollback|status]
```

### ssl-setup.sh

Features:
- Let's Encrypt certificate installation
- Self-signed certificate generation
- Automatic renewal setup
- Certificate verification
- NGINX integration

Usage:
```bash
sudo ./deployment/scripts/ssl-setup.sh [domain] [email] [cert-type]
```

## Monitoring and Maintenance

### Health Monitoring

The health check script monitors:
- Web frontend availability
- API backend connectivity
- NGINX service status
- Disk space usage
- Memory consumption
- SSL certificate validity

### Log Monitoring

The log monitor tracks:
- HTTP error rates (4xx, 5xx responses)
- Slow response times
- Suspicious traffic patterns
- NGINX error conditions
- Application errors and warnings
- Resource usage trends

### Alerts

Configure email alerts by setting the `ALERT_EMAIL` environment variable:
```bash
export ALERT_EMAIL="admin@your-domain.com"
```

Alert conditions:
- High error rates
- Critical system errors
- SSL certificate expiration
- High resource usage
- Service unavailability

## Performance Optimizations

### NGINX Optimizations

- **Gzip compression** for text assets
- **Static file caching** with long expiry
- **Connection keep-alive** for API requests
- **Rate limiting** for security
- **SSL session reuse**

### Build Optimizations

- **Minification** of JS/CSS files
- **Asset bundling** and optimization
- **Tree shaking** to remove unused code
- **Production environment** variables

## Security Features

### NGINX Security

- **Security headers** (HSTS, CSP, X-Frame-Options, etc.)
- **Rate limiting** to prevent abuse
- **SSL/TLS** with modern cipher suites
- **Access control** for sensitive endpoints
- **Request size limits**

### Certificate Management

- **Automatic renewal** for Let's Encrypt certificates
- **Certificate validation** and monitoring
- **Secure key storage** with proper permissions
- **OCSP stapling** for certificate validation

## Troubleshooting

### Common Issues

1. **Build fails:**
   - Check Node.js version (18+ required)
   - Verify npm dependencies
   - Check disk space
   - Review build logs: `/var/log/claude-web-ui/build.log`

2. **Deployment fails:**
   - Verify NGINX is installed and running
   - Check file permissions
   - Ensure proper sudo access
   - Review deployment logs: `/var/log/claude-web-ui/deploy.log`

3. **SSL issues:**
   - Verify domain DNS resolution
   - Check firewall settings (ports 80, 443)
   - Review certificate status: `/usr/local/bin/claude-ssl-status`
   - Check SSL logs: `/var/log/claude-web-ui/ssl-setup.log`

4. **API connection issues:**
   - Verify Claude Batch Server is running on port 5000
   - Check firewall rules for internal connections
   - Review NGINX error logs
   - Test API directly: `curl http://localhost:5000/api/health`

### Log Locations

- **Build logs:** `/var/log/claude-web-ui/build.log`
- **Deployment logs:** `/var/log/claude-web-ui/deploy.log`
- **SSL setup logs:** `/var/log/claude-web-ui/ssl-setup.log`
- **Health check logs:** `/var/log/claude-web-ui/health-check.log`
- **Log monitor logs:** `/var/log/claude-web-ui/log-monitor.log`
- **NGINX access logs:** `/var/log/nginx/claude-web-ui-access.log`
- **NGINX error logs:** `/var/log/nginx/claude-web-ui-error.log`

### Debug Commands

```bash
# Check service status
sudo systemctl status nginx
sudo systemctl status claude-health-monitor
sudo systemctl status claude-log-monitor

# Test NGINX configuration
sudo nginx -t

# Check certificate status
sudo /usr/local/bin/claude-ssl-status

# Manual health check
sudo ./deployment/monitoring/health-check.sh

# View recent logs
sudo tail -f /var/log/nginx/claude-web-ui-error.log
sudo tail -f /var/log/claude-web-ui/deploy.log

# Test deployment
curl -k https://localhost/health
curl -k https://localhost/api/health
```

## Backup and Recovery

### Automated Backups

The deployment script automatically:
- Backs up current deployment before updates
- Maintains 10 recent deployment backups
- Creates rollback points for quick recovery

### Manual Backup

```bash
# Backup current deployment
sudo cp -r /var/www/claude-web-ui/dist /var/backups/claude-web-ui/manual-backup-$(date +%Y%m%d-%H%M%S)

# Backup NGINX configuration
sudo cp /etc/nginx/sites-available/claude-web-ui /var/backups/claude-web-ui/nginx-config-backup-$(date +%Y%m%d-%H%M%S)
```

### Rollback Procedure

```bash
# Automatic rollback to last working deployment
sudo ./deployment/scripts/deploy.sh rollback

# Manual rollback
sudo rm -rf /var/www/claude-web-ui/dist/*
sudo cp -r /var/backups/claude-web-ui/last-working/* /var/www/claude-web-ui/dist/
sudo chown -R www-data:www-data /var/www/claude-web-ui/dist
sudo systemctl reload nginx
```

## Production Checklist

Before going live:

- [ ] SSL certificate installed and valid
- [ ] NGINX configuration tested
- [ ] Health monitoring configured
- [ ] Log monitoring configured
- [ ] Email alerts configured
- [ ] Log rotation configured
- [ ] Firewall rules configured
- [ ] Backup strategy implemented
- [ ] Domain DNS configured
- [ ] Performance testing completed
- [ ] Security scanning completed

## Support

For issues and questions:
- Check logs in `/var/log/claude-web-ui/`
- Review this documentation
- Test individual components
- Check service status and configurations

## Updates and Maintenance

### Regular Updates

1. **Update application:**
   ```bash
   cd /home/jsbattig/Dev/claude-server/claude-web-ui
   git pull
   ./deployment/scripts/build.sh
   sudo ./deployment/scripts/deploy.sh production
   ```

2. **Update SSL certificates:**
   ```bash
   sudo certbot renew
   ```

3. **Update system packages:**
   ```bash
   sudo apt update && sudo apt upgrade -y
   sudo systemctl restart nginx
   ```

### Monitoring Maintenance

- Review health check reports weekly
- Analyze log monitor reports monthly
- Clean old backups quarterly
- Update alert configurations as needed
- Test rollback procedures regularly