#!/bin/sh
set -eu

# ---- Helper function to read Docker secrets ----
read_secret() {
    secret_path="/run/secrets/$1"
    if [ -f "$secret_path" ]; then
        cat "$secret_path"
    else
        echo ""
    fi
}

# ---- Configure defaults ----
NODE_NUMBER="${NODE_NUMBER:-0}"
ALLOWED_HOSTS="${ALLOWED_HOSTS:-*}"
DEFAULT_LOGLEVEL="${DEFAULT_LOGLEVEL:-Information}"
HOSTING_DEFAULT_LOGLEVEL="${HOSTING_DEFAULT_LOGLEVEL:-Information}"

# Optional music provider credentials
# Read from Docker secrets if available, otherwise use environment variables
APPLE_TEAM_ID="${APPLE_TEAM_ID:-}"
APPLE_KEY_ID="${APPLE_KEY_ID:-}"
APPLE_KEY_PATH="${APPLE_KEY_PATH:-}"

# Try to read Spotify client secret from Docker secret
SPOTIFY_CLIENT_SECRET_FROM_SECRET=$(read_secret "spotify_client_secret")
SPOTIFY_CLIENT_ID="${SPOTIFY_CLIENT_ID:-}"
SPOTIFY_CLIENT_SECRET="${SPOTIFY_CLIENT_SECRET:-$SPOTIFY_CLIENT_SECRET_FROM_SECRET}"

# Try to read Tidal client secret from Docker secret
TIDAL_CLIENT_SECRET_FROM_SECRET=$(read_secret "tidal_client_secret")
TIDAL_CLIENT_ID="${TIDAL_CLIENT_ID:-}"
TIDAL_CLIENT_SECRET="${TIDAL_CLIENT_SECRET:-$TIDAL_CLIENT_SECRET_FROM_SECRET}"

# Try to read Discord token from Docker secret
DISCORD_TOKEN_FROM_SECRET=$(read_secret "discord_token")
DISCORD_TOKEN="${DISCORD_TOKEN:-$DISCORD_TOKEN_FROM_SECRET}"

# Optional Bluesky PDS configuration
BLUESKY_PDS_URL="${BLUESKY_PDS_URL:-https://bsky.social}"
BLUESKY_IDENTIFIER="${BLUESKY_IDENTIFIER:-}"
# Try to read Bluesky password from Docker secret
BLUESKY_PASSWORD_FROM_SECRET=$(read_secret "bluesky_password")
BLUESKY_PASSWORD="${BLUESKY_PASSWORD:-$BLUESKY_PASSWORD_FROM_SECRET}"

CACHE_DAYS="${CACHE_DAYS:-7}"
CACHE_DB_PATH="${CACHE_DB_PATH:-medialinkscache.db}"

# Authentication and rate limiting configuration
CONNECTION_STRING="${CONNECTION_STRING:-Data Source=/app/data/tunebridge.db}"
# Try to read API key salt from Docker secret
API_KEY_SALT_FROM_SECRET=$(read_secret "api_key_salt")
API_KEY_SALT="${API_KEY_SALT:-$API_KEY_SALT_FROM_SECRET}"
RATE_LIMIT_REQUESTS_PER_HOUR="${RATE_LIMIT_REQUESTS_PER_HOUR:-20}"

# escape backslashes (for path safety) ----
escape_bs() { printf '%s' "$1" | sed 's/\\/\\\\/g'; }

# 1) Remove existing appsettings.json if present
[ -f /app/appsettings.json ] && rm -f /app/appsettings.json

# 2) Create new appsettings.json
cat > /app/appsettings.json <<EOF
{
  "Kestrel": {
    "Endpoints": {
      "Http": {
        "Url": "http://0.0.0.0:10000"
      }
    }
  },
  "TuneBridge": {
    "NodeNumber": $NODE_NUMBER,
    "AppleTeamId": "$APPLE_TEAM_ID",
    "AppleKeyId": "$APPLE_KEY_ID",
    "AppleKeyPath": "$(escape_bs "$APPLE_KEY_PATH")",
    "SpotifyClientId": "$SPOTIFY_CLIENT_ID",
    "SpotifyClientSecret": "$SPOTIFY_CLIENT_SECRET",
    "TidalClientId": "$TIDAL_CLIENT_ID",
    "TidalClientSecret": "$TIDAL_CLIENT_SECRET",
    "DiscordToken": "$DISCORD_TOKEN",
    "ConnectionString": "$(escape_bs "$CONNECTION_STRING")",
    "ApiKeySalt": "$API_KEY_SALT",
    "RateLimitRequestsPerHour": $RATE_LIMIT_REQUESTS_PER_HOUR,
    "BlueskyPdsUrl": "$BLUESKY_PDS_URL",
    "BlueskyIdentifier": "$BLUESKY_IDENTIFIER",
    "BlueskyPassword": "$BLUESKY_PASSWORD",
    "CacheDays": $CACHE_DAYS,
    "CacheDbPath": "$CACHE_DB_PATH"
  },
  "Logging": {
    "LogLevel": {
      "Default": "$DEFAULT_LOGLEVEL",
      "Microsoft.Hosting.Lifetime": "$HOSTING_DEFAULT_LOGLEVEL"
    }
  },
  "AllowedHosts": "$ALLOWED_HOSTS"
}
EOF

# 3) Start Caddy in the background
echo "Starting Caddy reverse proxy..."
caddy run --config /etc/caddy/Caddyfile --adapter caddyfile &
CADDY_PID=$!

# Give Caddy a moment to start
sleep 2

# 4) Launch the TuneBridge application
echo "Starting TuneBridge application..."
exec "/app/TuneBridge"
