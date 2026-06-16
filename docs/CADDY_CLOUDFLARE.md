# Caddy with Cloudflare DNS Plugin

This document explains the custom Caddy Docker image with Cloudflare DNS plugin integration used by BridgeBeats.

## Overview

BridgeBeats uses a custom Caddy image that includes the Cloudflare DNS plugin. This enables automated certificate management for wildcard domains using DNS-01 ACME challenges, which is required for the ATProto PDS (Personal Data Server) subdomain wildcards.

## Why Cloudflare DNS Plugin?

The ATProto PDS requires wildcard DNS support (`*.pds.bridgebeats.link`) to handle user-specific subdomains. Standard HTTP-01 ACME challenges cannot validate wildcard certificates, so BridgeBeats uses DNS-01 challenges via the Cloudflare API. The Cloudflare DNS challenge is also used for the `dashboard.<domain>` subdomain certificate.

### Features

- **Wildcard certificate support** - Obtain and renew wildcard SSL/TLS certificates for the PDS subdomains
- **DNS-01 challenge** - Uses the Cloudflare API for ACME DNS challenge validation
- **Automatic renewal** - Certificates renew before expiration
- **Multi-architecture** - Built for both `linux/amd64` and `linux/arm64` platforms

## Docker Image

The custom Caddy image is published to Docker Hub:

```
tsmarvin/caddy-cloudflare:latest
```

### Image Tags

- `latest` - Built from the `main` branch (production)
- `develop` - Built from the `develop` branch (development)
- `pr-<number>` - Built from pull requests (testing)

The shipped `containers/docker-compose.yml` references `tsmarvin/caddy-cloudflare:develop` for the `bridgebeats-caddy` service. Pin the tag you want for your environment.

## Configuration

### 1. Cloudflare API Token

Create a Cloudflare API token with the following permissions:

