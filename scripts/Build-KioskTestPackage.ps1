[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $DevelopmentPublicKeyPath,

    [Parameter(Mandatory)]
    [ValidatePattern('^COM[0-9]{1,3}$')]
    [string] $ComPort,

    [ValidateRange(0, 127)]
    [int] $SspAddress = 0,

    [string] $CashDeviceUsername,

    [Security.SecureString] $CashDevicePassword,

    [string] $CashDeviceSourceDirectory,

    [string] $OutputDirectory,

    [ValidateRange(1, 60000)]
    [int] $PollIntervalMs = 200,

    [ValidateRange(100, 60000)]
    [int] $RequestTimeoutMs = 5000,

    [ValidateRange(1, 20)]
    [int] $MaximumPollFailures = 3
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Invoke-Checked {
    param(
        [Parameter(Mandatory)][string] $Executable,
        [Parameter(Mandatory)][string[]] $Arguments
    )

    & $Executable @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Executable failed with exit code $LASTEXITCODE."
    }
}

function Resolve-PathInside {
    param(
        [Parameter(Mandatory)][string] $Candidate,
        [Parameter(Mandatory)][string] $Parent,
        [Parameter(Mandatory)][string] $Description
    )

    $resolvedCandidate = [IO.Path]::GetFullPath($Candidate)
    $resolvedParent = [IO.Path]::GetFullPath($Parent)
    $relative = [IO.Path]::GetRelativePath($resolvedParent, $resolvedCandidate)

    if ($relative -eq '.') {
        throw "$Description must be a child of $resolvedParent"
    }

    if ($relative -eq '..' -or
        $relative.StartsWith('..' + [IO.Path]::DirectorySeparatorChar, [StringComparison]::Ordinal)) {
        throw "$Description must remain inside $resolvedParent"
    }

    return $resolvedCandidate
}

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifactsRoot = Join-Path $repositoryRoot 'artifacts'
[IO.Directory]::CreateDirectory($artifactsRoot) | Out-Null

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $artifactsRoot 'SelfCheckoutKiosk-Test-x64'
}

