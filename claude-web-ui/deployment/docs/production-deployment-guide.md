# Claude Web UI - Complete Production Deployment Guide

## Overview

This comprehensive guide covers the complete production deployment of the Claude Web UI behind NGINX with SSL, monitoring, performance optimization, and security hardening.

## Prerequisites

### System Requirements
- **Operating System**: Ubuntu 20.04+ or Debian 11+ (recommended)
- **CPU**: 2+ cores (4+ cores recommended for high traffic)
- **Memory**: 4GB RAM minimum (8GB+ recommended)
- **Storage**: 20GB+ available space (SSD recommended)
- **Network**: Static IP address and domain name for SSL

### Software Requirements
- **Node.js**: Version 18.0.0 or higher
- **npm**: Version 8.0.0 or higher
- **NGINX**: Version 1.18+ with SSL modules
- **SSL Certificate**: Let's Encrypt or commercial certificate
- **Claude Batch Server**: Running on port 5000

### Access Requirements
- Root or sudo access on the server
- DNS configuration access for domain setup
- Email service for monitoring alerts (optional)

## Quick Start (Automated)

For a complete automated deployment, run:

```bash
# 1. Clone and navigate to project
cd /home/jsbattig/Dev/claude-server/claude-web-ui

# 2. Make all scripts executable
sudo chmod +x deployment/scripts/*.sh
sudo chmod +x deployment/monitoring/*.sh

# 3. Run complete deployment
sudo ./deployment/scripts/deploy.sh production

# 4. Setup SSL certificate
sudo ./deployment/scripts/ssl-setup.sh your-domain.com admin@your-domain.com letsencrypt

# 5. Apply performance optimizations
sudo ./deployment/scripts/performance-tuning.sh --with-redis

# 6. Start monitoring
sudo systemctl enable claude-health-monitor
sudo systemctl start claude-health-monitor
```

## Detailed Step-by-Step Installation

### Step 1: System Preparation

1. **Update system packages:**
   ```bash
   sudo apt update && sudo apt upgrade -y
   sudo reboot  # If kernel updates were installed
   ```

2. **Install required packages:**
   ```bash
   sudo apt install -y nginx nodejs npm git curl wget unzip \
                      certbot python3-certbot-nginx \
                      redis-server htop iotop nethogs \
                      jq bc mailutils logrotate
   ```

3. **Verify installations:**
   ```bash
   node --version    # Should be 18+
   npm --version     # Should be 8+
   nginx -v          # Should be 1.18+
   ```

### Step 2: Project Setup

1. **Navigate to project directory:**
   ```bash
   cd /home/jsbattig/Dev/claude-server/claude-web-ui
   ```

2. **Make scripts executable:**
   ```bash
   chmod +x deployment/scripts/*.sh
   chmod +x deployment/monitoring/*.sh
   ```

3. **Create necessary directories:**
   ```bash
   sudo mkdir -p /var/www/claude-web-ui
   sudo mkdir -p /var/log/claude-web-ui
   sudo mkdir -p /var/backups/claude-web-ui
   sudo chown -R www-data:www-data /var/www/claude-web-ui
   ```

### Step 3: Application Build

```bash
# Build the application
./deployment/scripts/build.sh

# Verify build output
ls -la dist/
```

### Step 4: NGINX Configuration

1. **Deploy NGINX configuration:**
   ```bash
   sudo cp deployment/nginx/claude-web-ui.conf /etc/nginx/sites-available/claude-web-ui
   sudo ln -sf /etc/nginx/sites-available/claude-web-ui /etc/nginx/sites-enabled/
   ```

2. **Remove default site (optional):**
   ```bash
   sudo rm -f /etc/nginx/sites-enabled/default
   ```

3. **Test configuration:**
   ```bash
   sudo nginx -t
   ```

### Step 5: SSL Certificate Setup

#### Option A: Let's Encrypt (Production)

```bash
# Setup Let's Encrypt certificate
sudo ./deployment/scripts/ssl-setup.sh your-domain.com admin@your-domain.com letsencrypt

# Verify certificate
sudo /usr/local/bin/claude-ssl-status
```

#### Option B: Self-Signed (Development/Testing)

```bash
# Generate self-signed certificate
sudo ./deployment/scripts/ssl-setup.sh localhost admin@localhost self-signed
```

