# Deployment Guide

TuneBridge is designed for easy deployment across various platforms. This guide covers deployment options and best practices.

## Docker Deployment

Docker images are automatically built and published via GitHub Actions to Docker Hub.

### Using Pre-built Images

Pull the latest image from Docker Hub:

```bash
docker pull $DOCKERHUB_USERNAME/tunebridge-test:latest
```

Run the container:

```bash
docker run -p 10000:10000 \
  -e SPOTIFY_CLIENT_ID="your_client_id" \
  -e SPOTIFY_CLIENT_SECRET="your_client_secret" \
  $DOCKERHUB_USERNAME/tunebridge-test:latest
```

**Note**: Images are built for `linux/arm64` platform as configured in the GitHub workflow.

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
# Pull latest image from Docker Hub
docker pull $DOCKERHUB_USERNAME/tunebridge-test:latest

# Stop and remove old container
docker stop tunebridge
docker rm tunebridge

# Start new container
docker run -d --name tunebridge ...
```