if ([string]::IsNullOrWhiteSpace($CashDeviceSourceDirectory)) {
    $CashDeviceSourceDirectory = Join-Path $repositoryRoot `
        'CashDevice-REST-API-V1.6.1-RC.4-Net8.0'
}

$finalPackageRoot = Resolve-PathInside `
    -Candidate $OutputDirectory `
    -Parent $artifactsRoot `
    -Description 'The package output path'
$packageRoot = Resolve-PathInside `
    -Candidate (Join-Path $artifactsRoot '.kiosk-package-staging') `
    -Parent $artifactsRoot `
    -Description 'The package staging path'
$applicationPublish = Resolve-PathInside `
    -Candidate (Join-Path $artifactsRoot '.kiosk-app-publish') `
    -Parent $artifactsRoot `
    -Description 'The application publish path'
$generatorPublish = Resolve-PathInside `
    -Candidate (Join-Path $artifactsRoot '.license-generator-publish') `
    -Parent $artifactsRoot `
    -Description 'The generator publish path'

if (@($packageRoot, $applicationPublish, $generatorPublish) |
    Where-Object {
        $finalPackageRoot.Equals(
            $_,
            [StringComparison]::OrdinalIgnoreCase)
    }) {
    throw 'The package output path conflicts with an internal temporary path.'
}

$cashDeviceSource = [IO.Path]::GetFullPath($CashDeviceSourceDirectory)
$publicKeyPath = [IO.Path]::GetFullPath($DevelopmentPublicKeyPath)

if (-not (Test-Path -LiteralPath $publicKeyPath -PathType Leaf)) {
    throw "The approved development public key was not found: $publicKeyPath"
}

$publicKeyPem = [IO.File]::ReadAllText($publicKeyPath)
if ($publicKeyPem.Contains('PRIVATE KEY', [StringComparison]::Ordinal)) {
    throw 'DevelopmentPublicKeyPath contains private signing material.'
}

if (-not $publicKeyPem.Contains('BEGIN PUBLIC KEY', [StringComparison]::Ordinal) -and
    -not $publicKeyPem.Contains('BEGIN RSA PUBLIC KEY', [StringComparison]::Ordinal)) {
    throw 'DevelopmentPublicKeyPath is not a supported RSA public-key PEM.'
}

$requiredVendorFiles = @(
    'CashDevice-RestAPI.exe',
    'CashDevice-RestAPI.dll',
    'CashDevice-RestAPI.deps.json',
    'CashDevice-RestAPI.runtimeconfig.json',
    'serverconfig.json',
    'users.json',
    'ITLCashDevice.dll',
    'ITLComms.dll'
)

foreach ($requiredFile in $requiredVendorFiles) {
    $candidate = Join-Path $cashDeviceSource $requiredFile
    if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
        throw "The approved RC.4 runtime is incomplete; missing $requiredFile"
    }
}

$serverConfiguration = Get-Content -Raw `
    -LiteralPath (Join-Path $cashDeviceSource 'serverconfig.json') |
    ConvertFrom-Json
$serverPort = [int]$serverConfiguration.ServerConfig.Port

if ($serverPort -lt 1 -or $serverPort -gt 65535) {
    throw 'The RC.4 serverconfig.json contains an invalid HTTP port.'
}

if ([string]::IsNullOrWhiteSpace($CashDeviceUsername)) {
    $userConfiguration = Get-Content -Raw `
        -LiteralPath (Join-Path $cashDeviceSource 'users.json') |
        ConvertFrom-Json
    $CashDeviceUsername = [string]$userConfiguration.Username
}

if ([string]::IsNullOrWhiteSpace($CashDeviceUsername)) {
    throw 'CashDeviceUsername could not be resolved from the approved runtime.'
}

if ($null -eq $CashDevicePassword) {
    $configuredCashPassword =
        $env:SELF_CHECKOUT_CASH_DEVICE_PASSWORD

    if ([string]::IsNullOrWhiteSpace($configuredCashPassword)) {
        $CashDevicePassword = Read-Host `
            'CashDevice REST password for the approved test account' `
            -AsSecureString
    }
    else {
        $CashDevicePassword = ConvertTo-SecureString `
            $configuredCashPassword `
            -AsPlainText `
            -Force
        $configuredCashPassword = $null
    }
}

$passwordPointer = [IntPtr]::Zero
$plainPassword = $null

