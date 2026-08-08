#!/bin/bash
set -eu

read_bootstrap_secret() {
    secret_path="/app/bootstrap/$1"
    if [ ! -s "$secret_path" ]; then
        echo "ERROR: Required BridgeBeats bootstrap secret '$1' is missing." >&2
        exit 1
    fi
    tr -d '\r\n' < "$secret_path"
}

export DOTNET_ENVIRONMENT="${DOTNET_ENVIRONMENT:-Production}"
export ASPNETCORE_ENVIRONMENT="${ASPNETCORE_ENVIRONMENT:-$DOTNET_ENVIRONMENT}"

# Aspire dashboard and orchestration infrastructure.
export ASPIRE_ALLOW_UNSECURED_TRANSPORT="true"
export ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL="http://localhost:18889"
export ASPIRE_DASHBOARD_UNSECURED_ALLOW_ANONYMOUS="true"
export DASHBOARD__MCP__DISABLED="true"
export ASPIRE_STORE_PATH="/app/data"

redis_password="$(read_bootstrap_secret redis_password)"

: "${BridgeBeats__IdentityConnectionString:?BridgeBeats__IdentityConnectionString must be set in docker-compose.yml}"
: "${BridgeBeats__DataProtectionKeyPath:?BridgeBeats__DataProtectionKeyPath must be set in docker-compose.yml}"
: "${BridgeBeats__LogDirPath:?BridgeBeats__LogDirPath must be set in docker-compose.yml}"
: "${BridgeBeats__NodeNumber:?BridgeBeats__NodeNumber must be set in docker-compose.yml}"
: "${DASHBOARD__FRONTEND__PUBLICURL:?DASHBOARD__FRONTEND__PUBLICURL must be set in docker-compose.yml}"

if [ ! -d "$BridgeBeats__DataProtectionKeyPath" ]; then
    echo "ERROR: Persistent Data Protection key-ring directory is unavailable: $BridgeBeats__DataProtectionKeyPath" >&2
    exit 1
fi
mkdir -p "$BridgeBeats__LogDirPath"

# Bootstrap-only process configuration. All application/provider settings are loaded from the
# encrypted database aggregate by AppHost and projected to its child processes.
export ConnectionStrings__redis="redis:6379,password=${redis_password}"

export DcpPublisher__CliPath="/app/tools/dcp/dcp"
export DcpPublisher__DashboardPath="/app/tools/dashboard/Aspire.Dashboard.dll"

echo "Starting BridgeBeats AppHost (Environment: $DOTNET_ENVIRONMENT)..."
exec /src/BridgeBeats.AppHost/BridgeBeats.AppHost
