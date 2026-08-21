<#
.SYNOPSIS
    Builds and packages both the Standalone Portable (Unpackaged) and Installer versions
    of the Self-Checkout Kiosk System in Release mode under dist/.
#>

[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Step {
    param([string]$Message)
    Write-Host ">> $Message" -ForegroundColor Cyan
}

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$distRoot = Join-Path $repoRoot "dist"
$portableDir = Join-Path $distRoot "SelfCheckoutKiosk-Portable"
$installerDir = Join-Path $distRoot "SelfCheckoutKiosk-Installer"
$legacyPackageDir = Join-Path $distRoot "SelfCheckoutKiosk-Package"

Write-Host "=================================================================" -ForegroundColor Cyan
Write-Host "       SELF-CHECKOUT KIOSK - FULL RELEASE PACKAGING PIPELINE     " -ForegroundColor Yellow
Write-Host "       Target: $Configuration | Runtime: $Runtime                " -ForegroundColor Cyan
Write-Host "=================================================================" -ForegroundColor Cyan
Write-Host ""

# 1. Stop any running kiosk or daemon processes before packaging
Write-Step "Stopping any active kiosk or simulator instances..."
Get-Process SelfCheckoutKiosk*, CashDeviceSimulator*, GenerateLicense* -ErrorAction SilentlyContinue |
    Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 500

# 2. Clean previous dist staging
Write-Step "Cleaning dist staging directories..."
foreach ($dir in @($portableDir, $installerDir, $legacyPackageDir)) {
    if (Test-Path $dir) {
        Remove-Item -LiteralPath $dir -Recurse -Force -ErrorAction SilentlyContinue
    }
    [IO.Directory]::CreateDirectory($dir) | Out-Null
}

$appProj = Join-Path $repoRoot "src\SelfCheckoutKiosk.App\SelfCheckoutKiosk.App.csproj"
$simProj = Join-Path $repoRoot "tools\CashDeviceSimulator\CashDeviceSimulator.csproj"
$licProj = Join-Path $repoRoot "tools\DevLicenseTokenGenerator\DevLicenseTokenGenerator.csproj"

# 2. Publish KioskApp (Self-Contained WinUI 3 Release)
Write-Step "Publishing SelfCheckoutKiosk.App (Self-Contained $Configuration)..."
$kioskAppDest = Join-Path $portableDir "KioskApp"
& dotnet publish $appProj -c $Configuration -r $Runtime --self-contained `
    -p:Platform=x64 `
    -p:WindowsPackageType=None `
    -p:WindowsAppSDKSelfContained=true `
    -p:PublishTrimmed=false `
    -o $kioskAppDest

if ($LASTEXITCODE -ne 0) { throw "Failed to publish KioskApp." }

# 3. Publish Cash Device API Daemon
Write-Step "Publishing CashDeviceSimulator API Daemon..."
$cashApiDest = Join-Path $portableDir "CashDeviceSimulator-API"
& dotnet publish $simProj -c $Configuration -r $Runtime --self-contained `
    -p:Platform=x64 `
    -p:PublishSingleFile=true `
    -p:PublishTrimmed=false `
    -o $cashApiDest

if ($LASTEXITCODE -ne 0) { throw "Failed to publish CashDeviceSimulator." }

# 4. Publish License Generator Tool
Write-Step "Publishing License Generator CLI and Interactive Tool..."
$licGenDest = Join-Path $portableDir "LicenseGenerator"
& dotnet publish $licProj -c $Configuration -r $Runtime --self-contained `
    -p:Platform=x64 `
    -p:PublishSingleFile=true `
    -p:PublishTrimmed=false `
    -o $licGenDest

if ($LASTEXITCODE -ne 0) { throw "Failed to publish License Generator." }

# 5. Create Standalone / Portable Launchers & Docs in Portable Dir
Write-Step "Assembling Portable package launchers and documents..."

$startKioskWithApiBat = @'
@echo off
setlocal enabledelayedexpansion
title Self-Checkout Kiosk Launcher
cd /d "%~dp0"

echo =================================================================
echo        STARTING SELF-CHECKOUT KIOSK (PORTABLE MODE)              
echo =================================================================
echo.

:: 1. Ensure node-locked license exists
if not exist "%~dp0KioskApp\license.token" (
    echo [SETUP] Generating node-locked license token for this device...
    if exist "%~dp0LicenseGenerator\GenerateLicense.exe" (
        "%~dp0LicenseGenerator\GenerateLicense.exe" --output "%~dp0KioskApp\license.token"
    )
    echo.
)

:: 2. Launch Cash Device REST API Daemon
if exist "%~dp0CashDeviceSimulator-API\CashDeviceSimulator.exe" (
    echo [1/2] Starting Cash Device REST API Daemon (Port 5055/5000)...
    start "Cash Device API Daemon" /min "%~dp0CashDeviceSimulator-API\CashDeviceSimulator.exe"
    timeout /t 2 /nobreak >nul
)

:: 3. Launch SelfCheckout Kiosk UI Application
echo [2/2] Launching Self-Checkout Kiosk Application (Windows x64)...
cd /d "%~dp0KioskApp"
start "" "%~dp0KioskApp\SelfCheckoutKiosk.App.exe"

echo.
echo [INFO] Self-Checkout Kiosk has launched successfully!
'@
[IO.File]::WriteAllText((Join-Path $portableDir "Start-Kiosk-With-API.bat"), $startKioskWithApiBat)

$startKioskBat = @'
@echo off
title Start Self-Checkout Kiosk
cd /d "%~dp0KioskApp"
start "" "%~dp0KioskApp\SelfCheckoutKiosk.App.exe"
'@
[IO.File]::WriteAllText((Join-Path $portableDir "Start-Kiosk.bat"), $startKioskBat)

$startCashApiBat = @'
@echo off
title Start Cash Device API Daemon
cd /d "%~dp0CashDeviceSimulator-API"
"%~dp0CashDeviceSimulator-API\CashDeviceSimulator.exe"
'@
[IO.File]::WriteAllText((Join-Path $portableDir "Start-CashDevice-API.bat"), $startCashApiBat)

$genLicenseBat = @'
@echo off
title Generate Machine License
cd /d "%~dp0"
"%~dp0LicenseGenerator\GenerateLicense.exe" --output "%~dp0KioskApp\license.token"
'@
[IO.File]::WriteAllText((Join-Path $portableDir "Generate-License-For-This-Device.bat"), $genLicenseBat)

# Copy License and Readme to Portable package
if (Test-Path (Join-Path $repoRoot "LICENSE")) {
    Copy-Item -LiteralPath (Join-Path $repoRoot "LICENSE") -Destination (Join-Path $portableDir "LICENSE.txt") -Force
}
if (Test-Path (Join-Path $repoRoot "README.md")) {
    Copy-Item -LiteralPath (Join-Path $repoRoot "README.md") -Destination (Join-Path $portableDir "README.txt") -Force
}

# Also sync to legacy package folder for backward compatibility
Copy-Item -Path (Join-Path $portableDir "*") -Destination $legacyPackageDir -Recurse -Force

# 6. Build the Installer Package in dist/SelfCheckoutKiosk-Installer
Write-Step "Assembling Installer package..."
$installerPackagePayload = Join-Path $installerDir "Package"
[IO.Directory]::CreateDirectory($installerPackagePayload) | Out-Null
Copy-Item -Path (Join-Path $portableDir "*") -Destination $installerPackagePayload -Recurse -Force

# Copy installer engine & launchers into installer root
$installerBat = @'
@echo off
setlocal enabledelayedexpansion
title Self-Checkout Kiosk V2 — Installer
cd /d "%~dp0"

net session >nul 2>&1
if %errorLevel% neq 0 (
    echo =================================================================
    echo    REQUESTING ADMINISTRATOR PRIVILEGES FOR INSTALLATION...
    echo =================================================================
    powershell -Command "Start-Process cmd -ArgumentList '/c `\"%~dp0Install.bat`\"' -Verb RunAs"
    exit /b
)

