<#
.SYNOPSIS
    Builds and packages SelfCheckoutKiosk into two clean distributions:
      dist\SelfCheckoutKiosk-Portable   - copy anywhere, double-click Start-Kiosk.bat
      dist\SelfCheckoutKiosk-Installer  - run Install.bat as admin

.DESCRIPTION
    - Clears dist\ completely before each build
    - Publishes self-contained win-x64 KioskApp
    - Publishes CashDeviceSimulator and drops it inside app\ (next to the .exe)
      so the kiosk finds it automatically, exactly like pressing F5 in the IDE
    - Auto-generates a universal license.token (no hardware locking, works everywhere)
    - Creates a minimal Start-Kiosk.bat: cd into app\, launch the .exe, done
    - The app itself manages starting/stopping CashDeviceSimulator in the background
    - Writes a plain-text deployment README
#>

[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$Runtime       = 'win-x64'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Step { param([string]$Msg) Write-Host ""; Write-Host ">> $Msg" -ForegroundColor Cyan }
function Write-Ok   { param([string]$Msg) Write-Host "   [OK] $Msg" -ForegroundColor Green }
function Write-Warn { param([string]$Msg) Write-Host "   [!!] $Msg" -ForegroundColor Yellow }

$repoRoot    = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$distRoot    = Join-Path $repoRoot "dist"
$portableDir = Join-Path $distRoot "SelfCheckoutKiosk-Portable"
$installerDir = Join-Path $distRoot "SelfCheckoutKiosk-Installer"

$appProj = Join-Path $repoRoot "src\SelfCheckoutKiosk.App\SelfCheckoutKiosk.App.csproj"
$simProj = Join-Path $repoRoot "tools\CashDeviceSimulator\CashDeviceSimulator.csproj"
$licProj = Join-Path $repoRoot "tools\DevLicenseTokenGenerator\DevLicenseTokenGenerator.csproj"

Write-Host ""
Write-Host "=================================================================" -ForegroundColor Cyan
Write-Host "   SELF-CHECKOUT KIOSK -- RELEASE PACKAGING" -ForegroundColor Yellow
Write-Host "   Config: $Configuration | Runtime: $Runtime" -ForegroundColor Cyan
Write-Host "=================================================================" -ForegroundColor Cyan

# ─── STEP 1: Kill any running processes, wipe dist\ completely ───────────────
Write-Step "Killing running instances and clearing dist\ ..."
Get-Process SelfCheckoutKiosk*, CashDeviceSimulator*, GenerateLicense* -ErrorAction SilentlyContinue |
    Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 500

if (Test-Path $distRoot) {
    Remove-Item -LiteralPath $distRoot -Recurse -Force -ErrorAction SilentlyContinue
}
[IO.Directory]::CreateDirectory($portableDir) | Out-Null
[IO.Directory]::CreateDirectory($installerDir) | Out-Null
Write-Ok "dist\ cleared and recreated fresh"

# ─── STEP 2: Publish KioskApp ────────────────────────────────────────────────
Write-Step "Publishing SelfCheckoutKiosk.App (self-contained $Configuration)..."
$appDest = Join-Path $portableDir "app"
& dotnet publish $appProj `
    -c $Configuration -r $Runtime --self-contained `
    -p:Platform=x64 `
    -p:WindowsPackageType=None `
    -p:PublishTrimmed=false `
    -o $appDest

if ($LASTEXITCODE -ne 0) { throw "KioskApp publish failed." }

# Ensure WinUI 3 PRI resource index is present
$appBinDir    = Join-Path $repoRoot "src\SelfCheckoutKiosk.App\bin\x64\$Configuration\net10.0-windows10.0.19041.0\win-x64"
$appPriSource = Join-Path $appBinDir "SelfCheckoutKiosk.App.pri"
if (-not (Test-Path $appPriSource)) {
    $appPriSource = Join-Path $repoRoot "src\SelfCheckoutKiosk.App\bin\x64\$Configuration\net10.0-windows10.0.19041.0\SelfCheckoutKiosk.App.pri"
}
if (Test-Path $appPriSource) {
    Copy-Item -LiteralPath $appPriSource -Destination (Join-Path $appDest "SelfCheckoutKiosk.App.pri") -Force
    Copy-Item -LiteralPath $appPriSource -Destination (Join-Path $appDest "resources.pri") -Force
    Write-Ok "WinUI 3 resources.pri bundled"
}
Write-Ok "KioskApp published to app\"

# ─── STEP 3: Publish CashDeviceSimulator, drop it inside app\ ────────────────
#
#  The simulator is the FALLBACK for machines without physical hardware.
#  The real ITL CashDevice-RestAPI is bundled separately in Step 3b below.
#
Write-Step "Publishing CashDeviceSimulator (fallback for testing)..."
$simStage = Join-Path $env:TEMP ("CashSimStage_" + [guid]::NewGuid().ToString("N"))
[IO.Directory]::CreateDirectory($simStage) | Out-Null
& dotnet publish $simProj `
    -c $Configuration -r $Runtime --self-contained `
    -p:Platform=x64 `
    -p:PublishSingleFile=true `
    -p:PublishTrimmed=false `
    -o $simStage

if ($LASTEXITCODE -ne 0) { throw "CashDeviceSimulator publish failed." }

$simExe = Join-Path $simStage "CashDeviceSimulator.exe"
if (Test-Path $simExe) {
    Copy-Item -LiteralPath $simExe -Destination (Join-Path $appDest "CashDeviceSimulator.exe") -Force
    Write-Ok "CashDeviceSimulator.exe placed inside app\ (simulator fallback)"
} else {
    Write-Warn "CashDeviceSimulator.exe not found -- cash will fall back to dotnet run"
}
Remove-Item -LiteralPath $simStage -Recurse -Force -ErrorAction SilentlyContinue

# ─── STEP 3b: Bundle real ITL CashDevice-RestAPI if available ────────────────
#
#  Searches well-known locations for the real ITL REST API package.
#  When found, copies the entire folder into app\ so the published release
#  behaves identically to pressing F5 on the dev machine with hardware.
#
#  CashApiProcessManager priority:  CashDevice-RestAPI.exe  >  CashDeviceSimulator.exe
#  So dropping it into app\ is all that's needed — no config required.
#
Write-Step "Looking for real ITL CashDevice-RestAPI to bundle..."
$userProfile = [Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)
$itlSearchPaths = @(
    (Join-Path $userProfile "Desktop\CA\CashDevice-REST-API-V1.6.1-RC.4-Net8.0 1\CashDevice-REST-API-V1.6.1-RC.4-Net8.0"),
    (Join-Path $userProfile "Desktop\CashDevice-REST-API-V1.6.1-RC.4-Net8.0"),
    (Join-Path $userProfile "Downloads\CashDevice-REST-API-V1.6.1-RC.4-Net8.0"),
    "C:\ITL device\ITL sdk package\CashDevice-REST-API-V1.6.1-RC.4-Net8.0",
    "F:\ITL device\ITL sdk package\CashDevice-REST-API-V1.6.1-RC.4-Net8.0",
    "D:\ITL device\ITL sdk package\CashDevice-REST-API-V1.6.1-RC.4-Net8.0"
)

$itlApiFound = $false
foreach ($itlPath in $itlSearchPaths) {
    $itlExe = Join-Path $itlPath "CashDevice-RestAPI.exe"
    if (Test-Path $itlExe) {
        Write-Host "   Found ITL API at: $itlPath" -ForegroundColor Cyan
        # Copy the entire ITL API folder contents into app\
        # The exe and all its DLLs/configs go side-by-side with SelfCheckoutKiosk.App.exe
        Copy-Item -Path "$itlPath\*" -Destination $appDest -Recurse -Force
        Write-Ok "Real ITL CashDevice-RestAPI bundled into app\ -- physical hardware ENABLED"
        $itlApiFound = $true
        break
    }
}

if (-not $itlApiFound) {
    Write-Warn "ITL CashDevice-RestAPI not found on this machine -- only simulator will be available"
    Write-Host "   To enable real hardware: copy CashDevice-RestAPI folder contents into app\" -ForegroundColor Gray
}



# ─── STEP 4: Auto-generate universal license.token ───────────────────────────
#
#  Generates with HardwareId="*" -- works on every machine, no node-locking.
#  Copies into app\ (so the kiosk finds it on startup) and into the portable
#  root (backup / reference copy).
#
Write-Step "Generating universal license.token..."
$licStage = Join-Path $env:TEMP ("LicStage_" + [guid]::NewGuid().ToString("N"))
[IO.Directory]::CreateDirectory($licStage) | Out-Null
& dotnet publish $licProj `
    -c $Configuration -r $Runtime --self-contained `
    -p:Platform=x64 `
    -p:PublishSingleFile=true `
    -p:PublishTrimmed=false `
    -o $licStage

$licExe   = Join-Path $licStage "GenerateLicense.exe"
$licToken = Join-Path $portableDir "license.token"

if ($LASTEXITCODE -eq 0 -and (Test-Path $licExe)) {
    & $licExe --universal --quiet --output $licToken 2>&1 | Out-Null
    if (Test-Path $licToken) {
        Copy-Item -LiteralPath $licToken -Destination (Join-Path $appDest "license.token") -Force
        Write-Ok "Universal license.token generated and placed in app\ and portable root"
    } else {
        Write-Warn "GenerateLicense.exe ran but produced no file -- kiosk will auto-unlock at runtime"
    }
    Remove-Item -LiteralPath $licStage -Recurse -Force -ErrorAction SilentlyContinue
} else {
    Write-Warn "License generator build failed -- kiosk will auto-unlock at runtime"
    if (Test-Path $licStage) { Remove-Item -LiteralPath $licStage -Recurse -Force -ErrorAction SilentlyContinue }
}

# ─── STEP 5: Write Start-Kiosk.bat ───────────────────────────────────────────
#
#  Three lines. Identical to pressing F5:
#    1. cd into app\ (sets AppContext.BaseDirectory correctly)
#    2. launch SelfCheckoutKiosk.App.exe
#  The app automatically starts CashDeviceSimulator.exe in the background
#  via CashApiProcessManager.EnsureCashApiRunningAsync(). No bat script
#  needs to manage the API process.
#
Write-Step "Writing Start-Kiosk.bat..."
$startBat = '@echo off' + "`r`n" +
            'cd /d "%~dp0app"' + "`r`n" +
            'start "" "SelfCheckoutKiosk.App.exe"' + "`r`n"
[IO.File]::WriteAllText((Join-Path $portableDir "Start-Kiosk.bat"), $startBat)
Write-Ok "Start-Kiosk.bat written (3 lines, same behaviour as F5)"

# ─── STEP 6: Write README-DEPLOYMENT.txt ─────────────────────────────────────
Write-Step "Writing README-DEPLOYMENT.txt..."
$readme = @'
SELF-CHECKOUT KIOSK V2 -- DEPLOYMENT GUIDE
===========================================

QUICK START (PORTABLE)
-----------------------
1. Copy the whole "SelfCheckoutKiosk-Portable" folder to any
   Windows 10/11 x64 PC (USB drive, network share, etc.)
2. Double-click  Start-Kiosk.bat
   Done.

What happens when you double-click Start-Kiosk.bat:
  - Changes directory into app\
  - Launches SelfCheckoutKiosk.App.exe
  - The app checks for a cash API on port 5000. If nothing is running:
      * If CashDevice-RestAPI.exe is in app\   -> starts real ITL API  (HARDWARE)
      * Otherwise CashDeviceSimulator.exe      -> starts simulator     (TESTING)
  - When you close the kiosk window the cash API shuts down too
  This is exactly the same as pressing F5 in Visual Studio.

LICENSE
--------
A pre-generated universal license.token is already included.
It works on every machine -- no action needed, no hardware locking.

FOLDER STRUCTURE
-----------------
SelfCheckoutKiosk-Portable\
  app\                          <- Everything the kiosk needs
    SelfCheckoutKiosk.App.exe   <- Main kiosk (WinUI 3)
    CashDeviceSimulator.exe     <- Cash REST API fallback (simulator/testing)
    license.token               <- Universal license (ready to go)
    Assets\                     <- Images, fonts, media
    ...                         <- Runtime DLLs
  Start-Kiosk.bat               <- Double-click to launch
  license.token                 <- Backup copy of the license
  README-DEPLOYMENT.txt         <- This file

REAL PHYSICAL CASH MACHINE (ITL NV200 / NV400 / SmartPayout)
--------------------------------------------------------------
  The app AUTOMATICALLY uses real hardware if CashDevice-RestAPI.exe
  is placed inside app\. The simulator is only used as a fallback.

  To activate real hardware:
  1. Copy CashDevice-RestAPI.exe (from your ITL SDK package) into app\
  2. Connect the USB-Serial cable for the cash machine
  3. Double-click Start-Kiosk.bat as normal

  Priority order (first found wins):
    app\CashDevice-RestAPI.exe    <- REAL HARDWARE (use this for production)
    app\CashDeviceSimulator.exe   <- Simulator fallback (testing only)

  NOTE: CashDevice-RestAPI.exe is NOT included in this package because
  it is proprietary ITL hardware software. Get it from your ITL SDK.

DEPLOYING TO ANOTHER PC
-------------------------
  1. Copy SelfCheckoutKiosk-Portable to the target machine
  2. Double-click Start-Kiosk.bat
  No reinstallation, no license regeneration needed.

INSTALLER EDITION
------------------
  Run Install.bat as Administrator.
  Default install path: C:\SelfCheckoutKiosk
  Creates a Desktop shortcut automatically.

LOGS
-----
  app\startup_crash.log    <- Startup / crash errors
  app\hardware_audit.log   <- Cash and hardware audit trail
'@
[IO.File]::WriteAllText((Join-Path $portableDir "README-DEPLOYMENT.txt"), $readme)
Write-Ok "README-DEPLOYMENT.txt written"

# ─── STEP 7: Assemble Installer ──────────────────────────────────────────────
Write-Step "Assembling Installer package..."

# Installer payload = everything in portable
$installerPayload = Join-Path $installerDir "payload"
Copy-Item -Path "$portableDir\*" -Destination $installerPayload -Recurse -Force

# Install.bat -- self-elevates then calls Installer.ps1
$installBat = '@echo off' + "`r`n" +
              'net session >nul 2>&1' + "`r`n" +
              'if %errorLevel% neq 0 (' + "`r`n" +
              '    powershell -Command "Start-Process cmd -ArgumentList ''/c \"%~f0\"'' -Verb RunAs"' + "`r`n" +
              '    exit /b' + "`r`n" +
              ')' + "`r`n" +
              'powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Installer.ps1"' + "`r`n" +
              'pause' + "`r`n"
[IO.File]::WriteAllText((Join-Path $installerDir "Install.bat"), $installBat)

# Installer.ps1 -- copies payload, adds firewall rule, creates Desktop shortcut
$installPs1 = @'
param([string]$InstallPath = "C:\SelfCheckoutKiosk")
$ErrorActionPreference = "Stop"
$payload = Join-Path $PSScriptRoot "payload"

Write-Host "Installing Self-Checkout Kiosk to $InstallPath ..." -ForegroundColor Cyan

if (-not (Test-Path $InstallPath)) { [IO.Directory]::CreateDirectory($InstallPath) | Out-Null }
Copy-Item -Path "$payload\*" -Destination $InstallPath -Recurse -Force
Write-Host "  [OK] Files copied" -ForegroundColor Green

try {
    netsh advfirewall firewall add rule `
        name="SelfCheckout CashAPI 5000" dir=in action=allow `
        protocol=TCP localport=5000 profile=any | Out-Null
    Write-Host "  [OK] Firewall rule added for port 5000" -ForegroundColor Green
} catch {}

try {
    $shell = New-Object -ComObject WScript.Shell
    $lnkPath = Join-Path ([Environment]::GetFolderPath("Desktop")) "Self-Checkout Kiosk.lnk"
    $lnk = $shell.CreateShortcut($lnkPath)
    $lnk.TargetPath       = Join-Path $InstallPath "Start-Kiosk.bat"
    $lnk.WorkingDirectory = $InstallPath
    $lnk.Description      = "Self-Checkout Kiosk"
    $lnk.Save()
    Write-Host "  [OK] Desktop shortcut -> $lnkPath" -ForegroundColor Green
} catch {}

Write-Host ""
Write-Host "Installation complete!" -ForegroundColor Green
Write-Host "Run: $InstallPath\Start-Kiosk.bat" -ForegroundColor White
'@
[IO.File]::WriteAllText((Join-Path $installerDir "Installer.ps1"), $installPs1)

if (Test-Path (Join-Path $repoRoot "LICENSE")) {
    Copy-Item -LiteralPath (Join-Path $repoRoot "LICENSE") -Destination (Join-Path $installerDir "LICENSE.txt") -Force
}
Write-Ok "Installer package assembled"

# ─── DONE ─────────────────────────────────────────────────────────────────────
Write-Host ""
Write-Host "=================================================================" -ForegroundColor Green
Write-Host "   PACKAGING COMPLETE" -ForegroundColor Green
Write-Host "=================================================================" -ForegroundColor Green
Write-Host ""
Write-Host "PORTABLE : $portableDir" -ForegroundColor Yellow
Write-Host "           -> double-click Start-Kiosk.bat" -ForegroundColor Gray
Write-Host ""
Write-Host "INSTALLER: $installerDir" -ForegroundColor Yellow
Write-Host "           -> run Install.bat as Administrator" -ForegroundColor Gray
Write-Host ""

