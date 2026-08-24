# ==============================================================================
# Self-Checkout Kiosk V2 - Standalone Distribution Packaging Script
# Generates a portable, self-contained Release package in dist/SelfCheckoutKiosk/
# ==============================================================================

param(
    [switch]$SkipTests = $false,
    [switch]$NoPause = $false
)

$ErrorActionPreference = "Stop"
$RepoRoot = $PSScriptRoot
if (-not $RepoRoot) { $RepoRoot = Get-Location }

$PackagingSuccess = $false

try {
    Write-Host "==================================================" -ForegroundColor Cyan
    Write-Host "  PACKAGING SELF-CHECKOUT KIOSK V2 STANDALONE     " -ForegroundColor Cyan
    Write-Host "==================================================" -ForegroundColor Cyan

    # 1. Stop Any Running Kiosk / Hardware Processes (prevent file locking)
    Write-Host "[1/7] Checking for running kiosk processes..." -ForegroundColor Yellow
    $LockedProcesses = @("SelfCheckoutKiosk.App", "CashDevice-RestAPI", "CashDeviceSimulator", "GenerateLicense", "DevLicenseTokenGenerator")
    foreach ($procName in $LockedProcesses) {
        $procs = Get-Process -Name $procName -ErrorAction SilentlyContinue
        if ($procs) {
            Write-Host "  -> Terminating active process: $procName" -ForegroundColor DarkYellow
            $procs | Stop-Process -Force -ErrorAction SilentlyContinue
        }
    }
    Start-Sleep -Milliseconds 800

    # 2. Run Tests (unless skipped)
    if (-not $SkipTests) {
        Write-Host "[2/7] Running Solution Test Suite..." -ForegroundColor Yellow
        dotnet test "$RepoRoot\SelfCheckoutKiosk.sln" -c Release --nologo
        if ($LASTEXITCODE -ne 0) {
            throw "Test suite execution failed (exit code $LASTEXITCODE). Packaging aborted."
        }
        Write-Host "  -> All tests passed!" -ForegroundColor Green
    }
    else {
        Write-Host "[2/7] Skipping tests (-SkipTests specified)..." -ForegroundColor Gray
    }

    # 3. Clean & Prepare Output Folder
    Write-Host "[3/7] Preparing Output Folder: dist/SelfCheckoutKiosk..." -ForegroundColor Yellow
    $DistDir = Join-Path $RepoRoot "dist\SelfCheckoutKiosk"
    if (-not (Test-Path $DistDir)) {
        New-Item -ItemType Directory -Force -Path $DistDir | Out-Null
    }
    else {
        # Clean folder contents without deleting the root directory node
        # (avoids locking error if Explorer or shell is open in the folder)
        Get-ChildItem -Path $DistDir -Force -ErrorAction SilentlyContinue | ForEach-Object {
            try {
                Remove-Item -Path $_.FullName -Recurse -Force -ErrorAction Stop
            }
            catch {
                Write-Warning "Could not delete '$($_.Name)' (file may be locked). It will be overwritten during publish."
            }
        }
    }

    # 4. Publish Main WinUI 3 App
    Write-Host "[4/7] Publishing WinUI 3 Touch App (Release / win-x64)..." -ForegroundColor Yellow
    dotnet publish "$RepoRoot\src\SelfCheckoutKiosk.App\SelfCheckoutKiosk.App.csproj" `
        -c Release `
        -r win-x64 `
        --self-contained true `
        -p:WindowsPackageType=None `
        -p:WindowsAppSDKSelfContained=true `
        -o $DistDir

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish for SelfCheckoutKiosk.App failed with exit code $LASTEXITCODE"
    }

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
    }
    elseif (Test-Path $DistResourcesPri) {
        if (-not (Test-Path $DistAppPri)) {
            Copy-Item $DistResourcesPri $DistAppPri -Force
        }
        Write-Host "  -> Verified published resources.pri." -ForegroundColor Green
    }
    else {
        Write-Warning "Could not find SelfCheckoutKiosk.App.pri or resources.pri! Application may fail to load XAML."
    }

    # 6. Package Cash API & Simulator into isolated CashAPI/ subfolder
    Write-Host "[6/7] Packaging Cash API into CashAPI/ subfolder..." -ForegroundColor Yellow
    $CashApiDir = Join-Path $DistDir "CashAPI"
    if (-not (Test-Path $CashApiDir)) {
        New-Item -ItemType Directory -Force -Path $CashApiDir | Out-Null
    }

    $ItlPackage = Join-Path $RepoRoot "CashDevice-REST-API-V1.6.1-RC.4-Net8.0"
    if (Test-Path $ItlPackage) {
        Copy-Item "$ItlPackage\*" $CashApiDir -Recurse -Force
    }

    $SimDir = Join-Path $CashApiDir "Simulator"
    if (-not (Test-Path $SimDir)) {
        New-Item -ItemType Directory -Force -Path $SimDir | Out-Null
    }
    dotnet publish "$RepoRoot\tools\CashDeviceSimulator\CashDeviceSimulator.csproj" `
        -c Release `
        -r win-x64 `
        --self-contained true `
        -o $SimDir

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish for CashDeviceSimulator failed with exit code $LASTEXITCODE"
    }

    # 7. Package License Generator & Generate Token
    Write-Host "[7/7] Packaging License Generator & 1-Click Launcher..." -ForegroundColor Yellow
    $LicGenDir = Join-Path $DistDir "LicenseGenerator"
    if (-not (Test-Path $LicGenDir)) {
        New-Item -ItemType Directory -Force -Path $LicGenDir | Out-Null
    }

    dotnet publish "$RepoRoot\tools\DevLicenseTokenGenerator\DevLicenseTokenGenerator.csproj" `
        -c Release `
        -r win-x64 `
        --self-contained true `
        -o $LicGenDir

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish for DevLicenseTokenGenerator failed with exit code $LASTEXITCODE"
    }

    $GenExe = Join-Path $LicGenDir "GenerateLicense.exe"
    if (Test-Path $GenExe) {
        & $GenExe --silent --output (Join-Path $DistDir "license.token")
    }

    # Create Start-Kiosk.bat Launcher
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

    # Create Test-Hardware.bat Hardware Diagnostics Tool
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
if exist "CashAPI\Simulator\CashDeviceSimulator.exe" (
    echo   [OK] CashDeviceSimulator.exe present ^(in CashAPI\Simulator\^).
) else (
    if exist "CashAPI\CashDeviceSimulator.exe" (
        echo   [OK] CashDeviceSimulator.exe present.
    ) else (
        echo   [FAIL] CashDeviceSimulator.exe missing!
    )
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
    Write-Host "==================================================" -ForegroundColor Cyan
    Write-Host "  Ready to deploy or launch via Start-Kiosk.bat" -ForegroundColor Cyan
    Write-Host "==================================================" -ForegroundColor Green

    $PackagingSuccess = $true
}
catch {
    Write-Host ""
    Write-Host "==================================================" -ForegroundColor Red
    Write-Host "  PACKAGING FAILED!                               " -ForegroundColor Red
    Write-Host "==================================================" -ForegroundColor Red
    Write-Host "Error Details: $_" -ForegroundColor Red
    Write-Host "Stack Trace: $($_.ScriptStackTrace)" -ForegroundColor DarkRed
    Write-Host "==================================================" -ForegroundColor Red
    $PackagingSuccess = $false
}
finally {
    if (-not $NoPause) {
        Write-Host ""
        Write-Host "Press Enter to exit..." -ForegroundColor Gray
        Read-Host | Out-Null
    }

    if (-not $PackagingSuccess) {
        exit 1
    }
}
