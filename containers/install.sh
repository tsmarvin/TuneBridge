#!/bin/bash
# BridgeBeats Installation Script
# This script downloads and configures BridgeBeats for Docker deployment
#
# Usage:
#   curl -sSL https://raw.githubusercontent.com/tsmarvin/BridgeBeats/develop/containers/install.sh | bash
#   curl -sSL https://raw.githubusercontent.com/tsmarvin/BridgeBeats/develop/containers/install.sh | bash -s -- --branch main
#   curl -sSL https://raw.githubusercontent.com/tsmarvin/BridgeBeats/develop/containers/install.sh | bash -s -- --directory /opt/bridgebeats

set -e

# =============================================================================
# Configuration
# =============================================================================
BRANCH="develop"
INSTALL_DIR="./bridgebeats"
REPO_OWNER="tsmarvin"
REPO_NAME="BridgeBeats"
TIMESTAMP=$(date +%Y%m%d-%H%M%S)
UPGRADE_LOG="upgrade.log"

# Files to download from the repository
DOWNLOAD_FILES=("docker-compose.yml" "Caddyfile" ".env.example")

# Secret files configuration: name|description|auto_generate
SECRET_FILES=(
    "apple_key.p8|Apple Music private key (.p8 file)|false"
    "spotify_client_secret.txt|Spotify API client secret|false"
    "tidal_client_secret.txt|Tidal API client secret|false"
    "discord_token.txt|Discord bot token|false"
    "atproto_password.txt|ATProto app password|false"
    "api_key_salt.txt|API key salt (random string for security)|true"
    "redis_password.txt|Redis password (random string for authentication)|true"
    "cloudflare_api_token.txt|Cloudflare API token for DNS challenges|false"
)

# =============================================================================
# Argument Parsing
# =============================================================================
while [[ $# -gt 0 ]]; do
    case $1 in
        --branch|-b)
            BRANCH="$2"
            shift 2
            ;;
        --directory|-d)
            INSTALL_DIR="$2"
            shift 2
            ;;
        --help|-h)
            echo "BridgeBeats Installation Script"
            echo ""
            echo "Usage: install.sh [OPTIONS]"
            echo ""
            echo "Options:"
            echo "  --branch, -b      Git branch to download from (default: develop)"
            echo "  --directory, -d   Installation directory (default: ./bridgebeats)"
            echo "  --help, -h        Show this help message"
            exit 0
            ;;
        *)
            echo "[ERROR] Unknown option: $1"
            echo "Use --help for usage information"
            exit 1
            ;;
    esac
done

# Construct base URL for raw file downloads
BASE_URL="https://raw.githubusercontent.com/${REPO_OWNER}/${REPO_NAME}/${BRANCH}/containers"

# =============================================================================
# Helper Functions
# =============================================================================
print_header() {
    echo ""
    echo "=========================================="
    echo "$1"
    echo "=========================================="
    echo ""
}

print_section() {
    echo ""
    echo "--- $1 ---"
    echo ""
}

log_upgrade() {
    local message="$1"
    echo "[${TIMESTAMP}] ${message}" >> "${UPGRADE_LOG}"
}

# Check if a secret file has content (without reading the actual value)
secret_has_content() {
    local file="$1"
    if [[ -f "$file" ]]; then
        # Use grep to check if file has non-whitespace content
        if grep -qE '\S' "$file" 2>/dev/null; then
            # Check it's a placeholder
            if ! grep -qE '^(your_|REPLACE_|-----BEGIN)' "$file" 2>/dev/null; then
                return 0
            fi
        fi
    fi
    return 1
}

# Generate a random string using available methods
generate_random_string() {
    if command -v openssl &> /dev/null; then
        openssl rand -base64 32
    elif [[ -f /dev/urandom ]]; then
        head -c 32 /dev/urandom | base64
    else
        # Fallback - not cryptographically secure but better than nothing
        echo "REPLACE_WITH_RANDOM_VALUE_$(date +%s)_$$"
    fi
}

# Get environment variable names from a file
get_env_var_names() {
    local file="$1"
    grep -E '^[A-Z_][A-Z0-9_]*=' "$file" 2>/dev/null | cut -d= -f1 | sort -u
}

