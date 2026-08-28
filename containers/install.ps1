#Requires -Version 5.1
<#
.SYNOPSIS
    BridgeBeats Installation Script for Windows

.DESCRIPTION
    This script downloads and configures BridgeBeats for Docker deployment on Windows.
    Linux and macOS hosts must use install.sh instead — it sets the required file ownership
    and permissions that this Windows installer does not apply.

    It validates dependencies, downloads configuration files, and prepares the
    environment for running BridgeBeats via Docker Compose.

.PARAMETER Branch
    Git branch to download from (default: develop)

.PARAMETER Directory
    Installation directory (default: .\bridgebeats)

.EXAMPLE
    # Run directly from GitHub:
    iwr -useb https://raw.githubusercontent.com/tsmarvin/BridgeBeats/develop/containers/install.ps1 | iex

    # Or download and run with parameters:
    .\install.ps1 -Branch main -Directory C:\BridgeBeats

.LINK
    https://github.com/tsmarvin/BridgeBeats
#>

[CmdletBinding()]
param(
    [Parameter()]
    [string]$Branch = 'develop',

    [Parameter()]
    [string]$Directory = '.\bridgebeats'
)

# =============================================================================
# Configuration
# =============================================================================
$ErrorActionPreference = 'Stop'
$RepoOwner  = 'tsmarvin'
$RepoName   = 'BridgeBeats'
$Timestamp  = Get-Date -Format 'yyyyMMdd-HHmmss'
$UpgradeLog = 'upgrade.log'

# Files to download from the repository
$DownloadFiles = @('docker-compose.yml', 'Caddyfile')

# Files to backup during upgrades
$BackupFiles = @('docker-compose.yml', 'Caddyfile')

# Construct base URL for raw file downloads
$Domain = "https://raw.githubusercontent.com/$RepoOwner/$RepoName/$Branch/containers"

# =============================================================================
# Helper Functions
# =============================================================================
function Write-Header {
    param([string]$Message)
    Write-Host "`n==========================================" -ForegroundColor Cyan
    Write-Host $Message -ForegroundColor Cyan
    Write-Host "==========================================`n" -ForegroundColor Cyan
}

function Write-Section {
    param([string]$Message)
    Write-Host "`n--- $Message ---`n" -ForegroundColor Yellow
}

function Write-Ok {
    param([string]$Message)
    Write-Host "[OK] $Message" -ForegroundColor Green
}

function Write-Warn {
    param([string]$Message)
    Write-Host "[WARN] $Message" -ForegroundColor Yellow
}

function Write-Err {
    param([string]$Message)
    Write-Host "[ERROR] $Message" -ForegroundColor Red
}

function Write-UpgradeLog {
    param([string]$Message)
    $logPath = Join-Path -Path $Directory -ChildPath $UpgradeLog
    $logEntry = "[$Timestamp] $Message"
    Add-Content -Path $logPath -Value $logEntry
}

# =============================================================================
# Dependency Validation
# =============================================================================
Write-Header 'BridgeBeats Installation Script'

Write-Host "Branch: $Branch"
Write-Host "Install Directory: $Directory`n"

Write-Section 'Checking Dependencies'

$missingDeps = @()
$warnings = @()

# Check for Docker
$dockerCmd = Get-Command docker -ErrorAction Ignore
if ($dockerCmd) {
    try {
        $dockerVersion = & docker --version 2>$null
        Write-Ok "Docker: $dockerVersion"
    } catch {
        Write-Ok 'Docker: available'
    }
} else {
    $missingDeps += 'docker - Install from https://docs.docker.com/get-docker/'
}

# Check for Docker Compose (v2 plugin)
$composeWorks = $false
try {
    $null = & docker compose version 2>$null
    if ($LASTEXITCODE -eq 0) {
        $composeVersion = & docker compose version --short 2>$null
        Write-Ok "Docker Compose: $composeVersion"

        # Check for v2+
        if ($composeVersion -match '^1\.') {
            $missingDeps += "docker compose v2+ - Current version $composeVersion is too old. Update Docker or install compose plugin."
        } else {
            $composeWorks = $true
        }
    }
} catch {
    # Compose not available
}

