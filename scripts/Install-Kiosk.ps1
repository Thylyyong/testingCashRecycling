<#
.SYNOPSIS
    Installs the Self-Checkout Kiosk System V2 on Windows 10/11 x64.

.DESCRIPTION
    Deploys application binaries, cash device API bridge, assets, generates a
    machine-locked cryptographic license token, sets up firewall rules, and
    creates Start Menu and Desktop shortcuts.
#>

[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [string]$InstallPath = 'C:\SelfCheckoutKiosk',

    [switch]$Silent,
    [switch]$AutoStartKiosk,
    [switch]$BuildFromSource,
    [switch]$NoShortcuts,
    [switch]$NoFirewall
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Banner {
    Write-Host "=================================================================" -ForegroundColor Cyan
    Write-Host "         SELF-CHECKOUT KIOSK SYSTEM V2 - INSTALLER               " -ForegroundColor Yellow
    Write-Host "         Windows 10 / 11 x64 Offline-First Standalone            " -ForegroundColor Cyan
    Write-Host "=================================================================" -ForegroundColor Cyan
    Write-Host ""
}

function Test-IsAdmin {
    $currentPrincipal = New-Object Security.Principal.WindowsPrincipal(
        [Security.Principal.WindowsIdentity]::GetCurrent()
    )
    return $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Resolve-RepositoryRoot {
    $scriptDir = if ([string]::IsNullOrWhiteSpace($PSScriptRoot)) { (Get-Location).Path } else { $PSScriptRoot }
    $parent = [IO.Path]::GetFullPath((Join-Path $scriptDir ".."))
    if (Test-Path (Join-Path $parent "SelfCheckoutKiosk.sln")) {
        return $parent
    }
    if (Test-Path (Join-Path $scriptDir "SelfCheckoutKiosk.sln")) {
        return [IO.Path]::GetFullPath($scriptDir)
    }
    return [IO.Path]::GetFullPath((Get-Location).Path)
}

Write-Banner

$isAdmin = Test-IsAdmin
if (-not $isAdmin) {
    Write-Host "[WARNING] Running without Administrator privileges." -ForegroundColor Yellow
    Write-Host "          Firewall rules and system shortcuts may require elevation." -ForegroundColor DarkYellow
    Write-Host ""
}

$repoRoot = Resolve-RepositoryRoot
$packageSource = Join-Path $repoRoot "dist\SelfCheckoutKiosk-Package"

# Check if package exists or if we need to build it
$packageReady = (Test-Path (Join-Path $packageSource "KioskApp\SelfCheckoutKiosk.App.exe")) -and (-not $BuildFromSource)

if (-not $packageReady) {
    Write-Host "[BUILD] Standalone distribution package not found or rebuild requested." -ForegroundColor Cyan
    Write-Host "        Building Self-Checkout Kiosk and tools from source..." -ForegroundColor Gray
    
    $appProj = Join-Path $repoRoot "src\SelfCheckoutKiosk.App\SelfCheckoutKiosk.App.csproj"
    $simProj = Join-Path $repoRoot "tools\CashDeviceSimulator\CashDeviceSimulator.csproj"
    $licProj = Join-Path $repoRoot "tools\DevLicenseTokenGenerator\DevLicenseTokenGenerator.csproj"

    if (-not (Test-Path $appProj)) {
        throw "Could not locate project files in repository root: $repoRoot"
    }

    $kioskAppPublish = Join-Path $packageSource "KioskApp"
    $cashApiPublish = Join-Path $packageSource "CashDeviceSimulator-API"
    $licensePublish = Join-Path $packageSource "LicenseGenerator"

    [IO.Directory]::CreateDirectory($kioskAppPublish) | Out-Null
    [IO.Directory]::CreateDirectory($cashApiPublish) | Out-Null
    [IO.Directory]::CreateDirectory($licensePublish) | Out-Null

    Write-Host "  -> Publishing Kiosk App (Self-Contained win-x64)..." -ForegroundColor Gray
    & dotnet publish $appProj -c Release -r win-x64 --self-contained -p:WindowsPackageType=None -p:WindowsAppSDKSelfContained=true -p:PublishTrimmed=false -o $kioskAppPublish
    if ($LASTEXITCODE -ne 0) { throw "Failed to publish Kiosk App." }

    Write-Host "  -> Publishing Cash Device API Daemon..." -ForegroundColor Gray
    & dotnet publish $simProj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:PublishTrimmed=false -o $cashApiPublish
    if ($LASTEXITCODE -ne 0) { throw "Failed to publish Cash Device API Daemon." }

    Write-Host "  -> Publishing License Generator Tool..." -ForegroundColor Gray
    & dotnet publish $licProj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:PublishTrimmed=false -o $licensePublish
    if ($LASTEXITCODE -ne 0) { throw "Failed to publish License Generator." }

    Write-Host "[BUILD] Standalone package built successfully." -ForegroundColor Green
    Write-Host ""
}

# Prompt for installation path if interactive
if (-not $Silent) {
    Write-Host "Default Installation Directory: $InstallPath" -ForegroundColor White
    $userPath = Read-Host "Press ENTER to accept or enter custom installation directory"
    if (-not [string]::IsNullOrWhiteSpace($userPath)) {
        $InstallPath = $userPath.Trim()
    }
    Write-Host ""
}

$InstallPath = [IO.Path]::GetFullPath($InstallPath)
Write-Host "[1/6] Preparing installation target: $InstallPath" -ForegroundColor Cyan

if (-not (Test-Path $InstallPath)) {
    [IO.Directory]::CreateDirectory($InstallPath) | Out-Null
}

# Preserve existing configuration or logs if reinstalling
$targetConfig = Join-Path $InstallPath "KioskApp\Config"
$tempConfigBackup = $null
if (Test-Path $targetConfig) {
    $tempConfigBackup = Join-Path ([IO.Path]::GetTempPath()) ("KioskConfigBackup_" + [Guid]::NewGuid().ToString("N"))
    Copy-Item -LiteralPath $targetConfig -Destination $tempConfigBackup -Recurse -Force
}

Write-Host "[2/6] Copying package files..." -ForegroundColor Cyan
Copy-Item -Path (Join-Path $packageSource "*") -Destination $InstallPath -Recurse -Force

# Restore custom configuration if existed
if ($null -ne $tempConfigBackup -and (Test-Path $tempConfigBackup)) {
    Copy-Item -Path (Join-Path $tempConfigBackup "*") -Destination $targetConfig -Recurse -Force
    Remove-Item -LiteralPath $tempConfigBackup -Recurse -Force -ErrorAction SilentlyContinue
}

# Copy root license and readme to installed directory
if (Test-Path (Join-Path $repoRoot "LICENSE")) {
    Copy-Item -LiteralPath (Join-Path $repoRoot "LICENSE") -Destination (Join-Path $InstallPath "LICENSE.txt") -Force
}
if (Test-Path (Join-Path $repoRoot "README.md")) {
    Copy-Item -LiteralPath (Join-Path $repoRoot "README.md") -Destination (Join-Path $InstallPath "README.txt") -Force
}

Write-Host "[3/6] Generating and provisioning node-locked license token..." -ForegroundColor Cyan
$generatorExe = Join-Path $InstallPath "LicenseGenerator\GenerateLicense.exe"
$targetLicenseToken = Join-Path $InstallPath "KioskApp\license.token"

if (Test-Path $generatorExe) {
    try {
        & $generatorExe --output $targetLicenseToken | Out-Null
        if (Test-Path $targetLicenseToken) {
            Copy-Item -LiteralPath $targetLicenseToken -Destination (Join-Path $InstallPath "license.token") -Force
            Write-Host "      [OK] Node-locked license token generated and activated for this machine." -ForegroundColor Green
        }
    }
    catch {
        Write-Host "      [WARNING] License token generator encountered a warning: $_" -ForegroundColor Yellow
    }
} else {
    Write-Host "      [NOTE] Generator executable not present; skipping auto-activation." -ForegroundColor DarkYellow
}

Write-Host "[4/6] Creating desktop and start menu shortcuts..." -ForegroundColor Cyan
if (-not $NoShortcuts) {
    $wscriptShell = New-Object -ComObject WScript.Shell

    $appIconPath = Join-Path $InstallPath "KioskApp\Assets\Logo\ca.ico"
    if (-not (Test-Path $appIconPath)) {
        $appIconPath = Join-Path $InstallPath "KioskApp\SelfCheckoutKiosk.App.exe"
    }

    # 1. Desktop Shortcut
    $desktopFolder = [Environment]::GetFolderPath([Environment+SpecialFolder]::DesktopDirectory)
    $desktopShortcutPath = Join-Path $desktopFolder "Self-Checkout Kiosk.lnk"
    $shortcut = $wscriptShell.CreateShortcut($desktopShortcutPath)
    $shortcut.TargetPath = Join-Path $InstallPath "Start-Kiosk-With-API.bat"
    $shortcut.WorkingDirectory = $InstallPath
    $shortcut.IconLocation = "$appIconPath,0"
    $shortcut.Description = "Launch Self-Checkout Kiosk with Cash API Bridge"
    $shortcut.Save()
    Write-Host "      [OK] Desktop shortcut created: $desktopShortcutPath" -ForegroundColor Green

    # 2. Start Menu Shortcuts
    $startMenuPrograms = [Environment]::GetFolderPath([Environment+SpecialFolder]::Programs)
    $kioskStartFolder = Join-Path $startMenuPrograms "Self-Checkout Kiosk"
    if (-not (Test-Path $kioskStartFolder)) {
        [IO.Directory]::CreateDirectory($kioskStartFolder) | Out-Null
    }

    $smMain = $wscriptShell.CreateShortcut((Join-Path $kioskStartFolder "Self-Checkout Kiosk.lnk"))
    $smMain.TargetPath = Join-Path $InstallPath "Start-Kiosk-With-API.bat"
    $smMain.WorkingDirectory = $InstallPath
    $smMain.IconLocation = "$appIconPath,0"
    $smMain.Description = "Launch Self-Checkout Kiosk"
    $smMain.Save()

    $smDirect = $wscriptShell.CreateShortcut((Join-Path $kioskStartFolder "Self-Checkout Kiosk (App Only).lnk"))
    $smDirect.TargetPath = Join-Path $InstallPath "KioskApp\SelfCheckoutKiosk.App.exe"
    $smDirect.WorkingDirectory = (Join-Path $InstallPath "KioskApp")
    $smDirect.IconLocation = "$appIconPath,0"
    $smDirect.Description = "Self-Checkout Kiosk Standalone App"
    $smDirect.Save()

    $smLicense = $wscriptShell.CreateShortcut((Join-Path $kioskStartFolder "Regenerate Machine License.lnk"))
    $smLicense.TargetPath = (Join-Path $InstallPath "LicenseGenerator\GenerateLicense.exe")
    $smLicense.WorkingDirectory = (Join-Path $InstallPath "LicenseGenerator")
    $smLicense.Description = "Generate or refresh node-locked license token"
    $smLicense.Save()

    $smUninstall = $wscriptShell.CreateShortcut((Join-Path $kioskStartFolder "Uninstall Self-Checkout Kiosk.lnk"))
    $smUninstall.TargetPath = (Join-Path $InstallPath "Uninstall.bat")
    $smUninstall.WorkingDirectory = $InstallPath
    $smUninstall.Description = "Uninstall Self-Checkout Kiosk"
    $smUninstall.Save()

    Write-Host "      [OK] Start Menu folder created: $kioskStartFolder" -ForegroundColor Green
}

Write-Host "[5/6] Configuring Windows Firewall rules..." -ForegroundColor Cyan
if (-not $NoFirewall -and $isAdmin) {
    try {
        netsh advfirewall firewall add rule name="SelfCheckout CashAPI (Port 5055)" dir=in action=allow protocol=TCP localport=5055 profile=any | Out-Null
        netsh advfirewall firewall add rule name="SelfCheckout CashAPI (Port 5000)" dir=in action=allow protocol=TCP localport=5000 profile=any | Out-Null
        Write-Host "      [OK] Firewall rules configured for API ports 5055 and 5000." -ForegroundColor Green
    }
    catch {
        Write-Host "      [NOTE] Could not set firewall rules automatically: $_" -ForegroundColor DarkYellow
    }
} else {
    Write-Host "      [NOTE] Skipped firewall setup (elevation or flag required)." -ForegroundColor Gray
}

Write-Host "[6/6] Generating uninstaller..." -ForegroundColor Cyan
$uninstallerScriptPath = Join-Path $InstallPath "Uninstall.ps1"
$uninstallerContent = @"
Write-Host "=================================================================" -ForegroundColor Cyan
Write-Host "       UNINSTALLING SELF-CHECKOUT KIOSK SYSTEM                   " -ForegroundColor Yellow
Write-Host "=================================================================" -ForegroundColor Cyan
Write-Host ""
`$desktopShortcut = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::DesktopDirectory)) "Self-Checkout Kiosk.lnk"
`$startMenuFolder = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::Programs)) "Self-Checkout Kiosk"
if (Test-Path `$desktopShortcut) {
    Remove-Item -LiteralPath `$desktopShortcut -Force -ErrorAction SilentlyContinue
    Write-Host "Removed desktop shortcut." -ForegroundColor Green
}
if (Test-Path `$startMenuFolder) {
    Remove-Item -LiteralPath `$startMenuFolder -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host "Removed Start Menu shortcuts." -ForegroundColor Green
}
Write-Host ""
Write-Host "To completely remove application files, delete:" -ForegroundColor White
Write-Host "  $InstallPath" -ForegroundColor Yellow
Write-Host ""
Write-Host "Uninstall complete." -ForegroundColor Green
"@
[IO.File]::WriteAllText($uninstallerScriptPath, $uninstallerContent)

