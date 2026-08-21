# ==============================================================================
# Self-Checkout Kiosk V2 - Standalone Distribution Packaging Script
# Generates a portable, self-contained Release package in dist/SelfCheckoutKiosk/
# ==============================================================================

param(
    [switch]$SkipTests = $false
)

$ErrorActionPreference = "Stop"
$RepoRoot = $PSScriptRoot
if (-not $RepoRoot) { $RepoRoot = Get-Location }

Write-Host "==================================================" -ForegroundColor Cyan
Write-Host "  PACKAGING SELF-CHECKOUT KIOSK V2 STANDALONE     " -ForegroundColor Cyan
Write-Host "==================================================" -ForegroundColor Cyan

# 1. Stop Any Running Kiosk / Hardware Processes (prevent file locking)
Write-Host "[1/7] Checking for running kiosk processes..." -ForegroundColor Yellow
$LockedProcesses = @("SelfCheckoutKiosk.App", "CashDevice-RestAPI", "CashDeviceSimulator", "GenerateLicense")
foreach ($procName in $LockedProcesses) {
    Get-Process -Name $procName -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
}
Start-Sleep -Milliseconds 500

# 2. Run Tests (unless skipped)
if (-not $SkipTests) {
    Write-Host "[2/7] Running Solution Test Suite..." -ForegroundColor Yellow
    dotnet test "$RepoRoot\SelfCheckoutKiosk.sln" -c Release --nologo
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Test suite execution failed. Packaging aborted."
        exit 1
    }
    Write-Host "  -> All tests passed!" -ForegroundColor Green
} else {
    Write-Host "[2/7] Skipping tests (-SkipTests specified)..." -ForegroundColor Gray
}

# 3. Clean & Prepare Output Folder
Write-Host "[3/7] Preparing Output Folder: dist/SelfCheckoutKiosk..." -ForegroundColor Yellow
$DistDir = Join-Path $RepoRoot "dist\SelfCheckoutKiosk"
if (Test-Path $DistDir) {
    Remove-Item -Recurse -Force $DistDir
}
New-Item -ItemType Directory -Force -Path $DistDir | Out-Null

# 4. Publish Main WinUI 3 App
Write-Host "[4/7] Publishing WinUI 3 Touch App (Release / win-x64)..." -ForegroundColor Yellow
dotnet publish "$RepoRoot\src\SelfCheckoutKiosk.App\SelfCheckoutKiosk.App.csproj" `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:WindowsPackageType=None `
    -p:WindowsAppSDKSelfContained=true `
    -o $DistDir

# 5. Copy & Validate PRI Resource Indexes
Write-Host "[5/7] Validating PRI Resource Indexes..." -ForegroundColor Yellow
$DistResourcesPri = Join-Path $DistDir "resources.pri"
$DistAppPri = Join-Path $DistDir "SelfCheckoutKiosk.App.pri"

# Look for source PRI from build directory if not in dist
$SourcePri = "$RepoRoot\src\SelfCheckoutKiosk.App\bin\Release\net10.0-windows10.0.19041.0\win-x64\SelfCheckoutKiosk.App.pri"
if (-not (Test-Path $SourcePri)) {
    $SearchPri = Get-ChildItem -Path "$RepoRoot\src\SelfCheckoutKiosk.App\bin" -Filter "SelfCheckoutKiosk.App.pri" -Recurse -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($SearchPri) {
        $SourcePri = $SearchPri.FullName
    }
}

if (Test-Path $SourcePri) {
    if (-not (Test-Path $DistResourcesPri) -or ((Get-Item $DistResourcesPri).FullName -ne (Get-Item $SourcePri).FullName)) {
        Copy-Item $SourcePri $DistResourcesPri -Force
    }
    if (-not (Test-Path $DistAppPri) -or ((Get-Item $DistAppPri).FullName -ne (Get-Item $SourcePri).FullName)) {
        Copy-Item $SourcePri $DistAppPri -Force
    }
    Write-Host "  -> Successfully verified and synced PRI resource indexes." -ForegroundColor Green
} elseif (Test-Path $DistResourcesPri) {
    if (-not (Test-Path $DistAppPri)) {
        Copy-Item $DistResourcesPri $DistAppPri -Force
    }
    Write-Host "  -> Verified published resources.pri." -ForegroundColor Green
} else {
    Write-Warning "Could not find SelfCheckoutKiosk.App.pri or resources.pri! Application may fail to load XAML."
}

# 6. Package Cash API & Simulator into isolated CashAPI/ subfolder
Write-Host "[6/7] Packaging Cash API into CashAPI/ subfolder..." -ForegroundColor Yellow
$CashApiDir = Join-Path $DistDir "CashAPI"
New-Item -ItemType Directory -Force -Path $CashApiDir | Out-Null

$ItlPackage = Join-Path $RepoRoot "CashDevice-REST-API-V1.6.1-RC.4-Net8.0"
if (Test-Path $ItlPackage) {
    Copy-Item "$ItlPackage\*" $CashApiDir -Recurse -Force
}

$SimDir = Join-Path $CashApiDir "Simulator"
New-Item -ItemType Directory -Force -Path $SimDir | Out-Null
dotnet publish "$RepoRoot\tools\CashDeviceSimulator\CashDeviceSimulator.csproj" `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -o $SimDir