1. Go to [Cloudflare Dashboard](https://dash.cloudflare.com/profile/api-tokens)
2. Click "Create Token"
3. Use the "Edit zone DNS" template or create a custom token with:
   - **Permissions**: `Zone:DNS:Edit`
   - **Zone Resources**: Include your domain (e.g., `bridgebeats.link`)
4. Copy the generated token

### 2. Provide the token to Caddy

The token reaches Caddy as the `CLOUDFLARE_API_TOKEN` environment variable. The compose stack sources it from `.env` and passes it to the `bridgebeats-caddy` service; the Caddyfile then reads it with `{env.CLOUDFLARE_API_TOKEN}`.

Set the value in `.env`:

```bash
# .env
CLOUDFLARE_API_TOKEN=your_cloudflare_api_token
```

The installation script also creates a placeholder `secrets/cloudflare_api_token.txt`. The compose stack as shipped consumes the token from the `CLOUDFLARE_API_TOKEN` environment variable rather than mounting that file into the Caddy container, so the value you set in `.env` is what Caddy uses.

> **Verify:** if you change the deployment to mount `cloudflare_api_token.txt` as a Docker/Swarm secret instead of passing the env var, update the Caddyfile `tls` blocks to read `{file /run/secrets/cloudflare_api_token}` and add the corresponding mount to the `bridgebeats-caddy` service.

### 3. Docker Compose Configuration

The `bridgebeats-caddy` service passes the token via `environment:` (see `containers/docker-compose.yml`):

```yaml
bridgebeats-caddy:
  image: tsmarvin/caddy-cloudflare:develop
  container_name: bridgebeats-caddy
  ports:
    - "80:80"
    - "443:443"
  networks:
    - public
    - internal
  environment:
    - DOMAIN=${DOMAIN:-bridgebeats.link}
    - SHORT_DOMAIN=${SHORT_DOMAIN:-bbeats.link}
    - PDS_HOSTNAME=${PDS_HOSTNAME:-pds.bridgebeats.link}
    - CADDY_ADMIN_EMAIL=${CADDY_ADMIN_EMAIL:-admin@bridgebeats.link}
    - CLOUDFLARE_API_TOKEN=${CLOUDFLARE_API_TOKEN:-}
  volumes:
    - ./Caddyfile:/etc/caddy/Caddyfile
    - bridgebeats-caddy-data:/data/caddy
    - bridgebeats-caddy-config:/config/caddy
    - ./logs/caddy:/var/log/caddy
```

### 4. Caddyfile Configuration

The wildcard PDS site in `containers/Caddyfile` uses the Cloudflare DNS challenge, reading the token from the environment:

```caddyfile
*.{$PDS_HOSTNAME}, {$PDS_HOSTNAME} {
    # Use Cloudflare DNS challenge for wildcard certificate
    tls {
        dns cloudflare {env.CLOUDFLARE_API_TOKEN}
    }
    reverse_proxy bridgebeats-pds:3000
}
```

The `dashboard.{$DOMAIN}` site uses the same `dns cloudflare {env.CLOUDFLARE_API_TOKEN}` challenge. The primary site (`{$DOMAIN}`, `www`, and the short domain) does not need the DNS challenge and uses standard ACME issuance.

## Deployment

### Using Docker Compose

From the `containers/` directory:

1. Configure environment:
   ```bash
   cd containers
   cp .env.example .env
   nano .env  # Set DOMAIN, CADDY_ADMIN_EMAIL, PDS_HOSTNAME, and CLOUDFLARE_API_TOKEN
   ```

2. Start the services:
   ```bash
   docker compose up -d
   ```

3. Verify certificate issuance:
   ```bash
   docker compose logs bridgebeats-caddy | grep -i certificate
   ```

### Manual Docker Build

To build the custom Caddy image locally (from the repository root):

```bash
docker build -f containers/Dockerfile.caddy -t caddy-cloudflare:local .
```

## Automated Publishing

The custom Caddy image is built and published via GitHub Actions when changes to `containers/Dockerfile.caddy` or the workflow file are pushed to:

- `main` branch → `latest` tag
- `develop` branch → `develop` tag
- Pull requests → `pr-<number>` tag

### Workflow File

`.github/workflows/publish-caddy-cloudflare.yml`

The workflow:
1. Builds per-architecture images for `linux/amd64` and `linux/arm64`
2. Pushes each architecture by digest to Docker Hub
3. Creates a multi-arch manifest list
4. Emits SBOM and provenance attestations (`sbom: true`, `provenance: mode=max`)
5. Requires the `DOCKERHUB_USERNAME` and `DOCKERHUB_TOKEN` secrets

See the [SBOM and Provenance Guide](SBOM_AND_PROVENANCE.md) for how to inspect the Caddy image's attestations.

## Security Considerations

### API Token Scope

**Important**: Grant only the minimum required permissions to your Cloudflare API token:

- **Zone:DNS:Edit** - Required for DNS-01 challenges
- Avoid granting additional permissions (account-level access, zone settings, etc.)

### Token Storage

- Keep the token in `.env` (or in your orchestrator's secret store); never commit it to version control
- `.env` and `secrets/` are excluded via `.gitignore`
- For Docker Swarm or Kubernetes, supply the token through the platform's secret mechanism
- Rotate tokens regularly

### Network Security

The Caddy container is the only service published on the host (ports 80/443). It joins both the `public` and `internal` bridge networks; the application and Redis join only `internal`:

```yaml
networks:
  public:
    driver: bridge
  internal:
    driver: bridge
```

> **Note:** In the shipped `docker-compose.yml` the `internal` network is a standard bridge and is not declared with `internal: true`. Isolation comes from not publishing the app and Redis ports to the host, not from a Docker-level `internal` flag. If you require strict no-egress isolation for the internal network, add `internal: true` to its definition and verify the PDS and outbound provider calls still function.

## Troubleshooting

### Certificate Not Issued

**Issue**: Caddy fails to obtain the wildcard certificate

**Solutions**:

1. Confirm the token is present in the container environment:
   ```bash
   docker compose exec bridgebeats-caddy printenv CLOUDFLARE_API_TOKEN
   ```

2. Check Caddy logs:
   ```bash
   docker compose logs bridgebeats-caddy
   ```

3. Verify DNS propagation:
   ```bash
   dig _acme-challenge.pds.bridgebeats.link TXT
   ```

4. Ensure the API token has `Zone:DNS:Edit` for the correct zone in the Cloudflare dashboard

### Rate Limiting

**Issue**: Let's Encrypt rate limit exceeded

**Solution**: Let's Encrypt enforces per-domain certificate rate limits. Use the staging environment for testing:

```caddyfile
{
    acme_ca https://acme-staging-v02.api.letsencrypt.org/directory
}
```

### Image Pull Failures

**Issue**: Cannot pull the custom Caddy image

**Solutions**:

1. Verify the image tag in `docker-compose.yml` matches a published tag (`latest`, `develop`, or a PR tag)
2. Check that the image exists on Docker Hub
3. Ensure the host has internet access
4. Try pulling manually:
   ```bash
   docker pull tsmarvin/caddy-cloudflare:develop
   ```

## References

- [Caddy Documentation](https://caddyserver.com/docs/)
- [Caddy Cloudflare DNS Provider](https://caddyserver.com/docs/modules/dns.providers.cloudflare)
- [Cloudflare API Tokens](https://developers.cloudflare.com/fundamentals/api/get-started/create-token/)
- [Let's Encrypt Rate Limits](https://letsencrypt.org/docs/rate-limits/)
- [ACME DNS-01 Challenge](https://letsencrypt.org/docs/challenge-types/#dns-01-challenge)

## Contributing

To modify the Caddy image:

1. Edit `containers/Dockerfile.caddy`
2. Test locally: `docker build -f containers/Dockerfile.caddy -t test .`
3. Open a pull request
4. The workflow builds and pushes the image on merge to `develop` or `main`

## Version Information

- **Builder base image**: `caddy:2-builder` (digest-pinned in `Dockerfile.caddy`)
- **Runtime base image**: `caddy:2` (digest-pinned in `Dockerfile.caddy`)
- **Cloudflare plugin**: `github.com/caddy-dns/cloudflare`, built in via `xcaddy`
