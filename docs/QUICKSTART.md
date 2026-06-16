# Quick Start Guide - BridgeBeats with Docker Compose

This guide gets you up and running with BridgeBeats using Docker Compose in a few minutes.

## Prerequisites

- Docker Engine 20.10+ and Docker Compose 2.0+
- API credentials for at least one music provider (Apple Music, Spotify, or Tidal)

## One-Line Installation

The fastest way to get started is with the installation script. You do not need to clone the repository.

### Linux / macOS

```bash
curl -sSL https://raw.githubusercontent.com/tsmarvin/BridgeBeats/develop/containers/install.sh | bash
```

### Windows (PowerShell)

```powershell
iwr -useb https://raw.githubusercontent.com/tsmarvin/BridgeBeats/develop/containers/install.ps1 | iex
```

### Installation Options

You can customize the installation with parameters:

```bash
# Linux/macOS - specify branch and directory
curl -sSL https://raw.githubusercontent.com/tsmarvin/BridgeBeats/develop/containers/install.sh | bash -s -- --branch main --directory /opt/bridgebeats

# Windows - specify branch and directory
.\install.ps1 -Branch main -Directory C:\BridgeBeats
```

Both scripts default to the `develop` branch and an install directory of `./bridgebeats`.

The installation script will:

1. Validate required dependencies (Docker, Docker Compose, curl/wget)
2. Create the installation directory
3. Download `docker-compose.yml`, `Caddyfile`, `.env.example`, and `truncate_seq.sh`
4. Create `.env` from `.env.example` if one does not already exist
5. Set up the `secrets/` directory with placeholder files
6. Auto-generate random values for `api_key_salt.txt`, `redis_password.txt`, `internal_service_key.txt`, and an ES256 JWK for `atproto_oauth_key.json`
7. Report any secrets or environment variables that still need configuration
8. On upgrades: back up existing `docker-compose.yml`, `Caddyfile`, and the `secrets/` directory before making changes

After installation, follow the printed prompts to configure your credentials and start the application.

---

## Manual Installation

If you prefer to set things up manually, follow these steps.

### Step 1: Clone the Repository

```bash
git clone https://github.com/tsmarvin/BridgeBeats.git
cd BridgeBeats/containers
```

### Step 2: Set Up Secrets

Create the secrets directory and the secret files. BridgeBeats reads each value as a Docker secret mounted under `/run/secrets/`.

```bash
mkdir -p secrets
chmod 700 secrets

# Provider and integration secrets (fill these in with your own values)
touch secrets/apple_key.p8
touch secrets/spotify_client_secret.txt
touch secrets/tidal_client_secret.txt
touch secrets/discord_token.txt
touch secrets/atproto_password.txt
touch secrets/cloudflare_api_token.txt

# Auto-generated secrets
openssl rand -base64 32 > secrets/api_key_salt.txt
openssl rand -base64 32 > secrets/redis_password.txt
openssl rand -base64 32 > secrets/internal_service_key.txt

# Set permissions
chmod 600 secrets/*
```

`internal_service_key.txt` is required: it is the shared key worker services present to authenticate to the web API, and the container fails to start without it. The installation script generates this value automatically; when setting up by hand, generate it yourself as shown above.

The ATProto OAuth signing key (`secrets/atproto_oauth_key.json`) is an ES256 JWK. The installation script generates it for you. To create one by hand, see the [ATProto Lexicon Setup Guide](ATPROTO_LEXICON.md).

### Step 3: Add Your Credentials

Edit the secret files with your actual credentials. At least one provider is required.

**Apple Music**

```bash
# Replace with your actual .p8 private key file
cp /path/to/your/AuthKey_XXXXX.p8 secrets/apple_key.p8
```

**Spotify**

```bash
echo "your_spotify_client_secret_here" > secrets/spotify_client_secret.txt
```

**Tidal**

```bash
echo "your_tidal_client_secret_here" > secrets/tidal_client_secret.txt
```

### Step 4: Configure Environment

Copy the example environment file and edit it:

```bash
cp .env.example .env
nano .env  # or use your preferred editor
```

The non-secret half of each provider's credentials (the public client IDs and Apple's Team/Key identifiers) lives in `.env`; the matching secrets live in the `secrets/` directory from Step 2.

