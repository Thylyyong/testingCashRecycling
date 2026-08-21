<#
.SYNOPSIS
    Root Installer entrypoint for Self-Checkout Kiosk System V2.
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

$scriptPath = Join-Path $PSScriptRoot "scripts\Install-Kiosk.ps1"
& $scriptPath -InstallPath $InstallPath `
              -Silent:$Silent `
              -AutoStartKiosk:$AutoStartKiosk `
              -BuildFromSource:$BuildFromSource `
              -NoShortcuts:$NoShortcuts `
              -NoFirewall:$NoFirewall
