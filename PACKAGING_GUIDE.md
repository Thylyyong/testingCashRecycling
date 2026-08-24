# Self-Checkout Kiosk V2 — Standalone Packaging Guide

This guide documents the exact, verified procedure to package the Self-Checkout Kiosk into a standalone, portable distribution (`dist/SelfCheckoutKiosk/`).

The packaged application **runs identically to pressing F5 in Visual Studio**: double-clicking `SelfCheckoutKiosk.App.exe` (or `Start-Kiosk.bat`) automatically starts both the WinUI 3 touch application and the Cash Device REST API background daemon seamlessly.

---

## 📁 Package Layout (`dist/SelfCheckoutKiosk/`)

All Cash API files, native libraries, and simulator dependencies are cleanly isolated inside the `CashAPI/` subfolder so that the application root remains minimal and organized:

```
dist/SelfCheckoutKiosk/
│
├── SelfCheckoutKiosk.App.exe        # Main WinUI 3 touch application (launches app & manages Cash API)
├── Start-Kiosk.bat                  # 1-Click launcher (checks license & launches kiosk)
├── resources.pri                    # WinUI 3 compiled resource index
├── SelfCheckoutKiosk.App.pri        # App resource index
├── license.token                    # Machine-locked offline cryptographic license
│
├── Assets/                          # UI assets (images, flags, icons, fonts, sound effects)
├── Config/                          # Runtime JSON configuration (hardware, branding, store rules)
│
├── CashAPI/                         # ISOLATED SUBFOLDER for all Cash Device REST API files
│   ├── CashDevice-RestAPI.exe       # ITL Hardware REST API daemon (primary)
│   ├── CashDeviceSimulator.exe      # Standalone fallback Cash API simulator
│   ├── runtimes/                    # Native serial/USB communication runtimes
│   └── *.dll, *.json                # API dependencies and configs
│
└── LicenseGenerator/                # Standalone offline license generator tool
    └── GenerateLicense.exe
```

---

## ⚙️ How It Works (The "F5" Auto-Start Engine)

1. **Auto-Discovery:** When `SelfCheckoutKiosk.App.exe` launches, `CashApiProcessManager` searches `dist/SelfCheckoutKiosk/CashAPI/` for `CashDevice-RestAPI.exe` or `CashDeviceSimulator.exe`.
2. **Background Daemon Execution:** It launches the API daemon in the background (`CreateNoWindow = true`, `WindowStyle = Hidden`) and probes `http://127.0.0.1:5000` / `http://127.0.0.1:5055` until it responds.
3. **Graceful Teardown:** When the kiosk application is closed (or attendant exits), the background API process is automatically terminated with no lingering orphaned processes.

---

## 🛠️ Step-by-Step Manual Packaging Commands

Open **PowerShell** in the repository root (`c:\Users\viti\source\repos\SelfCheckoutKiosk-V2 - Merge`):

### Step 1: Clean and Prepare Output Directory
```powershell
if (Test-Path "dist") { Remove-Item -Recurse -Force "dist" }
New-Item -ItemType Directory -Force -Path "dist\SelfCheckoutKiosk"
```

### Step 2: Publish the WinUI 3 App (Unpackaged, Self-Contained)
```powershell
dotnet publish src\SelfCheckoutKiosk.App\SelfCheckoutKiosk.App.csproj `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:WindowsPackageType=None `
    -p:WindowsAppSDKSelfContained=true `
    -o dist\SelfCheckoutKiosk
```

### Step 3: Copy WinUI 3 PRI Resource Files
```powershell
Copy-Item "src\SelfCheckoutKiosk.App\bin\x64\Release\net10.0-windows10.0.19041.0\win-x64\SelfCheckoutKiosk.App.pri" "dist\SelfCheckoutKiosk\resources.pri" -Force
Copy-Item "src\SelfCheckoutKiosk.App\bin\x64\Release\net10.0-windows10.0.19041.0\win-x64\SelfCheckoutKiosk.App.pri" "dist\SelfCheckoutKiosk\SelfCheckoutKiosk.App.pri" -Force
```

### Step 4: Package Cash API & Simulator into `dist\SelfCheckoutKiosk\CashAPI\`
```powershell
# 1. Create the isolated CashAPI directory
New-Item -ItemType Directory -Force -Path "dist\SelfCheckoutKiosk\CashAPI"

