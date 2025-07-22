# HTTPS Configuration Guide for Claude-Server

## Overview

The Claude-Server is **already configured for HTTPS** with nginx as a reverse proxy handling SSL termination. You only need to provide SSL certificates to enable HTTPS functionality.

## Current HTTPS Architecture

```
Internet → Nginx (HTTPS:443) → Claude-Server (.NET:80) → Internal Services
```

**Benefits of this architecture:**
- SSL termination at nginx layer (better performance)
- Automatic HTTP to HTTPS redirection
- Modern TLS configuration (TLS 1.2/1.3)
- Secure cipher suites
- Proper proxy headers for the .NET application

## Quick Start - Enable HTTPS

### 1. Create SSL Directory

```bash
# Navigate to your claude-server directory
cd /path/to/your/claude-server/claude-batch-server/docker

# Create SSL directory
mkdir -p ssl
```

### 2. Choose Certificate Option

#### Option A: Self-Signed Certificates (Development/Testing)

```bash
# Generate self-signed certificate (valid for 365 days)
openssl req -x509 -nodes -days 365 -newkey rsa:2048 \
  -keyout ./ssl/server.key \
  -out ./ssl/server.crt \
  -subj "/C=US/ST=State/L=City/O=Organization/CN=localhost"

# Set proper permissions
chmod 600 ./ssl/server.key
chmod 644 ./ssl/server.crt
```

#### Option B: Let's Encrypt (Production) - Domain Required

```bash
# Install certbot
sudo apt update && sudo apt install certbot

# Generate certificate (replace yourdomain.com)
sudo certbot certonly --standalone -d yourdomain.com

# Copy certificates to docker directory
sudo cp /etc/letsencrypt/live/yourdomain.com/fullchain.pem ./ssl/server.crt
sudo cp /etc/letsencrypt/live/yourdomain.com/privkey.pem ./ssl/server.key

# Set permissions
sudo chown $USER:$USER ./ssl/server.*
chmod 600 ./ssl/server.key
chmod 644 ./ssl/server.crt
```

#### Option C: Custom Certificate Authority

```bash
# If you have certificates from your organization's CA
cp /path/to/your/certificate.crt ./ssl/server.crt
cp /path/to/your/private.key ./ssl/server.key

# Set permissions
chmod 600 ./ssl/server.key
chmod 644 ./ssl/server.crt
```

### 3. Start Services

```bash
# From claude-batch-server/docker directory
docker-compose up -d

# Verify HTTPS is working
curl -k https://localhost:8443/health
```

## Configuration Details

### Current Nginx Configuration

The nginx service is pre-configured with:

```nginx
# HTTP to HTTPS redirect
server {
    listen 80;
    return 301 https://$server_name$request_uri;
}

# HTTPS configuration
server {
    listen 443 ssl http2;
    
    ssl_certificate /etc/nginx/ssl/server.crt;
    ssl_certificate_key /etc/nginx/ssl/server.key;
    
    ssl_protocols TLSv1.2 TLSv1.3;
    ssl_ciphers HIGH:!aNULL:!MD5;
    
    # Proxy to .NET application
    location / {
        proxy_pass http://claude-server:80;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto https;
    }
}
```

### Docker Compose Configuration

```yaml
services:
  nginx:
    image: nginx:alpine
    ports:
      - "8080:80"   # HTTP redirect
      - "8443:443"  # HTTPS
    volumes:
      - ./ssl:/etc/nginx/ssl:ro  # SSL certificates (read-only)
      - ./nginx.conf:/etc/nginx/nginx.conf:ro
    depends_on:
      - claude-server

  claude-server:
    # Internal HTTP communication (no SSL needed)
    environment:
      - ASPNETCORE_URLS=http://+:80
```

## Production Deployment

### 1. Domain Configuration

For production deployment with a real domain:

```bash
# Update nginx configuration to use your domain
sed -i 's/localhost/yourdomain.com/g' nginx.conf

# Or manually edit nginx.conf to set server_name
server_name yourdomain.com;
```

### 2. Let's Encrypt with Auto-Renewal

```bash
# Create renewal script
cat > ssl/renew-certs.sh << 'EOF'
#!/bin/bash
certbot renew --quiet
cp /etc/letsencrypt/live/yourdomain.com/fullchain.pem ./ssl/server.crt
cp /etc/letsencrypt/live/yourdomain.com/privkey.pem ./ssl/server.key
docker-compose restart nginx
EOF

chmod +x ssl/renew-certs.sh

# Add to crontab for automatic renewal
echo "0 3 * * * /path/to/ssl/renew-certs.sh" | crontab -
```