# Get the full line for an environment variable from a file
get_env_var_line() {
    local file="$1"
    local var_name="$2"
    grep -E "^${var_name}=" "$file" 2>/dev/null | head -1
}

# =============================================================================
# Dependency Validation
# =============================================================================
print_header "BridgeBeats Installation Script"

echo "Branch: ${BRANCH}"
echo "Install Directory: ${INSTALL_DIR}"
echo ""

print_section "Checking Dependencies"

missing_deps=()
warnings=()

# Check for Docker
if command -v docker &> /dev/null; then
    docker_version=$(docker --version 2>/dev/null || echo "unknown")
    echo "[OK] Docker: ${docker_version}"
else
    missing_deps+=("docker - Install from https://docs.docker.com/get-docker/")
fi

# Check for Docker Compose (v2 plugin)
if docker compose version &> /dev/null 2>&1; then
    compose_version=$(docker compose version --short 2>/dev/null || echo "unknown")
    echo "[OK] Docker Compose: ${compose_version}"

    # Check for v2+
    if [[ "$compose_version" =~ ^1\. ]]; then
        missing_deps+=("docker compose v2+ - Current version ${compose_version} is too old. Update Docker or install compose plugin.")
    fi
else
    missing_deps+=("docker compose - Install Docker Compose plugin: https://docs.docker.com/compose/install/")
fi

# Check for curl or wget
if command -v curl &> /dev/null; then
    echo "[OK] curl: available"
    DOWNLOAD_CMD="curl -sSL -o"
elif command -v wget &> /dev/null; then
    echo "[OK] wget: available"
    DOWNLOAD_CMD="wget -q -O"
else
    missing_deps+=("curl or wget - Required for downloading files")
fi

# Check for openssl (optional but recommended)
if command -v openssl &> /dev/null; then
    echo "[OK] openssl: available (for generating secure random secrets)"
else
    warnings+=("openssl not found - Will use fallback for generating random secrets")
fi