if (-not $composeWorks -and $missingDeps.Count -eq 0) {
    $missingDeps += 'docker compose - Install Docker Compose plugin: https://docs.docker.com/compose/install/'
}

# Report warnings
if ($warnings.Count -gt 0) {
    Write-Host ''
    foreach ($warning in $warnings) {
        Write-Warn $warning
    }
}

# Report missing dependencies
if ($missingDeps.Count -gt 0) {
    Write-Err "`nMissing required dependencies:"
    foreach ($dep in $missingDeps) {
        Write-Host "  - $dep"
    }
    Write-Host "`nPlease install the missing dependencies and run this script again."
    exit 1
}

Write-Host ''
Write-Ok 'All required dependencies are available'

# =============================================================================
# Create Installation Directory
# =============================================================================
Write-Section 'Setting Up Installation Directory'

if (Test-Path $Directory) {
    Write-Ok "Directory already exists: $Directory"
} else {
    New-Item -Path $Directory -ItemType Directory -Force | Out-Null
    Write-Ok "Created directory: $Directory"
}

# Convert to absolute path and change to it
$Directory = Resolve-Path $Directory
Set-Location $Directory
Write-Ok "Working directory: $Directory"

# =============================================================================
# Download Configuration Files
# =============================================================================
Write-Section 'Downloading Configuration Files'

foreach ($file in $DownloadFiles) {
    $sourceUrl = "$Domain/$file"
    $destPath = Join-Path -Path $Directory -ChildPath $file

    # Backup existing file if it's in the backup list
    if ((Test-Path $destPath) -and ($file -in $BackupFiles)) {
        $backupName = "$file.bak.$Timestamp"
        $backupPath = Join-Path -Path $Directory -ChildPath $backupName
        Copy-Item -Path $destPath -Destination $backupPath
        Write-Ok "Backed up existing $file to $backupName"
        Write-UpgradeLog "Backed up $file to $backupName"
    }

    # Download the file
    try {
        Invoke-WebRequest -Uri $sourceUrl -OutFile $destPath -UseBasicParsing
        Write-Ok "Downloaded: $file"
    } catch {
        Write-Err "Failed to download: $file"
        Write-Host "  URL: $sourceUrl"
        Write-Host "  Error: $($_.Exception.Message)"
        exit 1
    }
}

# =============================================================================
# Setup Logs Directory
# =============================================================================
Write-Section 'Setting Up Logs'

$logsDir = Join-Path -Path $Directory -ChildPath 'logs'

if (Test-Path $logsDir) {
    Write-Ok "Logs directory already exists: $logsDir"
} else {
    New-Item -Path $logsDir -ItemType Directory -Force | Out-Null
    Write-Ok "Created logs directory: $logsDir"
}

$dataDirs = @(
    (Join-Path $Directory 'data\app'),
    (Join-Path $Directory 'data\dp-keys'),
    (Join-Path $Directory 'data\redis'),
    (Join-Path $Directory 'data\pds'),
    (Join-Path $Directory 'logs\caddy')
)
foreach ($d in $dataDirs) {
    if (-not (Test-Path $d)) { New-Item -Path $d -ItemType Directory -Force | Out-Null }
}
Write-Ok 'Data directories present: data\{app,dp-keys,redis,pds}, logs\caddy'

# =============================================================================
# Check for Existing Containers
# =============================================================================
Write-Section 'Checking Existing Deployment'

# BridgeBeats container names from docker-compose.yml
$BridgeBeatsContainers = @('bridgebeats-bootstrap', 'bridgebeats', 'bridgebeats-redis', 'bridgebeats-pds', 'bridgebeats-caddy')

