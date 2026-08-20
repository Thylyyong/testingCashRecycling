[CmdletBinding()]
param(
    [ValidatePattern('^[A-Za-z0-9._:-]{4,128}$')]
    [string] $HardwareId,

    [ValidateRange(1, 30)]
    [int] $ExpiresInDays = 7,

    [string] $PrivateKeyPath,

    [string] $OutputTokenPath = (Join-Path $PSScriptRoot 'development-license.token'),

    [string] $GeneratorPath = (Join-Path $PSScriptRoot 'DevLicenseTokenGenerator.exe'),

    [string] $ExpectedPublicKeyPath =
        (Join-Path (Split-Path -Parent $PSScriptRoot) 'Config\development-license-public.pem')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($HardwareId)) {
    $HardwareId = Read-Host 'HardwareId shown by the kiosk activation screen'
}

if ([string]::IsNullOrWhiteSpace($PrivateKeyPath)) {
    $PrivateKeyPath = $env:SELF_CHECKOUT_DEV_PRIVATE_KEY_PATH
}

if ([string]::IsNullOrWhiteSpace($PrivateKeyPath)) {
    throw 'An external development private key is required. Supply -PrivateKeyPath or set SELF_CHECKOUT_DEV_PRIVATE_KEY_PATH.'
}

$resolvedGenerator = [IO.Path]::GetFullPath($GeneratorPath)
$resolvedPrivateKey = [IO.Path]::GetFullPath($PrivateKeyPath)
$resolvedPublicKey = [IO.Path]::GetFullPath($ExpectedPublicKeyPath)
$resolvedOutput = [IO.Path]::GetFullPath($OutputTokenPath)

if (-not (Test-Path -LiteralPath $resolvedGenerator -PathType Leaf)) {
    throw "DevLicenseTokenGenerator.exe was not found: $resolvedGenerator"
}

if (-not (Test-Path -LiteralPath $resolvedPrivateKey -PathType Leaf)) {
    throw "The external development private key was not found: $resolvedPrivateKey"
}

if (-not (Test-Path -LiteralPath $resolvedPublicKey -PathType Leaf)) {
    throw "The package development public key was not found: $resolvedPublicKey"
}

& $resolvedGenerator `
    --hardware-id $HardwareId `
    --expires-in-days $ExpiresInDays `
    --private-key $resolvedPrivateKey `
    --expected-public-key $resolvedPublicKey `
    --output $resolvedOutput

if ($LASTEXITCODE -ne 0) {
    throw "Development license generation failed with exit code $LASTEXITCODE."
}

$token = [IO.File]::ReadAllText($resolvedOutput).Trim()

if (Get-Command Set-Clipboard -ErrorAction SilentlyContinue) {
    Set-Clipboard -Value $token
    Write-Host 'The signed token was copied to the clipboard.'
}
else {
    Write-Host 'Clipboard integration is unavailable; copy the token from:'
    Write-Host $resolvedOutput
}

Write-Host 'Paste the token into the kiosk Development Activation screen.'
