# Docker Deployment Guide

This guide covers deploying TuneBridge using Docker with Caddy as a secure reverse proxy.

## Architecture Overview

The TuneBridge Docker deployment consists of:

1. **TuneBridge Application** - .NET 9.0 web application (port 10000)
2. **Caddy Reverse Proxy** - Automatic HTTPS with Let's Encrypt (ports 80/443)
3. **Docker Secrets** - Secure credential management
4. **Persistent Volumes** - Data and certificate storage

## Quick Start

### Local Development

1. **Clone the repository:**
   ```bash
   git clone https://github.com/tsmarvin/TuneBridge.git
   cd TuneBridge
   ```

2. **Set up secrets directory:**
   ```bash
   ./setup-secrets.sh
   ```

3. **Configure secrets:**
   Edit the files in `./secrets/` with your actual credentials:
   - `apple_key.p8` - Your Apple Music private key
   - `spotify_client_secret.txt` - Spotify client secret
   - `tidal_client_secret.txt` - Tidal client secret
   - `discord_token.txt` - Discord bot token (optional)
   - `bluesky_password.txt` - Bluesky app password (optional)
   - `api_key_salt.txt` - Random salt for API key hashing

4. **Configure environment variables:**
   ```bash
   cp .env.example .env
   # Edit .env with your configuration
   ```

5. **Start the services:**
   ```bash
   docker-compose up -d
   ```

6. **Access the application:**
   - HTTP: http://localhost
   - HTTPS: https://localhost (self-signed cert for local dev)
   - Direct access: http://localhost:10000

### Production Deployment

#### Prerequisites

- Docker Engine 20.10+
- Docker Compose 2.0+
- A domain name pointing to your server
- Firewall rules allowing ports 80 and 443

#### Steps

1. **Set up the server:**
   ```bash
   # Clone repository
   git clone https://github.com/tsmarvin/TuneBridge.git
   cd TuneBridge
   ```

2. **Configure secrets:**
   ```bash
   ./setup-secrets.sh
   # Edit all secret files in ./secrets/
   ```

3. **Configure production environment:**
   ```bash
   cp .env.example .env
   nano .env
   ```

   Key settings for production:
   ```bash
   # Your actual domain
   DOMAIN=tunebridge.yourdomain.com
   
   # Your email for Let's Encrypt
   CADDY_ADMIN_EMAIL=admin@yourdomain.com
   
   # Other API credentials...
   APPLE_TEAM_ID=YOUR_TEAM_ID
   APPLE_KEY_ID=YOUR_KEY_ID
   # ... etc
   ```

4. **Start in production mode:**
   ```bash
   docker-compose up -d
   ```

5. **Monitor logs:**
   ```bash
   docker-compose logs -f
   ```

6. **Verify HTTPS:**
   Visit `https://tunebridge.yourdomain.com` - Caddy will automatically obtain and renew Let's Encrypt certificates.

## Docker Secrets

Docker secrets provide secure credential management. The application supports reading secrets from `/run/secrets/` in the container.

### Available Secrets

| Secret Name | Description | Required |
|-------------|-------------|----------|
| `apple_key` | Apple Music private key (.p8) | No* |
| `spotify_client_secret` | Spotify API client secret | No* |
| `tidal_client_secret` | Tidal API client secret | No* |
| `discord_token` | Discord bot token | No** |
| `bluesky_password` | Bluesky app password | No*** |
| `api_key_salt` | Salt for API key hashing | Yes |

\* At least one music provider required  
\** Required for Discord integration  
\*** Required for Bluesky PDS caching

### Using Docker Swarm Secrets (Production)

For production deployments using Docker Swarm:

```bash
# Create secrets
docker secret create apple_key ./secrets/apple_key.p8
docker secret create spotify_client_secret ./secrets/spotify_client_secret.txt
docker secret create api_key_salt ./secrets/api_key_salt.txt

# Deploy stack
docker stack deploy -c docker-compose.yml tunebridge
```

## Caddy Configuration

### Default Configuration

The default `Caddyfile` provides:

- **Automatic HTTPS** - Let's Encrypt certificates
- **HTTP to HTTPS redirect**
- **Security headers** - HSTS, CSP, X-Frame-Options, etc.
- **Health checks** - Monitors TuneBridge on `/health`
- **Reverse proxy** - Forwards requests to TuneBridge on port 10000
- **Error handling** - Custom error pages

### Custom Caddyfile

To use a custom Caddyfile:

1. Create your custom `Caddyfile`:
   ```bash
   cp Caddyfile Caddyfile.custom
   # Edit Caddyfile.custom
   ```

2. Mount it in docker-compose.yml:
   ```yaml
   volumes:
     - ./Caddyfile.custom:/etc/caddy/Caddyfile:ro
   ```

### Local Development (Self-Signed Certificates)

For local development with `localhost` or custom domains:

1. Edit `.env`:
   ```bash
   DOMAIN=localhost
   ```

2. Accept the self-signed certificate warning in your browser

3. Alternatively, use `http://localhost:10000` for direct access without HTTPS

### Production Domains

For production with real domains:

1. Ensure DNS points to your server
2. Configure the domain in `.env`:
   ```bash
   DOMAIN=tunebridge.yourdomain.com
   ```
