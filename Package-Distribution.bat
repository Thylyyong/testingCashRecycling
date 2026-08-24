@echo off
setlocal
title Self-Checkout Kiosk - Standalone Packaging Tool
cd /d "%~dp0"

echo =================================================================
echo        PACKAGING SELF-CHECKOUT KIOSK V2 STANDALONE               
echo =================================================================
echo.

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Package-Distribution.ps1" -NoPause %*
set EXIT_CODE=%ERRORLEVEL%

echo.
if %EXIT_CODE% equ 0 (
    echo =================================================================
    echo  [SUCCESS] Packaging finished successfully!
    echo =================================================================
) else (
    echo =================================================================
    echo  [ERROR] Packaging failed with exit code %EXIT_CODE%!
    echo =================================================================
)
echo.
echo Press any key to exit...
pause >nul
exit /b %EXIT_CODE%
