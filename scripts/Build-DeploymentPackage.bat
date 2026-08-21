@echo off
setlocal enabledelayedexpansion
title Build Self-Checkout Kiosk Deployment Packages (Portable + Installer)
cd /d "%~dp0.."

echo =================================================================
echo   BUILDING SELF-CHECKOUT KIOSK PACKAGES (PORTABLE + INSTALLER)   
echo =================================================================
echo.

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Build-DeploymentPackage.ps1" -Configuration Release -Runtime win-x64

echo.
pause
