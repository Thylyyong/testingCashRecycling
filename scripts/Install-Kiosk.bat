@echo off
setlocal enabledelayedexpansion
title Self-Checkout Kiosk V2 — Installer
cd /d "%~dp0.."

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
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-Kiosk.ps1"
echo.
pause