### Step 6: Application Deployment

```bash
# Deploy application to NGINX
sudo ./deployment/scripts/deploy.sh production

# Verify deployment
curl -k https://localhost/health
```

### Step 7: Performance Optimization

```bash
# Apply performance optimizations
sudo ./deployment/scripts/performance-tuning.sh --with-redis

# Restart services to apply optimizations
sudo systemctl restart nginx
sudo systemctl restart redis-server
```

### Step 8: Monitoring Setup

1. **Configure health monitoring:**
   ```bash
   # Create systemd service
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
   Environment=ALERT_EMAIL=admin@your-domain.com
   
   [Install]
   WantedBy=multi-user.target
   EOF
   
   sudo systemctl enable claude-health-monitor
   sudo systemctl start claude-health-monitor
   ```

2. **Configure log monitoring:**
   ```bash
   # Create systemd service
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
   Environment=ALERT_EMAIL=admin@your-domain.com
   
   [Install]
   WantedBy=multi-user.target
   EOF
   
   sudo systemctl enable claude-log-monitor
   sudo systemctl start claude-log-monitor
   ```

3. **Setup log analysis cron job:**
   ```bash
   # Add to root crontab
   sudo crontab -e
   
   # Add this line for daily log analysis at 6 AM
   0 6 * * * /home/jsbattig/Dev/claude-server/claude-web-ui/deployment/monitoring/log-analysis.sh 24
   ```

### Step 9: Backup Configuration

```bash
# Create initial full backup
sudo ./deployment/scripts/backup-and-rollback.sh backup initial-production-backup

# Setup automated backups
sudo crontab -e

# Add these lines for automated backups
0 2 * * * /home/jsbattig/Dev/claude-server/claude-web-ui/deployment/scripts/backup-and-rollback.sh backup
0 3 * * 0 /home/jsbattig/Dev/claude-server/claude-web-ui/deployment/scripts/backup-and-rollback.sh clean 10
```

### Step 10: Firewall Configuration

```bash
# Configure UFW firewall
sudo ufw allow ssh
sudo ufw allow 80/tcp
sudo ufw allow 443/tcp
sudo ufw --force enable

# Verify firewall status
sudo ufw status
```

## Configuration Files

### Key Configuration Files

| File | Purpose |
|------|---------|
| `/etc/nginx/sites-available/claude-web-ui` | NGINX site configuration |
| `/etc/ssl/certs/claude-web-ui.crt` | SSL certificate |
| `/etc/ssl/private/claude-web-ui.key` | SSL private key |
| `/var/www/claude-web-ui/dist/` | Web application files |
| `/etc/systemd/system/claude-*.service` | Monitoring services |

### Environment Variables

Set these environment variables for monitoring:

```bash
export ALERT_EMAIL="admin@your-domain.com"
export CHECK_INTERVAL=300
export LOG_RETENTION_DAYS=30
```

## Deployment Scripts Reference

### Available Scripts

| Script | Purpose | Usage |
|--------|---------|-------|
| `build.sh` | Build application | `./build.sh [--skip-tests] [--verbose]` |
| `deploy.sh` | Deploy to NGINX | `sudo ./deploy.sh [production\|development\|rollback\|status]` |
| `ssl-setup.sh` | SSL certificate setup | `sudo ./ssl-setup.sh [domain] [email] [cert-type]` |
| `backup-and-rollback.sh` | Backup management | `sudo ./backup-and-rollback.sh [backup\|restore\|rollback]` |
| `performance-tuning.sh` | Performance optimization | `sudo ./performance-tuning.sh [--with-redis]` |

### Monitoring Scripts

| Script | Purpose | Usage |
|--------|---------|-------|
| `health-check.sh` | Health monitoring | `./health-check.sh [health-url] [api-url]` |
| `log-monitor.sh` | Log monitoring | `./log-monitor.sh [--daemon]` |
| `log-analysis.sh` | Log analysis | `./log-analysis.sh [hours]` |

## Performance Optimizations

### NGINX Optimizations

The performance tuning script automatically configures:

- **Worker processes**: Optimized for CPU cores
- **Connection pooling**: Keep-alive connections
- **Gzip compression**: Reduced bandwidth usage
- **Static file caching**: Long-term browser caching
- **SSL optimization**: Session reuse and modern ciphers

