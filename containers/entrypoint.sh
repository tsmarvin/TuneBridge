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
BASEURL="${BASEURL:-"bridgebeats.link"}"
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
ATPROTO_USER_DID="${ATPROTO_USER_DID:-}"
# Try to read ATProto password from Docker secret
ATPROTO_PASSWORD="$(read_secret "atproto_password")"

CACHE_DAYS="${CACHE_DAYS:-7}"
LINK_CACHE_CONNECTION_STRING="${LINK_CACHE_CONNECTION_STRING:-Data Source=/app/data/bridgebeats.db}"
CARD_CACHE_EXPIRATION_HOURS="${CARD_CACHE_EXPIRATION_HOURS:-1}"
CARD_CACHE_CLEANUP_INTERVAL="${CARD_CACHE_CLEANUP_INTERVAL:-500}"

# Authentication and rate limiting configuration
IDENTITY_CONNECTION_STRING="${IDENTITY_CONNECTION_STRING:-Data Source=/app/data/bridgebeats.db}"
# Try to read API key salt from Docker secret
API_KEY_SALT="$(read_secret "api_key_salt")"
RATE_LIMIT_REQUESTS_PER_HOUR="${RATE_LIMIT_REQUESTS_PER_HOUR:-20}"

# Logging configuration
LOG_FILE_PATH="${LOG_FILE_PATH:-/app/data/logs/bridgebeats-.log}"

# Resilience configuration
RESILIENCE_MAX_RETRY_AFTER_SECONDS="${RESILIENCE_MAX_RETRY_AFTER_SECONDS:-120}"
RESILIENCE_MAX_RETRY_ATTEMPTS="${RESILIENCE_MAX_RETRY_ATTEMPTS:-5}"
RESILIENCE_TOTAL_TIMEOUT_MINUTES="${RESILIENCE_TOTAL_TIMEOUT_MINUTES:-10}"
RESILIENCE_ATTEMPT_TIMEOUT_SECONDS="${RESILIENCE_ATTEMPT_TIMEOUT_SECONDS:-10}"

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
  "BridgeBeats": {
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
    "ATProtoUserDID": "$ATPROTO_USER_DID",
    "ATProtoPassword": "$ATPROTO_PASSWORD",
    "CacheDays": $CACHE_DAYS,
    "LinkCacheConnectionString": "$(escape_bs "$LINK_CACHE_CONNECTION_STRING")",
    "BaseUrl": "$BASEURL",
    "LogFilePath": "$(escape_bs "$LOG_FILE_PATH")",
    "CardCacheExpirationHours": $CARD_CACHE_EXPIRATION_HOURS,
    "CardCacheCleanupInterval": $CARD_CACHE_CLEANUP_INTERVAL,
    "Resilience": {
      "MaxRetryAfterSeconds": $RESILIENCE_MAX_RETRY_AFTER_SECONDS,
      "MaxRetryAttempts": $RESILIENCE_MAX_RETRY_ATTEMPTS,
      "TotalTimeoutMinutes": $RESILIENCE_TOTAL_TIMEOUT_MINUTES,
      "AttemptTimeoutSeconds": $RESILIENCE_ATTEMPT_TIMEOUT_SECONDS
    },
    "Workers": {
      "UseWorkerServices": true,
      "SpotifyWorkerEnabled": $( [ -n "$SPOTIFY_CLIENT_ID" ] && [ -n "$SPOTIFY_CLIENT_SECRET" ] && echo "true" || echo "false" ),
      "AppleMusicWorkerEnabled": $( [ -n "$APPLE_TEAM_ID" ] && [ -n "$APPLE_KEY_ID" ] && [ -n "$APPLE_KEY_PATH" ] && echo "true" || echo "false" ),
      "TidalWorkerEnabled": $( [ -n "$TIDAL_CLIENT_ID" ] && [ -n "$TIDAL_CLIENT_SECRET" ] && echo "true" || echo "false" )
    }
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
  "AllowedHosts": "*"
}
EOF

# 3) Set up environment variables for Aspire AppHost parameters
# These are read by the AppHost to configure workers
export Parameters__SpotifyClientId="$SPOTIFY_CLIENT_ID"
export Parameters__SpotifyClientSecret="$SPOTIFY_CLIENT_SECRET"
export Parameters__AppleTeamId="$APPLE_TEAM_ID"
export Parameters__AppleKeyId="$APPLE_KEY_ID"
export Parameters__AppleKeyPath="$APPLE_KEY_PATH"
export Parameters__TidalClientId="$TIDAL_CLIENT_ID"
export Parameters__TidalClientSecret="$TIDAL_CLIENT_SECRET"
export Parameters__DiscordToken="$DISCORD_TOKEN"
export Parameters__ATProtoIdentifier="$ATPROTO_IDENTIFIER"
export Parameters__ATProtoPassword="$ATPROTO_PASSWORD"
export Parameters__ATProtoUserDID="$ATPROTO_USER_DID"
export Parameters__ApiKeySalt="$API_KEY_SALT"
export Parameters__NodeNumber="$NODE_NUMBER"
export Parameters__BaseUrl="$BASEURL"
export Parameters__RateLimitRequestsPerHour="$RATE_LIMIT_REQUESTS_PER_HOUR"
export Parameters__CacheDays="$CACHE_DAYS"
export Parameters__LinkCacheConnectionString="$LINK_CACHE_CONNECTION_STRING"
export Parameters__IdentityConnectionString="$IDENTITY_CONNECTION_STRING"
export Parameters__LogFilePath="$LOG_FILE_PATH"
export Parameters__CardCacheExpirationHours="$CARD_CACHE_EXPIRATION_HOURS"
export Parameters__CardCacheCleanupInterval="$CARD_CACHE_CLEANUP_INTERVAL"
export Parameters__ResilienceMaxRetryAfterSeconds="$RESILIENCE_MAX_RETRY_AFTER_SECONDS"
export Parameters__ResilienceMaxRetryAttempts="$RESILIENCE_MAX_RETRY_ATTEMPTS"
export Parameters__ResilienceTotalTimeoutMinutes="$RESILIENCE_TOTAL_TIMEOUT_MINUTES"
export Parameters__ResilienceAttemptTimeoutSeconds="$RESILIENCE_ATTEMPT_TIMEOUT_SECONDS"

# 4) Launch the BridgeBeats AppHost (orchestrator)
echo "Starting BridgeBeats AppHost..."
exec "/app/apphost/BridgeBeats.AppHost"