$uninstallBatPath = Join-Path $InstallPath "Uninstall.bat"
$uninstallBatContent = @"
@echo off
title Uninstall Self-Checkout Kiosk
cd /d "%~dp0"
echo =================================================================
echo          UNINSTALL SELF-CHECKOUT KIOSK SYSTEM                   
echo =================================================================
echo.
powershell.exe -ExecutionPolicy Bypass -File "%~dp0Uninstall.ps1"
echo.
pause
"@
[IO.File]::WriteAllText($uninstallBatPath, $uninstallBatContent)

if ($AutoStartKiosk) {
    try {
        $runKey = "HKLM:\Software\Microsoft\Windows\CurrentVersion\Run"
        Set-ItemProperty -Path $runKey -Name "SelfCheckoutKiosk" -Value (Join-Path $InstallPath "Start-Kiosk-With-API.bat")
        Write-Host "      [OK] Kiosk registered for automatic startup on Windows boot." -ForegroundColor Green
    }
    catch {
        Write-Host "      [NOTE] Could not set auto-start in registry: $_" -ForegroundColor DarkYellow
    }
}

Write-Host ""
Write-Host "=================================================================" -ForegroundColor Green
Write-Host "       INSTALLATION COMPLETED SUCCESSFULLY!                      " -ForegroundColor Green
Write-Host "=================================================================" -ForegroundColor Green
Write-Host ""
Write-Host "Application Directory : $InstallPath" -ForegroundColor White
Write-Host "Main Launcher         : $InstallPath\Start-Kiosk-With-API.bat" -ForegroundColor White
Write-Host "License Token         : $InstallPath\KioskApp\license.token" -ForegroundColor White
Write-Host ""
Write-Host "To start the Kiosk now:" -ForegroundColor Cyan
Write-Host "  -> Double-click the 'Self-Checkout Kiosk' icon on your Desktop, OR" -ForegroundColor Gray
Write-Host "  -> Run: $InstallPath\Start-Kiosk-With-API.bat" -ForegroundColor Gray
Write-Host ""
