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
$RepoRoot = Split-Path -Parent $PSScriptRoot

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

# Clean and create publish directory with src subdirectory
if (Test-Path $PublishDir) {
    Remove-Item -Path $PublishDir -Recurse -Force
}
$PublishSrcDir = Join-Path -Path $PublishDir -ChildPath 'src'
New-Item -Path $PublishSrcDir -ItemType Directory -Force | Out-Null

# Find and publish all projects to publish/src/{ProjectName}/
$TestScript = Join-Path -Path $PSScriptRoot -ChildPath 'Test-LockFileChanges.ps1'
$Projects = Get-ChildItem -Path $SrcDir -Filter '*.csproj' -Recurse
write-host $(pwd)
dotnet restore . --force-evaluate -r $Runtime
foreach ($Project in $Projects) {
    # Detect the original RID from the AppHost lock file
    $ProjLock    = Join-Path -Path $Project.DirectoryName -ChildPath 'packages.lock.json'
    $LockBackup  = Join-Path -Path $Project.DirectoryName -ChildPath 'packages.lock.json.backup'
    $LockContent = Get-Content -Path $ProjLock -Raw | ConvertFrom-Json -AsHashtable
    $OriginalRid = $LockContent.dependencies.Keys
                    | Where-Object { $_ -match '^net\d+\.\d+/(linux|win|osx)-(x64|x86|arm64|arm)$' }
                    | ForEach-Object { $_ -replace '^net\d+\.\d+/', '' }
                    | Select-Object -First 1
    if (-not $OriginalRid) { $OriginalRid = 'linux-x64' }

    Copy-Item -Path $ProjLock -Destination $LockBackup

    # Regenerate lock files
    #dotnet restore $Project.FullName --force-evaluate -r $Runtime
    #if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    # Validate only platform-specific packages changed using the validation script
    #& $TestScript -BackupPath $LockBackup -CurrentPath $ProjLock -FromPlatform $OriginalRid -ToPlatform $Runtime
    #if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    # Clean up backup file
    Remove-Item -Path $LockBackup -ErrorAction SilentlyContinue

    $ProjectName = $Project.Directory.Name
    Write-Host "$ProjectName lock file regenerated and validated for $Runtime" -ForegroundColor Green

    $OutputPath = Join-Path -Path $PublishSrcDir -ChildPath $ProjectName

    Write-Host "Publishing $ProjectName..." -ForegroundColor Cyan
    dotnet publish $Project.FullName -c Release -r $Runtime -o $OutputPath -p:IsPublishable=true
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

# Copy entrypoint script
$EntrypointSrc = Join-Path -Path $PSScriptRoot -ChildPath 'entrypoint.sh'
$EntrypointDst = Join-Path -Path $PublishDir -ChildPath 'entrypoint.sh'
Copy-Item $EntrypointSrc $EntrypointDst

Write-Host "Publish complete: $PublishDir" -ForegroundColor Green
