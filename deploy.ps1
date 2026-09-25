<#
    Builds and copies KeeAutoUpdate.dll into the KeePass Plugins folder.
    KeePass must be closed for the copy to succeed (it locks the DLL while running),
    and writing into Program Files requires an elevated (Run as Administrator) prompt.

    Usage: right click this file -> "Run with PowerShell", or from an elevated
    PowerShell prompt: .\deploy.ps1
#>
param(
    [string]$KeePassDir = "C:\Program Files\KeePass Password Safe 2"
)

$ErrorActionPreference = "Stop"

& (Join-Path $PSScriptRoot "build.ps1") -KeePassDir $KeePassDir

$src = Join-Path $PSScriptRoot "src\KeeAutoUpdate\bin\Release\net472\KeeAutoUpdate.dll"
$destDir = Join-Path $KeePassDir "Plugins"
$dest = Join-Path $destDir "KeeAutoUpdate.dll"

if (-not (Test-Path $destDir)) {
    New-Item -ItemType Directory -Path $destDir | Out-Null
}

Copy-Item -Path $src -Destination $dest -Force
Write-Host "Deployed to $dest" -ForegroundColor Green
Write-Host "Start KeePass and check the Tools menu for 'KeeAutoUpdate'." -ForegroundColor Green
