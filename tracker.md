# Self-Checkout Kiosk V2 — Blueprint Alignment & Task Tracker

> **Last Updated:** August 20, 2026  
> **Target Platform:** Windows 11 IoT Enterprise | .NET 10 (Native AOT compatible)  
> **Master Blueprint Reference:** [Kiosk_Architectural_Blueprint.md](file:///c:/Users/viti/source/repos/SelfCheckoutKiosk-V2%20-%20Merge/src/Kiosk_Architectural_Blueprint.md)  
> **Engineering Execution Plan & Work Packages:** [ENGINEERING_TASKS.md](file:///c:/Users/viti/source/repos/SelfCheckoutKiosk-V2%20-%20Merge/ENGINEERING_TASKS.md)

---

## 📊 Status Summary

| Status Indicator | Meaning | Count / Health |
| :--- | :--- | :--- |
| 🟢 **GREEN** | **Done & Verified** — Built, unit/integration tested, aligns with blueprint specifications. | **Core Architecture & Engine, DB, Security, Simulators, Provisioning, Age Approval** |
| 🟡 **YELLOW** | **In Progress / Provisional** — Implemented with simulations, stubs, or open review items awaiting hardware/SDK verification. | **Physical HAL Drivers, Presentation View-Binding Bridge, Media Sync** |
| 🔴 **RED** | **Not Done / Backlog** — Gaps identified in Supermarket Roadmap (Section 7) or pending feature extensions. | **Card/EMV, PLU Produce, Member Loyalty, Digital Receipts** |

---

## 🧩 Architectural Breakdown & Tracker

### 1. Section 1 & 2 — Architecture & Project Structure

| Status | Priority | Component / Requirement | Description & Current Alignment | Notes / File Reference |
| :---: | :---: | :--- | :--- | :--- |
| 🟢 | `HIGH` | **Layered Clean/Onion Solution** | Multi-project layout: Domain → Core → Infrastructure → Hal → Presentation/App. Strict dependency rule (Domain has 0 dependencies). | [SelfCheckoutKiosk.sln](file:///c:/Users/viti/source/repos/SelfCheckoutKiosk-V2%20-%20Merge/SelfCheckoutKiosk.sln) |
| 🟢 | `HIGH` | **AOT-Friendly Build Configuration** | `TreatWarningsAsErrors=true`, `Nullable=enable`, trimming annotations, compiled EF models, zero reflection in critical paths. | Configured across all `.csproj` files |
| 🟢 | `HIGH` | **Presentation/App Split (Section 2a)** | ViewModels cleanly isolated in headless `SelfCheckoutKiosk.Presentation` (net10.0), XAML views in `SelfCheckoutKiosk.App` (WinUI 3). | Ratified in Blueprint Section 2a |
| 🟢 | `HIGH` | **Standalone Unpackaged Distribution** | Self-contained unpackaged win-x64 release build in `dist/SelfCheckoutKiosk/` with isolated `CashAPI/` daemon subfolder, offline licensing, and 1-click execution. | [dist/SelfCheckoutKiosk](file:///c:/Users/viti/source/repos/SelfCheckoutKiosk-V2%20-%20Merge/dist/SelfCheckoutKiosk) |

---

### 2. Section 3 — Core Brain & Persistence (Category 1: Backend Team)

| Status | Priority | Component / Requirement | Description & Current Alignment | Notes / File Reference |
| :---: | :---: | :--- | :--- | :--- |
| 🟢 | `CRITICAL` | **`LLCoreLogicEngine` State Machine** | Deterministic states (`Idle`, `Scanning`, `AwaitingPayment`, `ProcessingCash`, `DispensingChange`, `TransactionComplete`, `ExactCashOnlyLockout`, `Faulted`). HAL event subscriber. | [LLCoreLogicEngine.cs](file:///c:/Users/viti/source/repos/SelfCheckoutKiosk-V2%20-%20Merge/src/SelfCheckoutKiosk.Core/Engine/LLCoreLogicEngine.cs) |
| 🟢 | `CRITICAL` | **`RegexRouter` Classification** | Classifies incoming scans: EAN-13 barcode, KHQR payment profile, `VCH-` offline voucher/coupon. | [RegexRouter.cs](file:///c:/Users/viti/source/repos/SelfCheckoutKiosk-V2%20-%20Merge/src/SelfCheckoutKiosk.Core/Engine/RegexRouter.cs) |
| 🟢 | `CRITICAL` | **Dual-Currency Calculator** | USD ledger total, USD tendered running sum, overpayment whole dollar dispense in USD, fractional remainder in KHR rounding **down** to dispensable notes. | [DualCurrencyCalculator.cs](file:///c:/Users/viti/source/repos/SelfCheckoutKiosk-V2%20-%20Merge/src/SelfCheckoutKiosk.Core/Currency/DualCurrencyCalculator.cs) |
| 🟢 | `CRITICAL` | **Offline License Enforcement** | Ed25519 asymmetric token validation, expiry check, node-lock hardware ID binding, tier brackets (**Lite**, **Pro**, **Enterprise**) and runtime `EnforceFeatureAccess` gates. | [OfflineLicenseManager.cs](file:///c:/Users/viti/source/repos/SelfCheckoutKiosk-V2%20-%20Merge/src/SelfCheckoutKiosk.Core/Licensing/OfflineLicenseManager.cs) |
| 🟢 | `HIGH` | **Encrypted SQLite Persistence** | SQLCipher AES-256 at rest, raw connection hook running `PRAGMA journal_mode=WAL;` and `PRAGMA synchronous=FULL;`. | [KioskDbContext.cs](file:///c:/Users/viti/source/repos/SelfCheckoutKiosk-V2%20-%20Merge/src/SelfCheckoutKiosk.Infrastructure/Data/KioskDbContext.cs) |
| 🟢 | `HIGH` | **Compiled EF Core Models & Seeding** | Pre-generated compiled models for fast startup / AOT compliance; catalog seeder initialized on first run. | [CompiledModels](file:///c:/Users/viti/source/repos/SelfCheckoutKiosk-V2%20-%20Merge/src/SelfCheckoutKiosk.Infrastructure/Data/CompiledModels) & [CatalogSeeder.cs](file:///c:/Users/viti/source/repos/SelfCheckoutKiosk-V2%20-%20Merge/src/SelfCheckoutKiosk.Infrastructure/Data/CatalogSeeder.cs) |
| 🟢 | `MEDIUM` | **Dev Licensing Tools** | Token generation and node-locked key management CLI utility for local dev and testing. | [DevLicenseTokenGenerator](file:///c:/Users/viti/source/repos/SelfCheckoutKiosk-V2%20-%20Merge/tools/DevLicenseTokenGenerator/Program.cs) |

---

### 3. Section 4 — Hardware Abstraction Layer & Connectivity (Category 2: Systems Team)

| Status | Priority | Component / Requirement | Description & Current Alignment | Notes / File Reference |
| :---: | :---: | :--- | :--- | :--- |
| 🟢 | `CRITICAL` | **`HardwareAppendLog`** | Crash-proof unbuffered audit log; opens with `FileOptions.WriteThrough` and `Flush(flushToDisk: true)` on escrow events. Paired resolution tracking (`COMMITTED_TO_VAULT` / `REJECTED`). | [HardwareAppendLog.cs](file:///c:/Users/viti/source/repos/SelfCheckoutKiosk-V2%20-%20Merge/src/SelfCheckoutKiosk.Core/Engine/HardwareAppendLog.cs) |
| 🟢 | `HIGH` | **`LowFloatMonitor`** | Monitors per-denomination note counts; triggers `LowFloatStateTriggered` when counts drop below safety threshold (15), locking out cash acceptance. | [LowFloatMonitor.cs](file:///c:/Users/viti/source/repos/SelfCheckoutKiosk-V2%20-%20Merge/src/SelfCheckoutKiosk.Core/Currency/LowFloatMonitor.cs) |
| 🟢 | `HIGH` | **Vendor Cash Recycler Simulation** | `Hal.Vendor.CashRecyclerX` simulation adapter complete with note escrow, acceptance, rejection, and change dispensing. | [VendorXCashRecycler.cs](file:///c:/Users/viti/source/repos/SelfCheckoutKiosk-V2%20-%20Merge/src/SelfCheckoutKiosk.Hal.Vendor.CashRecyclerX/VendorXCashRecycler.cs) |
| 🟢 | `HIGH` | **REST-Bridge Cash Recycler Adapter** | `Hal.Vendor.ItlRestCashRecycler` communicates with local HTTP REST daemon (`http://localhost:5055/`) for ITL NV200/NV11 hardware. | [ItlRestCashRecycler.cs](file:///c:/Users/viti/source/repos/SelfCheckoutKiosk-V2%20-%20Merge/src/SelfCheckoutKiosk.Hal.Vendor.ItlRestCashRecycler/ItlRestCashRecycler.cs) |
| 🟢 | `HIGH` | **Tailscale Sync Worker** | Background worker (`IHostedService`) pushing `SyncStatus = Pending` transactions over secure VPN overlay to central server with idempotent GUID ack. | [TailscaleSyncWorker.cs](file:///c:/Users/viti/source/repos/SelfCheckoutKiosk-V2%20-%20Merge/src/SelfCheckoutKiosk.Infrastructure/Sync/TailscaleSyncWorker.cs) |
| 🟢 | `HIGH` | **Physical Scanner Driver (Datalogic & Multi-COM)** | Multi-COM port auto-probe (`SerialPort.GetPortNames` + registry `SERIALCOMM`), real-time 2s background plug/unplug monitor, port conflict exclusion with Cash Recycler, binary noise filtering, and keyboard-wedge input support. | [DatalogicBarcodeScanner.cs](file:///c:/Users/viti/source/repos/SelfCheckoutKiosk-V2%20-%20Merge/src/SelfCheckoutKiosk.Hal.Vendor.DatalogicScanner/DatalogicBarcodeScanner.cs) · [App.xaml.cs](file:///c:/Users/viti/source/repos/SelfCheckoutKiosk-V2%20-%20Merge/src/SelfCheckoutKiosk.App/App.xaml.cs) |
| 🟢 | `HIGH` | **Physical Thermal Printer Driver (Epson m30 / Multi-Vendor)** | Win32 RAW Spooler (`winspool.drv`) bypasses GDI and targets specific receipt printers; multi-vendor discovery (`PrinterDiscovery`), real-time PnP USB plug/unplug hardware detection, continuous health monitoring, 48-col 80mm ($79.5 \pm 0.5\text{ mm}$) supermarket template, and Kanji/Chinese mode cancellation (`FS .`). | [EpsonReceiptPrinter.cs](file:///c:/Users/viti/source/repos/SelfCheckoutKiosk-V2%20-%20Merge/src/SelfCheckoutKiosk.Hal.Vendor.EpsonM30/EpsonReceiptPrinter.cs) · [WinSpoolRawPrinter.cs](file:///c:/Users/viti/source/repos/SelfCheckoutKiosk-V2%20-%20Merge/src/SelfCheckoutKiosk.Hal.Vendor.EpsonM30/WinSpoolRawPrinter.cs) · [PrinterDiscovery.cs](file:///c:/Users/viti/source/repos/SelfCheckoutKiosk-V2%20-%20Merge/src/SelfCheckoutKiosk.Hal.Vendor.EpsonM30/PrinterDiscovery.cs) |
| 🟡 | `MEDIUM` | **Physical Cash Device SDK Validation** | Validating binary SDK vendor DLLs on live kiosk hardware vs local REST simulator. | Awaiting target deployment machine |

---

### 4. Section 5 & 6 — Presentation Layer, UI & OS Hardening (Category 3: UI & Lead)

| Status | Priority | Component / Requirement | Description & Current Alignment | Notes / File Reference |
| :---: | :---: | :--- | :--- | :--- |
| 🟢 | `HIGH` | **OS Provisioning & Hardening Scripts** | Shell Launcher V2 config, BitLocker + TPM 2.0 key binding, USB storage lockdown (while allow-listing HID/serial peripherals), Windows Update scheduling. | [deploy/Provisioning/Provision-KioskOS.ps1](file:///c:/Users/viti/source/repos/SelfCheckoutKiosk-V2%20-%20Merge/deploy/Provisioning/Provision-KioskOS.ps1) |
| 🟢 | `HIGH` | **Headless Presentation ViewModels** | `MainShellViewModel`, `CartViewModel`, `PaymentSelectionViewModel`, `IngestionProgressViewModel`, `SuccessViewModel`, `AdminDiagnosticsViewModel`, `LockScreenViewModel`. | [SelfCheckoutKiosk.Presentation](file:///c:/Users/viti/source/repos/SelfCheckoutKiosk-V2%20-%20Merge/src/SelfCheckoutKiosk.Presentation) |
| 🟢 | `HIGH` | **Customer XAML Touchscreen Views** | `HomeView`, `CartView`, `PaymentSelectionView`, `PaymentOptionView`, `QRPaymentView`, `IngestionProgressView`, `SuccessView`, `KioskBaseView`. Standardized universal Help icon (`&#xE9CE;`), universal Admin navigation button (`&#xE7EE;`), and responsive pill alignment. | [SelfCheckoutKiosk.App/Views/Customer](file:///c:/Users/viti/source/repos/SelfCheckoutKiosk-V2%20-%20Merge/src/SelfCheckoutKiosk.App/Views/Customer) |
| 🟢 | `HIGH` | **Centralized Global Router & Route Registry** | Strongly typed `KioskRoute` enum, `AppRoutes` route table, and `AppRouter` 1-line semantic navigation helpers (`ToAdmin`, `ToHome`, `ToCart`, `BackToCustomer`). Standardized transition animation matrix. | [AppRouter.cs](file:///c:/Users/viti/source/repos/SelfCheckoutKiosk-V2%20-%20Merge/src/SelfCheckoutKiosk.App/Navigation/AppRouter.cs) · [AppRoutes.cs](file:///c:/Users/viti/source/repos/SelfCheckoutKiosk-V2%20-%20Merge/src/SelfCheckoutKiosk.App/Navigation/AppRoutes.cs) |
| 🟢 | `HIGH` | **Admin Diagnostics & Lockdown UI** | PIN-gated Diagnostics view displaying device link states, cassette counts, unresolved audit records, license details, and system reboot/shutdown controls. | [AdminDiagnosticsView.xaml](file:///c:/Users/viti/source/repos/SelfCheckoutKiosk-V2%20-%20Merge/src/SelfCheckoutKiosk.App/Views/Admin/AdminDiagnosticsView.xaml) |
| 🟢 | `HIGH` | **License Lockout Handling** | Hard stop on startup if license validation fails, rendering non-bypassable lockout overlay. **Bugfix:** `ShowLicenseLockout` now navigates `RootFrame` to a blank `Page` first, triggering `KioskBaseView.Unloaded` so the `MediaPlayer` is paused and cleared — stops video audio leaking under the overlay. | [MainWindow.xaml.cs](file:///c:/Users/viti/source/repos/SelfCheckoutKiosk-V2%20-%20Merge/src/SelfCheckoutKiosk.App/MainWindow.xaml.cs) |
| 🟡 | `MEDIUM` | **Presentation ↔ App XAML Binding Consolidation** | Ensure all WinUI 3 views fully bind against the ratified headless `Presentation.ViewModels` contracts rather than legacy App-internal viewmodels. | In progress |
| 🟢 | `HIGH` | **Dynamic Media & Branding Sync & Persistence** | Live store logo, company name, tagline, and store hours integration between `MediaBrandingView` and `HomeView`; JSON persistence across app restarts. | [MediaBrandingService.cs](file:///c:/Users/viti/source/repos/SelfCheckoutKiosk-V2%20-%20Merge/src/SelfCheckoutKiosk.App/Services/MediaBrandingService.cs) |
| 🟢 | `HIGH` | **Peripheral & Workflow Deduplication** | Scan deduplication (400ms debounce guard + single-pipeline dispatch), Khmer store hours localization, and SuccessView single-print enforcement. | [App.xaml.cs](file:///c:/Users/viti/source/repos/SelfCheckoutKiosk-V2%20-%20Merge/src/SelfCheckoutKiosk.App/App.xaml.cs) · [CartView.xaml.cs](file:///c:/Users/viti/source/repos/SelfCheckoutKiosk-V2%20-%20Merge/src/SelfCheckoutKiosk.App/Views/Customer/CartView.xaml.cs) · [SuccessView.xaml.cs](file:///c:/Users/viti/source/repos/SelfCheckoutKiosk-V2%20-%20Merge/src/SelfCheckoutKiosk.App/Views/Customer/SuccessView.xaml.cs) |
| 🟡 | `LOW` | **Attract Slideshow Rotation & Crossfade** | Idle slideshow overlay with crossfade timer and instant tap-to-dismiss (structure present in `HomeViewModel`/`MediaBrandingService`, full crossfade storyboard refinement). | [winui3-implementation-plan.md](file:///c:/Users/viti/source/repos/SelfCheckoutKiosk-V2%20-%20Merge/docs/implementation/winui3-implementation-plan.md) |

---

### 5. Section 7 — Supermarket-Standard Gap Roadmap (Category 4: Sprint 1+)

| Status | Priority | Feature / Gap Area | Description & Roadmap Path | Dependency / Notes |
| :---: | :---: | :--- | :--- | :--- |
| 🔴 | `HIGH` | **Card / EMV Payment Terminal** | `ICardPaymentTerminal` abstraction in Core, payment selection routing, HAL vendor adapter for PAX / Ingenico / Verifone. | Needs hardware terminal specs |
| 🟢 | `HIGH` | **Age-Restricted Item Approval** | Restricted item flag on `Product`, global assistance modal (localized in EN/KM), attendant approval card in `AdminDiagnosticsView`, and centralized single-add cart integration. **Quantity stacking fix:** `AgeRestrictedApprovalManager.Approve()` directly increments cart once, avoiding leaky page subscriptions. | [AgeRestrictedApprovalManager.cs](file:///c:/Users/viti/source/repos/SelfCheckoutKiosk-V2%20-%20Merge/src/SelfCheckoutKiosk.App/Services/AgeRestrictedApprovalManager.cs) |
| 🔴 | `MEDIUM` | **PLU Produce Lookup (No Barcode)** | 4–5 digit numeric PLU parsing in `RegexRouter`, UI category browse / weight scale integration. | Category 1 (Core) + UI |
| 🔴 | `MEDIUM` | **Customer Loyalty / Membership** | Scan membership barcode, associate `LoyaltyMemberId` with `Transaction`, apply promotional discounts before checkout. | Core + central sync schema |
| 🔴 | `LOW` | **Digital Receipts (SMS / Email)** | `IDigitalReceiptDispatcher` abstraction, offline queueing of receipt dispatch jobs via background sync worker. | Infrastructure / Sync |
| 🔴 | `LOW` | **Full Transaction Void & Attendant Refund** | State machine transition to void in-flight transactions; PIN-gated cash dispensing refund for completed orders. | Core + Admin Panel |
| 🔴 | `LOW` | **Centralized Multi-Kiosk Attendant Console** | Live remote monitoring queue for attendants across multiple physical kiosks. | Out of scope for on-kiosk client |

---

### 6. Blueprint Addendum — Open & Provisional Items

| Status | Priority | Topic | Resolution Needed | File Reference |
| :---: | :---: | :--- | :--- | :--- |
| 🟡 | `MEDIUM` | **LowFloatMonitor Threshold Model** | Decide whether to keep the per-denomination threshold dictionary (implemented) or strictly enforce a single flat 15 count (literal blueprint). | [LowFloatMonitor.cs](file:///c:/Users/viti/source/repos/SelfCheckoutKiosk-V2%20-%20Merge/src/SelfCheckoutKiosk.Core/Currency/LowFloatMonitor.cs) |
| 🟡 | `MEDIUM` | **REST Bridge vs Native SDK for ITL** | Standardize whether vendor adapters should talk via local REST daemons (`ItlRestCashRecycler`) or direct C-interop DLLs. | [ItlRestCashRecycler.cs](file:///c:/Users/viti/source/repos/SelfCheckoutKiosk-V2%20-%20Merge/src/SelfCheckoutKiosk.Hal.Vendor.ItlRestCashRecycler/ItlRestCashRecycler.cs) |
| 🟡 | `LOW` | **Catalog & Media Sync Protocol** | Formalize pull-sync specifications for promotional banners and local product override conflict resolution. | [Kiosk_Architectural_Blueprint_addendum.md](file:///c:/Users/viti/source/repos/SelfCheckoutKiosk-V2%20-%20Merge/docs/Kiosk_Architectural_Blueprint_addendum.md) |

---

## 🧪 Test Suite Health (Debug & Release Matrix)

| Test Suite Project | Target | Debug Status | Release Status | Passing Tests | Coverage Areas |
| :--- | :---: | :---: | :---: | :---: | :--- |
| **`SelfCheckoutKiosk.Core.Tests`** | net10.0 | 🟢 **PASS** | 🟢 **PASS** | **54 / 54** | DualCurrency (inc. KHR Round-Up), RegexRouter, State Machine, LowFloat, AuditLog, Licensing |
| **`SelfCheckoutKiosk.Infrastructure.Tests`** | net10.0 | 🟢 **PASS** | 🟢 **PASS** | **8 / 8** | SQLite/SQLCipher DB, Seeding, Hardware ID, Sync Worker |
| **`SelfCheckoutKiosk.Integration.Tests`** | net10.0 | 🟢 **PASS** | 🟢 **PASS** | **27 / 27** | Composition Seam, End-to-End Scan-to-Dispense, License Lockout, Supermarket Receipt Format |
| **`SelfCheckoutKiosk.Presentation.Tests`** | net10.0 | 🟢 **PASS** | 🟢 **PASS** | **9 / 9** | Headless MVVM ViewModels, Navigation, Cart line updates |
| **Total Test Suite** | | 🟢 **PASS** | 🟢 **PASS** | **98 / 98** | **100% Passing (0 failures, 0 skipped across Debug & Release)** |

## 📋 Recommended Next Actions (Sprint Priorities)

1. 🟢 **Physical Receipt Printer Integration:** Completed & verified with Win32 RAW Spooler, multi-vendor discovery, real-time PnP status detection, and 80mm padded supermarket template.
2. 🟢 **Physical Serial COM Scanner (Datalogic & Multi-COM):** Completed & verified with multi-COM auto-probe, 2s real-time monitor, collision protection, and binary noise filtering.
3. 🟡 **ViewModel Bridge Consolidation:** Complete the unification of WinUI XAML view bindings to headless `Presentation.ViewModels`.
4. 🔴 **Supermarket Gap Features (Sprint 1+):** Begin scoping `ICardPaymentTerminal` (Card/EMV payment terminal) and PLU produce lookup.
5. 🟡 **WP-05 Architecture Remarks:** Add XML doc comments and lifecycle flow diagram for cross-cutting component interactions.
