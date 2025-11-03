# Deployment Guide

TuneBridge is designed for easy deployment across various platforms. This guide covers deployment options and best practices.

## Docker Deployment (Recommended)

Docker provides the simplest deployment path with consistent behavior across environments.

### Building the Docker Image

```bash
docker build -t tunebridge .
```

### Running with Docker

#### Basic Setup

```bash
docker run -d \
  --name tunebridge \
  -p 10000:10000 \
  -e APPLE_TEAM_ID="your_team_id" \
  -e APPLE_KEY_ID="your_key_id" \
  -e APPLE_KEY_PATH="/app/key.p8" \
  -e SPOTIFY_CLIENT_ID="your_client_id" \
  -e SPOTIFY_CLIENT_SECRET="your_client_secret" \
  -v /path/to/your/AuthKey_KEYID.p8:/app/key.p8 \
  tunebridge
```

#### With All Services

```bash
docker run -d \
  --name tunebridge \
  -p 10000:10000 \
  -e APPLE_TEAM_ID="your_team_id" \
  -e APPLE_KEY_ID="your_key_id" \
  -e APPLE_KEY_PATH="/app/key.p8" \
  -e SPOTIFY_CLIENT_ID="your_client_id" \
  -e SPOTIFY_CLIENT_SECRET="your_client_secret" \
  -e TIDAL_CLIENT_ID="your_tidal_client_id" \
  -e TIDAL_CLIENT_SECRET="your_tidal_client_secret" \
  -e DISCORD_TOKEN="your_bot_token" \
  -e BLUESKY_PDS_URL="https://bsky.social" \
  -e BLUESKY_IDENTIFIER="your-handle.bsky.social" \
  -e BLUESKY_PASSWORD="your-app-password" \
  -v /path/to/your/AuthKey_KEYID.p8:/app/key.p8 \
  tunebridge
```

#### With Persistent Cache

```bash
docker run -d \
  --name tunebridge \
  -p 10000:10000 \
  -e SPOTIFY_CLIENT_ID="your_client_id" \
  -e SPOTIFY_CLIENT_SECRET="your_client_secret" \
  -v /path/to/cache:/app/cache \
  -e CACHE_DB_PATH="/app/cache/medialinkscache.db" \
  tunebridge
```

### Docker Compose

Create a `docker-compose.yml` file:

```yaml
version: '3.8'

services:
  tunebridge:
    build: .
    ports:
      - "10000:10000"
    environment:
      - APPLE_TEAM_ID=${APPLE_TEAM_ID}
      - APPLE_KEY_ID=${APPLE_KEY_ID}
      - APPLE_KEY_PATH=/app/key.p8
      - SPOTIFY_CLIENT_ID=${SPOTIFY_CLIENT_ID}
      - SPOTIFY_CLIENT_SECRET=${SPOTIFY_CLIENT_SECRET}
      - TIDAL_CLIENT_ID=${TIDAL_CLIENT_ID}
      - TIDAL_CLIENT_SECRET=${TIDAL_CLIENT_SECRET}
      - DISCORD_TOKEN=${DISCORD_TOKEN}
      - TuneBridge__BaseUrl=https://your-domain.com
    volumes:
      - ./keys/AuthKey.p8:/app/key.p8:ro
      - tunebridge-cache:/app/cache
    restart: unless-stopped

volumes:
  tunebridge-cache:
```

Run with:

```bash
docker-compose up -d
```

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

## Cloud Platform Deployment

### Azure Container Apps

```bash
# Create resource group
az group create --name tunebridge-rg --location eastus

# Create container app environment
az containerapp env create \
  --name tunebridge-env \
  --resource-group tunebridge-rg \
  --location eastus

# Deploy container app
az containerapp create \
  --name tunebridge \
  --resource-group tunebridge-rg \
  --environment tunebridge-env \
  --image your-registry/tunebridge:latest \
  --target-port 10000 \
  --ingress external \
  --env-vars \
    APPLE_TEAM_ID=secretref:apple-team-id \
    APPLE_KEY_ID=secretref:apple-key-id \
    SPOTIFY_CLIENT_ID=secretref:spotify-client-id \
    SPOTIFY_CLIENT_SECRET=secretref:spotify-client-secret
```

### Google Cloud Run

```bash
# Build and push to Google Container Registry
gcloud builds submit --tag gcr.io/PROJECT_ID/tunebridge

# Deploy to Cloud Run
gcloud run deploy tunebridge \
  --image gcr.io/PROJECT_ID/tunebridge \
  --platform managed \
  --region us-central1 \
  --allow-unauthenticated \
  --port 10000 \
  --set-env-vars SPOTIFY_CLIENT_ID=your_client_id \
  --set-secrets SPOTIFY_CLIENT_SECRET=spotify-secret:latest
```

### AWS ECS (Fargate)

1. Push image to ECR:
```bash
aws ecr create-repository --repository-name tunebridge
docker tag tunebridge:latest ACCOUNT_ID.dkr.ecr.REGION.amazonaws.com/tunebridge:latest
docker push ACCOUNT_ID.dkr.ecr.REGION.amazonaws.com/tunebridge:latest
```

2. Create task definition with environment variables
3. Create ECS service with Fargate launch type
4. Configure Application Load Balancer targeting port 10000

### Heroku

```bash
# Login to Heroku
heroku login

# Create app
heroku create your-tunebridge-app

# Set config vars
heroku config:set SPOTIFY_CLIENT_ID=your_client_id
heroku config:set SPOTIFY_CLIENT_SECRET=your_client_secret

# Deploy
git push heroku main
```

## Reverse Proxy Configuration

TuneBridge should be deployed behind a reverse proxy for SSL/TLS termination and additional security.

### Nginx

```nginx
server {
    listen 443 ssl http2;
    server_name tunebridge.example.com;

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

### Caddy

```caddy
tunebridge.example.com {
    reverse_proxy localhost:10000
}
```

### Apache

```apache
<VirtualHost *:443>
    ServerName tunebridge.example.com
    
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
-e TuneBridge__BaseUrl=https://tunebridge.example.com
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
-e ALLOWED_HOSTS=tunebridge.example.com
```

## Health Checks

TuneBridge doesn't currently expose a dedicated health endpoint. Configure health checks to verify the web server is responding:

```bash
curl -f http://localhost:10000/ || exit 1
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

### Metrics

Consider integrating with:
- Application Insights (Azure)
- CloudWatch (AWS)
- Cloud Logging (Google Cloud)
- Prometheus + Grafana (self-hosted)

## Scaling

### Horizontal Scaling

TuneBridge is stateless (except for the optional SQLite cache) and can be horizontally scaled:

1. **Without cache**: Deploy multiple instances behind a load balancer
2. **With cache**: Use a shared cache backend or accept cache misses across instances

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
dotnet build -c Release

# Restart service
systemctl restart tunebridge
```
