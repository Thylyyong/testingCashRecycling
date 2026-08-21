@echo off
title Generate Machine License Token
cd /d "%~dp0.."
echo =================================================================
echo        GENERATING OFFLINE LICENSE TOKEN FOR THIS DEVICE          
echo =================================================================
echo.

if exist "%~dp0..\dist\SelfCheckoutKiosk-Portable\LicenseGenerator\GenerateLicense.exe" (
    "%~dp0..\dist\SelfCheckoutKiosk-Portable\LicenseGenerator\GenerateLicense.exe" --output "%~dp0..\license.token"
) else if exist "%~dp0..\dist\SelfCheckoutKiosk-Package\LicenseGenerator\GenerateLicense.exe" (
    "%~dp0..\dist\SelfCheckoutKiosk-Package\LicenseGenerator\GenerateLicense.exe" --output "%~dp0..\license.token"
) else if exist "%~dp0..\tools\DevLicenseTokenGenerator\bin\x64\Release\net10.0\win-x64\GenerateLicense.exe" (
    "%~dp0..\tools\DevLicenseTokenGenerator\bin\x64\Release\net10.0\win-x64\GenerateLicense.exe" --output "%~dp0..\license.token"
) else if exist "%~dp0..\tools\DevLicenseTokenGenerator\bin\Debug\net10.0\GenerateLicense.exe" (
    "%~dp0..\tools\DevLicenseTokenGenerator\bin\Debug\net10.0\GenerateLicense.exe" --output "%~dp0..\license.token"
) else (
    echo Building and running license generator...
    dotnet run --project "%~dp0..\tools\DevLicenseTokenGenerator\DevLicenseTokenGenerator.csproj" -- --output "%~dp0..\license.token"
)

:: Also copy to KioskApp folders if present
if exist "%~dp0..\dist\SelfCheckoutKiosk-Portable\KioskApp" (
    copy /y "%~dp0..\license.token" "%~dp0..\dist\SelfCheckoutKiosk-Portable\KioskApp\license.token" >nul
)
if exist "%~dp0..\src\SelfCheckoutKiosk.App" (
    copy /y "%~dp0..\license.token" "%~dp0..\src\SelfCheckoutKiosk.App\license.token" >nul
)

echo.
echo [DONE] license.token generated and copied to required locations.
echo.
