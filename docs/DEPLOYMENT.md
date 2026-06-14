# Deployment Guide

This guide covers production deployment of BridgeBeats with Docker Compose.

> **Note**: For local development with the Aspire Dashboard, see the [Local Development Guide](LOCAL_DEVELOPMENT.md).

## Quick Start with Installation Script

The installation script is the supported deployment path. It downloads the configuration files, creates the directory layout with correct ownership, generates secrets, and registers the PDS maintenance cron job. You do not need to clone the repository.

### Linux / macOS

```bash
curl -sSL https://raw.githubusercontent.com/tsmarvin/BridgeBeats/develop/containers/install.sh | bash
```

### Windows (PowerShell)

```powershell
iwr -useb https://raw.githubusercontent.com/tsmarvin/BridgeBeats/develop/containers/install.ps1 | iex
```

### Installation Options

Customize the installation with parameters:

```bash
# Linux/macOS - specify branch and directory
curl -sSL https://raw.githubusercontent.com/tsmarvin/BridgeBeats/develop/containers/install.sh | bash -s -- --branch main --directory /opt/bridgebeats

# Windows - specify branch and directory
.\install.ps1 -Branch main -Directory C:\BridgeBeats
```

### What the Installation Script Does

1. **Validates dependencies** - Checks for Docker, Docker Compose v2+, curl/wget, and optionally openssl
2. **Downloads configuration files** - Fetches `docker-compose.yml`, `Caddyfile`, `.env.example`, and `truncate_seq.sh` from GitHub
3. **Sets up logs and data directories** - Creates `./logs/`, `./logs/caddy/`, and `./data/{app,dp-keys,redis,pds}/`; sets ownership for each directory to match the UID of the writing container (`1654` for the app, `root` for PDS/redis/caddy)
4. **Sets up secrets** - Creates the `secrets/` directory. Auto-generates `api_key_salt.txt`, `redis_password.txt`, `internal_service_key.txt`, and the `atproto_oauth_key.json` ES256 signing key; writes placeholders for the credentials you supply (Apple, Spotify, Tidal, Discord, ATProto, Cloudflare)
5. **Configures environment** - Creates `.env` from `.env.example` if not present
6. **Registers PDS sequencer trim** - Installs `sqlite3` on the host if missing and writes `/etc/cron.d/bridgebeats-pds-trim` (daily at 04:17, 14-day retention); see [PDS Sequencer Retention](#pds-sequencer-retention) below
7. **Handles updates** - On subsequent runs:
   - Backs up existing `docker-compose.yml` and `Caddyfile` with timestamps
   - Logs current container image SHAs to `upgrade.log` for rollback reference
   - Detects new environment variables in `.env.example` and displays them for manual addition
   - Prompts to pull latest container images

After running the script, edit your secrets and `.env` file, then start with `docker compose up -d`.

---

## Manual Setup with Docker Compose

The installation script handles secret generation, directory ownership, and the PDS cron job for you. Set up manually only if you need to. The compose stack mounts each secret as a file under `./secrets/` and persists data under `./data/`, so a working deployment needs all secret files present, the data directories owned by the correct UIDs, and the PDS environment variables set in `.env`.

```bash
git clone https://github.com/tsmarvin/BridgeBeats.git
cd BridgeBeats/containers
cp .env.example .env
# Edit .env: set DOMAIN, CADDY_ADMIN_EMAIL, the PDS_* values, and at least one music provider
```

Create the `secrets/` directory and populate every file the compose stack mounts (see the `volumes:` block in `docker-compose.yml`). The script's secret generation is the reference for what each file holds; replicating it by hand is error-prone, which is why the script is the supported path.

Once secrets, `.env`, and the `./data/` ownership are in place:

```bash
docker compose up -d
```

Visit `https://localhost` (or your configured domain) to reach BridgeBeats. The Aspire Dashboard is served on a dedicated subdomain: `https://dashboard.<your-domain>`.

For step-by-step instructions, see the [Quick Start Guide](QUICKSTART.md).

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

Sensitive values go in the `secrets/` directory (created by `install.sh`, or manually with `mkdir -p secrets && chmod 700 secrets`).

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

Set `DOMAIN` in `.env` (or `-e DOMAIN=...` for a raw `docker run`) to your public domain. The container entrypoint reads it into `BridgeBeats:Domain`, which governs auth-cookie scoping and OpenGraph card URL generation.

> **Migration note:** The `bridgebeats` service environment variable was renamed from `BASEURL` to `DOMAIN`. Deployments that set `BASEURL` must rename it to `DOMAIN`.

```bash
DOMAIN=bridgebeats.link
```

### Logging

Set log levels for production:

```bash
-e DEFAULT_LOGLEVEL=Warning
-e HOSTING_DEFAULT_LOGLEVEL=Warning
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

The Discord integration runs as a separate worker service (`BridgeBeats.Worker.Discord`). For large Discord deployments, run multiple instances with different node numbers:

```bash
# Instance 1 - shard 0
docker run -p 10001:10000 \
  -e BridgeBeats__DiscordToken=your_token \
  -e BridgeBeats__NodeNumber=0 \
  -e BridgeBeats__Domain=bridgebeats.link \
  tsmarvin/bridgebeats-discord:latest

# Instance 2 - shard 1
docker run -p 10002:10000 \
  -e BridgeBeats__DiscordToken=your_token \
  -e BridgeBeats__NodeNumber=1 \
  -e BridgeBeats__Domain=bridgebeats.link \
  tsmarvin/bridgebeats-discord:latest
```

**Important**: Each shard must have a unique `NODE_NUMBER`. Running multiple instances with the same node number will cause duplicate responses to the same messages.

The Discord worker calls the main BridgeBeats Web API for music lookups, so ensure `BridgeBeats__Domain` is set to the BridgeBeats host value (for example `bridgebeats.link`) for a running BridgeBeats Web instance.


## Security Considerations

1. **Use HTTPS** - Always deploy behind SSL/TLS
2. **Secure secrets** - Use secret management systems (not environment variables in production)
3. **Rate limiting** - Configure reverse proxy rate limiting as an additional layer
4. **API key rotation** - Regularly regenerate API keys
5. **Minimal permissions** - Run containers with minimal privileges

## Backup and Recovery

### Persistent Data

All persistent state lives under the host `./data/` bind-mount tree, alongside `./secrets/` and `.env`. Backup is a host-side file copy — no `docker cp` is needed.

Key paths:
- `./data/app/bridgebeats.db` — application SQLite database
- `./data/dp-keys/` — Data Protection key ring (losing these forces a session re-auth for all users)
- `./data/pds/` — PDS repo SQLite databases, DID/key material, and blob store
- `./data/redis/` — Redis AOF/RDB persistence

A concrete backup recipe (run from the deployment directory):

```bash
# For a consistent snapshot, bring the stack down first (avoids WAL-mode split copies)
docker compose down

# Archive everything needed to restore the deployment
tar -czf "../bridgebeats-backup-$(date +%Y%m%d-%H%M%S).tar.gz" \
    data secrets .env docker-compose.yml Caddyfile

# Bring the stack back up
docker compose up -d
```

For a hot copy without downtime, copy each SQLite database together with its `-wal` and `-shm` sidecar files (the databases run in WAL mode). A brief `docker compose stop` is still the safest path.

### Configuration

Keep a secure backup of:
- API credentials
- Private keys (.p8 files)
- Configuration files (`.env`, `docker-compose.yml`, `Caddyfile`)

### PDS Sequencer Retention

The PDS `sequencer.sqlite` holds the firehose event log in its `repo_seq` table. The stock Bluesky PDS never trims this table, so at bot-write volume it grows without bound — the BridgeBeats deployment reached ~14 GB before this was addressed.

`install.sh` registers a daily cron job at `/etc/cron.d/bridgebeats-pds-trim` that runs the vendored `truncate_seq.sh` (by [Bailey Townsend](https://tangled.org/strings/did:plc:rnpkyqnmsw4ipey6eotbdnnf/3milxyxx2hl22)) with a 14-day (336-hour) retention window. The script runs on the host using the host `sqlite3` binary, which `install.sh` installs if missing. Trim output appends to `./logs/pds-trim.log`.

The script runs `DELETE` only, never `VACUUM`. `DELETE` frees pages for SQLite to reuse but does not return space to the operating system, so the daily trim keeps `repo_seq` bounded and the file size plateaus near its 14-day steady state — it does not shrink an already-bloated file. `VACUUM` is not scheduled because it rewrites the whole database, needs roughly twice the file size in free disk, and holds an exclusive lock that stalls the PDS for its duration. Run `VACUUM` by hand if the file ever needs hard reclamation after a period of unmanaged growth.

> **Relay-cursor caveat:** Trimming `repo_seq` below a downstream consumer's last-seen cursor forces that consumer (a relay or AppView) to re-crawl or backfill rather than resume. With 14-day retention this matters only if a consumer is offline for more than 14 consecutive days.

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

### Using the Installation Script

The installation script handles updates gracefully:

```bash
# Navigate to your BridgeBeats installation directory
cd /path/to/bridgebeats

# Re-run the installation script
curl -sSL https://raw.githubusercontent.com/tsmarvin/BridgeBeats/develop/containers/install.sh | bash -s -- --directory .
```

The script will:
- Back up your existing `docker-compose.yml` and `Caddyfile` with timestamps
- Log current container image SHAs to `upgrade.log` before pulling new images
- Detect any new environment variables in `.env.example` and show you what to add
- Prompt whether to pull the latest container images
- Preserve all your existing secrets and `.env` configuration

### Manual Update

```bash
# Pull latest image from Docker Hub
docker pull tsmarvin/bridgebeats:latest

# Stop and remove old container
docker stop bridgebeats
docker rm bridgebeats

# Start new container
docker run -d --name bridgebeats ...
```

### Rollback

If you need to rollback after an update:

1. Check `upgrade.log` for the previous container image SHAs
2. Restore backed-up configuration files (for example, `docker-compose.yml.bak.20260204-143022`)
3. Pull the specific image version: `docker pull tsmarvin/bridgebeats:sha-<commit>`
