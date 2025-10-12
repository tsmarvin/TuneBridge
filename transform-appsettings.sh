#!/bin/sh
set -eu

# Script to transform appsettings.json template with environment variables
# Similar to Docker entrypoint.sh but for test environments

# ---- Configure defaults ----
NODE_NUMBER="${NODENUMBER:-0}"
APPLE_TEAM_ID="${APPLETEAMID:-}"
APPLE_KEY_ID="${APPLEKEYID:-}"
APPLE_KEY_PATH="${APPLEKEYPATH:-}"
SPOTIFY_CLIENT_ID="${SPOTIFYCLIENTID:-}"
SPOTIFY_CLIENT_SECRET="${SPOTIFYCLIENTSECRET:-}"
DISCORD_TOKEN="${DISCORDTOKEN:-}"
ALLOWED_HOSTS="${ALLOWED_HOSTS:-*}"
DEFAULT_LOGLEVEL="${DEFAULT_LOGLEVEL:-Warning}"
HOSTING_DEFAULT_LOGLEVEL="${HOSTING_DEFAULT_LOGLEVEL:-Warning}"

# Get the directory where the script is located
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"

# Determine output path
OUTPUT_PATH="${1:-$SCRIPT_DIR/TuneBridge.Tests/bin/Debug/net9.0/appsettings.json}"

# Create output directory if it doesn't exist
mkdir -p "$(dirname "$OUTPUT_PATH")"

# Escape backslashes (for path safety)
escape_bs() { printf '%s' "$1" | sed 's/\\/\\\\/g'; }

# Transform the template appsettings.json
cat > "$OUTPUT_PATH" <<EOF
{
  "TuneBridge": {
    "NodeNumber": $NODE_NUMBER,
    "AppleTeamId": "$APPLE_TEAM_ID",
    "AppleKeyId": "$APPLE_KEY_ID",
    "AppleKeyPath": "$(escape_bs "$APPLE_KEY_PATH")",
    "SpotifyClientId": "$SPOTIFY_CLIENT_ID",
    "SpotifyClientSecret": "$SPOTIFY_CLIENT_SECRET",
    "DiscordToken": "$DISCORD_TOKEN"
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

echo "Created appsettings.json at: $OUTPUT_PATH"
