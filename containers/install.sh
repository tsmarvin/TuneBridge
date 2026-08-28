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
DOWNLOAD_FILES=("docker-compose.yml" "Caddyfile")

# Files to backup during upgrades
BACKUP_FILES=("docker-compose.yml" "Caddyfile")

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
echo "  1. Set ownership of application data and logs for container access"
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
        echo "  sudo chown -R ${CONTAINER_UID}:${CONTAINER_UID} \$(pwd)/logs"
    fi
else
    echo "[WARN] sudo not available on this system"
    echo ""
    echo "You may need to run these commands manually later:"
    echo "  sudo chown -R ${CONTAINER_UID}:${CONTAINER_UID} \$(pwd)/logs"
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

# Set ownership of logs directory to container UID so the non-root container user can write logs.
# The container runs as UID ${CONTAINER_UID}; without this the bind-mounted ./logs stays owned by
# the host user and Serilog's file sink silently fails to write. Applied every run so existing
# installs with incorrect ownership are self-healed on upgrade.
if [[ "$USE_SUDO" == "true" ]]; then
    if sudo chown -R "${CONTAINER_UID}:${CONTAINER_UID}" "$LOGS_DIR" 2>/dev/null; then
        echo "[OK] Set logs ownership to UID ${CONTAINER_UID} (container user)"
    else
        echo "[WARN] Could not set logs ownership. Please run manually:"
        echo "       sudo chown -R ${CONTAINER_UID}:${CONTAINER_UID} $(pwd)/logs"
    fi
else
    echo ""
    echo "[ACTION REQUIRED] Set logs ownership for container access:"
    echo "  sudo chown -R ${CONTAINER_UID}:${CONTAINER_UID} $(pwd)/logs"
fi

# =============================================================================
# Setup Data Directories
# =============================================================================
print_section "Setting Up Data Directories"

# Create bind-mount targets for all persistent-data services (idempotent)
mkdir -p ./data/app ./data/dp-keys ./data/redis ./data/pds ./logs/caddy
echo "[OK] Data directories present: ./data/{app,dp-keys,redis,pds} ./logs/caddy"

# Set ownership per the actual writing UID of each service.
# Bind-mounts present host directory ownership directly to the container —
# wrong ownership causes silent persistence failures or startup crashes.
#
# Ownership table:
#   ./data/app                  → 1654:1654 (.NET app; APP_UID=1654)
#   ./data/dp-keys              → 1654:1654 (.NET Data Protection key ring)
#   ./data/pds                  → 0:0 root (PDS image has no USER directive)
#   ./data/redis                → 0:0 root (compose command: override uses "sh -c …";
#                                   gosu step-down is skipped, so redis-server runs as root.
#                                   If the "sh -c" override is removed, change to 999:1000.)
#   ./logs/caddy                → 0:0 root (caddy writes access logs as root)
#
# IMPORTANT: chown ./logs/caddy specifically — never the parent ./logs tree.
# The parent ./logs is owned by ${CONTAINER_UID} (set by the Logs block above)
# so the app's Serilog file sink can write there. Chowning the whole ./logs
# tree to root would break app logging.
if [[ "$USE_SUDO" == "true" ]]; then
    if sudo chown -R "${CONTAINER_UID}:${CONTAINER_UID}" ./data/app ./data/dp-keys 2>/dev/null; then
        echo "[OK] Set ./data/app and ./data/dp-keys ownership to UID ${CONTAINER_UID} (app user)"
    else
        echo "[WARN] Could not set application-data ownership. Please run manually:"
        echo "       sudo chown -R ${CONTAINER_UID}:${CONTAINER_UID} $(pwd)/data/app $(pwd)/data/dp-keys"
    fi
    if sudo chown -R 0:0 ./data/pds ./data/redis ./logs/caddy 2>/dev/null; then
        echo "[OK] Set ./data/pds, ./data/redis, and ./logs/caddy ownership to root"
    else
        echo "[WARN] Could not set data/pds, data/redis, or logs/caddy ownership. Please run manually:"
        echo "       sudo chown -R 0:0 $(pwd)/data/pds $(pwd)/data/redis $(pwd)/logs/caddy"
    fi
else
    echo ""
    echo "[ACTION REQUIRED] Set data directory ownership for container access:"
    echo "  sudo chown -R ${CONTAINER_UID}:${CONTAINER_UID} $(pwd)/data/app $(pwd)/data/dp-keys"
    echo "  sudo chown -R 0:0 $(pwd)/data/pds $(pwd)/data/redis $(pwd)/logs/caddy"
fi

# =============================================================================
# Check for Existing Containers
# =============================================================================
print_section "Checking Existing Deployment"

# BridgeBeats container names from docker-compose.yml
BRIDGEBEATS_CONTAINERS=("bridgebeats-bootstrap" "bridgebeats" "bridgebeats-redis" "bridgebeats-pds" "bridgebeats-caddy")

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
# Infrastructure Secrets
# =============================================================================
print_section "Configuring Infrastructure Secrets"

echo "[OK] Redis credentials will be generated in a Docker named volume on first start"
echo "[OK] Data Protection keys will persist in ./data/dp-keys"

# =============================================================================
# Summary and Next Steps
# =============================================================================
print_header "Installation Complete"

echo "Installation directory: $(pwd)"
echo ""

echo ""
echo "=========================================="
echo "Next Steps"
echo "=========================================="
echo ""
echo "1. Review the Caddy deployment values in: $(pwd)/docker-compose.yml"
echo "2. Optionally export REDIS_PASSWORD (32-128 base64/base64url characters), then run:"
echo "   docker compose run --rm -e REDIS_PASSWORD bridgebeats-bootstrap"
echo "   If skipped, BridgeBeats generates a cryptographically random password."
echo "3. Start BridgeBeats with:"
echo ""
echo "   cd $(pwd)"
echo "   docker compose up -d"
echo ""
echo "For more information, see:"
echo "  https://github.com/${REPO_OWNER}/${REPO_NAME}/blob/${BRANCH}/docs/DEPLOYMENT.md"
echo ""
