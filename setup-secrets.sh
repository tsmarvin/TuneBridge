#!/bin/bash
# Setup script for TuneBridge Docker deployment with secrets
# This script helps create the necessary directory structure and secret files

set -e

echo "=========================================="
echo "TuneBridge Docker Setup Script"
echo "=========================================="
echo ""

# Create secrets directory
SECRETS_DIR="./secrets"
if [ ! -d "$SECRETS_DIR" ]; then
    mkdir -p "$SECRETS_DIR"
    echo "✓ Created secrets directory: $SECRETS_DIR"
else
    echo "✓ Secrets directory already exists: $SECRETS_DIR"
fi

echo ""
echo "Setting up secret files..."
echo "Note: Edit these files with your actual credentials after creation"
echo ""

# Function to create a secret file if it doesn't exist
create_secret_file() {
    local file_path="$1"
    local description="$2"
    local example_content="$3"
    
    if [ ! -f "$file_path" ]; then
        echo "$example_content" > "$file_path"
        chmod 600 "$file_path"  # Restrict permissions
        echo "✓ Created: $file_path"
        echo "  Description: $description"
    else
        echo "⊘ Already exists: $file_path"
    fi
}

# Create secret files with placeholder content
create_secret_file "$SECRETS_DIR/apple_key.p8" \
    "Apple Music private key (.p8 file)" \
    "-----BEGIN PRIVATE KEY-----
REPLACE_WITH_YOUR_APPLE_MUSIC_PRIVATE_KEY
-----END PRIVATE KEY-----"

create_secret_file "$SECRETS_DIR/spotify_client_secret.txt" \
    "Spotify API client secret" \
    "your_spotify_client_secret_here"

create_secret_file "$SECRETS_DIR/tidal_client_secret.txt" \
    "Tidal API client secret" \
    "your_tidal_client_secret_here"

create_secret_file "$SECRETS_DIR/discord_token.txt" \
    "Discord bot token" \
    "your_discord_bot_token_here"

create_secret_file "$SECRETS_DIR/bluesky_password.txt" \
    "Bluesky app password" \
    "your_bluesky_app_password_here"

create_secret_file "$SECRETS_DIR/api_key_salt.txt" \
    "API key salt (random string for security)" \
    "$(openssl rand -base64 32 2>/dev/null || echo 'your_random_salt_here')"

echo ""
echo "=========================================="
echo "Setup Complete!"
echo "=========================================="
echo ""
echo "Next steps:"
echo "1. Edit the secret files in $SECRETS_DIR/ with your actual credentials"
echo "2. Copy .env.example to .env and configure your environment variables"
echo "3. Run 'docker-compose up -d' to start TuneBridge"
echo ""
echo "Important security notes:"
echo "• The secrets directory is excluded from git via .gitignore"
echo "• Secret files have restricted permissions (600)"
echo "• Never commit secret files to version control"
echo "• For production, use Docker Swarm secrets or Kubernetes secrets"
echo ""
echo "For more information, see README.md"
echo ""
