@echo off
REM Run setup.ps1 using PowerShell
powershell -NoProfile -ExecutionPolicy Bypass -Command "& ([scriptblock]::Create((irm https://github.com/kimmknight/raweb/releases/latest/download/install.ps1))) -AcceptAll"
IF %ERRORLEVEL% NEQ 0 (
    echo setup.ps1 failed.
    exit /b %ERRORLEVEL%
)

REM Copy App_Data contents to C:\inetpub\RAWeb\App_Data
xcopy "%~dp0App_Data\*" "C:\inetpub\RAWeb\App_Data\" /E /I /Y