# Check if any BridgeBeats containers exist
$containersExist = $false
$existingContainers = @()
try {
    $allContainers = & docker ps -a --format '{{.Names}}' 2>$null
    if ($LASTEXITCODE -eq 0 -and $allContainers) {
        foreach ($container in $BridgeBeatsContainers) {
            if ($allContainers -contains $container) {
                $containersExist = $true
                $existingContainers += $container
            }
        }
    }
} catch {
    # Docker not available or error
}

if ($containersExist) {
    Write-Ok 'Existing BridgeBeats containers detected:'
    foreach ($container in $existingContainers) {
        Write-Host "  - $container"
    }

    # Log upgrade session header
    Write-UpgradeLog ''
    Write-UpgradeLog "=== Upgrade: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') ==="
    Write-UpgradeLog "Branch: $Branch"

    # Log current container image information with RepoDigests
    Write-Host "`nCurrent container images:"
    Write-UpgradeLog 'Pre-update images:'

    foreach ($container in $existingContainers) {
        $image = & docker inspect --format '{{.Config.Image}}' $container 2>$null
        if (-not $image) {
            $image = 'unknown'
        }

        if ($image -ne 'unknown') {
            $digest = & docker inspect --format '{{index .RepoDigests 0}}' $image 2>$null
            if (-not $digest) {
                $digest = 'unknown'
            }
        } else {
            $digest = 'unknown'
        }

        Write-Host "  $container`: image=$image digest=$digest"
        Write-UpgradeLog "  $container`: image=$image digest=$digest"
    }

    # Log rollback info
    $composeBackup = "docker-compose.yml.bak.$Timestamp"
    $composeBackupPath = Join-Path -Path $Directory -ChildPath $composeBackup
    if (Test-Path $composeBackupPath) {
        Write-UpgradeLog "Rollback: Restore $composeBackup and run 'docker compose up -d'"
    }

    Write-Host "`nImage information has been logged to $UpgradeLog for rollback reference.`n"

    # Prompt for pulling new images
    $pullResponse = Read-Host 'Would you like to pull the latest container images? (y/N)'
    if ($pullResponse -match '^[Yy]') {
        Write-Host "`nPulling latest images..."
        & docker compose pull
        Write-UpgradeLog "Pulled latest images at $(Get-Date -Format 'HH:mm:ss')"
        Write-Ok 'Images updated'
    } else {
        Write-UpgradeLog 'Image pull skipped by user'
        Write-Ok 'Skipping image pull'
    }
} else {
    Write-Ok 'No existing containers found (fresh installation)'
}

# =============================================================================
# Infrastructure Secrets
# =============================================================================
Write-Section 'Configuring Infrastructure Secrets'
Write-Ok 'Redis credentials will be generated in a Docker named volume on first start'
Write-Ok 'Data Protection keys will persist in data\dp-keys'

# =============================================================================
# Summary and Next Steps
# =============================================================================
Write-Header 'Installation Complete'

Write-Host "Installation directory: $Directory`n"

Write-Host "`n==========================================" -ForegroundColor Cyan
Write-Host 'Next Steps' -ForegroundColor Cyan
Write-Host "==========================================`n" -ForegroundColor Cyan
Write-Host "1. Review the Caddy deployment values in: $Directory\docker-compose.yml"
Write-Host '2. Optionally set REDIS_PASSWORD to 32-128 base64/base64url characters, then run:'
Write-Host '   docker compose run --rm -e REDIS_PASSWORD bridgebeats-bootstrap'
Write-Host '   If skipped, BridgeBeats generates a cryptographically random password.'
Write-Host "3. Start BridgeBeats with:`n"
Write-Host "   cd $Directory"
Write-Host "   docker compose up -d`n"
Write-Host 'For more information, see:'
Write-Host "  https://github.com/$RepoOwner/$RepoName/blob/$Branch/docs/DEPLOYMENT.md`n"
