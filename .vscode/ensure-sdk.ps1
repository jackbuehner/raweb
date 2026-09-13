$ErrorActionPreference = "Stop"

$globalJsonPath = Join-Path $PSScriptRoot "..\global.json"
if (-not (Test-Path $globalJsonPath)) { exit 0 }

$targetVersion = (Get-Content $globalJsonPath -Raw | ConvertFrom-Json).sdk.version
$localDotnet = Join-Path $PSScriptRoot "..\.dotnet\dotnet.exe"

$needsInstall = $true
$currentVersion = "none"

if (Test-Path $localDotnet) {
    $currentVersion = (& $localDotnet --version 2>$null).Trim()
    if ($currentVersion -eq $targetVersion) {
        $needsInstall = $false
    }
}

if ($needsInstall) {
    Write-Host "[SDK Auto-Install] Target version '$targetVersion' missing or mismatched (found '$currentVersion'). Installing to .dotnet..." -ForegroundColor Yellow
    
    $scriptPath = Join-Path $env:TEMP "dotnet-install.ps1"
    Invoke-WebRequest -Uri "https://dot.net/v1/dotnet-install.ps1" -OutFile $scriptPath
    
    & $scriptPath -Version $targetVersion -InstallDir (Join-Path $PSScriptRoot "..\.dotnet")
    Remove-Item $scriptPath -Force
}
