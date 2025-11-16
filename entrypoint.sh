#!/bin/bash
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
BASEURL="${BASEURL:-"dev.tunebridge.media"}"
NODE_NUMBER="${NODE_NUMBER:-0}"
DEFAULT_LOGLEVEL="${DEFAULT_LOGLEVEL:-Information}"
HOSTING_DEFAULT_LOGLEVEL="${HOSTING_DEFAULT_LOGLEVEL:-Information}"

# Optional music provider credentials
# Read from Docker secrets if available, otherwise use environment variables
APPLE_TEAM_ID="${APPLE_TEAM_ID:-}"
APPLE_KEY_ID="${APPLE_KEY_ID:-}"
APPLE_KEY_PATH="${APPLE_KEY_PATH:-}"

# Try to read Spotify client secret from Docker secret
SPOTIFY_CLIENT_ID="${SPOTIFY_CLIENT_ID:-}"
SPOTIFY_CLIENT_SECRET="$(read_secret "spotify_client_secret")"

# Try to read Tidal client secret from Docker secret
TIDAL_CLIENT_ID="${TIDAL_CLIENT_ID:-}"
TIDAL_CLIENT_SECRET="$(read_secret "tidal_client_secret")"

# Try to read Discord token from Docker secret
DISCORD_TOKEN="$(read_secret "discord_token")"

# Optional ATProto PDS configuration
ATPROTO_IDENTIFIER="${ATPROTO_IDENTIFIER:-}"
# Try to read ATProto password from Docker secret
ATPROTO_PASSWORD="$(read_secret "atproto_password")"

CACHE_DAYS="${CACHE_DAYS:-7}"
LINK_CACHE_CONNECTION_STRING="${LINK_CACHE_CONNECTION_STRING:-Data Source=/app/data/tunebridge.db}"

# Authentication and rate limiting configuration
IDENTITY_CONNECTION_STRING="${IDENTITY_CONNECTION_STRING:-Data Source=/app/data/tunebridge.db}"
# Try to read API key salt from Docker secret
API_KEY_SALT="$(read_secret "api_key_salt")"
RATE_LIMIT_REQUESTS_PER_HOUR="${RATE_LIMIT_REQUESTS_PER_HOUR:-20}"

# Logging configuration
LOG_FILE_PATH="${LOG_FILE_PATH:-/app/data/logs/tunebridge-.log}"
OTLP_ENDPOINT="${OTLP_ENDPOINT:-http://aspire-dashboard:4317}"

# escape backslashes (for path safety) ----
escape_bs() { printf '%s' "$1" | sed 's/\\/\\\\/g'; }

# 0) Create logs directory if it doesn't exist
mkdir -p /app/data/logs

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
    "IdentityConnectionString": "$(escape_bs "$IDENTITY_CONNECTION_STRING")",
    "ApiKeySalt": "$API_KEY_SALT",
    "RateLimitRequestsPerHour": $RATE_LIMIT_REQUESTS_PER_HOUR,
    "ATProtoIdentifier": "$ATPROTO_IDENTIFIER",
    "ATProtoPassword": "$ATPROTO_PASSWORD",
    "CacheDays": $CACHE_DAYS,
    "LinkCacheConnectionString": "$(escape_bs "$LINK_CACHE_CONNECTION_STRING")",
    "BaseUrl": "$BASEURL",
    "LogFilePath": "$(escape_bs "$LOG_FILE_PATH")"
  },
  "Logging": {
    "LogLevel": {
      "Default": "$DEFAULT_LOGLEVEL",
      "Microsoft.Hosting.Lifetime": "$HOSTING_DEFAULT_LOGLEVEL",
      "Microsoft.AspNetCore.Hosting.Diagnostics": "Warning",
      "Microsoft.AspNetCore.Routing.EndpointMiddleware": "Warning"
    }
  },
  "Serilog": {
    "MinimumLevel": {
      "Default": "$DEFAULT_LOGLEVEL",
      "Override": {
        "Microsoft.AspNetCore.Hosting.Diagnostics": "Warning",
        "Microsoft.AspNetCore.Routing.EndpointMiddleware": "Warning"
      }
    }
  },
  "OpenTelemetry": {
    "OtlpEndpoint": "$OTLP_ENDPOINT"
  },
  "AllowedHosts": "*"
}
EOF

# 3) Launch the Aspire AppHost (which will start the TuneBridge application)
echo "Starting TuneBridge via Aspire AppHost..."
exec "/app/TuneBridge.AppHost"
