#Requires -RunAsAdministrator
<#
.SYNOPSIS
    DEV-SAFE kiosk-mode test rig: creates a dedicated, restricted local user
    and scopes Shell Launcher V2 to ONLY that user's SID. Your current
    (Admin) account's shell is never touched — SetDefaultShellLauncher below
    is what everyone else, including you, keeps getting: plain explorer.exe.

.DESCRIPTION
    Classic Windows "Assigned Access" (Set-AssignedAccess, single-app kiosk)
    targets Store/UWP apps with a package identity (AUMID) — this app is
    deliberately unpackaged (WindowsPackageType=None in the .csproj), so that
    cmdlet doesn't cleanly apply. Shell Launcher V2 is the equivalent
    mechanism for a Win32/WinUI3 app: it replaces the SHELL (explorer.exe)
    for one specific user SID with your app, leaving every other account's
    shell completely alone. That's what makes this safe to run on your own
    dev PC — your Admin account is simply never in Shell Launcher's
    configuration at all.

    This is intentionally the MINIMAL kiosk setup for local testing — no
    BitLocker, no USB lockdown, no AutoPlay changes. That's separate
    production-hardening scope (see Provision-KioskOS.ps1 in this same
    folder) and not what a "let me safely test this on my dev PC" pass needs.

.PARAMETER KioskUserName
    The dedicated local standard-user account to create/configure.
    Defaults to 'KioskUser'. Never your own account — don't pass your admin
    username here.

.PARAMETER KioskAppPath
    Full path to the built SelfCheckoutKiosk.App.exe.

.EXAMPLE
    .\Provision-DevKioskUser.ps1 -KioskAppPath "F:\Self checkout proj\cld\Self-checkout proj scaffold\SelfCheckoutKiosk\src\SelfCheckoutKiosk.App\bin\x64\Debug\net10.0-windows10.0.19041.0\win-x64\SelfCheckoutKiosk.App.exe"

.NOTES
    RUN THIS YOURSELF, ELEVATED. It creates a local Windows account and
    changes system shell configuration — both are the kind of action that
    should always be something you run deliberately, not something handed to
    you already executed.

    First run enables the Client-EmbeddedShellLauncher Windows feature if
    it isn't already on, which requires a REBOOT before Shell Launcher takes
    effect. Nothing kiosk-related activates until after that reboot, and
    your Admin account is unaffected the entire time.

    TO UNDO EVERYTHING THIS SCRIPT DID:
        Get-CimInstance -Namespace root\standardcimv2\embedded -ClassName Win32_ShellLauncherClass |
            Invoke-CimMethod -MethodName RemoveCustomShellLauncher -Arguments @{ Sid = (Get-LocalUser -Name 'KioskUser').SID.Value }
        Remove-LocalUser -Name 'KioskUser'
        Disable-WindowsOptionalFeature -Online -FeatureName Client-EmbeddedShellLauncher -NoRestart
    (adjust the username if you passed a different -KioskUserName)
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$KioskUserName = 'KioskUser',

    [Parameter(Mandatory = $true)]
    [ValidateScript({ Test-Path $_ -PathType Leaf })]
    [string]$KioskAppPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$currentUser = [Security.Principal.WindowsIdentity]::GetCurrent().Name
if ($KioskUserName -eq ($currentUser -split '\\')[-1]) {
    throw "KioskUserName ('$KioskUserName') matches the account you're currently logged in as ('$currentUser'). Refusing to continue - this script must target a SEPARATE, dedicated account, never the one you're using right now."
}

Write-Host "Current session: $currentUser (untouched by this script)" -ForegroundColor Cyan
Write-Host "Provisioning restricted test account: $KioskUserName" -ForegroundColor Cyan

# 1. Dedicated local STANDARD user — never an admin. Random password since
#    nobody needs to type it in; sign-in happens via "Switch user" once
#    Shell Launcher is active, or you can sign in directly to test.
$existingUser = Get-LocalUser -Name $KioskUserName -ErrorAction SilentlyContinue
if (-not $existingUser) {
    $password = ConvertTo-SecureString ([System.Guid]::NewGuid().ToString('N') + 'Aa1!') -AsPlainText -Force
    New-LocalUser -Name $KioskUserName -Password $password -PasswordNeverExpires -UserMayNotChangePassword `
        -Description 'Dev-only kiosk-mode test account (Shell Launcher V2 scoped to this SID only).' | Out-Null
    Write-Host "  Created local standard-user account '$KioskUserName'." -ForegroundColor DarkGray
}
else {
    Write-Host "  Account '$KioskUserName' already exists - reusing it." -ForegroundColor DarkGray
}

# 2. Client-EmbeddedShellLauncher is the Windows feature backing Shell
#    Launcher V2. A reboot is required after first enable, before ANY
#    shell-launcher configuration takes effect.
$feature = Get-WindowsOptionalFeature -Online -FeatureName Client-EmbeddedShellLauncher
$rebootPending = $false
if ($feature.State -ne 'Enabled') {
    $result = Enable-WindowsOptionalFeature -Online -FeatureName Client-EmbeddedShellLauncher -All -NoRestart
    $rebootPending = $result.RestartNeeded
}

$sid = (Get-LocalUser -Name $KioskUserName).SID.Value

$shellLauncherClass = Get-CimInstance -Namespace 'root\standardcimv2\embedded' -ClassName Win32_ShellLauncherClass

# CRITICAL SAFETY LINE: every account NOT explicitly configured below —
# including yours — keeps plain explorer.exe. This is what makes it
# impossible for this script to affect your own login.
Invoke-CimMethod -InputObject $shellLauncherClass -MethodName SetDefaultShellLauncher `
    -Arguments @{ Shell = 'explorer.exe'; ShellArguments = '' } | Out-Null

Invoke-CimMethod -InputObject $shellLauncherClass -MethodName RemoveCustomShellLauncher `
    -Arguments @{ Sid = $sid } -ErrorAction SilentlyContinue | Out-Null
Invoke-CimMethod -InputObject $shellLauncherClass -MethodName AddCustomShellLauncher `
    -Arguments @{ Sid = $sid; Shell = $KioskAppPath; ShellArguments = '' } | Out-Null

# Action 0 = RestartShell (relaunch the app if it exits/crashes).
# Deliberately NOT Action 1 (RestartDevice) or 2 (Shutdown) — a crash during
# testing should never reboot or power off your physical dev PC.
Invoke-CimMethod -InputObject $shellLauncherClass -MethodName DefineRestartAction `
    -Arguments @{ Sid = $sid; Action = 0 } | Out-Null

Invoke-CimMethod -InputObject $shellLauncherClass -MethodName SetEnabled -Arguments @{ Enabled = $true } | Out-Null

Write-Host ""
Write-Host "Done. '$KioskUserName' -> $KioskAppPath (RestartShell on exit/crash)." -ForegroundColor Green
Write-Host "Your account ('$currentUser') is unaffected - still plain explorer.exe." -ForegroundColor Green
if ($rebootPending) {
    Write-Warning "A reboot is required before Shell Launcher takes effect. Nothing kiosk-related is active until then."
}
Write-Host ""
Write-Host "To test: Ctrl+Alt+Del -> Switch user -> sign in as '$KioskUserName'." -ForegroundColor Yellow
Write-Host "To get back: Ctrl+Alt+Del -> Switch user -> sign back in as '$currentUser'. Your session keeps running in the background the whole time." -ForegroundColor Yellow
