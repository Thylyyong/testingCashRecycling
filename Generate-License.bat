@echo off
title Generate Machine License Token
cd /d "%~dp0"
echo =================================================================
echo        GENERATING OFFLINE LICENSE TOKEN FOR THIS DEVICE          
echo =================================================================
echo.

if exist "%~dp0dist\SelfCheckoutKiosk-Package\LicenseGenerator\GenerateLicense.exe" (
    "%~dp0dist\SelfCheckoutKiosk-Package\LicenseGenerator\GenerateLicense.exe" --output "%~dp0license.token"
) else if exist "%~dp0tools\DevLicenseTokenGenerator\bin\Debug\net10.0\GenerateLicense.exe" (
    "%~dp0tools\DevLicenseTokenGenerator\bin\Debug\net10.0\GenerateLicense.exe" --output "%~dp0license.token"
) else (
    echo Building and running license generator...
    dotnet run --project "%~dp0tools\DevLicenseTokenGenerator\DevLicenseTokenGenerator.csproj" -- --output "%~dp0license.token"
)

:: Also copy to KioskApp folder if present
if exist "%~dp0dist\SelfCheckoutKiosk-Package\KioskApp" (
    copy /y "%~dp0license.token" "%~dp0dist\SelfCheckoutKiosk-Package\KioskApp\license.token" >nul
)
if exist "%~dp0src\SelfCheckoutKiosk.App" (
    copy /y "%~dp0license.token" "%~dp0src\SelfCheckoutKiosk.App\license.token" >nul
)

echo.
echo [DONE] license.token generated and copied to required locations.
echo.