echo =================================================================
echo        SELF-CHECKOUT KIOSK SYSTEM V2 — WINDOWS INSTALLER         
echo =================================================================
echo.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Installer.ps1"
echo.
pause
'@
[IO.File]::WriteAllText((Join-Path $installerDir "Install.bat"), $installerBat)
[IO.File]::WriteAllText((Join-Path $installerDir "Setup.bat"), $installerBat)

# Copy scripts/Install-Kiosk.ps1 logic into installerDir/Installer.ps1
$installerEngineScript = @'
[CmdletBinding()]
param(
    [string]$InstallPath = 'C:\SelfCheckoutKiosk',
    [switch]$Silent,
    [switch]$AutoStartKiosk,
    [switch]$NoShortcuts,
    [switch]$NoFirewall
)

Write-Host "=================================================================" -ForegroundColor Cyan
Write-Host "         SELF-CHECKOUT KIOSK SYSTEM V2 - INSTALLER               " -ForegroundColor Yellow
Write-Host "         Windows 10 / 11 x64 Offline-First Standalone            " -ForegroundColor Cyan
Write-Host "=================================================================" -ForegroundColor Cyan
Write-Host ""

$payloadDir = Join-Path $PSScriptRoot "Package"
if (-not (Test-Path $payloadDir)) {
    throw "Installer payload package directory missing: $payloadDir"
}