try {
    $passwordPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR(
        $CashDevicePassword)
    $plainPassword = [Runtime.InteropServices.Marshal]::PtrToStringBSTR(
        $passwordPointer)

    if ([string]::IsNullOrWhiteSpace($plainPassword)) {
        throw 'The CashDevice REST password cannot be empty.'
    }

    if (Test-Path -LiteralPath $packageRoot) {
        $verifiedTarget = Resolve-PathInside `
            -Candidate $packageRoot `
            -Parent $artifactsRoot `
            -Description 'The package cleanup target'
        Remove-Item -LiteralPath $verifiedTarget -Recurse -Force
    }

    foreach ($temporaryPublish in @($applicationPublish, $generatorPublish)) {
        if (Test-Path -LiteralPath $temporaryPublish) {
            $verifiedPublish = Resolve-PathInside `
                -Candidate $temporaryPublish `
                -Parent $artifactsRoot `
                -Description 'The temporary publish path'
            Remove-Item -LiteralPath $verifiedPublish -Recurse -Force
        }
    }

    Invoke-Checked dotnet @(
        'restore',
        (Join-Path $repositoryRoot 'src\SelfCheckoutKiosk.App\SelfCheckoutKiosk.App.csproj'),
        '-r', 'win-x64',
        '-p:Platform=x64',
        '-p:EnableDevelopmentLicense=true'
    )

    Invoke-Checked dotnet @(
        'restore',
        (Join-Path $repositoryRoot 'tools\SelfCheckoutKiosk.DevLicenseTokenGenerator\SelfCheckoutKiosk.DevLicenseTokenGenerator.csproj'),
        '-r', 'win-x64'
    )

    Invoke-Checked dotnet @(
        'publish',
        (Join-Path $repositoryRoot 'src\SelfCheckoutKiosk.App\SelfCheckoutKiosk.App.csproj'),
        '-c', 'Release',
        '-r', 'win-x64',
        '-p:Platform=x64',
        '-p:EnableDevelopmentLicense=true',
        '-p:WindowsPackageType=None',
        '-p:WindowsAppSDKSelfContained=true',
        '--self-contained', 'true',
        '--no-restore',
        '-o', $applicationPublish
    )

    Invoke-Checked dotnet @(
        'publish',
        (Join-Path $repositoryRoot 'tools\SelfCheckoutKiosk.DevLicenseTokenGenerator\SelfCheckoutKiosk.DevLicenseTokenGenerator.csproj'),
        '-c', 'Release',
        '-r', 'win-x64',
        '--self-contained', 'true',
        '-p:PublishSingleFile=true',
        '-p:PublishTrimmed=false',
        '--no-restore',
        '-o', $generatorPublish
    )

    Copy-Item -Path (Join-Path $applicationPublish '*') `
        -Destination $packageRoot -Recurse -Force

    Get-ChildItem -LiteralPath $packageRoot -Filter '*.pdb' -File -Recurse |
        Remove-Item -Force

    $configDirectory = Join-Path $packageRoot 'Config'
    $toolsDirectory = Join-Path $packageRoot 'Tools'
    $logsDirectory = Join-Path $packageRoot 'logs'
    $cashDeviceParent = Join-Path $packageRoot 'CashDevice'
    $cashDeviceTarget = Join-Path $cashDeviceParent `
        'CashDevice-REST-API-V1.6.1-RC.4-Net8.0'

    [IO.Directory]::CreateDirectory($configDirectory) | Out-Null
    [IO.Directory]::CreateDirectory($toolsDirectory) | Out-Null
    [IO.Directory]::CreateDirectory($logsDirectory) | Out-Null
    [IO.Directory]::CreateDirectory($cashDeviceTarget) | Out-Null

    [IO.File]::WriteAllText(
        (Join-Path $configDirectory 'development-license-public.pem'),
        $publicKeyPem)

    $testConfiguration = [ordered]@{
        environment = 'Development'
        licenseMode = 'Development'
        cashHardwareMode = 'Physical'
        developmentPublicKeyRelativePath = 'Config\development-license-public.pem'
        cashDevice = [ordered]@{
            baseUrl = "http://127.0.0.1:$serverPort/"
            username = $CashDeviceUsername
            password = $plainPassword
            comPort = $ComPort.ToUpperInvariant()
            currency = 'USD,KHR'
            sspAddress = $SspAddress
            encryptionKey = $null
            pollIntervalMs = $PollIntervalMs
            requestTimeoutMs = $RequestTimeoutMs
            maximumPollFailures = $MaximumPollFailures
        }
    }

    $testConfiguration |
        ConvertTo-Json -Depth 5 |
        Set-Content -LiteralPath `
            (Join-Path $configDirectory 'kiosk-test.json') `
            -Encoding utf8NoBOM

    $generatorExecutable = Join-Path $generatorPublish `
        'DevLicenseTokenGenerator.exe'
    if (-not (Test-Path -LiteralPath $generatorExecutable -PathType Leaf)) {
        throw 'DevLicenseTokenGenerator.exe was not produced by publish.'
    }

    Copy-Item -LiteralPath $generatorExecutable `
        -Destination (Join-Path $toolsDirectory 'DevLicenseTokenGenerator.exe')
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'scripts\Generate-DevLicense.ps1') `
        -Destination (Join-Path $toolsDirectory 'Generate-DevLicense.ps1')

    Get-ChildItem -LiteralPath $cashDeviceSource -Force |
        Where-Object { $_.Name -ne 'REST API Logs' } |
        Copy-Item -Destination $cashDeviceTarget -Recurse -Force
    [IO.Directory]::CreateDirectory(
        (Join-Path $cashDeviceTarget 'REST API Logs')) | Out-Null

    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'Data\products.txt') `
        -Destination (Join-Path $packageRoot 'products.txt')

    $packageReadme = @"
