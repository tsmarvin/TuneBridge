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

# UID for the .NET container's non-root user (default APP_UID in .NET containers)
CONTAINER_UID=1654

# Files to download from the repository
DOWNLOAD_FILES=("docker-compose.yml" "Caddyfile" ".env.example")

# Files to backup during upgrades (excludes .env.example as it's just a template)
BACKUP_FILES=("docker-compose.yml" "Caddyfile")

# Secret files configuration: name|description|auto_generate
SECRET_FILES=(
    "apple_key.p8|Apple Music private key (.p8 file)|false"
    "spotify_client_secret.txt|Spotify API client secret|false"
    "tidal_client_secret.txt|Tidal API client secret|false"
    "discord_token.txt|Discord bot token|false"
    "atproto_password.txt|ATProto app password|false"
    "atproto_oauth_key.json|ATProto OAuth signing key (ES256 JWK)|true"
    "api_key_salt.txt|API key salt (random string for security)|true"
    "redis_password.txt|Redis password (random string for authentication)|true"
    "internal_service_key.txt|Internal service key (worker/API authentication)|true"
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
# Uses sudo if USE_SUDO is set and file is not readable
secret_has_content() {
    local file="$1"
    if [[ -f "$file" ]]; then
        # Check if file is readable by current user
        if [[ -r "$file" ]]; then
            # Use grep to check if file has non-whitespace content
            if grep -qE '\S' "$file" 2>/dev/null; then
                # Check it's not a placeholder
                if ! grep -qE '^(your_|REPLACE_WITH_)' "$file" 2>/dev/null; then
                    return 0
                fi
            fi
        elif [[ "$USE_SUDO" == "true" ]]; then
            # File exists but not readable, use sudo
            if sudo grep -qE '\S' "$file" 2>/dev/null; then
                # Check it's not a placeholder
                if ! sudo grep -qE '^(your_|REPLACE_WITH_)' "$file" 2>/dev/null; then
                    return 0
                fi
            fi
        else
            # File exists but not readable and no sudo - assume it has valid content
            # to avoid overwriting secrets we can't read
            echo "[WARN] Cannot read ${file} (permission denied) - assuming valid content"
            return 0
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

# Generate an ES256 (P-256) signing key in JWK format
generate_es256_jwk() {
    if command -v openssl &> /dev/null; then
        # Generate an EC P-256 key pair with openssl
        local privkey
        privkey=$(openssl ecparam -genkey -name prime256v1 -noout 2>/dev/null)

        # Export key parameters
        local params
        params=$(echo "$privkey" | openssl ec -text -noout 2>/dev/null)

        # Use a small inline Python/Python3 script to parse and emit JWK
        if command -v python3 &> /dev/null; then
            echo "$privkey" | python3 -c "
import sys, json, hashlib, base64, uuid
from subprocess import run, PIPE

pem = sys.stdin.read()
# Use openssl to get the raw key bytes
result = run(['openssl', 'ec', '-text', '-noout'], input=pem, capture_output=True, text=True)
lines = result.stdout.strip().split('\n')

# Parse the hex bytes from openssl output
hex_bytes = ''
in_priv = False
in_pub = False
priv_hex = ''
pub_hex = ''
for line in lines:
    line = line.strip()
    if 'priv:' in line:
        in_priv = True
        in_pub = False
        continue
    elif 'pub:' in line:
        in_priv = False
        in_pub = True
        continue
    elif 'ASN1 OID:' in line or 'NIST CURVE:' in line:
        in_priv = False
        in_pub = False
        continue
    if in_priv:
        priv_hex += line.replace(':', '')
    if in_pub:
        pub_hex += line.replace(':', '')

priv_bytes = bytes.fromhex(priv_hex)
pub_bytes = bytes.fromhex(pub_hex)

# Public key is 04 || x || y (uncompressed)
if pub_bytes[0] == 0x04:
    x = pub_bytes[1:33]
    y = pub_bytes[33:65]
else:
    sys.exit(1)

# Ensure d is exactly 32 bytes (pad with leading zeros if needed)
d = priv_bytes[-32:] if len(priv_bytes) >= 32 else priv_bytes.rjust(32, b'\x00')

def b64url(b):
    return base64.urlsafe_b64encode(b).rstrip(b'=').decode()

jwk = {
    'kty': 'EC',
    'crv': 'P-256',
    'x': b64url(x),
    'y': b64url(y),
    'd': b64url(d),
    'kid': uuid.uuid4().hex,
    'alg': 'ES256',
    'use': 'sig'
}
print(json.dumps(jwk))
"
        else
            echo '{}' # Placeholder if python3 not available
            echo "[WARN] python3 not found - ATProto OAuth key needs manual generation" >&2
        fi
    else
        echo '{}' # Placeholder if openssl not available
        echo "[WARN] openssl not found - ATProto OAuth key needs manual generation" >&2
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
# Sudo Consent
# =============================================================================
print_section "Privilege Check"

USE_SUDO=false
SUDO_AVAILABLE=false

# Check if sudo is available
if command -v sudo &> /dev/null; then
    SUDO_AVAILABLE=true
fi

# Explain why sudo is needed
echo "This script may need elevated privileges (sudo) to:"
echo "  1. Set ownership of secrets directory for container access (fresh install)"
echo "  2. Read existing secrets owned by the container user (re-run/upgrade)"
echo ""

if [[ "$SUDO_AVAILABLE" == "true" ]]; then
    read -p "Allow this script to use sudo when needed? (Y/n): " sudo_response
    if [[ ! "$sudo_response" =~ ^[Nn]$ ]]; then
        USE_SUDO=true
        echo "[OK] Sudo access granted"
    else
        echo "[OK] Sudo declined - manual commands will be shown when needed"
        echo ""
        echo "You may need to run these commands manually later:"
        echo "  sudo chown -R ${CONTAINER_UID}:${CONTAINER_UID} \$(pwd)/secrets"
    fi
else
    echo "[WARN] sudo not available on this system"
    echo ""
    echo "You may need to run these commands manually later:"
    echo "  sudo chown -R ${CONTAINER_UID}:${CONTAINER_UID} \$(pwd)/secrets"
fi

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

# Helper to check if file should be backed up
should_backup() {
    local file="$1"
    for backup_file in "${BACKUP_FILES[@]}"; do
        if [[ "$file" == "$backup_file" ]]; then
            return 0
        fi
    done
    return 1
}

for file in "${DOWNLOAD_FILES[@]}"; do
    source_url="${BASE_URL}/${file}"

    # Backup existing file if it's in the backup list
    if [[ -f "$file" ]] && should_backup "$file"; then
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
# Setup Logs Directory
# =============================================================================
print_section "Setting Up Logs"

LOGS_DIR="./logs"

if [[ -d "$LOGS_DIR" ]]; then
    echo "[OK] Logs directory already exists: ${LOGS_DIR}"
else
    mkdir -p "$LOGS_DIR"
    echo "[OK] Created logs directory: ${LOGS_DIR}"
fi

# =============================================================================
# Check for Existing Containers
# =============================================================================
print_section "Checking Existing Deployment"

SECRETS_DIR="./secrets"

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

    # Log upgrade session header
    log_upgrade ""
    log_upgrade "=== Upgrade: $(date '+%Y-%m-%d %H:%M:%S') ==="
    log_upgrade "Branch: ${BRANCH}"

    # Back up secrets before any modifications
    if [[ -d "$SECRETS_DIR" ]]; then
        backup_timestamp=$(date +%Y%m%d_%H%M%S)
        backup_dir="./secrets_backup_${backup_timestamp}"
        if cp -r "$SECRETS_DIR" "$backup_dir"; then
            echo "[OK] Backed up secrets to ${backup_dir}/"
            log_upgrade "Backed up secrets to ${backup_dir}/"
        else
            echo "[WARN] Failed to back up secrets directory"
            log_upgrade "WARN: Failed to back up secrets directory"
        fi

        # Prune old backups, keep 3 most recent
        ls -dt ./secrets_backup_*/ 2>/dev/null | tail -n +4 | xargs rm -rf
    fi

    # Log current container image information with RepoDigests
    echo ""
    echo "Current container images:"
    log_upgrade "Pre-update images:"

    for container in "${existing_containers[@]}"; do
        image=$(docker inspect --format="{{.Config.Image}}" "$container" 2>/dev/null || echo "unknown")
        if [[ -n "$image" && "$image" != "unknown" ]]; then
            digest=$(docker inspect --format="{{index .RepoDigests 0}}" "$image" 2>/dev/null || echo "unknown")
        else
            digest="unknown"
        fi
        echo "  ${container}: image=${image} digest=${digest}"
        log_upgrade "  ${container}: image=${image} digest=${digest}"
    done

    # Log rollback info
    compose_backup="docker-compose.yml.bak.${TIMESTAMP}"
    if [[ -f "$compose_backup" ]]; then
        log_upgrade "Rollback: Restore ${compose_backup} and run 'docker compose up -d'"
    fi

    echo ""
    echo "Image information has been logged to ${UPGRADE_LOG} for rollback reference."
    echo ""

    # Prompt for pulling new images
    read -p "Would you like to pull the latest container images? (y/N): " pull_response
    if [[ "$pull_response" =~ ^[Yy]$ ]]; then
        echo ""
        echo "Pulling latest images..."
        docker compose pull
        log_upgrade "Pulled latest images at $(date '+%H:%M:%S')"
        echo "[OK] Images updated"
    else
        log_upgrade "Image pull skipped by user"
        echo "[OK] Skipping image pull"
    fi
else
    echo "[OK] No existing containers found (fresh installation)"
fi

# =============================================================================
# Setup Secrets Directory
# =============================================================================
print_section "Setting Up Secrets"

secrets_created=false

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

    if [[ -d "$secret_path" ]]; then
        rm -rf "$secret_path"
        echo "[WARN] Removed directory at ${secret_name} (expected file)"
    fi

    if [[ -f "$secret_path" ]]; then
        if secret_has_content "$secret_path"; then
            echo "[OK] Secret exists and has content: ${secret_name}"
        else
            echo "[WARN] Secret exists but appears empty or contains placeholder content: ${secret_name}"
        fi
    else
        if [[ "$auto_generate" == "true" ]]; then
            # Auto-generate this secret
            if [[ "$secret_name" == "atproto_oauth_key.json" ]]; then
                generate_es256_jwk > "$secret_path"
            else
                generate_random_string > "$secret_path"
            fi
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

# Set ownership of secrets directory to container UID so the non-root container user can read them
if [[ "$secrets_created" == "true" ]]; then
    if [[ "$USE_SUDO" == "true" ]]; then
        echo ""
        echo "Setting ownership of secrets for container access..."
        if sudo chown -R "${CONTAINER_UID}:${CONTAINER_UID}" "$SECRETS_DIR" 2>/dev/null; then
            echo "[OK] Set secrets ownership to UID ${CONTAINER_UID} (container user)"
        else
            echo "[WARN] Could not set secrets ownership. Please run manually:"
            echo "       sudo chown -R ${CONTAINER_UID}:${CONTAINER_UID} $(pwd)/secrets"
        fi
    else
        echo ""
        echo "[ACTION REQUIRED] Set secrets ownership for container access:"
        echo "  sudo chown -R ${CONTAINER_UID}:${CONTAINER_UID} $(pwd)/secrets"
    fi
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
