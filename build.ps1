<#
    Builds the KeeAutoUpdate plugin in Release mode.
    Usage: .\build.ps1 [-KeePassDir "C:\path\to\KeePass"]
#>
param(
    [string]$KeePassDir = "C:\Program Files\KeePass Password Safe 2"
)

$ErrorActionPreference = "Stop"
$proj = Join-Path $PSScriptRoot "src\KeeAutoUpdate\KeeAutoUpdate.csproj"

dotnet build $proj -c Release "-p:KeePassDir=$KeePassDir"