3. Caddy automatically obtains Let's Encrypt certificates

## Security Best Practices

### 1. Use Docker Secrets

Always use Docker secrets for sensitive data in production:

```yaml
secrets:
  - apple_key
  - spotify_client_secret
  # ... etc
```

### 2. Restrict File Permissions

Secret files should have restrictive permissions:

```bash
chmod 600 secrets/*
```

### 3. Never Commit Secrets

The `.gitignore` is configured to exclude:
- `.env`
- `secrets/`
- `docker-compose.override.yml`

### 4. Use Strong API Key Salt

Generate a strong random salt:

```bash
openssl rand -base64 32 > secrets/api_key_salt.txt
```

### 5. Regular Updates

Keep the Docker image updated:

```bash
docker-compose pull
docker-compose up -d
```

### 6. Monitor Logs

Regularly check logs for security issues:

```bash
docker-compose logs -f tunebridge
```

### 7. Firewall Configuration

Only expose necessary ports:

```bash
# Allow HTTP and HTTPS
ufw allow 80/tcp
ufw allow 443/tcp

# Block direct access to TuneBridge (port 10000 should not be exposed)
```

## Volumes and Data Persistence

### Named Volumes

The deployment uses named volumes for persistence:

| Volume | Purpose |
|--------|---------|
| `tunebridge-data` | Application data (databases, cache) |
| `caddy-data` | Caddy certificates and configuration |
| `caddy-config` | Caddy runtime configuration |
| `caddy-logs` | Caddy access logs |

### Backup

To backup application data:

```bash
# Create backup directory
mkdir -p backups

# Backup TuneBridge data
docker run --rm -v tunebridge-data:/data -v $(pwd)/backups:/backup alpine tar czf /backup/tunebridge-data-$(date +%Y%m%d).tar.gz /data

# Backup Caddy data (certificates)
docker run --rm -v caddy-data:/data -v $(pwd)/backups:/backup alpine tar czf /backup/caddy-data-$(date +%Y%m%d).tar.gz /data
```

### Restore

To restore from backup:

```bash
# Restore TuneBridge data
docker run --rm -v tunebridge-data:/data -v $(pwd)/backups:/backup alpine tar xzf /backup/tunebridge-data-YYYYMMDD.tar.gz -C /

# Restore Caddy data
docker run --rm -v caddy-data:/data -v $(pwd)/backups:/backup alpine tar xzf /backup/caddy-data-YYYYMMDD.tar.gz -C /
```

## Troubleshooting

### Container Won't Start

1. Check logs:
   ```bash
   docker-compose logs tunebridge
   ```

2. Verify secrets exist:
   ```bash
   ls -la secrets/
   ```

3. Verify environment variables:
   ```bash
   docker-compose config
   ```

### HTTPS Certificate Issues

1. Check Caddy logs:
   ```bash
   docker-compose logs tunebridge | grep caddy
   ```

2. Verify DNS points to your server:
   ```bash
   nslookup tunebridge.yourdomain.com
   ```

3. Ensure ports 80 and 443 are accessible:
   ```bash
   netstat -tlnp | grep -E ':(80|443)'
   ```

### Connection Refused

1. Verify TuneBridge is running:
   ```bash
   docker-compose ps
   ```

2. Check TuneBridge logs:
   ```bash
   docker-compose logs tunebridge | grep TuneBridge
   ```

3. Test direct access:
   ```bash
   curl http://localhost:10000/health
   ```

### Permission Errors

1. Verify volume ownership:
   ```bash
   docker-compose exec tunebridge ls -la /app/data
   ```

2. Reset volume permissions if needed:
   ```bash
   docker-compose exec --user root tunebridge chown -R $APP_UID:$APP_UID /app/data
   ```

## Performance Tuning

### Resource Limits

Adjust resource limits in `docker-compose.yml`:

```yaml
deploy:
  resources:
    limits:
      cpus: '2'        # Adjust based on load
      memory: 1G       # Adjust based on usage
    reservations:
      cpus: '0.5'
      memory: 256M
```

### Caddy Cache

Enable caching in Caddyfile for static assets:

```caddyfile
@static {
    path *.css *.js *.png *.jpg *.jpeg *.gif *.svg *.woff *.woff2
}
header @static Cache-Control "public, max-age=31536000"
```

## Monitoring

### Health Checks

Docker health checks are configured:

```bash
# Check health status
docker-compose ps

# View health check logs
docker inspect tunebridge | grep -A 10 Health
```

### Logs

Access logs are stored in the `caddy-logs` volume:

```bash
docker run --rm -v caddy-logs:/logs alpine tail -f /logs/access.log
```

## Updating

To update TuneBridge:

```bash
# Pull latest changes
git pull

# Rebuild and restart
docker-compose up -d --build

# Clean up old images
docker image prune -f
```

## Uninstalling

To completely remove TuneBridge:

```bash
# Stop and remove containers
docker-compose down

# Remove volumes (WARNING: This deletes all data)
docker-compose down -v

# Remove images
docker rmi tunebridge:latest
```

## Support

For issues and questions:
- GitHub Issues: https://github.com/tsmarvin/TuneBridge/issues
- Documentation: See README.md
