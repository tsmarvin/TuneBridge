#!/bin/bash

# Test runner script for TuneBridge
# This script demonstrates how to run the test suite locally

set -e

echo "==================================="
echo "TuneBridge Test Runner"
echo "==================================="
echo ""

# Check if .NET is installed
if ! command -v dotnet &> /dev/null; then
    echo "ERROR: .NET SDK not found. Please install .NET 9.0 SDK."
    echo "Visit: https://dotnet.microsoft.com/download/dotnet/9.0"
    exit 1
fi

echo "✓ .NET SDK found: $(dotnet --version)"
echo ""

# Function to check if secrets are available
check_secrets() {
    local has_secrets=true
    
    if [ -z "$APPLETEAMID" ] || [ -z "$APPLEKEYID" ]; then
        has_secrets=false
    fi
    
    if [ -z "$APPLEKEYPATH" ] && [ -z "$APPLEPRIVATEKEY" ]; then
        has_secrets=false
    fi
    
    if [ -z "$SPOTIFYCLIENTID" ] || [ -z "$SPOTIFYCLIENTSECRET" ]; then
        has_secrets=false
    fi
    
    echo "$has_secrets"
}

# Restore dependencies
echo "Restoring dependencies..."
dotnet restore
echo ""

# Build the solution
echo "Building solution..."
dotnet build --no-restore --configuration Release
echo ""

# Transform appsettings.json for tests
echo "Transforming appsettings.json..."
chmod +x ./transform-appsettings.sh
./transform-appsettings.sh
echo ""

# Run Unit Tests (always run, don't need secrets)
echo "==================================="
echo "Running Unit Tests"
echo "==================================="
dotnet test --no-build --configuration Release --filter "FullyQualifiedName~Unit" --logger "console;verbosity=normal"
echo ""

# Check if we have secrets for integration tests
HAS_SECRETS=$(check_secrets)

if [ "$HAS_SECRETS" = "true" ]; then
    echo "✓ API credentials detected"
    echo ""
    
    # Run Integration Tests
    echo "==================================="
    echo "Running Integration Tests"
    echo "==================================="
    dotnet test --no-build --configuration Release --filter "FullyQualifiedName~Integration" --logger "console;verbosity=normal"
    echo ""
    
    # Run End-to-End Tests
    echo "==================================="
    echo "Running End-to-End Tests"
    echo "==================================="
    dotnet test --no-build --configuration Release --filter "FullyQualifiedName~EndToEnd" --logger "console;verbosity=normal"
    echo ""
else
    echo "⚠ API credentials not found - skipping integration and E2E tests"
    echo ""
    echo "To run all tests, set the following environment variables:"
    echo "  - APPLETEAMID"
    echo "  - APPLEKEYID"
    echo "  - APPLEKEYPATH (or APPLEPRIVATEKEY)"
    echo "  - SPOTIFYCLIENTID"
    echo "  - SPOTIFYCLIENTSECRET"
    echo ""
    echo "See TESTING.md for more details."
    echo ""
fi

echo "==================================="
echo "Test run complete!"
echo "==================================="
