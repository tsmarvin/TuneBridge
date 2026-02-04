#!/usr/bin/env pwsh
#Requires -Version 7.0
<#
.SYNOPSIS
    Builds and publishes all BridgeBeats projects to a standardized folder structure.

.DESCRIPTION
    This script produces the same folder structure used by Docker, enabling
    the entrypoint.sh to work identically in both local and containerized environments.

.PARAMETER Runtime
    The .NET runtime identifier (RID) to publish for.
    Examples: linux-x64, linux-arm64, win-x64, win-arm64, osx-arm64

.EXAMPLE
    ./Build-Publish.ps1 -Runtime linux-x64
#>
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('linux-x64', 'linux-arm64', 'win-x64', 'win-arm64', 'osx-arm64')]
    [string]$Runtime
)

$ErrorActionPreference = 'Stop'

# Determine repository root and source directory
# Script is in containers/ - parent is either:
#   - Repo root (local): projects are in src/ subdirectory
#   - /src in Docker: projects are directly here (COPY src/ .)
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = Split-Path -Parent $ScriptDir

# Check if we're in Docker context (projects directly in parent) or local (projects in src/)
if (Test-Path (Join-Path -Path $RepoRoot -ChildPath 'BridgeBeats.AppHost')) {
    # Docker context: projects are directly in the working directory
    $SrcDir = $RepoRoot
    $PublishDir = Join-Path -Path $RepoRoot -ChildPath 'publish'
} else {
    # Local context: projects are in src/ subdirectory
    $SrcDir = Join-Path -Path $RepoRoot -ChildPath 'src'
    $PublishDir = Join-Path -Path $RepoRoot -ChildPath 'publish'
}

# Clean and create publish directory
if (Test-Path $PublishDir) {
    Remove-Item -Path $PublishDir -Recurse -Force
}
New-Item -Path $PublishDir -ItemType Directory | Out-Null

# Project definitions: [ProjectName, OutputFolder]
# Note: AppHost is handled separately due to Aspire.AppHost.Sdk special behavior
$Projects = @(
    @('BridgeBeats.Web', 'web'),
    @('BridgeBeats.Worker.Spotify', 'worker-spotify'),
    @('BridgeBeats.Worker.AppleMusic', 'worker-applemusic'),
    @('BridgeBeats.Worker.Tidal', 'worker-tidal'),
    @('BridgeBeats.Worker.Discord', 'worker-discord'),
    @('BridgeBeats.Worker.SagaCoordinator', 'worker-sagacoordinator'),
    @('BridgeBeats.Worker.JetStreamWatcher', 'worker-jetstreamwatcher'),
    @('BridgeBeats.Worker.CacheBootstrap', 'worker-cachebootstrap')
)

$AppHostProj = Join-Path $SrcDir 'BridgeBeats.AppHost' 'BridgeBeats.AppHost.csproj'

# Try locked-mode restore first; if it fails due to RID mismatch, regenerate and validate
Write-Host 'Restoring packages (locked-mode)...' -ForegroundColor Cyan
$restoreResult = dotnet restore $AppHostProj --locked-mode -r $Runtime 2>&1
if ($LASTEXITCODE -ne 0) {
    # Check if failure is due to RID mismatch in lock files
    if ($restoreResult -match 'runtime identifiers have changed') {
        Write-Host 'Lock files were generated for a different RID. Regenerating...' -ForegroundColor Yellow

        # Detect the original RID from the AppHost lock file
        $AppHostLock = Join-Path -Path $SrcDir -ChildPath 'BridgeBeats.AppHost' -AdditionalChildPath 'packages.lock.json'
        $LockContent = Get-Content $AppHostLock -Raw | ConvertFrom-Json -AsHashtable
        $OriginalRid = $LockContent.dependencies.Keys
                     | Where-Object { $_ -match '^net\d+\.\d+/(linux|win|osx)-(x64|x86|arm64|arm)$' }
                     | ForEach-Object { $_ -replace '^net\d+\.\d+/', '' }
                     | Select-Object -First 1
        if (-not $OriginalRid) { $OriginalRid = 'linux-x64' }

        # Find all lock files and back them up
        $LockFiles = Get-ChildItem -Path $SrcDir -Recurse -Filter 'packages.lock.json'
        foreach ($LockFile in $LockFiles) {
            Copy-Item -Path $LockFile.FullName -Destination "$($LockFile.FullName).backup"
        }

        # Regenerate lock files
        dotnet restore $AppHostProj --force-evaluate -r $Runtime
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

        # Validate only platform-specific packages changed using the validation script
        $TestScript = Join-Path -Path $ScriptDir -ChildPath 'Test-LockFileChanges.ps1'
        $AppHostLockBackup = Join-Path -Path $SrcDir -ChildPath 'BridgeBeats.AppHost' -AdditionalChildPath 'packages.lock.json.backup'
        $AppHostLockCurrent = Join-Path -Path $SrcDir -ChildPath 'BridgeBeats.AppHost' -AdditionalChildPath 'packages.lock.json'
        & $TestScript -BackupPath $AppHostLockBackup -CurrentPath $AppHostLockCurrent -FromPlatform $OriginalRid -ToPlatform $Runtime
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

        # Clean up backup files
        foreach ($LockFile in $LockFiles) {
            Remove-Item -Path "$($LockFile.FullName).backup" -ErrorAction SilentlyContinue
        }

        Write-Host "Lock files regenerated and validated for $Runtime" -ForegroundColor Green
    } else {
        Write-Error "Restore failed: $restoreResult"
        exit 1
    }
}

# Build AppHost
Write-Host 'Building BridgeBeats.AppHost...' -ForegroundColor Cyan
dotnet build $AppHostProj -c Release -r $Runtime
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# Copy AppHost build output to publish directory
$AppHostBuildDir = Join-Path -Path $SrcDir -ChildPath 'BridgeBeats.AppHost' -AdditionalChildPath 'bin', 'Release', 'net10.0', $Runtime
$AppHostPublishDir = Join-Path -Path $PublishDir -ChildPath 'apphost'
Copy-Item -Path $AppHostBuildDir -Destination $AppHostPublishDir -Recurse
Write-Host 'Copied AppHost build output -> apphost/' -ForegroundColor Cyan

# Publish each project (publish includes build)
foreach ($Project in $Projects) {
    $ProjectName = $Project[0]
    $OutputFolder = $Project[1]
    $ProjectPath = Join-Path -Path $SrcDir -ChildPath $ProjectName -AdditionalChildPath "$ProjectName.csproj"
    $OutputPath = Join-Path -Path $PublishDir -ChildPath $OutputFolder

    Write-Host "Publishing $ProjectName -> $OutputFolder/" -ForegroundColor Cyan
    dotnet publish $ProjectPath -c Release -r $Runtime -o $OutputPath
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

# Copy entrypoint script
$EntrypointSrc = Join-Path -Path $ScriptDir -ChildPath 'entrypoint.sh'
$EntrypointDst = Join-Path -Path $PublishDir -ChildPath 'entrypoint.sh'
Copy-Item $EntrypointSrc $EntrypointDst

Write-Host "Publish complete: $PublishDir" -ForegroundColor Green