SelfCheckoutKiosk controlled x64 development/hardware test package

Runtime: CashDevice-REST-API-V1.6.1-RC.4-Net8.0 (release candidate)
Launch: .\SelfCheckoutKiosk.App.exe

Development activation:
1. Launch the kiosk and copy the displayed HardwareId.
2. Run .\Tools\Generate-DevLicense.ps1 -PrivateKeyPath <external-approved-private-key.pem>
3. Paste the signed token into the activation screen.

The development private key must remain external to this folder.
The CashDevice REST credential in Config\kiosk-test.json is test-only;
protect this controlled package accordingly.

Physical payout remains disabled. Use exact-cash one-note testing only.
"@
    [IO.File]::WriteAllText(
        (Join-Path $packageRoot 'README-TEST-PACKAGE.txt'),
        $packageReadme)

    $sourceFiles = Get-ChildItem -LiteralPath $packageRoot -File -Recurse |
        Where-Object { $_.Extension -in @('.cs', '.xaml', '.sln', '.csproj') }
    if ($sourceFiles) {
        throw 'Source-code files were found in the generated test package.'
    }

    $forbiddenKeyFiles = Get-ChildItem -LiteralPath $packageRoot -File -Recurse |
        Where-Object {
            $_.Extension -in @(
                '.key', '.pfx', '.p12', '.ppk', '.jks', '.keystore') -or
            ($_.Extension -eq '.pem' -and
             $_.Name -ne 'development-license-public.pem')
        }
    if ($forbiddenKeyFiles) {
        throw 'Forbidden private-key file types were found in the test package.'
    }

    $textFiles = Get-ChildItem -LiteralPath $packageRoot -File -Recurse |
        Where-Object {
            $_.Extension -in @(
                '.json', '.config', '.txt', '.ps1', '.pem', '.xml',
                '.env', '.yaml', '.yml')
        }

    foreach ($textFile in $textFiles) {
        if (Select-String -LiteralPath $textFile.FullName `
            -Pattern '-----BEGIN (RSA |EC |OPENSSH )?PRIVATE KEY-----' `
            -Quiet) {
            throw 'Private signing material was found in the test package.'
        }

        if (Select-String -LiteralPath $textFile.FullName `
            -SimpleMatch $repositoryRoot -Quiet) {
            throw "A repository-absolute path was found in $($textFile.FullName)"
        }
    }

    if (-not (Test-Path -LiteralPath `
        (Join-Path $packageRoot 'SelfCheckoutKiosk.App.exe') -PathType Leaf)) {
        throw 'The published kiosk executable is missing from the package.'
    }

    if (-not (Test-Path -LiteralPath `
        (Join-Path $cashDeviceTarget 'CashDevice-RestAPI.exe') -PathType Leaf)) {
        throw 'The RC.4 server executable is missing from the package.'
    }

    if (Test-Path -LiteralPath $finalPackageRoot) {
        $verifiedFinalTarget = Resolve-PathInside `
            -Candidate $finalPackageRoot `
            -Parent $artifactsRoot `
            -Description 'The final package replacement target'
        Remove-Item -LiteralPath $verifiedFinalTarget -Recurse -Force
    }

    [IO.Directory]::CreateDirectory(
        ([IO.Path]::GetDirectoryName($finalPackageRoot))) | Out-Null
    Move-Item -LiteralPath $packageRoot -Destination $finalPackageRoot

    Write-Host 'Kiosk test package created and security-checked:'
    Write-Host $finalPackageRoot
}
finally {
    if ($passwordPointer -ne [IntPtr]::Zero) {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($passwordPointer)
    }

    $plainPassword = $null

    foreach ($temporaryPath in @(
        $applicationPublish,
        $generatorPublish,
        $packageRoot)) {
        if (Test-Path -LiteralPath $temporaryPath) {
            $verifiedTemporaryPath = Resolve-PathInside `
                -Candidate $temporaryPath `
                -Parent $artifactsRoot `
                -Description 'The temporary packaging cleanup path'
            Remove-Item -LiteralPath $verifiedTemporaryPath -Recurse -Force
        }
    }
}
