# Self-Checkout Kiosk V2 — Engineering Execution Plan & Task Tracker

> **Document ID:** `ENG-TASK-2026-V2`  
> **Status:** Active Execution Tracker  
> **Scope:** Currency Rounding Rules, UI Attendant Assistance, Receipt Printer Reliability, Bill Acceptor Stabilization, Production Packaging & Root Cleanup  
> **Companion Documents:** [tracker.md](file:///c:/Users/viti/source/repos/SelfCheckoutKiosk-V2%20-%20Merge/tracker.md) · [Kiosk_Architectural_Blueprint.md](file:///c:/Users/viti/source/repos/SelfCheckoutKiosk-V2%20-%20Merge/src/Kiosk_Architectural_Blueprint.md)

---

## 🧭 Executive Summary & Priority Matrix

| Task ID | Work Package | Priority | Status | Target Area |
| :--- | :--- | :---: | :---: | :--- |
| **WP-01** | [Dual-Currency KHR Round-Up on Total Calculation](#wp-01-dual-currency-khr-round-up-on-total-calculation) | `P0` | 🟢 `COMPLETED` | `DualCurrencyCalculator` & UI Total Conversions |
| **WP-02** | [Attendant Assistance Flow Across All Payment Screens](#wp-02-attendant-assistance-flow-across-all-payment-screens) | `P1` | 🟢 `COMPLETED` | Cash, QR, and Payment Option UI screens |
| **WP-03** | [Receipt Printer Pipeline & Error Recovery](#wp-03-receipt-printer-pipeline--error-recovery) | `P0` | 🟢 `COMPLETED` | ESC/POS direct stream, USB/COM fallback |
| **WP-04** | [Generic Bill Acceptor & ITL Hardware Stabilization](#wp-04-generic-bill-acceptor--itl-hardware-stabilization) | `P0` | 🟡 `IN PROGRESS` | ITL NV200/NV11, simulator & validator |
| **WP-05** | [Architecture Interaction Flow & Component Remarks](#wp-05-architecture-interaction-flow--component-remarks) | `P1` | 🔴 `TODO` | In-depth code remarks & lifecycle mapping |
| **WP-06** | [Project Structure Optimization & Dead Code Removal](#wp-06-project-structure-optimization--dead-code-removal) | `P1` | 🟢 `COMPLETED` | Safe cleanup, unused tools & structure pruning |
| **WP-07** | [Development Runtime Tools & License Generator](#wp-07-development-runtime-tools--license-generator) | `P1` | 🟢 `COMPLETED` | Standalone CLI tools, DevLicenseTokenGenerator, and quickstart helpers |
| **WP-08** | [Admin Diagnostics & Age-Restricted Approval Workflow](#wp-08-admin-diagnostics--age-restricted-approval-workflow) | `P0` | 🟢 `COMPLETED` | Vault breakdown, receipt reprint, age approval modal, store branding & persistence |
| **WP-09** | [Peripheral & Workflow Stabilization (Scan 2x, Khmer Hours, Single-Print)](#wp-09-peripheral--workflow-stabilization-scan-2x-khmer-hours-single-print) | `P0` | 🟢 `COMPLETED` | Barcode scan deduplication, Khmer 'Open until' localization, SuccessView single-print guarantee |
| **WP-10** | [Standalone Unpackaged Portable Distribution (`dist/SelfCheckoutKiosk`)](#wp-10-standalone-unpackaged-portable-distribution-distselfcheckoutkiosk) | `P0` | 🟢 `COMPLETED` | Unpackaged self-contained publish, isolated `CashAPI/` subfolder, 1-click launcher, and Debug/Release validation |
| **WP-11** | [Dynamic COM Port Auto-Probe & Multi-Device Hardware Discovery](#wp-11-dynamic-com-port-auto-probe--multi-device-hardware-discovery) | `P0` | 🟢 `COMPLETED` | Serial scanner all-COM sweep, Cash Recycler dynamic port allocation, Admin Diagnostics live port reflect |

---

## 🛠️ Detailed Work Packages

---

### WP-01: Dual-Currency KHR Round-Up on Total Calculation
**Objective:** When converting item totals or basket subtotals into KHR, enforce the Cambodian retail rounding rule: any fractional amount between 1 and 99 KHR (e.g. 50 KHR) must **always round UP to the nearest 100 KHR**, while change dispensing continues to round **DOWN** to prevent dispensing unheld currency.

- [x] **1.1 Rounding Rule Implementation in `DualCurrencyCalculator`:**
  - Implemented `CalculateTotalKhr(decimal totalUsd, decimal exchangeRate)` and `RoundUpToNearest100Khr(decimal rawKhr)`.
    $$\text{Total KHR} = \lceil (\text{totalUsd} \times \text{exchangeRate}) / 100 \rceil \times 100$$
  - Example: $\$1.01 \times 4100 = 4141\text{ KHR} \rightarrow 4200\text{ KHR}$ (41 KHR remainder rounds UP to 100).
- [x] **1.2 UI & Receipt Synchronization:**
  - `CartViewModel`, `CartService`, `QRPaymentViewModel`, `IngestionProgressViewModel`, and `EpsonReceiptPrinter` now use `CalculateTotalKhr`.
- [x] **1.3 Unit Tests:**
  - Added test cases in `DualCurrencyCalculatorTests.cs` (Exact, 1 KHR, 50 KHR, 99 KHR remainders, Invalid rate). All 98 tests passing.

---

### WP-02: Attendant Assistance Flow Across All Payment Screens
**Objective:** Provide a prominent, accessible "Help / Call Attendant" button on all customer payment screens (`PaymentSelectionView`, `QRPaymentView`, `IngestionProgressView`), enabling customers to summon support without cancelling their transaction.

- [x] **2.1 Presentation State & UI Triggers:**
  - Added accessible `HelpButton` controls across `PaymentSelectionView.xaml`, `QRPaymentView.xaml`, and `IngestionProgressView.xaml`.
- [x] **2.2 Localized Assistance Dialogs:**
  - Integrated localized `HelpIsOnTheWay` and `HelpMessage` modal dialogs supporting English and Khmer languages.
- [x] **2.3 Safe Flow Continuation:**
  - Customers can dismiss the assistance dialog and proceed with their checkout without resetting their active transaction or losing scanned items.

---

### WP-03: Receipt Printer Pipeline & Multi-Vendor Discovery
**Objective:** Ensure rock-solid thermal printing on Epson EU-m30 / TM-m30 (and compatible ESC/POS printers) with native Win32 RAW Spooler delivery, multi-vendor auto-discovery, real-time USB plug/unplug hardware detection, out-of-paper detection, and honest status reporting.

- [x] **3.1 Native Win32 Spooler RAW Mode (`WinSpoolRawPrinter.cs`):**
  - Uses direct Win32 `winspool.drv` P/Invoke (`OpenPrinter`, `StartDocPrinter`, `StartPagePrinter`, `WritePrinter`, `EndPagePrinter`, `EndDocPrinter`, `ClosePrinter`).
  - Bypasses Windows GDI rasterization and sends pure binary ESC/POS command streams directly to the targeted POS printer.
  - **Never touches or conflicts with the laptop's default printer** (e.g. `EPSON L6490` or `Microsoft Print to PDF`).
- [x] **3.2 Multi-Vendor POS Printer Auto-Discovery (`PrinterDiscovery.cs`):**
  - Automatically enumerates installed system printers and prioritizes thermal/receipt printers (Epson `EU-m30`, `TM-m30`, `TM-T88`, `TM-T20`, `Generic / Text Only`, Star Micronics, Citizen, Bixolon, Xprinter, Rongta, POS-58, POS-80, Zebra).
  - Explicitly filters out virtual and office printers (`Microsoft Print to PDF`, `OneNote`, `Fax`, `XPS Document Writer`).
  - Supports environment and config overrides (`SELFCHECKOUTKIOSK_PRINTER_NAME`, `SELFCHECKOUTKIOSK_PRINTER_PORT`, `kiosk-test.json`).
- [x] **3.3 Real-Time PnP Hardware Plug/Unplug Detection:**
  - Integrated `cfgmgr32.dll` (`CM_Locate_DevNode` & `CM_Get_DevNode_Status`) and Win32 Spooler status queries in `WinSpoolRawPrinter.QueryPrinterStatus`.
  - Instantly detects if the physical USB cable is unplugged or powered off (`"USB Cable Unplugged / Disconnected"`).
  - Integrated into the 2-second background loop in `App.xaml.cs` (`StartContinuousHardwareMonitor`), updating `HardwareStatusManager` and Admin Diagnostics dynamically.
- [x] **3.4 Live Admin Diagnostics & Reprint Execution:**
  - `AdminDiagnosticsViewModel` displays the true detected device name (e.g. `EPSON EU-m30`), port (`USB001`), and real-time connection badge (Green/Red).
  - "Reprint Last Receipt" checks device availability and gives instant status feedback.
- [x] **3.5 Format & Currency Synchronization:**
  - Synchronized receipt formatting with the KHR round-up rule on totals and verified dual-currency layout.
- [x] **3.6 Supermarket Receipt Template & 80mm Paper Alignment (79.5mm / 48 Cols):**
  - Formats content across 48 columns (576 dots @ 203 DPI / 72.0mm printable width) matching standard $79.5 \pm 0.5\text{ mm}$ ($3.13" \pm 0.02"$) thermal paper with balanced 3.75mm physical hardware margins.
  - Dynamically itemizes basket products (SKU, Qty, Unit Price, Line Total) captured from `CartItemSnapshot` in `Payment.cs`.
  - Completely removed tax and `CHANGE DUE` (shows only `TOTAL DUE` in USD & KHR, and `PAID ([METHOD])`).
  - Injected `FS .` (`0x1C, 0x2E`) Kanji cancel command, `ESC t 0` (PC437 ASCII), and strict ASCII sanitization to permanently eliminate accidental Chinese/Asian character output.

---

### WP-04: Generic Bill Acceptor & ITL Hardware Stabilization
**Objective:** Ensure ITL NV200/NV11, generic SSP/cctalk bill validator connection, escrow polling, stack, reject, and process auto-start in `CashApiProcessManager` work reliably.

- [x] **4.1 Cash API Background Execution & Debug Toggle:**
  - `CashApiProcessManager` runs the Cash API / Simulator process seamlessly in the **background** (`CreateNoWindow = true`, `UseShellExecute = false`, `WindowStyle = ProcessWindowStyle.Hidden`) on kiosk startup (F5) and during auto-reconnect retry loops without popup console windows.
  - Added debug toggle: Developers can switch `CashApiProcessManager.RunInBackground = false` in code or set environment variable `SELFCHECKOUTKIOSK_SHOW_CASH_API_WINDOW=1` / `true` to bring up the visible console window for debugging whenever needed.
- [x] **4.2 Escrow & Stack Protocol Handshake:**
  - Verified and finalized escrow lifecycle in `LLCoreLogicEngine.cs` + `HardwareAppendLog.cs` + `ItlRestCashRecycler.cs` + `VendorXCashRecycler.cs`.
  - Normal cash acceptance: Note is accepted, stacked to vault, and change is dispensed via `DualCurrencyCalculator`.
  - Exact-cash-only lockout: Note exceeding balance is immediately rejected and returned to customer; notes never remain stuck in escrow.
- [ ] **4.3 Serial COM Auto-Discovery & Health Probing:**
  - Stabilize prioritized COM port discovery for ITL validator hardware.
- [ ] **4.4 Generic Protocol Adapter Interface:**
  - Ensure `ICashRecycler` contract cleanly abstracts both REST-bridge and direct serial SSP/cctalk bill acceptors without leaking vendor-specific abstractions into Core.

---

### WP-05: Architecture Interaction Flow & Component Remarks
**Objective:** Add comprehensive function-level remarks, docstrings, and an interaction flow diagram illustrating how Core Brain, HAL Adapters, Persistence, Audit Log, and Presentation ViewModels interact during each checkout phase.

- [ ] **5.1 End-to-End Interaction Lifecycle Remarks:**
  - Document Scan Phase → Payment Selection → Cash Ingestion & Audit → Change Dispensing → Receipt & Cloud Sync.
- [ ] **5.2 In-Code Remarks:**
  - Add standard XML doc comments (`<summary>`, `<remarks>`, `<param>`) on all core interfaces and public API methods documenting cross-cutting dependencies.

---

### WP-06: Project Structure Optimization & Clean Root Reorganization
**Objective:** Streamline repository organization, move build/packaging helper scripts to `scripts/`, remove dead code, and maintain a minimalist root.

- [x] **6.1 Move Batch Helpers to `scripts/`:**
  - Moved `Build-DeploymentPackage.bat`, `QuickStart-Kiosk.bat`, `Generate-License.bat`, `Install-Kiosk.bat` into [`scripts/`](file:///c:/Users/viti/source/repos/SelfCheckoutKiosk-V2%20-%20Merge/scripts/).
  - Removed redundant root wrappers (`Installer.bat`, `Installer.ps1`).
- [x] **6.2 Expand and Harden `.gitignore`:**
  - Added comprehensive ignore rules for `dist/`, `publish/`, `artifacts/`, `*.log`, `*.db`, `*.dmp`, and cryptographic keys.
- [x] **6.3 Prune Orphaned Tools & Redundancies:**
  - Removed deprecated `KeyGen` tool superseded by `DevLicenseTokenGenerator`.

---

### WP-07: Development Runtime Tools & License Generator
**Objective:** Provide essential developer runtime utilities, fast development launchers, and node-locked cryptographic license generation for local execution.

- [x] **7.1 Dev License Token Generator (`tools/DevLicenseTokenGenerator`):**
  - Standalone single-file CLI utility supporting local hardware ID binding, token inspection, and automatic synchronization across development folders.
- [x] **7.2 Fast Development Launchers (`scripts/QuickStart-Kiosk.bat` & `scripts/Generate-License.bat`):**
  - Automatic license check and node-locked token generation on fresh environments.
- [x] **7.3 Packaging & Installer Cleanup:**
  - Pruned complex packaging scripts and installer suites to maintain a clean source repository focused on direct runtime and visual studio execution.

---

### WP-08: Admin Diagnostics & Age-Restricted Approval Workflow
**Objective:** Provide full attendant diagnostics UI, real-time vault breakdown tracking and reset confirmation, thermal receipt reprinting, genuine external hardware connectivity status, and a global staff assistance / age-restricted item approval pipeline.

- [x] **8.1 Dynamic Vault Breakdown & Reset Confirmation:**
  - Implemented `VaultInventoryService` tracking deposited notes across KHR (100, 500, 1000, 5000, 10000, 20000, 50000) and USD ($1, $5, $10, $20, $50, $100).
  - Wired into `PaymentService.TrySubmitCash` and `HandlePhysicalEscrowResolved`.
  - Added "Reset Vault Breakdown" button in `AdminDiagnosticsView.xaml` triggering a WinUI `ContentDialog` confirmation before resetting counts.
- [x] **8.2 Store & Reprint Last Receipt:**
  - Updated `ReceiptPrinterService` to preserve `_lastPayment`.
  - Wired "Reprint Last Receipt" button to reprint the cached transaction marked as `[DUPLICATE / REPRINT]`.
- [x] **8.3 Genuine External Hardware Status:**
  - `HardwareStatusManager` and `AdminDiagnosticsViewModel` strictly report genuine external serial / USB port availability for scanner and printer, showing red/green status indicators.
- [x] **8.4 Age-Restricted Customer Alert & Admin Approval:**
  - Created `AgeRestrictedApprovalManager` to manage pending approvals across customer and admin views.
  - In `CartView.xaml.cs` and `App.xaml.cs`, scanning age-restricted products triggers the modal dialog ("One moment — staff assistance needed" / "សូមរង់ចាំមួយភ្លែត — ត្រូវការជំនួយពីបុគ្គលិក") with blue circular badge, Admin shortcut, and full Khmer (`km`) / English (`en`) dictionary localization and font switching.
  - In `AdminDiagnosticsView.xaml`, renders the top approval card matching reference design.
  - **Quantity Stacking Fix:** `AgeRestrictedApprovalManager.Approve()` directly invokes `App.CartServiceInstance.AddItem(...)` exactly once upon attendant approval. Removed leaky view-level event subscriptions in `CartView` that caused duplicate/multiplied quantities when scanning and approving subsequent items.
- [x] **8.5 Admin Diagnostics Design & Navigation:**
  - Restored `AdminDiagnosticsView.xaml` to match `MediaBrandingView.xaml` design language (`#F1F5F9` background, white top bar, `CornerRadius="12"` cards with `#CBD5E1` borders, responsive 2-column ↔ portrait layout, section header style).
  - Reset Vault Counts button uses red danger styling with dedicated hover/pressed visual states and a destructive `ContentDialog` confirmation.
  - Implemented `INavigationService.NavigateBackToCustomer()` which prunes admin stack entries and returns to whichever customer screen opened Admin.
- [x] **8.6 Dynamic Store Branding & Cross-Session Persistence:**
  - Updated `BrandingConfig` model to track `CompanyName`, `Tagline`, `LogoFileName` (with fallback to `ca.ico`), `StoreHours`, and `KioskId`.
  - Added JSON serialization & local disk persistence (`branding_config.json`) in `MediaBrandingService`, automatically loading saved branding and playlist on application boot.
  - Updated `HomeViewModel` and `HomeView.xaml` to dynamically render the store logo, brand name, tagline, and store hours in the header and footer across both 16:9 Landscape and 9:16 Portrait kiosk aspect ratios.
- [x] **8.7 Peripheral Robustness & UI Consistency Fixes:**
  - **Scanner Binary Noise Filtering:** Added strict validation in `DatalogicBarcodeScanner.IsValidBarcodeString` and `CartView.ProcessScannedBarcodeAsync` to filter out non-barcode binary serial traffic.
  - **Payment Decoupling:** Added `isCash` parameter to `IPaymentService.BeginTransaction(...)` and configured `QRPaymentViewModel` with `isCash: false`.
  - **Universal Help Button Consistency:** Unified Help button icon across all customer views to Segoe Fluent glyph `&#xE9CE;`.

---

### WP-09: Peripheral & Workflow Stabilization (Scan 2x, Khmer Hours, Single-Print)
**Objective:** Resolve double-quantity additions on barcode scans, localize store hours ("Open until") in Khmer (`km`), and enforce single-print execution on `SuccessView` during admin navigation cycles.

- [x] **9.1 Barcode Scan Deduplication:**
  - Centralized scan processing inside `App.HandleBarcodeScannedInternal` with a 400ms debounce guard against hardware bounce.
  - Removed duplicate root-level keyboard event handler (`_dialogScanKeyHandler`) and duplicate serial scanner subscription (`Scanner_OnBarcodeScanned`) in `CartView`.
  - Delegated `CartView.ProcessScannedBarcodeAsync` as the single pipeline for cart-level scanning (handling age restriction approval, price check mode, not-found dialogs, and single-item addition).
- [x] **9.2 Khmer Store Hours Localization:**
  - Bound `StoreHoursText` in `HomeView.xaml` to `{x:Bind Localizer.GetString('OpenUntil'), Mode=OneWay}`.
  - Synchronized `km.json` (`"OpenUntil": "បើករហូតដល់ 10:00 PM"`) and `en.json` (`"OpenUntil": "Open until 10:00 PM"`), with fallback handling in `HomeViewModel.StoreHours`.
- [x] **9.3 SuccessView Single-Print Guarantee:**
  - Added `IsReceiptPrinted` state on `Payment` model.
  - Checked `Payment.IsReceiptPrinted` and tracked processed transaction IDs in `SuccessView.xaml.cs` and `SuccessViewModel.cs` to prevent reprinting when attendants navigate to Admin mode and return back to `SuccessView`.

---

### WP-10: Standalone Unpackaged Portable Distribution (`dist/SelfCheckoutKiosk`)
**Objective:** Provide a clean, standalone unpackaged release distribution in `dist/SelfCheckoutKiosk` that executes identically to F5 development debugging (running both the WinUI 3 touch application and the Cash API background daemon), keeps Cash API files neatly isolated inside a `CashAPI/` subfolder, and establishes a strict continuous test matrix.

- [x] **10.1 Unpackaged Publish Pipeline:**
  - Published `SelfCheckoutKiosk.App` self-contained unpackaged win-x64 binaries into `dist/SelfCheckoutKiosk`.
  - Copied compiled resource indexes (`resources.pri`, `SelfCheckoutKiosk.App.pri`) and bundled `Assets/` and `Config/` into the package root.
- [x] **10.2 Isolated `CashAPI/` Subfolder:**
  - Placed all Cash Device REST API executables, DLLs, and simulator binaries into `dist/SelfCheckoutKiosk/CashAPI/` to prevent root clutter.
  - Configured `CashApiProcessManager` to automatically discover and manage the daemon lifecycle inside `CashAPI/`.
- [x] **10.3 1-Click Launcher & Offline Licensing:**
  - Included `Start-Kiosk.bat` and `LicenseGenerator/GenerateLicense.exe` with a pre-generated machine `license.token`.
- [x] **10.4 Continuous Build & Test Verification Protocol:**
  - Established standard testing protocol: **Always test Debug build, Debug tests (98/98), Release build, and Release tests (98/98)** on any codebase modifications.

---

### WP-11: Dynamic COM Port Auto-Probe & Multi-Device Hardware Discovery
**Objective:** Eliminate static COM port assumptions (`COM4`, `COM5`), dynamically probe all system COM ports (Registry and Win32 PnP) for serial barcode scanners and ITL cash recyclers, prevent port locking collisions, and continuously monitor plug/unplug events every 2 seconds.

- [x] **11.1 Continuous Serial Barcode Scanner Auto-Probe:**
  - Implemented `App.ProbeAndConnectBarcodeScannerAsync` scanning all candidate COM ports from `SerialPort.GetPortNames()` and registry `HARDWARE\DEVICEMAP\SERIALCOMM`.
  - Added collision protection: automatically excludes the COM port claimed by the active Cash Recycler.
  - Added continuous 2-second polling in `StartContinuousHardwareMonitor` to detect when a scanner is plugged into any USB port after startup.
- [x] **11.2 Multi-Device Cash Recycler Dynamic Port Discovery:**
  - Diagnosed and resolved multi-machine COM port numbering variations (Windows dynamic PnP USB-Serial assignment).
  - Ensured `GetPrioritizedComPorts()` prioritizes real USB-serial hardware while filtering out virtual Bluetooth ports.
  - Documented driver requirements (Innovative Technology USB-SSP driver) for multi-device deployments.
- [x] **11.3 Dynamic Admin Diagnostics Port Display:**
  - Updated `HardwareStatusManager` to expose `ScannerPort` and `CashDevicePort`.
  - Updated `AdminDiagnosticsViewModel` to dynamically render detected port names (e.g. `Port 5000 / COM5`, `USB-COM Serial (COM4) • 9600 Baud`, or `USB Keyboard Wedge (HID)`) instead of hardcoded strings.

---

## 📈 Execution Sequence

```mermaid
graph TD
    A["WP-01: Dual-Currency KHR Round-Up Total"] --> B["WP-02: Attendant Assistance Flow on Payments"]
    B --> C["WP-03: Thermal Receipt Printer & Supermarket Template"]
    C --> D["WP-04: Bill Acceptor & ITL REST Bridge"]
    D --> E["WP-06: Project Structure Optimization & Root Clean"]
    E --> F["WP-07: Dev Runtime Tools & Licensing"]
    F --> G["WP-08: Admin Diagnostics, Age Approval & Store Branding"]
    G --> H["WP-09: Peripheral & Workflow Stabilization"]
    H --> I["WP-10: Standalone Unpackaged Portable Distribution"]
    I --> J["WP-11: Dynamic COM Port Auto-Probe & Multi-Device Discovery"]
```