### System Optimizations

- **Kernel parameters**: Network and file system tuning
- **File limits**: Increased limits for high concurrency
- **Memory management**: Optimized for web workloads
- **Redis caching**: Optional in-memory caching

### Expected Performance Improvements

- **Response time**: 20-40% faster for static assets
- **Throughput**: 2-3x more concurrent connections
- **CPU usage**: 10-15% reduction under load
- **Memory usage**: More efficient caching

## Security Features

### NGINX Security

- **Security headers**: HSTS, CSP, X-Frame-Options, etc.
- **Rate limiting**: Protection against abuse
- **SSL/TLS**: Modern protocols and ciphers only
- **Request filtering**: Size limits and validation

### System Security

- **Firewall**: UFW with minimal open ports
- **User permissions**: Proper file ownership
- **Log monitoring**: Automated threat detection
- **SSL certificates**: Automatic renewal

### Security Monitoring

The monitoring system detects:
- **Suspicious IP addresses**: High request volumes
- **Attack patterns**: SQL injection, XSS attempts
- **Error rates**: Potential security issues
- **Certificate expiration**: SSL certificate monitoring

## Monitoring and Alerting

### Health Monitoring

The system monitors:
- **Web frontend**: Response time and availability
- **API backend**: Claude Batch Server connectivity
- **NGINX service**: Process and configuration health
- **System resources**: CPU, memory, disk usage
- **SSL certificates**: Validity and expiration

### Log Analysis

Automated analysis includes:
- **Error rates**: HTTP 4xx/5xx responses
- **Performance**: Response times and slow requests
- **Security**: Attack patterns and suspicious activity
- **Traffic patterns**: Request volumes and trends

### Alert Conditions

Alerts are triggered for:
- **High error rates**: >10% error responses
- **Slow performance**: >20 requests taking >5 seconds
- **Security threats**: Attack patterns detected
- **System issues**: High resource usage or service failures
- **SSL expiration**: Certificate expires within 7 days

## Backup and Recovery

### Automated Backups

The system creates:
- **Daily backups**: Full application and configuration
- **Retention policy**: Keep 10 recent backups
- **Compression**: Gzip compression for space efficiency
- **Verification**: Backup integrity checks

### Backup Components

Each backup includes:
- **Web files**: Application dist directory
- **NGINX configuration**: Site configuration files
- **SSL certificates**: Certificate and private key files
- **Logs**: Application and system logs
- **Metadata**: Backup manifest with system info

### Recovery Procedures

```bash
# List available backups
sudo ./backup-and-rollback.sh list

# Quick rollback to last working
sudo ./backup-and-rollback.sh rollback

# Restore specific backup
sudo ./backup-and-rollback.sh restore backup-20240315-120000

# Verify backup integrity
sudo ./backup-and-rollback.sh verify backup-20240315-120000
```

## Troubleshooting

### Common Issues

1. **Build Failures**
   ```bash
   # Check Node.js version
   node --version
   
   # Clear npm cache
   npm cache clean --force
   
   # Check build logs
   tail -f /var/log/claude-web-ui/build.log
   ```

2. **Deployment Failures**
   ```bash
   # Check NGINX configuration
   sudo nginx -t
   
   # Check file permissions
   ls -la /var/www/claude-web-ui/dist/
   
   # Check deployment logs
   tail -f /var/log/claude-web-ui/deploy.log
   ```

3. **SSL Issues**
   ```bash
   # Check certificate status
   sudo /usr/local/bin/claude-ssl-status
   
   # Test SSL configuration
   openssl s_client -connect localhost:443 -servername your-domain.com
   
   # Check SSL logs
   tail -f /var/log/claude-web-ui/ssl-setup.log
   ```

4. **Performance Issues**
   ```bash
   # Check system resources
   htop
   iotop
   
   # Check NGINX status
   sudo systemctl status nginx
   
   # Analyze logs
   ./deployment/monitoring/log-analysis.sh 24
   ```

### Log Locations