if (-not $Silent) {
    Write-Host "Default Installation Directory: $InstallPath" -ForegroundColor White
    $userPath = Read-Host "Press ENTER to accept or enter custom installation directory"
    if (-not [string]::IsNullOrWhiteSpace($userPath)) {
        $InstallPath = $userPath.Trim()
    }
    Write-Host ""
}

$InstallPath = [IO.Path]::GetFullPath($InstallPath)
Write-Host "[1/5] Preparing installation target: $InstallPath" -ForegroundColor Cyan
if (-not (Test-Path $InstallPath)) {
    [IO.Directory]::CreateDirectory($InstallPath) | Out-Null
}

Write-Host "[2/5] Copying application files..." -ForegroundColor Cyan
Copy-Item -Path (Join-Path $payloadDir "*") -Destination $InstallPath -Recurse -Force

Write-Host "[3/5] Generating node-locked license token for this machine..." -ForegroundColor Cyan
$generatorExe = Join-Path $InstallPath "LicenseGenerator\GenerateLicense.exe"
$targetLicenseToken = Join-Path $InstallPath "KioskApp\license.token"
if (Test-Path $generatorExe) {
    try {
        & $generatorExe --output $targetLicenseToken | Out-Null
        if (Test-Path $targetLicenseToken) {
            Copy-Item -LiteralPath $targetLicenseToken -Destination (Join-Path $InstallPath "license.token") -Force
            Write-Host "      [OK] License token activated for this machine." -ForegroundColor Green
        }
    } catch {
        Write-Host "      [WARNING] License token auto-generation warning: $_" -ForegroundColor Yellow
    }
}