**Minimum required configuration:**

```bash
# At least one set of music provider credentials is required.

# Apple Music
APPLE_TEAM_ID=YOUR_TEAM_ID
APPLE_KEY_ID=YOUR_KEY_ID

# Spotify
SPOTIFY_CLIENT_ID=YOUR_CLIENT_ID

# Tidal
TIDAL_CLIENT_ID=YOUR_CLIENT_ID

# Domain (Caddy uses this for HTTPS certificates)
DOMAIN=localhost
```

See the [Configuration Guide](CONFIGURATION.md) for the full environment-variable reference.

### Step 5: Start BridgeBeats

```bash
docker compose up -d
```

This starts four services:

- `bridgebeats` - the application and Aspire orchestration host (internal network only)
- `redis` - cache, request queue, and rate-limit tracker
- `bridgebeats-pds` - a Bluesky Personal Data Server for ATProto caching
- `bridgebeats-caddy` - the reverse proxy that terminates HTTPS and fronts the application

Only Caddy publishes ports to the host (80 and 443). The application itself listens on port 10000 inside the internal Docker network and is reached through Caddy.

### Step 6: Access BridgeBeats

- **HTTPS (recommended)**: https://localhost
  - Accept the self-signed certificate warning in your browser for `localhost`.
- **HTTP**: http://localhost (redirects to HTTPS)

## Verify It's Working

```bash
# Check that services are running
docker compose ps

# View logs
docker compose logs -f

# Test the health endpoint through Caddy
curl -k https://localhost/health
```

Expected response:

```json
{"status":"healthy","timestamp":"2026-..."}
```

You can also check the application's health directly inside the container:

```bash
docker compose exec bridgebeats curl --fail --silent http://localhost:10000/health
```

## Next Steps

### Create a User Account

1. Navigate to https://localhost in your browser
2. Register a new account
3. Save the API key provided (it is shown only once)

### Test Music Lookup

Use the web interface, or call the public URL-lookup endpoint:

```bash
# Look up a song by its provider URL (public, no API key required)
curl -k -X POST https://localhost/music/lookup/url \
  -H "Content-Type: application/json" \
  -d '{"uri": "https://music.apple.com/us/album/..."}'
```

### Discord Bot (Optional)

If you want the Discord bot:

1. Add your bot token to `secrets/discord_token.txt`
2. Restart: `docker compose restart`

See the [Discord Bot Guide](DISCORD_BOT.md) for setup details.

## Common Issues

### "No music provider credentials configured"

**Solution**: Set credentials for at least one provider in `.env` and the matching secret file, then restart.

### Container exits immediately with an `INTERNAL_SERVICE_KEY` error

**Solution**: `secrets/internal_service_key.txt` is empty. Generate a value (`openssl rand -base64 32 > secrets/internal_service_key.txt`) and restart. The installation script does this for you.

### Port Already in Use

**Solution**: Stop other services using ports 80 or 443, or change the published ports for the `bridgebeats-caddy` service in `docker-compose.yml`:

```yaml
ports:
  - "8080:80"   # HTTP
  - "8443:443"  # HTTPS
```

### Certificate Warnings

**Normal for localhost**: Caddy issues a self-signed certificate for `localhost`. In production with a real domain, Caddy obtains Let's Encrypt certificates automatically.

## Stopping and Cleaning Up

```bash
# Stop services
docker compose down

# Stop and remove data volumes (WARNING: deletes all data)
docker compose down -v
```

## Production Deployment

For production:

1. Set `DOMAIN` in `.env` to your actual domain name
2. Point DNS at your server
3. Run the same steps above

Caddy obtains Let's Encrypt certificates automatically. See the [Deployment Guide](DEPLOYMENT.md) for full production instructions and the [Caddy + Cloudflare Guide](CADDY_CLOUDFLARE.md) for DNS and wildcard-certificate setup.

## Getting Help

- Documentation: [README.md](https://github.com/tsmarvin/BridgeBeats#readme)
- Deployment Guide: [DEPLOYMENT.md](DEPLOYMENT.md)
- Configuration Guide: [CONFIGURATION.md](CONFIGURATION.md)
- Issues: https://github.com/tsmarvin/BridgeBeats/issues