# 2. Copy the ITL REST API release package
Copy-Item "CashDevice-REST-API-V1.6.1-RC.4-Net8.0\*" "dist\SelfCheckoutKiosk\CashAPI" -Recurse -Force

# 3. Publish the fallback Cash Device Simulator into the same CashAPI folder
dotnet publish tools\CashDeviceSimulator\CashDeviceSimulator.csproj `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -o dist\SelfCheckoutKiosk\CashAPI
```

### Step 5: Package License Generator & Generate Token
```powershell
# 1. Publish DevLicenseTokenGenerator
New-Item -ItemType Directory -Force -Path "dist\SelfCheckoutKiosk\LicenseGenerator"
dotnet publish tools\DevLicenseTokenGenerator\DevLicenseTokenGenerator.csproj `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -o dist\SelfCheckoutKiosk\LicenseGenerator

# 2. Generate node-locked license.token for this device
& "dist\SelfCheckoutKiosk\LicenseGenerator\GenerateLicense.exe" --silent --output "dist\SelfCheckoutKiosk\license.token"
```

### Step 6: Create 1-Click `Start-Kiosk.bat` Launcher
```powershell
Set-Content -Path "dist\SelfCheckoutKiosk\Start-Kiosk.bat" -Value @'
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
'@
```

---

## ⚡ 1-Click Complete Packaging Automation

You can build and package the standalone distribution in one click:

### Option A: Double-Click `Package-Distribution.bat` (Recommended)
Simply double-click [`Package-Distribution.bat`](file:///c:/Users/viti/source/repos/SelfCheckoutKiosk-V2%20-%20Merge/Package-Distribution.bat) in the repository root. It runs tests, cleans, builds, packages, and pauses at the end so you can inspect the output without the window closing.

### Option B: Run PowerShell Script
```powershell
.\Package-Distribution.ps1
```
Or to skip running tests:
```powershell
.\Package-Distribution.ps1 -SkipTests
```

---

## 🛠️ Step-by-Step Manual Packaging Commands

# 1. Clean output folder
if (Test-Path 'dist') { Remove-Item -Recurse -Force 'dist' };
New-Item -ItemType Directory -Force -Path 'dist\SelfCheckoutKiosk' | Out-Null;

# 2. Publish Main App
Write-Host '[1/5] Publishing WinUI 3 Touch App...' -ForegroundColor Yellow;
dotnet publish src\SelfCheckoutKiosk.App\SelfCheckoutKiosk.App.csproj -c Release -r win-x64 --self-contained true -p:WindowsPackageType=None -p:WindowsAppSDKSelfContained=true -o dist\SelfCheckoutKiosk;

# 3. Copy PRI resource indexes
Write-Host '[2/5] Copying PRI Resource Indexes...' -ForegroundColor Yellow;
Copy-Item 'src\SelfCheckoutKiosk.App\bin\x64\Release\net10.0-windows10.0.19041.0\win-x64\SelfCheckoutKiosk.App.pri' 'dist\SelfCheckoutKiosk\resources.pri' -Force;
Copy-Item 'src\SelfCheckoutKiosk.App\bin\x64\Release\net10.0-windows10.0.19041.0\win-x64\SelfCheckoutKiosk.App.pri' 'dist\SelfCheckoutKiosk\SelfCheckoutKiosk.App.pri' -Force;

# 4. Package Cash API into isolated subfolder
Write-Host '[3/5] Packaging Cash API into CashAPI/ subfolder...' -ForegroundColor Yellow;
New-Item -ItemType Directory -Force -Path 'dist\SelfCheckoutKiosk\CashAPI' | Out-Null;
Copy-Item 'CashDevice-REST-API-V1.6.1-RC.4-Net8.0\*' 'dist\SelfCheckoutKiosk\CashAPI' -Recurse -Force;
dotnet publish tools\CashDeviceSimulator\CashDeviceSimulator.csproj -c Release -r win-x64 --self-contained true -o dist\SelfCheckoutKiosk\CashAPI;

# 5. Package License Generator & Generate Token
Write-Host '[4/5] Publishing License Generator & Creating Token...' -ForegroundColor Yellow;
New-Item -ItemType Directory -Force -Path 'dist\SelfCheckoutKiosk\LicenseGenerator' | Out-Null;
dotnet publish tools\DevLicenseTokenGenerator\DevLicenseTokenGenerator.csproj -c Release -r win-x64 --self-contained true -o dist\SelfCheckoutKiosk\LicenseGenerator;
& 'dist\SelfCheckoutKiosk\LicenseGenerator\GenerateLicense.exe' --silent --output 'dist\SelfCheckoutKiosk\license.token';

# 6. Create Start-Kiosk.bat
Write-Host '[5/5] Creating Start-Kiosk.bat...' -ForegroundColor Yellow;
Set-Content -Path 'dist\SelfCheckoutKiosk\Start-Kiosk.bat' -Value '@echo off`ntitle Self-Checkout Kiosk`ncd /d `\"%~dp0`\"`n`nif not exist `\"license.token`\" (`n    if exist `\"LicenseGenerator\GenerateLicense.exe`\" (`n        `\"LicenseGenerator\GenerateLicense.exe`\" --silent --output `\"license.token`\"`n    )`n)`n`nstart `\"`\" `\"%~dp0SelfCheckoutKiosk.App.exe`\"`n';

