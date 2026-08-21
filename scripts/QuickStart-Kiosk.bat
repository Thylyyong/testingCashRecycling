@echo off
setlocal enabledelayedexpansion
title Self-Checkout Kiosk — Quick Start
cd /d "%~dp0.."

echo =================================================================
echo        QUICK START: SELF-CHECKOUT KIOSK SYSTEM                   
echo =================================================================
echo.

:: 1. Ensure node-locked license exists
if not exist "%~dp0..\license.token" (
    echo [1/3] Generating node-locked license token for this machine...
    call "%~dp0Generate-License.bat"
) else (
    echo [1/3] Machine license token found: %~dp0..\license.token
)

:: 2. Launch Cash Device API Simulator / Bridge if present
echo [2/3] Checking Cash Device API Daemon...
if exist "%~dp0..\dist\SelfCheckoutKiosk-Portable\CashDeviceSimulator-API\CashDeviceSimulator.exe" (
    start "Cash Device API Daemon" /min "%~dp0..\dist\SelfCheckoutKiosk-Portable\CashDeviceSimulator-API\CashDeviceSimulator.exe"
    timeout /t 2 /nobreak >nul
) else if exist "%~dp0..\tools\CashDeviceSimulator\bin\Debug\net10.0\CashDeviceSimulator.exe" (
    start "Cash Device API Daemon" /min "%~dp0..\tools\CashDeviceSimulator\bin\Debug\net10.0\CashDeviceSimulator.exe"
    timeout /t 2 /nobreak >nul
) else (
    echo       [NOTE] Running Cash API via dotnet CLI...
    start "Cash Device API Daemon" /min dotnet run --project "%~dp0..\tools\CashDeviceSimulator\CashDeviceSimulator.csproj"
    timeout /t 2 /nobreak >nul
)

:: 3. Launch Self-Checkout Kiosk WinUI 3 Application
echo [3/3] Launching Self-Checkout Kiosk WinUI 3 Application...
if exist "%~dp0..\dist\SelfCheckoutKiosk-Portable\KioskApp\SelfCheckoutKiosk.App.exe" (
    cd /d "%~dp0..\dist\SelfCheckoutKiosk-Portable\KioskApp"
    start "" "%~dp0..\dist\SelfCheckoutKiosk-Portable\KioskApp\SelfCheckoutKiosk.App.exe"
) else (
    cd /d "%~dp0..\src\SelfCheckoutKiosk.App"
    start "" dotnet run --project "%~dp0..\src\SelfCheckoutKiosk.App\SelfCheckoutKiosk.App.csproj" -p:WindowsPackageType=None
)

echo.
echo [SUCCESS] Self-Checkout Kiosk launched!
echo.
