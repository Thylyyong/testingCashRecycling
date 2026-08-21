@echo off
title Self-Checkout Kiosk - Hardware & Environment Diagnostic
color 0B
cd /d "%~dp0"

echo ======================================================================
echo          SELF-CHECKOUT KIOSK V2 - HARDWARE & SYSTEM DIAGNOSTIC
echo ======================================================================
echo.

:: 1. Check Windows Version & Architecture
echo [1/6] Checking Operating System Architecture...
echo       OS: %OS% (%PROCESSOR_ARCHITECTURE%)
echo.

:: 2. Check .NET 8.0 Runtime (Required for ITL CashDevice-RestAPI.exe)
echo [2/6] Checking .NET Runtimes (for Cash Recycler API)...
dotnet --list-runtimes 2>nul | findstr /i "Microsoft.AspNetCore.App 8.0" >nul
if %errorlevel% equ 0 (
    echo       [PASS] .NET 8.0 ASP.NET Core Runtime is INSTALLED.
) else (
    echo       [WARNING] .NET 8.0 ASP.NET Core Runtime is NOT INSTALLED or 'dotnet' not in PATH!
    echo                 Innovative Technology's CashDevice-RestAPI.exe requires .NET 8.0.
    echo                 If physical cash acceptor fails, install ASP.NET Core 8.0 Runtime:
    echo                 https://dotnet.microsoft.com/download/dotnet/8.0
)
echo.

:: 3. Check System Serial COM Ports (Cash Recycler & Barcode Scanner)
echo [3/6] Enumerating Windows Serial COM Ports (Device Manager)...
powershell -NoProfile -Command "Get-CimInstance Win32_PnPEntity | Where-Object { $_.PNPClass -eq 'Ports' -or $_.Name -like '*COM*' -or $_.Name -like '*Serial*' } | Select-Object Name, DeviceID | Format-Table -AutoSize"
echo.

:: 4. Check Cash API Binary Presence
echo [4/6] Checking Cash API Files in CashAPI\ ...
if exist "%~dp0..\dist\SelfCheckoutKiosk\CashAPI\CashDevice-RestAPI.exe" (
    echo       [PASS] Physical ITL Cash API found: dist\SelfCheckoutKiosk\CashAPI\CashDevice-RestAPI.exe
) else if exist "%~dp0..\CashDevice-REST-API-V1.6.1-RC.4-Net8.0\CashDevice-RestAPI.exe" (
    echo       [PASS] Physical ITL Cash API found: CashDevice-REST-API-V1.6.1-RC.4-Net8.0\CashDevice-RestAPI.exe
) else (
    echo       [FAIL] CashDevice-RestAPI.exe NOT found!
)
echo.

:: 5. Check Offline License Token
echo [5/6] Checking License Token...
if exist "%~dp0..\dist\SelfCheckoutKiosk\license.token" (
    echo       [PASS] license.token exists in dist\SelfCheckoutKiosk.
) else (
    echo       [INFO] license.token missing in dist. Run Generate-License.bat to create one.
)
echo.

:: 6. Summary & Recommendations
echo [6/6] Diagnostic Summary:
echo       - If Cash Acceptor says 'Offline / No Cash Machine Connected':
echo         a) Check if ITL USB cable is plugged in.
echo         b) Check if ITL 12V/24V power supply is connected (LEDs on acceptor lit).
echo         c) Check if the ITL USB-SSP Driver is installed in Device Manager.
echo         d) Ensure .NET 8.0 Desktop + ASP.NET Core Runtime is installed on this PC.
echo.
echo ======================================================================
pause
