# Self-Checkout Kiosk System V2 — Enterprise Edition

[![Build Status](https://img.shields.io/badge/build-passing-brightgreen.svg)]()
[![Platform](https://img.shields.io/badge/platform-Windows%2010%20%2F%2011%20x64-blue.svg)]()
[![Architecture](https://img.shields.io/badge/architecture-Clean%20Architecture%20%2B%20WinUI%203-darkblue.svg)]()
[![License](https://img.shields.io/badge/license-Enterprise%20Proprietary-red.svg)](LICENSE)

An offline-first, high-reliability touch kiosk system designed for supermarkets and retail stores running on Windows 10/11 x64. Features dual-currency transactions (USD / KHR), offline cryptographic node-locked licensing, dynamic store branding, and direct hardware abstraction for thermal printers, barcode scanners, and cash recyclers.

---

## ⚡ Quick Start & Development

### 1. Launch via Helper Script
Double-click **[`scripts/QuickStart-Kiosk.bat`](scripts/QuickStart-Kiosk.bat)**. It automatically ensures a license token exists, launches the Cash Device API bridge, and opens the kiosk WinUI 3 UI.

### 2. Run via .NET CLI
```powershell
# 1. Generate local dev license token
scripts\Generate-License.bat

# 2. Run WinUI 3 application
dotnet run --project src\SelfCheckoutKiosk.App\SelfCheckoutKiosk.App.csproj -p:WindowsPackageType=None
```

### 3. Standalone Packaging Guide
See **[`PACKAGING_GUIDE.md`](PACKAGING_GUIDE.md)** for instructions on generating the portable `dist/SelfCheckoutKiosk/` release with the isolated `CashAPI/` subfolder.

---

## 📦 Project Structure

```
├── scripts/                                  # Development & Automation Scripts
│   ├── QuickStart-Kiosk.bat                  # Fast development launcher
│   └── Generate-License.bat                  # Standalone node-locked machine license generator
├── src/
│   ├── SelfCheckoutKiosk.Domain             # Core entity models & business rules (0 dependencies)
│   ├── SelfCheckoutKiosk.Core               # Contracts, currency math, licensing engine, HAL interfaces
│   ├── SelfCheckoutKiosk.Infrastructure     # SQLCipher database, hardware ID provider, local sync
│   ├── SelfCheckoutKiosk.Presentation       # MVVM view models, state machines, business workflows
│   ├── SelfCheckoutKiosk.App                # WinUI 3 desktop composition root & XAML views
│   ├── SelfCheckoutKiosk.Hal.Vendor.EpsonM30 # Thermal receipt printer driver (ESC/POS & WinSpool)
│   ├── SelfCheckoutKiosk.Hal.Vendor.DatalogicScanner # Barcode & 2D scanner serial/USB driver
│   └── SelfCheckoutKiosk.Hal.Vendor.ItlRestCashRecycler # ITL NV200/NV11 Cash recycler REST client
├── tools/
│   ├── CashDeviceSimulator                  # Cash Recycler REST API Daemon Simulator (Port 5055)
│   └── DevLicenseTokenGenerator             # Node-locked cryptographic license generator (GenerateLicense.exe)
├── tests/                                   # Unit and Integration test projects
├── Directory.Build.props                    # Shared Roslyn analyzer and build properties
├── LICENSE                                  # Enterprise EULA & Terms of Use
├── README.md                                # System & deployment documentation
└── SelfCheckoutKiosk.sln                    # Visual Studio 2022/2026 Solution
```

---

## 🔌 Hardware & Peripherals Support

| Peripheral | Supported Hardware | Protocol / Connection | Fallback / Simulation |
| :--- | :--- | :--- | :--- |
| **Receipt Printer** | Epson EU-m30, TM-m30, TM-T88, Generic ESC/POS (80mm / 48-col) | USB Raw Spool (`winspool.drv`), Serial COM, TCP | File / Virtual Spooler |
| **Barcode Scanner** | Datalogic Gryphon, QuickScan, Generic USB HID / CDC-ACM | Serial COM Port (with noise filtering & suffix parsing) | Software barcode input / Keyboard emulation |
| **Cash Recycler** | ITL NV200 Spectral, NV11, Smart Payout | ITL REST Bridge (e.g. `CashDeviceSimulator.exe` on Port 5055/5000) | Local Cash Simulator API Daemon |
| **QR Payment** | Dynamic KHQR (Bakong), Generic Payment QR | REST / WebSocket QR Gateway | Mock Payment Generator |

---

## 🔑 Offline Node-Locked Licensing

The system uses ECDSA P-256 / SHA-256 cryptographic signatures tied to the physical machine's hardware ID (motherboard UUID, CPU processor ID, and BIOS serial).

1. To generate a license for the current machine:
   ```cmd
   scripts\Generate-License.bat
   ```
2. The generated `license.token` is placed in the application root and automatically validated on startup with zero internet access required.
3. If hardware components change, run `scripts\Generate-License.bat` again to refresh the node lock.

---

## 📄 License

This software is licensed under the **Enterprise Proprietary License Agreement**. See the [`LICENSE`](LICENSE) file for complete terms and conditions.