Write-Host "[4/5] Creating Desktop and Start Menu shortcuts..." -ForegroundColor Cyan
if (-not $NoShortcuts) {
    $wscriptShell = New-Object -ComObject WScript.Shell
    $appIconPath = Join-Path $InstallPath "KioskApp\Assets\Logo\ca.ico"
    if (-not (Test-Path $appIconPath)) {
        $appIconPath = Join-Path $InstallPath "KioskApp\SelfCheckoutKiosk.App.exe"
    }

    # Desktop Shortcut
    $desktopFolder = [Environment]::GetFolderPath([Environment+SpecialFolder]::DesktopDirectory)
    $desktopShortcutPath = Join-Path $desktopFolder "Self-Checkout Kiosk.lnk"
    $shortcut = $wscriptShell.CreateShortcut($desktopShortcutPath)
    $shortcut.TargetPath = Join-Path $InstallPath "Start-Kiosk-With-API.bat"
    $shortcut.WorkingDirectory = $InstallPath
    $shortcut.IconLocation = "$appIconPath,0"
    $shortcut.Description = "Launch Self-Checkout Kiosk with Cash API Bridge"
    $shortcut.Save()
    Write-Host "      [OK] Desktop shortcut created: $desktopShortcutPath" -ForegroundColor Green

    # Start Menu
    $startMenuPrograms = [Environment]::GetFolderPath([Environment+SpecialFolder]::Programs)
    $kioskStartFolder = Join-Path $startMenuPrograms "Self-Checkout Kiosk"
    if (-not (Test-Path $kioskStartFolder)) { [IO.Directory]::CreateDirectory($kioskStartFolder) | Out-Null }

    $smMain = $wscriptShell.CreateShortcut((Join-Path $kioskStartFolder "Self-Checkout Kiosk.lnk"))
    $smMain.TargetPath = Join-Path $InstallPath "Start-Kiosk-With-API.bat"
    $smMain.WorkingDirectory = $InstallPath
    $smMain.IconLocation = "$appIconPath,0"
    $smMain.Description = "Launch Self-Checkout Kiosk"
    $smMain.Save()

    $smLicense = $wscriptShell.CreateShortcut((Join-Path $kioskStartFolder "Regenerate Machine License.lnk"))
    $smLicense.TargetPath = (Join-Path $InstallPath "LicenseGenerator\GenerateLicense.exe")
    $smLicense.WorkingDirectory = (Join-Path $InstallPath "LicenseGenerator")
    $smLicense.Description = "Generate or refresh node-locked license token"
    $smLicense.Save()

    Write-Host "      [OK] Start Menu folder created: $kioskStartFolder" -ForegroundColor Green
}

Write-Host "[5/5] Configuring Windows Firewall..." -ForegroundColor Cyan
if (-not $NoFirewall) {
    try {
        netsh advfirewall firewall add rule name="SelfCheckout CashAPI (Port 5055)" dir=in action=allow protocol=TCP localport=5055 profile=any | Out-Null
        netsh advfirewall firewall add rule name="SelfCheckout CashAPI (Port 5000)" dir=in action=allow protocol=TCP localport=5000 profile=any | Out-Null
        Write-Host "      [OK] Firewall rules configured." -ForegroundColor Green
    } catch {}
}

Write-Host ""
Write-Host "=================================================================" -ForegroundColor Green
Write-Host "       INSTALLATION COMPLETED SUCCESSFULLY!                      " -ForegroundColor Green
Write-Host "=================================================================" -ForegroundColor Green
Write-Host "Application Directory : $InstallPath" -ForegroundColor White
Write-Host "Main Launcher         : $InstallPath\Start-Kiosk-With-API.bat" -ForegroundColor White
Write-Host ""
'@
[IO.File]::WriteAllText((Join-Path $installerDir "Installer.ps1"), $installerEngineScript)

if (Test-Path (Join-Path $repoRoot "LICENSE")) {
    Copy-Item -LiteralPath (Join-Path $repoRoot "LICENSE") -Destination (Join-Path $installerDir "LICENSE.txt") -Force
}
if (Test-Path (Join-Path $repoRoot "README.md")) {
    Copy-Item -LiteralPath (Join-Path $repoRoot "README.md") -Destination (Join-Path $installerDir "README.txt") -Force
}

Write-Host ""
Write-Host "=================================================================" -ForegroundColor Green
Write-Host "       PACKAGING COMPLETED SUCCESSFULLY!                         " -ForegroundColor Green
Write-Host "=================================================================" -ForegroundColor Green
Write-Host ""
Write-Host "1. Portable (Unpackaged) Version:" -ForegroundColor Yellow
Write-Host "   Path: $portableDir" -ForegroundColor White
Write-Host "   Usage: Copy folder anywhere and run 'Start-Kiosk-With-API.bat'" -ForegroundColor Gray
Write-Host ""
Write-Host "2. Installer Version:" -ForegroundColor Yellow
Write-Host "   Path: $installerDir" -ForegroundColor White
Write-Host "   Usage: Run 'Install.bat' or 'Setup.bat' as Administrator" -ForegroundColor Gray
Write-Host ""
