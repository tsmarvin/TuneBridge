# Deployment Guide

TuneBridge is designed for easy deployment across various platforms. This guide covers deployment options and best practices.

## Native Deployment

For deployments without Docker, you can run TuneBridge as a native .NET application.

### Prerequisites

- [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- Linux, Windows, or macOS

### Build and Run

```bash
# Clone the repository
git clone https://github.com/tsmarvin/TuneBridge.git
cd TuneBridge

# Build the application
dotnet build

# Run the application
dotnet run
```

### Production Build

```bash
# Publish a self-contained executable
dotnet publish -c Release -r linux-x64 --self-contained

# The executable will be in:
# src/bin/Release/net9.0/linux-x64/publish/TuneBridge
```

Supported runtime identifiers:
- `linux-x64` - Linux (x64)
- `linux-arm64` - Linux (ARM64)
- `win-x64` - Windows (x64)
- `win-arm64` - Windows (ARM64)
- `osx-arm64` - macOS (ARM64)

**Note**: After deploying the native build, you'll need to manually configure it as a system service if you want it to start automatically. The application does not automatically register as a systemd service.

## Cloud Platform Deployment

Deployment to cloud platforms is currently being evaluated. Check back for updates on supported platforms.

## Reverse Proxy Configuration

TuneBridge should be deployed behind a reverse proxy for SSL/TLS termination and additional security.

### Nginx

```nginx
server {
    listen 443 ssl http2;
    server_name tunebridge.media;

    ssl_certificate /path/to/cert.pem;
    ssl_certificate_key /path/to/key.pem;

    location / {
        proxy_pass http://localhost:10000;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection keep-alive;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_cache_bypass $http_upgrade;
    }
}
```

### Apache

```apache
<VirtualHost *:443>
    ServerName tunebridge.media
    
    SSLEngine on
    SSLCertificateFile /path/to/cert.pem
    SSLCertificateKeyFile /path/to/key.pem
    
    ProxyPreserveHost On
    ProxyPass / http://localhost:10000/
    ProxyPassReverse / http://localhost:10000/
</VirtualHost>
```

## Environment Configuration

### Production Settings

Update `TuneBridge__BaseUrl` to your public domain:

```bash
-e TuneBridge__BaseUrl=https://tunebridge.media
```

This ensures OpenGraph cards generate correct URLs.

### Logging

Set appropriate log levels for production:

```bash
-e DEFAULT_LOGLEVEL=Warning
-e HOSTING_DEFAULT_LOGLEVEL=Warning
```

### Allowed Hosts

Restrict allowed hosts for security:

```bash
-e ALLOWED_HOSTS=tunebridge.media
```

## Monitoring

### Application Logs

TuneBridge logs to stdout/stderr by default. Configure log aggregation based on your platform:

**Docker:**
```bash
docker logs -f tunebridge
```

**Kubernetes:**
```bash
kubectl logs -f deployment/tunebridge
```

## Scaling

### Horizontal Scaling

TuneBridge is stateless (except for the optional SQLite cache) and can be horizontally scaled by deploying multiple instances behind a load balancer.

### Discord Bot Sharding

For large Discord deployments, use the `NODE_NUMBER` environment variable:

```bash
# Instance 1
-e NODE_NUMBER=0

# Instance 2
-e NODE_NUMBER=1
```

## Security Considerations

1. **Use HTTPS** - Always deploy behind SSL/TLS
2. **Secure secrets** - Use secret management systems (not environment variables in production)
3. **Network isolation** - Deploy in private networks where possible
4. **Rate limiting** - Configure reverse proxy rate limiting as an additional layer
5. **API key rotation** - Regularly regenerate API keys
6. **Minimal permissions** - Run containers with minimal privileges

## Backup and Recovery

### SQLite Cache

If using the local SQLite cache:

```bash
# Backup
docker cp tunebridge:/app/cache/medialinkscache.db ./backup/

# Restore
docker cp ./backup/medialinkscache.db tunebridge:/app/cache/
```

### Configuration

Keep a secure backup of:
- API credentials
- Private keys (.p8 files)
- Configuration files

## Troubleshooting

### Container won't start

Check logs:
```bash
docker logs tunebridge
```

Common issues:
- Missing required environment variables
- Invalid API credentials
- Missing .p8 key file mount

### Connection refused

Verify:
- Container is running: `docker ps`
- Port mapping is correct: `-p 10000:10000`
- Firewall allows traffic on port 10000

### Discord bot not responding

Check:
- `DISCORD_TOKEN` is set correctly
- Bot has required permissions in Discord server
- Bot is invited to the server

## Updates

### Docker

```bash
# Pull latest image
docker pull your-registry/tunebridge:latest

# Stop and remove old container
docker stop tunebridge
docker rm tunebridge

# Start new container
docker run -d --name tunebridge ...
```

### Native

```bash
# Pull latest code
git pull

# Rebuild
dotnet build --no-restore --configuration Release

# If using systemd, restart service
systemctl restart tunebridge
```

**Note**: The systemctl command assumes you have manually configured TuneBridge as a systemd service.
