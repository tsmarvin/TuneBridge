# Caddy with Cloudflare DNS Plugin

This document explains the custom Caddy Docker image with Cloudflare DNS plugin integration used by BridgeBeats.

## Overview

BridgeBeats uses a custom Caddy image that includes the Cloudflare DNS plugin. This enables automated certificate management for wildcard domains using DNS-01 ACME challenges, which is required for the ATProto PDS (Personal Data Server) subdomain wildcards.

## Why Cloudflare DNS Plugin?

The ATProto PDS requires wildcard DNS support (`*.pds.bridgebeats.link`) to handle user-specific subdomains. Standard HTTP-01 ACME challenges cannot validate wildcard certificates, so we use DNS-01 challenges via the Cloudflare API.

### Features

- **Wildcard Certificate Support** - Automatically obtain and renew wildcard SSL/TLS certificates
- **DNS-01 Challenge** - Uses Cloudflare API for ACME DNS challenge validation
- **Automatic Renewal** - Certificates automatically renew before expiration
- **Multi-Architecture** - Built for both `linux/amd64` and `linux/arm64` platforms

## Docker Image

The custom Caddy image is published to Docker Hub:

```
<DOCKERHUB_USERNAME>/caddy-cloudflare:latest
```

### Image Tags

- `latest` - Built from the `main` branch (production)
- `develop` - Built from the `develop` branch (development)
- `pr-<number>` - Built from pull requests (testing)

## Configuration

### 1. Cloudflare API Token

Create a Cloudflare API token with the following permissions:

1. Go to [Cloudflare Dashboard](https://dash.cloudflare.com/profile/api-tokens)
2. Click "Create Token"
3. Use the "Edit zone DNS" template or create a custom token with:
   - **Permissions**: `Zone:DNS:Edit`
   - **Zone Resources**: Include your domain (e.g., `bridgebeats.link`)
4. Copy the generated token

### 2. Secret File Setup

Run the setup script to create the secrets directory and files:

```bash
./setup-secrets.sh
```

Then edit the Cloudflare API token file with your actual token:

```bash
nano secrets/cloudflare_api_token.txt
# Paste your Cloudflare API token and save
```

Ensure proper permissions:

```bash
chmod 600 secrets/cloudflare_api_token.txt
```

### 3. Docker Compose Configuration

The `docker-compose.yml` is already configured to mount the Cloudflare token as a Docker secret:

```yaml
caddy:
  image: ${DOCKERHUB_USERNAME}/caddy-cloudflare:latest
  volumes:
    # Secret mounts (read-only)
    - ./secrets/cloudflare_api_token.txt:/run/secrets/cloudflare_api_token:ro
  # ... other configuration
```

### 4. Caddyfile Configuration

The wildcard domain configuration in `Caddyfile` reads the token from the secret file:

```caddyfile
*.{$PDS_HOSTNAME}, {$PDS_HOSTNAME} {
    # Use Cloudflare DNS challenge for wildcard certificate
    tls {
        dns cloudflare {file /run/secrets/cloudflare_api_token}
    }
    
    reverse_proxy pds:3000
}
```

## Deployment

### Using Docker Compose

1. Set up secrets:
```bash
./setup-secrets.sh
nano secrets/cloudflare_api_token.txt  # Add your Cloudflare API token
```

2. Configure environment:
```bash
cp .env.example .env
nano .env  # Configure your domain and other settings
```

3. Start the services:
```bash
docker-compose up -d
```

4. Verify the certificate:
```bash
docker-compose logs caddy | grep -i certificate
```

### Manual Docker Build

To build the custom Caddy image locally:

```bash
docker build -f Dockerfile.caddy -t caddy-cloudflare:local .
```

## Automated Publishing

The custom Caddy image is automatically built and published via GitHub Actions when changes are pushed to:

- `main` branch → `latest` tag
- `develop` branch → `develop` tag
- Pull requests → `pr-<number>` tag

### Workflow File

`.github/workflows/publish-caddy-cloudflare.yml`

The workflow:
1. Builds the image for `linux/amd64` and `linux/arm64` platforms
2. Pushes per-architecture images to Docker Hub
3. Creates a multi-arch manifest
4. Requires `DOCKERHUB_USERNAME` and `DOCKERHUB_TOKEN` secrets

## Security Considerations

### API Token Scope

**Important**: Only grant the minimum required permissions to your Cloudflare API token:

- ✅ **Zone:DNS:Edit** - Required for DNS-01 challenges
- ❌ Avoid granting additional permissions (account-level access, zone settings, etc.)

### Token Storage

- Store the API token in the `secrets/` directory (`cloudflare_api_token.txt`)
- Never commit the token to version control (excluded via `.gitignore`)
- Use Docker Swarm secrets or Kubernetes secrets for production deployments
- Rotate tokens regularly

### Network Security

The Caddy container is exposed on public networks (ports 80/443) but internal services are isolated:

```yaml
networks:
  public:
    driver: bridge
  internal:
    driver: bridge
    internal: true  # No external access
```

## Troubleshooting

### Certificate Not Issued

**Issue**: Caddy fails to obtain wildcard certificate

**Solutions**:

1. Verify Cloudflare API token is mounted:
   ```bash
   docker-compose exec caddy cat /run/secrets/cloudflare_api_token
   ```

2. Check Caddy logs:
   ```bash
   docker-compose logs caddy
   ```

3. Verify DNS propagation:
   ```bash
   dig _acme-challenge.pds.bridgebeats.link TXT
   ```

4. Ensure API token has correct permissions in Cloudflare dashboard

### Rate Limiting

**Issue**: Let's Encrypt rate limit exceeded

**Solution**: Let's Encrypt has rate limits (50 certificates per domain per week). Use the staging environment for testing:

```caddyfile
{
    acme_ca https://acme-staging-v02.api.letsencrypt.org/directory
}
```

### Image Pull Failures

**Issue**: Cannot pull custom Caddy image

**Solutions**:

1. Verify Docker Hub username is correct in `.env`
2. Check the image exists on Docker Hub
3. Ensure you have internet access
4. Try pulling manually:
   ```bash
   docker pull ${DOCKERHUB_USERNAME}/caddy-cloudflare:latest
   ```

## References

- [Caddy Documentation](https://caddyserver.com/docs/)
- [Caddy DNS Providers](https://caddyserver.com/docs/modules/dns.providers.cloudflare)
- [Cloudflare API Tokens](https://developers.cloudflare.com/fundamentals/api/get-started/create-token/)
- [Let's Encrypt Rate Limits](https://letsencrypt.org/docs/rate-limits/)
- [ACME DNS-01 Challenge](https://letsencrypt.org/docs/challenge-types/#dns-01-challenge)

## Contributing

To modify the Caddy image:

1. Edit `Dockerfile.caddy`
2. Test locally: `docker build -f Dockerfile.caddy -t test .`
3. Open a pull request
4. The workflow will automatically build and push the image

## Version Information

- **Caddy Version**: 2.x (latest stable)
- **Cloudflare Plugin**: Latest (via xcaddy)
- **Base Image**: `caddy:2-builder`, `caddy:2`