# 7. Package License Generator & Generate Token
Write-Host "[7/7] Packaging License Generator & 1-Click Launcher..." -ForegroundColor Yellow
$LicGenDir = Join-Path $DistDir "LicenseGenerator"
New-Item -ItemType Directory -Force -Path $LicGenDir | Out-Null

dotnet publish "$RepoRoot\tools\DevLicenseTokenGenerator\DevLicenseTokenGenerator.csproj" `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -o $LicGenDir

$GenExe = Join-Path $LicGenDir "GenerateLicense.exe"
if (Test-Path $GenExe) {
    & $GenExe --silent --output (Join-Path $DistDir "license.token")
}

# 7. Create Start-Kiosk.bat Launcher
$BatContent = @"
@echo off
title Self-Checkout Kiosk
cd /d "%~dp0"

:: If license token is missing, generate it automatically
if not exist "license.token" (
    if exist "LicenseGenerator\GenerateLicense.exe" (
        "LicenseGenerator\GenerateLicense.exe" --silent --output "license.token"
    )
)

:: Launch the kiosk application (it automatically manages the Cash API lifecycle)
start "" "%~dp0SelfCheckoutKiosk.App.exe"
"@

Set-Content -Path (Join-Path $DistDir "Start-Kiosk.bat") -Value $BatContent

# 8. Create Test-Hardware.bat Hardware Diagnostics Tool
$TestBatContent = @"
@echo off
title Self-Checkout Kiosk - Hardware Diagnostic Utility
cd /d "%~dp0"
echo =======================================================
echo   Self-Checkout Kiosk V2 - Hardware Diagnostic Tool
echo =======================================================
echo.

echo [1/4] Checking .NET Runtimes...
dotnet --list-runtimes 2>nul | findstr /i "Microsoft.AspNetCore.App 8."
if %ERRORLEVEL% equ 0 (
    echo   [OK] ASP.NET Core 8.0 Runtime found.
) else (
    echo   [WARN] ASP.NET Core 8.0 Runtime NOT detected!
    echo          Download from https://dotnet.microsoft.com/download/dotnet/8.0
)
echo.

echo [2/4] Detecting Active Serial COM Ports...
powershell -NoProfile -Command "[System.IO.Ports.SerialPort]::GetPortNames() | ForEach-Object { Write-Host '  Found Port:' `$_ -ForegroundColor Cyan }"
echo.

echo [3/4] Checking Cash API Executables...
if exist "CashAPI\CashDevice-RestAPI.exe" (
    echo   [OK] CashDevice-RestAPI.exe present.
) else (
    echo   [FAIL] CashDevice-RestAPI.exe missing!
)
if exist "CashAPI\CashDeviceSimulator.exe" (
    echo   [OK] CashDeviceSimulator.exe present.
) else (
    echo   [FAIL] CashDeviceSimulator.exe missing!
)
echo.

echo [4/4] Checking License Token...
if exist "license.token" (
    echo   [OK] license.token present.
) else (
    echo   [WARN] license.token missing. Generating...
    if exist "LicenseGenerator\GenerateLicense.exe" (
        "LicenseGenerator\GenerateLicense.exe" --silent --output "license.token"
        echo   [OK] Generated license.token.
    )
)
echo.
echo =======================================================
echo Diagnostic complete. Press any key to exit.
pause >nul
"@

Set-Content -Path (Join-Path $DistDir "Test-Hardware.bat") -Value $TestBatContent

Write-Host "==================================================" -ForegroundColor Green
Write-Host "  PACKAGING COMPLETE: dist/SelfCheckoutKiosk/     " -ForegroundColor Green
Write-Host "==================================================" -ForegroundColor Green
