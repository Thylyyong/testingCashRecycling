@echo off
setlocal enabledelayedexpansion
title Build Self-Checkout Kiosk Deployment Package
cd /d "%~dp0"

echo =================================================================
echo        BUILDING SELF-CHECKOUT KIOSK DEPLOYMENT PACKAGE           
echo =================================================================
echo.

powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "& '%~dp0scripts\Install-Kiosk.ps1' -BuildFromSource -NoShortcuts -NoFirewall -InstallPath '%~dp0dist\SelfCheckoutKiosk-Package'"

echo.
echo =================================================================
echo Package ready at: %~dp0dist\SelfCheckoutKiosk-Package
echo =================================================================
echo.
pause
