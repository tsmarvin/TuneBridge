# Quick Start Guide - BridgeBeats with Docker Compose

This guide will get you up and running with BridgeBeats using Docker Compose in under 5 minutes.

## Prerequisites

- Docker Engine 20.10+ and Docker Compose 2.0+
- API credentials for at least one music provider (Apple Music, Spotify, or Tidal)

## One-Line Installation

The fastest way to get started is with the installation script. No need to clone the repository.

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

The installation script will:
1. Validate all required dependencies (Docker, Docker Compose, etc.)
2. Create the installation directory with all necessary files
3. Download `docker-compose.yml`, `Caddyfile`, and `.env.example`
4. Set up the `secrets/` directory with placeholder files
5. Auto-generate random secrets for `api_key_salt.txt` and `redis_password.txt`
6. Alert you to any secrets or environment variables that need configuration
7. On updates: back up existing files, log container image SHAs, and detect new environment variables

After installation, follow the prompts to configure your credentials and start the application.

---

## Manual Installation

If you prefer to set things up manually, follow these steps.

### Step 1: Clone the Repository

```bash
git clone https://github.com/tsmarvin/BridgeBeats.git
cd BridgeBeats/containers
```

### Step 2: Set Up Secrets

Create the secrets directory and placeholder files:

```bash
mkdir -p secrets
chmod 700 secrets

# Create placeholder secret files
touch secrets/apple_key.p8
touch secrets/spotify_client_secret.txt
touch secrets/tidal_client_secret.txt
touch secrets/discord_token.txt
touch secrets/atproto_password.txt
touch secrets/cloudflare_api_token.txt

# Generate random secrets
openssl rand -base64 32 > secrets/api_key_salt.txt
openssl rand -base64 32 > secrets/redis_password.txt

# Set permissions
chmod 600 secrets/*
```

#### Step 3: Add Your Credentials

Edit the secret files with your actual credentials:

#### Apple Music (Option 1)
```bash
# Replace with your actual .p8 private key file
cp /path/to/your/AuthKey_XXXXX.p8 secrets/apple_key.p8
```

#### Spotify (Option 2)
```bash
# Add your Spotify client secret
echo "your_spotify_client_secret_here" > secrets/spotify_client_secret.txt
```

#### Tidal (Option 3)
```bash
# Add your Tidal client secret
echo "your_tidal_client_secret_here" > secrets/tidal_client_secret.txt
```

#### Step 4: Configure Environment

Copy the example environment file and edit it:

```bash
cp .env.example .env
nano .env  # or use your preferred editor
```

**Minimum required configuration:**

```bash
# At least one of these sets of credentials is required:

# Apple Music
APPLE_TEAM_ID=YOUR_TEAM_ID
APPLE_KEY_ID=YOUR_KEY_ID

# Spotify
SPOTIFY_CLIENT_ID=YOUR_CLIENT_ID

# Tidal
TIDAL_CLIENT_ID=YOUR_CLIENT_ID

# Domain (for local development, keep as localhost)
DOMAIN=localhost
```

#### Step 5: Start BridgeBeats

```bash
docker compose up -d
```

This will:
1. Pull the Docker images
2. Start BridgeBeats with Caddy reverse proxy
3. Set up automatic HTTPS (with self-signed cert for localhost)

### Step 6: Access BridgeBeats

- **HTTPS (recommended)**: https://localhost
  - Accept the self-signed certificate warning in your browser
- **HTTP**: http://localhost (redirects to HTTPS)
- **Direct access**: http://localhost:10000

## Verify It's Working

```bash
# Check if services are running
docker compose ps

# View logs
docker compose logs -f

# Test the health endpoint
curl http://localhost:10000/health
```

Expected response:
```json
{"status":"healthy","timestamp":"2024-..."}
```

## Next Steps

### Create a User Account

1. Navigate to https://localhost in your browser
2. Register a new account
3. Save the API key provided (it will only be shown once)

### Test Music Lookup

Use the web interface or API:

```bash
# Example: Look up a song by URL
curl -X POST http://localhost:10000/music/lookup/url \
  -H "Content-Type: application/json" \
  -d '{"uri": "https://music.apple.com/us/album/..."}'
```

### Discord Bot (Optional)

If you configured Discord:

1. Add `DISCORD_TOKEN` to your `.env` file
2. Add the token to `secrets/discord_token.txt`
3. Restart: `docker compose restart`

## Common Issues

### "No music provider credentials configured"

**Solution**: Ensure you've set credentials for at least one provider in your `.env` file and the corresponding secret files.

### Port Already in Use

**Solution**: Stop other services using ports 80, 443, or 10000, or change the ports in `docker-compose.yml`:

```yaml
ports:
  - "8080:80"   # HTTP
  - "8443:443"  # HTTPS
```

### Certificate Warnings

**Normal for localhost**: Self-signed certificates are used for local development. In production with a real domain, Let's Encrypt certificates are obtained automatically.

## Stopping and Cleaning Up

```bash
# Stop services
docker compose down

# Stop and remove volumes (WARNING: deletes all data)
docker compose down -v
```

## Production Deployment

For production deployment:

1. Update `DOMAIN` in `.env` to your actual domain name
2. Ensure DNS points to your server
3. Use the same steps above
4. Caddy will automatically obtain Let's Encrypt certificates

See the [Deployment Guide](docs/DEPLOYMENT.md) for detailed production deployment instructions.

## Getting Help

- Documentation: [README.md](../README.md)
- Deployment Guide: [docs/DEPLOYMENT.md](DEPLOYMENT.md)
- Issues: https://github.com/tsmarvin/BridgeBeats/issues
