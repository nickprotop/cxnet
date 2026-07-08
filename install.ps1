# cxnet Installer for Windows
# Usage: irm https://raw.githubusercontent.com/nickprotop/cxnet/main/install.ps1 | iex
# Copyright (c) Nikolaos Protopapas. All rights reserved.
# Licensed under the MIT License.

$ErrorActionPreference = "Stop"

$repo = "nickprotop/cxnet"
$installDir = "$env:LOCALAPPDATA\cxnet"

Write-Host "Installing cxnet..." -ForegroundColor Cyan

# Detect architecture
$arch = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture
switch ($arch) {
    "X64"   { $binary = "cxnet-win-x64.exe" }
    "Arm64" { $binary = "cxnet-win-arm64.exe" }
    default {
        Write-Host "Error: Unsupported architecture: $arch" -ForegroundColor Red
        exit 1
    }
}

# Get latest release
Write-Host "Fetching latest release..."
$release = Invoke-RestMethod -Uri "https://api.github.com/repos/$repo/releases/latest"
$version = $release.tag_name -replace '^v', ''
$asset = $release.assets | Where-Object { $_.name -eq $binary }

if (-not $asset) {
    Write-Host "Error: Binary '$binary' not found in release $($release.tag_name)" -ForegroundColor Red
    exit 1
}

Write-Host "Latest version: $version"

# Create directories
New-Item -ItemType Directory -Force -Path $installDir | Out-Null

# Suppress Invoke-WebRequest's built-in progress UI: for a large file it re-renders per chunk and makes
# the download many times slower. We print our own status instead.
$ProgressPreference = 'SilentlyContinue'

# Download binary to a temp file, then move it into place. The binary is ~75 MB (self-contained), so a
# silent download can look hung — the messages below make the wait expected. Downloading to a temp file
# and moving it in also avoids failing when an existing cxnet.exe is in use (a running exe can't be
# overwritten in place).
Write-Host "Downloading $binary (~75 MB, this may take a moment)..."
$outputPath = Join-Path $installDir "cxnet.exe"
$tmpPath = Join-Path $installDir ".cxnet.download.tmp"
try {
    Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $tmpPath
    Move-Item -Force -Path $tmpPath -Destination $outputPath
    Write-Host "Download complete." -ForegroundColor Green
}
finally {
    if (Test-Path $tmpPath) { Remove-Item -Force $tmpPath }
}

# Download uninstaller (small — no temp-file dance needed)
$uninstallUrl = "https://raw.githubusercontent.com/$repo/main/uninstall.ps1"
$uninstallPath = Join-Path $installDir "cxnet-uninstall.ps1"
Invoke-WebRequest -Uri $uninstallUrl -OutFile $uninstallPath

# Add to PATH if not already there
$userPath = [Environment]::GetEnvironmentVariable("Path", "User")
if ($userPath -notlike "*$installDir*") {
    [Environment]::SetEnvironmentVariable("Path", "$installDir;$userPath", "User")
    Write-Host "Added $installDir to user PATH" -ForegroundColor Green
}

Write-Host ""
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Cyan
Write-Host "  cxnet v$version installed!" -ForegroundColor Green
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Cyan
Write-Host ""
Write-Host "  Binary:  $outputPath"
Write-Host ""
Write-Host "  Run:     cxnet"
Write-Host "  Remove:  cxnet-uninstall.ps1"
Write-Host ""
Write-Host "  Note: Restart your terminal for PATH changes to take effect." -ForegroundColor Yellow