### 3. Enhanced Security Headers (Optional)

Add to nginx configuration:

```nginx
server {
    listen 443 ssl http2;
    
    # Security headers
    add_header Strict-Transport-Security "max-age=31536000; includeSubDomains" always;
    add_header X-Frame-Options DENY always;
    add_header X-Content-Type-Options nosniff always;
    add_header X-XSS-Protection "1; mode=block" always;
    add_header Referrer-Policy "strict-origin-when-cross-origin" always;
    
    # ... rest of configuration
}
```

## Troubleshooting

### Common Issues

#### 1. Certificate Permissions Error
```bash
# Error: Permission denied
sudo chown $USER:$USER ssl/server.*
chmod 600 ssl/server.key
chmod 644 ssl/server.crt
```

#### 2. Nginx Won't Start
```bash
# Check nginx configuration
docker-compose exec nginx nginx -t

# Check logs
docker-compose logs nginx
```

#### 3. Certificate Validation Errors
```bash
# Check certificate validity
openssl x509 -in ssl/server.crt -text -noout

# Verify private key matches
openssl x509 -noout -modulus -in ssl/server.crt | openssl md5
openssl rsa -noout -modulus -in ssl/server.key | openssl md5
# The MD5 hashes should match
```

#### 4. Browser Certificate Warnings (Self-Signed)
For development with self-signed certificates:
- Chrome: Click "Advanced" → "Proceed to localhost (unsafe)"
- Firefox: Click "Advanced" → "Accept the Risk and Continue"
- Or add certificate to browser's trusted certificates

### Verification Commands

```bash
# Test HTTP redirect
curl -I http://localhost:8080

# Test HTTPS (skip cert verification for self-signed)
curl -k https://localhost:8443/health

# Test SSL configuration
openssl s_client -connect localhost:8443 -servername localhost

# Check certificate expiration
echo | openssl s_client -servername localhost -connect localhost:8443 2>/dev/null | openssl x509 -noout -dates
```

## API Usage with HTTPS

Once HTTPS is enabled, update your API calls:

```bash
# Authentication
curl -k -X POST https://localhost:8443/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"testuser","password":"testpass"}'

# Create job with JWT token
curl -k -X POST https://localhost:8443/jobs \
  -H "Authorization: Bearer YOUR_JWT_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"prompt":"Hello Claude","repository":"test-repo"}'
```

## Security Considerations

### Production Checklist

- [ ] Use certificates from trusted CA (not self-signed)
- [ ] Configure automatic certificate renewal
- [ ] Update CORS policy for production domains
- [ ] Enable security headers
- [ ] Set up monitoring for certificate expiration
- [ ] Configure fail2ban for brute force protection
- [ ] Regular security updates for nginx and .NET runtime

### Development vs Production

| Feature | Development | Production |
|---------|-------------|------------|
| Certificates | Self-signed OK | Trusted CA required |
| CORS Policy | Permissive | Restrictive |
| Security Headers | Optional | Required |
| Certificate Renewal | Manual | Automated |
| Monitoring | Basic | Comprehensive |

## Architecture Benefits

The current nginx + .NET Core architecture provides:

1. **Performance**: SSL termination at nginx (more efficient than .NET Kestrel)
2. **Security**: Separation of concerns, nginx handles SSL/TLS
3. **Flexibility**: Easy to add caching, rate limiting, or WAF at nginx level
4. **Scalability**: Can easily add load balancing for multiple .NET instances
5. **Maintenance**: SSL certificate management centralized at nginx

## Next Steps

1. **Enable HTTPS** following this guide
2. **Test thoroughly** with your use cases
3. **Consider production enhancements**: domain, trusted certificates, monitoring
4. **Update API documentation** to use HTTPS endpoints
5. **Configure monitoring** for certificate expiration

## Support

If you encounter issues:

1. Check nginx and claude-server logs: `docker-compose logs`
2. Verify certificate files exist and have correct permissions
3. Test SSL configuration with openssl commands
4. Review nginx configuration syntax: `nginx -t`

The system is designed to be HTTPS-ready out of the box - you just need to provide the certificates!