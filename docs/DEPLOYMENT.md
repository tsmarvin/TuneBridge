# Deployment Guide

BridgeBeats is designed for easy deployment across various platforms. This guide covers deployment options and best practices.

> **Note**: This guide covers **production deployment**. For local development with the Aspire Dashboard, see the [Local Development Guide](LOCAL_DEVELOPMENT.md).

## Quick Start with Docker Compose

The easiest way to deploy BridgeBeats is with Docker Compose, which includes Caddy as a secure reverse proxy with automatic HTTPS.

```bash
git clone https://github.com/tsmarvin/BridgeBeats.git
cd BridgeBeats
./setup-secrets.sh
# Edit secrets/ with your credentials
cp .env.example .env
# Edit .env with your configuration
docker compose up -d
```

Visit `https://localhost` (or your configured domain) to access BridgeBeats.

For detailed Docker Compose deployment instructions, see the [Quick Start Guide](QUICKSTART.md).

## Docker Deployment

### Using Docker Compose (Recommended)

Docker Compose deployment includes:
- **Caddy reverse proxy** - Automatic HTTPS with Let's Encrypt
- **Security headers** - HSTS, CSP, X-Frame-Options, and more
- **Docker secrets** - Secure credential management
- **Health checks** - Automatic monitoring and restarts
- **Persistent volumes** - Data and certificate storage

#### Architecture

The Docker Compose setup consists of:
1. **BridgeBeats Application** - .NET 9.0 web application (port 10000)
2. **Caddy Reverse Proxy** - Automatic HTTPS with Let's Encrypt (ports 80/443)
3. **Docker Secrets** - Secure credential management
4. **Persistent Volumes** - Data and certificate storage

#### Configuration

Edit `.env` to configure your deployment:

```bash
# Domain for HTTPS certificates (use your actual domain in production)
DOMAIN=yourdomain.com
CADDY_ADMIN_EMAIL=admin@yourdomain.com

# API credentials (at least one provider required)
APPLE_TEAM_ID=your_team_id
APPLE_KEY_ID=your_key_id
SPOTIFY_CLIENT_ID=your_client_id
TIDAL_CLIENT_ID=your_client_id
```

Sensitive values go in the `secrets/` directory (created by `setup-secrets.sh`).

### Using Pre-built Images

Pull the latest image from Docker Hub:

```bash
docker pull tsmarvin/bridgebeats:latest
```

Run the container:

```bash
docker run -p 10000:10000 \
  -e SPOTIFY_CLIENT_ID="your_client_id" \
  -e SPOTIFY_CLIENT_SECRET="your_client_secret" \
  $DOCKERHUB_USERNAME/bridgebeats:latest
```

**Note**: Images are built for `linux/amd64` and `linux/arm64` platforms.

### SBOM and Provenance

All Docker images include Software Bill of Materials (SBOM) and provenance attestations for supply chain security. These attestations allow you to:
- Verify the image was built from official sources
- Inspect all software components and dependencies
- Check for known vulnerabilities
- Trace back to the exact source code and build process

See the [SBOM and Provenance Guide](SBOM_AND_PROVENANCE.md) for detailed instructions on accessing and verifying these attestations.

### Docker Secrets

For production deployments, use Docker secrets instead of environment variables:

```bash
# Create secrets
echo "your_spotify_secret" | docker secret create spotify_client_secret -
echo "your_api_salt" | docker secret create api_key_salt -

# Deploy with Docker Swarm
docker stack deploy -c docker-compose.yml bridgebeats
```

The entrypoint script automatically reads secrets from `/run/secrets/` and falls back to environment variables if secrets are not available.

## Environment Configuration

### Production Settings

Update `BridgeBeats__BaseUrl` to your public domain:

```bash
-e BridgeBeats__BaseUrl=bridgebeats.link
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
-e ALLOWED_HOSTS=bridgebeats.link
```

## Monitoring

### Application Logs

BridgeBeats logs to stdout/stderr by default. Configure log aggregation based on your platform:

**Docker:**
```bash
docker logs -f bridgebeats
```

## Scaling

### Horizontal Scaling

BridgeBeats is stateless (except for the optional SQLite cache) and can be horizontally scaled by deploying multiple instances behind a load balancer.

### Discord Bot Sharding

For large Discord deployments, use the `NODE_NUMBER` environment variable:

```bash
# Instance 1
-e NODE_NUMBER=0

# Instance 2
-e NODE_NUMBER=1
```

Note: If you do not increment this integer you could have multiple instances replying to the same events.


## Security Considerations

1. **Use HTTPS** - Always deploy behind SSL/TLS
2. **Secure secrets** - Use secret management systems (not environment variables in production)
3. **Rate limiting** - Configure reverse proxy rate limiting as an additional layer
4. **API key rotation** - Regularly regenerate API keys
5. **Minimal permissions** - Run containers with minimal privileges

## Backup and Recovery

### SQLite Cache

If using the local SQLite cache:

```bash
# Backup
docker cp bridgebeats:/app/cache/medialinkscache.db ./backup/

# Restore
docker cp ./backup/medialinkscache.db bridgebeats:/app/cache/
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
docker logs bridgebeats
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
# Pull latest image from Docker Hub
docker pull tsmarvin/bridgebeats:latest

# Stop and remove old container
docker stop bridgebeats
docker rm bridgebeats

# Start new container
docker run -d --name bridgebeats ...
```
