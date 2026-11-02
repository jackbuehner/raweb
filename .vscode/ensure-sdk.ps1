$ErrorActionPreference = "Stop"

$globalJsonPath = Join-Path $PSScriptRoot "..\global.json"
if (-not (Test-Path $globalJsonPath)) { exit 0 }

$targetVersion = (Get-Content $globalJsonPath -Raw | ConvertFrom-Json).sdk.version
$dotnetDir     = Join-Path $PSScriptRoot "..\.dotnet"
$localDotnet   = Join-Path $dotnetDir "dotnet.exe"
$targetSdkDir  = Join-Path $dotnetDir "sdk\$targetVersion"

Write-Host "[SDK Auto-Install] Target version: $targetVersion" -ForegroundColor Cyan
Write-Host "[SDK Auto-Install] Local dotnet path: $localDotnet" -ForegroundColor Cyan

$installedVersions = @()
$sdkRoot = Join-Path $dotnetDir "sdk"
if (Test-Path $sdkRoot) {
    $installedVersions = @(Get-ChildItem $sdkRoot -Directory | Select-Object -ExpandProperty Name)
}
$installedDisplay = if ($installedVersions.Count) { $installedVersions -join ", " } else { "none" }

$needsInstall = -not ((Test-Path $localDotnet) -and (Test-Path (Join-Path $targetSdkDir "dotnet.dll")))

Write-Host "[SDK Auto-Install] Installed SDKs: $installedDisplay" -ForegroundColor Cyan
Write-Host "[SDK Auto-Install] Needs install: $needsInstall" -ForegroundColor Cyan

if ($needsInstall) {
    Write-Host "[SDK Auto-Install] Target version '$targetVersion' not installed (found: $installedDisplay). Installing to .dotnet..." -ForegroundColor Yellow

    $scriptPath = Join-Path $env:TEMP "dotnet-install.ps1"
    Invoke-WebRequest -Uri "https://dot.net/v1/dotnet-install.ps1" -OutFile $scriptPath -UseBasicParsing

    try {
        & $scriptPath -Version $targetVersion -InstallDir $dotnetDir
    }
    finally {
        Remove-Item $scriptPath -Force -ErrorAction SilentlyContinue
    }
}