| Log Type | Location |
|----------|----------|
| Build logs | `/var/log/claude-web-ui/build.log` |
| Deployment logs | `/var/log/claude-web-ui/deploy.log` |
| SSL setup logs | `/var/log/claude-web-ui/ssl-setup.log` |
| Health check logs | `/var/log/claude-web-ui/health-check.log` |
| NGINX access logs | `/var/log/nginx/claude-web-ui-access.log` |
| NGINX error logs | `/var/log/nginx/claude-web-ui-error.log` |
| Performance metrics | `/var/log/claude-web-ui/performance-metrics.json` |

### Debug Commands

```bash
# Check all services
sudo systemctl status nginx claude-health-monitor claude-log-monitor

# Test URLs
curl -k https://localhost/health
curl -k https://localhost/api/health

# Check processes
ps aux | grep nginx
ps aux | grep node

# Check network connections
netstat -tlnp | grep :80
netstat -tlnp | grep :443
netstat -tlnp | grep :5000

# Check disk space
df -h
du -sh /var/www/claude-web-ui
du -sh /var/log/claude-web-ui
```

## Maintenance Procedures

### Regular Maintenance Tasks

#### Daily
- Monitor health check reports
- Review error logs for issues
- Check system resource usage

#### Weekly
- Analyze log analysis reports
- Review security alerts
- Check backup integrity
- Update application if needed

#### Monthly
- Review performance metrics
- Clean old logs and backups
- Update system packages
- Review SSL certificate status

#### Quarterly
- Security audit and penetration testing
- Performance tuning review
- Disaster recovery testing
- Configuration backup to external storage

### Update Procedures

1. **Application Updates**
   ```bash
   # Pull latest code
   cd /home/jsbattig/Dev/claude-server/claude-web-ui
   git pull origin main
   
   # Build and deploy
   ./deployment/scripts/build.sh
   sudo ./deployment/scripts/deploy.sh production
   ```

2. **System Updates**
   ```bash
   # Update packages
   sudo apt update && sudo apt upgrade -y
   
   # Restart services if kernel updated
   sudo reboot
   ```

3. **SSL Certificate Renewal**
   ```bash
   # Test renewal
   sudo certbot renew --dry-run
   
   # Force renewal if needed
   sudo certbot renew --force-renewal
   ```

## Production Checklist

### Pre-Deployment Checklist

- [ ] System requirements verified (CPU, memory, storage)
- [ ] All required packages installed
- [ ] Domain name and DNS configured
- [ ] Firewall rules configured
- [ ] SSL certificate obtained and verified
- [ ] Claude Batch Server running and accessible
- [ ] All scripts executable and tested
- [ ] Backup system configured
- [ ] Monitoring configured
- [ ] Alert email addresses configured

### Post-Deployment Checklist

- [ ] Application accessible via HTTPS
- [ ] Health check endpoint responding
- [ ] API connectivity working
- [ ] SSL certificate valid and auto-renewing
- [ ] NGINX configuration optimized
- [ ] Performance monitoring active
- [ ] Log monitoring active
- [ ] Backup system tested
- [ ] Security headers present
- [ ] Error handling working
- [ ] Load testing completed

### Security Checklist

- [ ] All default passwords changed
- [ ] Unnecessary services disabled
- [ ] Firewall properly configured
- [ ] SSL/TLS properly configured
- [ ] Security headers implemented
- [ ] Rate limiting configured
- [ ] Log monitoring for security events
- [ ] Regular security updates scheduled
- [ ] Backup encryption enabled
- [ ] Access controls implemented

## Support and Resources

### Getting Help

1. **Log Analysis**: Check system logs first
2. **Documentation**: Review this guide and script help
3. **Health Checks**: Run manual health checks
4. **Backup Recovery**: Use rollback for critical issues

### Performance Monitoring

- **Real-time**: Use htop, iotop, nethogs
- **Historical**: Review performance metrics JSON files
- **Trends**: Analyze log analysis reports
- **Alerts**: Configure email notifications

### Best Practices

1. **Regular backups**: Automated daily backups
2. **Monitoring**: 24/7 health and performance monitoring
3. **Updates**: Regular security and application updates
4. **Testing**: Test changes in development first
5. **Documentation**: Keep deployment logs and changes documented

This production deployment guide provides everything needed for a secure, performant, and maintainable Claude Web UI deployment. Follow the procedures carefully and customize the configuration for your specific environment.