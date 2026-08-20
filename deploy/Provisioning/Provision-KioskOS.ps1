#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Windows 11 IoT Enterprise provisioning for the self-checkout kiosk
    (Phase 3, Category 3 — OS hardening).

.DESCRIPTION
    Idempotent where practical. Performs, in order:
      1. Shell Launcher V2 — replaces explorer.exe with the kiosk AOT binary
         for a dedicated local standard-user account.
      2. BitLocker on the OS volume, TPM-bound, no recovery key left on site.
      3. Disable USB AutoPlay/AutoRun.
      4. USB class restriction: deny mass storage, allow-list HID (barcode
         scanner) and vendor serial/USB-communication device classes.

    Every step is independently gated by its own -Skip* switch so a step can
    be re-run or omitted without re-running the whole script.

.PARAMETER KioskUserName
    The dedicated local standard-user account whose shell is replaced.
    Created if it does not already exist.

.PARAMETER KioskAppPath
    Full path to the compiled kiosk AOT executable
    (SelfCheckoutKiosk.App.exe) that becomes KioskUserName's shell.

.PARAMETER BitLockerRecoveryEscrowTarget
    Where the (mandatory, but never locally-persisted) BitLocker recovery
    password protector is backed up: 'AzureAD' or 'ActiveDirectory'. See the
    BitLocker section below for why a recovery protector still exists even
    though the unlock protector is TPM-only.

.EXAMPLE
    .\Provision-KioskOS.ps1 -KioskUserName KioskUser `
        -KioskAppPath 'C:\Kiosk\SelfCheckoutKiosk.App.exe' `
        -BitLockerRecoveryEscrowTarget AzureAD

.NOTES
    Run on the target kiosk hardware, elevated, after Windows 11 IoT
    Enterprise + TPM 2.0 are confirmed present (script hard-stops otherwise).
    Every step here changes system/security configuration — review each
    section against your organization's exact Windows build/policy baseline
    (via gpedit.msc / rsop.msc) before rolling out to a fleet; the device
    installation restriction registry layout in particular has shifted
    slightly across Windows releases and should be validated locally.
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$KioskUserName = 'KioskUser',

    [Parameter(Mandatory = $true)]
    [ValidateScript({ Test-Path $_ -PathType Leaf })]
    [string]$KioskAppPath,

    [ValidateSet('AzureAD', 'ActiveDirectory')]
    [string]$BitLockerRecoveryEscrowTarget = 'AzureAD',

    [switch]$SkipShellLauncher,
    [switch]$SkipBitLocker,
    [switch]$SkipAutoPlayHardening,
    [switch]$SkipUsbClassRestriction
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Assert-Prerequisites {
    $os = Get-CimInstance -ClassName Win32_OperatingSystem
    if ($os.Caption -notmatch 'IoT Enterprise') {
        throw "This script targets Windows 11 IoT Enterprise; detected: '$($os.Caption)'. Hard stop."
    }

    $tpm = Get-Tpm -ErrorAction SilentlyContinue
    if (-not $tpm -or -not $tpm.TpmPresent -or -not $tpm.TpmReady) {
        throw 'TPM 2.0 is not present/ready. BitLocker TPM-only binding requires a working TPM. Hard stop.'
    }
}

