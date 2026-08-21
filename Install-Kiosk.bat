@echo off
setlocal enabledelayedexpansion
title Self-Checkout Kiosk V2 — Installer
cd /d "%~dp0"

:: -----------------------------------------------------------------------------
:: Check for Administrative Rights and Request Elevation if needed
:: -----------------------------------------------------------------------------
net session >nul 2>&1
if %errorLevel% neq 0 (
    echo =================================================================
    echo    REQUESTING ADMINISTRATOR PRIVILEGES FOR INSTALLATION...
    echo =================================================================
    powershell -Command "Start-Process cmd -ArgumentList '/c `\"%~dp0Install-Kiosk.bat`\"' -Verb RunAs"
    exit /b
)

echo =================================================================
echo        SELF-CHECKOUT KIOSK SYSTEM V2 — WINDOWS INSTALLER         
echo =================================================================
echo.
echo This installer will set up:
echo   - Self-Checkout Kiosk WinUI 3 Application
echo   - Cash Device REST API Bridge (Daemon)
echo   - Offline Node-Locked Machine License
echo   - Desktop and Start Menu Shortcuts
echo   - Windows Firewall Port Configurations
echo.
echo =================================================================
echo.

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Install-Kiosk.ps1"

echo.
pause