Write-Host '==================================================' -ForegroundColor Green;
Write-Host '  PACKAGING COMPLETE: dist/SelfCheckoutKiosk/     ' -ForegroundColor Green;
Write-Host '==================================================' -ForegroundColor Green;
"
```

---

## 🚀 How to Run the Packaged Kiosk

1. Open `dist\SelfCheckoutKiosk\` in File Explorer.
2. Double-click **`Start-Kiosk.bat`** (or **`SelfCheckoutKiosk.App.exe`**).
3. The application will:
   - Verify/generate `license.token`.
   - Start the Cash API background daemon from `CashAPI/`.
   - Open the full touchscreen Kiosk UI.

---

---

## 🖥️ Target Machine Prerequisites & Multi-Device Setup

If moving `dist\SelfCheckoutKiosk\` to a clean PC or other device where the cash acceptor does not connect, verify the following:

### 1. Install .NET 8.0 ASP.NET Core Runtime (Required for ITL Cash API)
- The main WinUI 3 kiosk application is self-contained (.NET 10), but Innovative Technology's third-party **`CashDevice-RestAPI.exe`** is a .NET 8.0 framework-dependent web service.
- On any target machine without .NET 8, download and install the **ASP.NET Core 8.0 Runtime (x64)**:
  - Official Download: [Microsoft .NET 8.0 Downloads](https://dotnet.microsoft.com/download/dotnet/8.0)
  - Or via winget: `winget install Microsoft.DotNet.AspNetCore.8`

### 2. Install Innovative Technology USB-SSP Driver
- Windows requires the ITL Virtual COM driver (`VID_2424`) to assign a `COM` port to the NV200/NV11 validator.
- Open Windows **Device Manager** $\rightarrow$ **Ports (COM & LPT)**.
- Ensure an ITL USB serial port (e.g. `COM3`, `COM4`, `COM5`, `COM6`) is listed.
- If it appears under *Other Devices* with a yellow exclamation mark, install the driver from the ITL SDK.

### 3. Check Validator 12V/24V Power Harness
- The USB cable only supplies serial data. The NV200/NV11 validator requires dedicated 12V/24V power. Ensure the bezel/acceptor LEDs are illuminated.

### 4. Run `Test-Hardware.bat` on the Target Machine
- A diagnostic utility [`dist\SelfCheckoutKiosk\Test-Hardware.bat`](file:///c:/Users/viti/source/repos/SelfCheckoutKiosk-V2%20-%20Merge/dist/SelfCheckoutKiosk/Test-Hardware.bat) is included in the package.
- Double-click `Test-Hardware.bat` on the target machine to test .NET 8 runtime installation, list active COM ports, and verify Cash API readiness.

---

## 🧪 Pre-Packaging Verification Checklist

Before packaging for release, always run the full test matrix:

```powershell
# 1. Debug Build & Tests
dotnet build -c Debug SelfCheckoutKiosk.sln
dotnet test -c Debug SelfCheckoutKiosk.sln

# 2. Release Build & Tests
dotnet build -c Release SelfCheckoutKiosk.sln
dotnet test -c Release SelfCheckoutKiosk.sln
```
All **98 unit & integration tests** must pass with **0 Errors and 0 Warnings**.