# ---------------------------------------------------------------------------
# 1. Shell Launcher V2 — KioskUser's shell becomes the kiosk AOT binary.
# ---------------------------------------------------------------------------
function Set-ShellLauncherConfiguration {
    param([string]$UserName, [string]$AppPath)

    Write-Host "[1/4] Configuring Shell Launcher V2 for '$UserName' -> '$AppPath'..." -ForegroundColor Cyan

    # Dedicated local standard-user account — never an admin, so a kiosk
    # breakout can't touch OS configuration even if the shell substitution
    # is somehow bypassed.
    $localUser = Get-LocalUser -Name $UserName -ErrorAction SilentlyContinue
    if (-not $localUser) {
        $password = ConvertTo-SecureString ([System.Guid]::NewGuid().ToString('N') + 'Aa1!') -AsPlainText -Force
        New-LocalUser -Name $UserName -Password $password -PasswordNeverExpires -UserMayNotChangePassword `
            -Description 'Dedicated self-checkout kiosk shell account (Shell Launcher V2 target).' | Out-Null
        Write-Host "  Created local standard-user account '$UserName'." -ForegroundColor DarkGray
    }

    # Client-EmbeddedShellLauncher is the Windows optional feature backing
    # Shell Launcher V2; a reboot is required after first enable.
    $feature = Get-WindowsOptionalFeature -Online -FeatureName Client-EmbeddedShellLauncher
    $rebootPending = $false
    if ($feature.State -ne 'Enabled') {
        $result = Enable-WindowsOptionalFeature -Online -FeatureName Client-EmbeddedShellLauncher -All -NoRestart
        $rebootPending = $result.RestartNeeded
    }

    $sid = (Get-LocalUser -Name $UserName).SID.Value

    # Shell Launcher's config provider lives in its own WMI namespace, not
    # the default root\cimv2.
    $shellLauncherClass = Get-CimInstance -Namespace 'root\standardcimv2\embedded' -ClassName Win32_ShellLauncherClass

    # Fallback shell for any account NOT explicitly configured below
    # (admin/service accounts) stays explorer.exe — only KioskUserName loses
    # the normal shell.
    Invoke-CimMethod -InputObject $shellLauncherClass -MethodName SetDefaultShellLauncher `
        -Arguments @{ Shell = 'explorer.exe'; ShellArguments = '' } | Out-Null

    Invoke-CimMethod -InputObject $shellLauncherClass -MethodName RemoveCustomShellLauncher `
        -Arguments @{ Sid = $sid } -ErrorAction SilentlyContinue | Out-Null
    Invoke-CimMethod -InputObject $shellLauncherClass -MethodName AddCustomShellLauncher `
        -Arguments @{ Sid = $sid; Shell = $AppPath; ShellArguments = '' } | Out-Null

    # v2 addition over v1: define what happens if the kiosk shell process
    # exits/crashes. Action 0 = restart the shell (never fall through to a
    # desktop). See Win32_ShellLauncherClass.DefineRestartAction.
    Invoke-CimMethod -InputObject $shellLauncherClass -MethodName DefineRestartAction `
        -Arguments @{ Sid = $sid; Action = 0 } | Out-Null

    Invoke-CimMethod -InputObject $shellLauncherClass -MethodName SetEnabled -Arguments @{ Enabled = $true } | Out-Null

    if ($rebootPending) {
        Write-Warning '  Client-EmbeddedShellLauncher feature enable requires a reboot before Shell Launcher takes effect.'
    }
    Write-Host '  Shell Launcher V2 configured.' -ForegroundColor Green
}

# ---------------------------------------------------------------------------
# 2. BitLocker — TPM-only unlock protector, no recovery key left on site.
# ---------------------------------------------------------------------------
function Enable-KioskBitLocker {
    param([ValidateSet('AzureAD', 'ActiveDirectory')][string]$EscrowTarget)

    Write-Host '[2/4] Enabling BitLocker (TPM-bound, no on-site recovery key)...' -ForegroundColor Cyan

    $volume = Get-BitLockerVolume -MountPoint 'C:'

    if ($volume.ProtectionStatus -eq 'On' -and ($volume.KeyProtector | Where-Object KeyProtectorType -eq 'Tpm')) {
        Write-Host '  BitLocker already on with a TPM protector; skipping re-enable.' -ForegroundColor DarkGray
    }
    else {
        # TPM-only protector: the volume auto-unlocks on boot as long as the
        # TPM's measured boot state matches, with NO password/PIN prompt at
        # a kiosk that has no keyboard-attending operator. This is the unlock
        # mechanism; it is NOT the recovery mechanism (see below).
        Enable-BitLocker -MountPoint 'C:' -TpmProtector -SkipHardwareTest -ErrorAction Stop | Out-Null
    }

    # A recovery password protector is still required — TPM protectors can
    # fail closed (firmware/BIOS update, disk moved to new hardware) and a
    # volume with ONLY a TPM protector and no recovery protector is
    # unrecoverable in that case. The constraint is "no recovery passwords
    # left ON SITE", not "no recovery protector exists" — so it's created,
    # immediately escrowed off-site, and never written to local disk, a
    # printer, or this script's own console/transcript.
    $existingRecovery = (Get-BitLockerVolume -MountPoint 'C:').KeyProtector |
        Where-Object KeyProtectorType -eq 'RecoveryPassword'

    if (-not $existingRecovery) {
        Add-BitLockerKeyProtector -MountPoint 'C:' -RecoveryPasswordProtector | Out-Null
        $existingRecovery = (Get-BitLockerVolume -MountPoint 'C:').KeyProtector |
            Where-Object KeyProtectorType -eq 'RecoveryPassword'
    }

    foreach ($protector in $existingRecovery) {
        switch ($EscrowTarget) {
            'AzureAD' {
                BackupToAAD-BitLockerKeyProtector -MountPoint 'C:' -KeyProtectorId $protector.KeyProtectorId
            }
            'ActiveDirectory' {
                Backup-BitLockerKeyProtector -MountPoint 'C:' -KeyProtectorId $protector.KeyProtectorId
            }
        }
    }

    Write-Host "  BitLocker on; recovery protector escrowed to $EscrowTarget, nothing persisted locally." -ForegroundColor Green
}

# ---------------------------------------------------------------------------
# 3. Disable USB AutoPlay / AutoRun.
# ---------------------------------------------------------------------------
function Disable-UsbAutoPlayAutoRun {
    Write-Host '[3/4] Disabling USB AutoPlay/AutoRun...' -ForegroundColor Cyan

    $explorerPolicyPaths = @(
        'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer',
        'HKLM:\SOFTWARE\Policies\Microsoft\Windows\Explorer'
    )
    foreach ($path in $explorerPolicyPaths) {
        New-Item -Path $path -Force | Out-Null
        # 0xFF = disable autorun on every drive type (removable, fixed,
        # network, CD, RAM disk, unknown) — kiosks have no legitimate
        # use for autorun on ANY of them.
        Set-ItemProperty -Path $path -Name 'NoDriveTypeAutoRun' -Type DWord -Value 0xFF
        Set-ItemProperty -Path $path -Name 'NoAutorun' -Type DWord -Value 1
    }

    $autoplayHandlerPath = 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\AutoplayHandlers'
    New-Item -Path $autoplayHandlerPath -Force | Out-Null
    Set-ItemProperty -Path $autoplayHandlerPath -Name 'DisableAutoplay' -Type DWord -Value 1

    Write-Host '  AutoPlay/AutoRun disabled machine-wide.' -ForegroundColor Green
}

# ---------------------------------------------------------------------------
# 4. USB device-class restriction: deny mass storage, allow HID + serial/
#    vendor USB-comm classes (barcode scanner, cash recycler bridge, etc.).
# ---------------------------------------------------------------------------
function Set-UsbClassRestrictions {
    Write-Host '[4/4] Restricting USB mass storage while allow-listing HID/serial...' -ForegroundColor Cyan

    # Primary, well-established mechanism: disable the USB Mass Storage
    # driver's service outright. This is class-specific by construction — it
    # does not touch USBSTOR-unrelated classes like HID or COM/serial, so
    # the scanner and the cash-recycler's USB-serial bridge are unaffected
    # without needing an explicit allow-list for them.
    $usbstor = 'HKLM:\SYSTEM\CurrentControlSet\Services\USBSTOR'
    Set-ItemProperty -Path $usbstor -Name 'Start' -Type DWord -Value 4 # 4 = Disabled

    # Belt-and-suspenders: the Device Installation Restrictions policy
    # additionally blocks a NEW mass-storage device from installing at all
    # (not just being usable once installed), and explicitly allow-lists the
    # device setup classes the kiosk's peripherals actually need. Validate
    # this exact value layout against Group Policy's "Device Installation
    # Restrictions" for your Windows 11 IoT Enterprise build (gpedit.msc >
    # Computer Configuration > Administrative Templates > System > Device
    # Installation > Device Installation Restrictions) before fleet rollout.
    $restrictionsRoot = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\DeviceInstall\Restrictions'
    New-Item -Path $restrictionsRoot -Force | Out-Null

    $denyClasses = @(
        '{4D36E967-E325-11CE-BFC1-08002BE10318}' # DiskDrive (USB mass storage enumerates here)
    )
    $allowClasses = @(
        '{745A17A0-74D3-11D0-B6FE-00A0C90F57DA}', # HID (barcode scanner)
        '{4D36E978-E325-11CE-BFC1-08002BE10318}', # Ports (COM & LPT) — vendor USB-serial bridges
        '{36FC9E60-C465-11CF-8056-444553540000}'  # USB (Universal Serial Bus controllers/hubs) — required for the bus itself to enumerate
    )

    New-ItemProperty -Path $restrictionsRoot -Name 'DenyDeviceClasses' -PropertyType MultiString -Value $denyClasses -Force | Out-Null
    New-ItemProperty -Path $restrictionsRoot -Name 'DenyDeviceClassesRetroactive' -PropertyType DWord -Value 1 -Force | Out-Null
    New-ItemProperty -Path $restrictionsRoot -Name 'AllowDeviceClasses' -PropertyType MultiString -Value $allowClasses -Force | Out-Null

    Write-Host '  USB mass storage denied; HID + serial/vendor comm classes allow-listed.' -ForegroundColor Green
}

# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------
Assert-Prerequisites

if (-not $SkipShellLauncher) { Set-ShellLauncherConfiguration -UserName $KioskUserName -AppPath $KioskAppPath }
if (-not $SkipBitLocker) { Enable-KioskBitLocker -EscrowTarget $BitLockerRecoveryEscrowTarget }
if (-not $SkipAutoPlayHardening) { Disable-UsbAutoPlayAutoRun }
if (-not $SkipUsbClassRestriction) { Set-UsbClassRestrictions }

Write-Host "`nProvisioning complete. Reboot required for Shell Launcher V2 to take effect." -ForegroundColor Yellow
