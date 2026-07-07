# cxnet Uninstaller for Windows
# Copyright (c) Nikolaos Protopapas. All rights reserved.
# Licensed under the MIT License.

$installDir = "$env:LOCALAPPDATA\cxnet"

Write-Host "cxnet Uninstaller" -ForegroundColor Cyan
Write-Host ""

# Remove binary
if (Test-Path "$installDir\cxnet.exe") {
    Remove-Item "$installDir\cxnet.exe" -Force
    Write-Host "Removed $installDir\cxnet.exe" -ForegroundColor Green
} else {
    Write-Host "Binary not found at $installDir\cxnet.exe"
}

# Remove uninstaller
if (Test-Path "$installDir\cxnet-uninstall.ps1") {
    Remove-Item "$installDir\cxnet-uninstall.ps1" -Force
}

# Remove install dir if empty
if ((Test-Path $installDir) -and (Get-ChildItem $installDir | Measure-Object).Count -eq 0) {
    Remove-Item $installDir -Force
}

# Remove from PATH
$userPath = [Environment]::GetEnvironmentVariable("Path", "User")
if ($userPath -like "*$installDir*") {
    $newPath = ($userPath -split ';' | Where-Object { $_ -ne $installDir }) -join ';'
    [Environment]::SetEnvironmentVariable("Path", $newPath, "User")
    Write-Host ""
    Write-Host "Removed $installDir from PATH" -ForegroundColor Green
}

Write-Host ""
Write-Host "cxnet uninstalled." -ForegroundColor Green
