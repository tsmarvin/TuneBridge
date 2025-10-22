#!/bin/sh
set -eu

# ---- Configure defaults ----
NODE_NUMBER="${NODE_NUMBER:-0}"
ALLOWED_HOSTS="${ALLOWED_HOSTS:-*}"
DEFAULT_LOGLEVEL="${DEFAULT_LOGLEVEL:-Information}"
HOSTING_DEFAULT_LOGLEVEL="${HOSTING_DEFAULT_LOGLEVEL:-Information}"

# Optional music provider credentials
APPLE_TEAM_ID="${APPLE_TEAM_ID:-}"
APPLE_KEY_ID="${APPLE_KEY_ID:-}"
APPLE_KEY_PATH="${APPLE_KEY_PATH:-}"
SPOTIFY_CLIENT_ID="${SPOTIFY_CLIENT_ID:-}"
SPOTIFY_CLIENT_SECRET="${SPOTIFY_CLIENT_SECRET:-}"
TIDAL_CLIENT_ID="${TIDAL_CLIENT_ID:-}"
TIDAL_CLIENT_SECRET="${TIDAL_CLIENT_SECRET:-}"

# Optional Discord bot token
DISCORD_TOKEN="${DISCORD_TOKEN:-}"

# Optional Bluesky PDS configuration
BLUESKY_PDS_URL="${BLUESKY_PDS_URL:-https://bsky.social}"
BLUESKY_IDENTIFIER="${BLUESKY_IDENTIFIER:-}"
BLUESKY_PASSWORD="${BLUESKY_PASSWORD:-}"
CACHE_DAYS="${CACHE_DAYS:-7}"
CACHE_DB_PATH="${CACHE_DB_PATH:-medialinkscache.db}"

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

# 3) Launch the app
exec "/app/TuneBridge"