# Report missing dependencies
if [[ ${#warnings[@]} -gt 0 ]]; then
    echo ""
    for warning in "${warnings[@]}"; do
        echo "[WARN] ${warning}"
    done
fi

if [[ ${#missing_deps[@]} -gt 0 ]]; then
    echo ""
    echo "[ERROR] Missing required dependencies:"
    for dep in "${missing_deps[@]}"; do
        echo "  - ${dep}"
    done
    echo ""
    echo "Please install the missing dependencies and run this script again."
    exit 1
fi

echo ""
echo "[OK] All required dependencies are available"

# =============================================================================
# Create Installation Directory
# =============================================================================
print_section "Setting Up Installation Directory"

if [[ -d "$INSTALL_DIR" ]]; then
    echo "[OK] Directory already exists: ${INSTALL_DIR}"
else
    mkdir -p "$INSTALL_DIR"
    echo "[OK] Created directory: ${INSTALL_DIR}"
fi

cd "$INSTALL_DIR"
echo "[OK] Working directory: $(pwd)"

# =============================================================================
# Download Configuration Files
# =============================================================================
print_section "Downloading Configuration Files"

for file in "${DOWNLOAD_FILES[@]}"; do
    source_url="${BASE_URL}/${file}"

    # Backup existing file if present
    if [[ -f "$file" ]]; then
        backup_name="${file}.bak.${TIMESTAMP}"
        cp "$file" "$backup_name"
        echo "[OK] Backed up existing ${file} to ${backup_name}"
        log_upgrade "Backed up ${file} to ${backup_name}"
    fi

    # Download the file
    if [[ "$DOWNLOAD_CMD" == "curl -sSL -o" ]]; then
        curl -sSL -o "$file" "$source_url"
    else
        wget -q -O "$file" "$source_url"
    fi

    if [[ -f "$file" ]]; then
        echo "[OK] Downloaded: ${file}"
    else
        echo "[ERROR] Failed to download: ${file}"
        exit 1
    fi
done

# =============================================================================
# Setup Secrets Directory
# =============================================================================
print_section "Setting Up Secrets"

SECRETS_DIR="./secrets"
secrets_created=false

# UID for the .NET container's non-root user (default APP_UID in .NET containers)
CONTAINER_UID=1654

if [[ -d "$SECRETS_DIR" ]]; then
    echo "[OK] Secrets directory already exists: ${SECRETS_DIR}"
else
    mkdir -p "$SECRETS_DIR"
    chmod 700 "$SECRETS_DIR"
    secrets_created=true
    echo "[OK] Created secrets directory: ${SECRETS_DIR}"
fi

# Track secrets that need user attention
missing_secrets=()

for secret_config in "${SECRET_FILES[@]}"; do
    IFS='|' read -r secret_name description auto_generate <<< "$secret_config"
    secret_path="${SECRETS_DIR}/${secret_name}"

    if [[ -f "$secret_path" ]]; then
        if secret_has_content "$secret_path"; then
            echo "[OK] Secret exists and has content: ${secret_name}"
        else
            if [[ "$auto_generate" == "true" ]]; then
                # Auto-generate this secret
                generate_random_string > "$secret_path"
                chmod 600 "$secret_path"
                echo "[OK] Auto-generated: ${secret_name}"
            else
                missing_secrets+=("${secret_name}|${description}")
                echo "[WARN] Secret exists but needs content: ${secret_name}"
            fi
        fi
    else
        if [[ "$auto_generate" == "true" ]]; then
            # Auto-generate this secret
            generate_random_string > "$secret_path"
            chmod 600 "$secret_path"
            echo "[OK] Auto-generated: ${secret_name}"
        else
            # Create placeholder
            if [[ "$secret_name" == "apple_key.p8" ]]; then
                cat > "$secret_path" << 'EOF'
-----BEGIN PRIVATE KEY-----
REPLACE_WITH_YOUR_APPLE_MUSIC_PRIVATE_KEY
-----END PRIVATE KEY-----
EOF
            else
                echo "your_${secret_name%.*}_here" > "$secret_path"
            fi
            chmod 600 "$secret_path"
            missing_secrets+=("${secret_name}|${description}")
            echo "[WARN] Created placeholder: ${secret_name} (needs your input)"
        fi
    fi
done

# Change ownership of secrets directory to container UID so the non-root container user can read them
if command -v sudo &> /dev/null; then
    echo ""
    echo "Setting ownership of secrets for container access (requires sudo)..."
    if sudo chown -R "${CONTAINER_UID}:${CONTAINER_UID}" "$SECRETS_DIR" 2>/dev/null; then
        echo "[OK] Set secrets ownership to UID ${CONTAINER_UID} (container user)"
    else
        echo "[WARN] Could not set secrets ownership. You may need to run manually:"
        echo "       sudo chown -R ${CONTAINER_UID}:${CONTAINER_UID} $(pwd)/secrets"
    fi
else
    echo ""
    echo "[WARN] sudo not available. Please set secrets ownership manually:"
    echo "       sudo chown -R ${CONTAINER_UID}:${CONTAINER_UID} $(pwd)/secrets"
fi

# =============================================================================
# Setup Environment File
# =============================================================================
print_section "Configuring Environment"

if [[ -f ".env" ]]; then
    echo "[OK] Environment file exists: .env"

    # Check for new variables in .env.example
    if [[ -f ".env.example" ]]; then
        existing_vars=$(get_env_var_names ".env")
        example_vars=$(get_env_var_names ".env.example")

        new_vars=()
        while IFS= read -r var; do
            if ! echo "$existing_vars" | grep -q "^${var}$"; then
                new_vars+=("$var")
            fi
        done <<< "$example_vars"

        if [[ ${#new_vars[@]} -gt 0 ]]; then
            echo ""
            echo "[WARN] New environment variables found in .env.example:"
            echo "       Please add these to your .env file:"
            echo ""
            for var in "${new_vars[@]}"; do
                line=$(get_env_var_line ".env.example" "$var")
                echo "  ${line}"
            done
            echo ""
        fi
    fi
else
    if [[ -f ".env.example" ]]; then
        cp ".env.example" ".env"
        echo "[OK] Created .env from .env.example"
        echo "[WARN] Please edit .env with your configuration values"
    else
        echo "[ERROR] No .env.example found to create .env from"
        exit 1
    fi
fi

# =============================================================================
# Check for Existing Containers
# =============================================================================
print_section "Checking Existing Deployment"

# BridgeBeats container names from docker-compose.yml
BRIDGEBEATS_CONTAINERS=("bridgebeats" "bridgebeats-redis" "bridgebeats-pds" "bridgebeats-caddy")

# Check if any BridgeBeats containers exist
containers_exist=false
existing_containers=()
for container in "${BRIDGEBEATS_CONTAINERS[@]}"; do
    if docker ps -a --format '{{.Names}}' 2>/dev/null | grep -q "^${container}$"; then
        containers_exist=true
        existing_containers+=("$container")
    fi
done

if [[ "$containers_exist" == "true" ]]; then
    echo "[OK] Existing BridgeBeats containers detected:"
    for container in "${existing_containers[@]}"; do
        echo "  - ${container}"
    done

    # Log current container image information
    echo ""
    echo "Current container images:"
    log_upgrade "=== Pre-update container images ==="

    # Get image information for each service
    while IFS= read -r line; do
        if [[ -n "$line" ]]; then
            echo "  ${line}"
            log_upgrade "${line}"
        fi
    done < <(docker compose images 2>/dev/null || echo "Unable to retrieve image information")

    echo ""
    echo "Image information has been logged to ${UPGRADE_LOG} for rollback reference."
    echo ""

    # Prompt for pulling new images
    read -p "Would you like to pull the latest container images? (y/N): " pull_response
    if [[ "$pull_response" =~ ^[Yy]$ ]]; then
        echo ""
        echo "Pulling latest images..."
        docker compose pull
        log_upgrade "Pulled latest images"
        echo "[OK] Images updated"
    else
        echo "[OK] Skipping image pull"
    fi
else
    echo "[OK] No existing containers found (fresh installation)"
fi

# =============================================================================
# Summary and Next Steps
# =============================================================================
print_header "Installation Complete"

echo "Installation directory: $(pwd)"
echo ""

# Report secrets that need attention
if [[ ${#missing_secrets[@]} -gt 0 ]]; then
    echo "[ACTION REQUIRED] The following secrets need your input:"
    echo ""
    for secret_info in "${missing_secrets[@]}"; do
        IFS='|' read -r name desc <<< "$secret_info"
        echo "  - secrets/${name}"
        echo "    ${desc}"
        echo ""
    done
fi

# Check .env for empty required values
echo "Checking environment configuration..."
env_issues=()

# Required environment variables to check
required_env_vars=(
    "DOMAIN"
    "BASEURL"
    "CADDY_ADMIN_EMAIL"
    "PDS_HOSTNAME"
    "PDS_JWT_SECRET"
    "PDS_ADMIN_PASSWORD"
)

# At least one provider required
provider_vars=(
    "APPLE_TEAM_ID"
    "SPOTIFY_CLIENT_ID"
    "TIDAL_CLIENT_ID"
)

for var in "${required_env_vars[@]}"; do
    if grep -qE "^${var}=\s*$" ".env" 2>/dev/null; then
        env_issues+=("${var} - Required but empty")
    fi
done

provider_found=false
for var in "${provider_vars[@]}"; do
    if grep -qE "^${var}=.+$" ".env" 2>/dev/null; then
        if ! grep -qE "^${var}=\s*$" ".env" 2>/dev/null; then
            provider_found=true
            break
        fi
    fi
done

if [[ "$provider_found" == "false" ]]; then
    env_issues+=("At least one music provider required (APPLE_TEAM_ID, SPOTIFY_CLIENT_ID, or TIDAL_CLIENT_ID)")
fi

if [[ ${#env_issues[@]} -gt 0 ]]; then
    echo ""
    echo "[ACTION REQUIRED] Environment configuration issues:"
    echo ""
    for issue in "${env_issues[@]}"; do
        echo "  - ${issue}"
    done
    echo ""
    echo "Edit .env to configure these values."
fi

echo ""
echo "=========================================="
echo "Next Steps"
echo "=========================================="
echo ""
echo "1. Edit secrets in: $(pwd)/secrets/"
echo "2. Edit environment in: $(pwd)/.env"
echo "3. Start BridgeBeats with:"
echo ""
echo "   cd $(pwd)"
echo "   docker compose up -d"
echo ""
echo "For more information, see:"
echo "  https://github.com/${REPO_OWNER}/${REPO_NAME}/blob/${BRANCH}/docs/DEPLOYMENT.md"
echo ""
