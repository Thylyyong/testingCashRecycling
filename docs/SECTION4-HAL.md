# Self-Checkout Kiosk V2 — Systems Team Reference

> **Section 4: Hardware Abstraction Layer & Connectivity — Sprint 0 Delivery**
>
> *For: Systems Team (Category 2 Work Package)*
> *Do not distribute externally.*

---

## Table of Contents

- [Project Overview](#project-overview)
- [System Architecture](#system-architecture)
- [Project Structure](#project-structure)
- [Team Ownership](#team-ownership)
- [Sprint Status](#sprint-status)
- [Getting Started](#getting-started)
- [Build & Test](#build--test)
- [Security Protocol](#security-protocol)
- [Hardware Peripherals](#hardware-peripherals)
- [Dependency Rules](#dependency-rules)
- [Open Items for Lead / Backend](#open-items-for-lead--backend)
- [Roadmap](#roadmap)

---

## Project Overview

| Property | Value |
|---|---|
| **Platform** | Windows 11 IoT Enterprise |
| **Runtime** | .NET 10 — Native AOT |
| **Architecture** | Clean / Onion — offline-first |
| **Currency** | USD ledger · KHR change |
| **Payment** | Cash recycler · KHQR digital |
| **Storage** | SQLite + SQLCipher (AES-256 at rest) |
| **Connectivity** | Tailscale VPN overlay (no public ports) |
| **Build** | `TreatWarningsAsErrors` · `Nullable enable` · AOT analyzers |

The kiosk is **offline-first** — no network connection is required to complete a sale. The cloud is a downstream subscriber to local truth, never a dependency for the transaction flow.

---

## System Architecture

```
┌─────────────────────────────────────────────────────────┐
│  PRESENTATION LAYER  (WinUI 3 / MVVM)                   │
│  Views ⇄ ViewModels  (data-bound, no hardware refs)      │
└───────────────────────────┬─────────────────────────────┘
                             │ events / commands
┌───────────────────────────▼─────────────────────────────┐
│  APPLICATION BRAIN  —  LLCoreLogicEngine                 │
│  Deterministic state machine                             │
│  RegexRouter · DualCurrencyCalculator · LicenseManager   │
└──────────┬────────────────────────────────┬─────────────┘
           │                                │
┌──────────▼───────────────┐  ┌─────────────▼────────────┐
│  HARDWARE ABSTRACTION     │  │  PERSISTENCE LAYER        │
│  ICashRecycler            │  │  KioskDbContext (EF Core) │
│  IBarcodeScanner          │  │  SQLite + SQLCipher       │
│  IReceiptPrinter          │  │  WAL · synchronous=FULL   │
│  HardwareAppendLog        │  │  SyncStatus tracking      │
│  LowFloatMonitor          │  └─────────────┬────────────┘
└──────────┬───────────────┘                 │ local commit
           │ physical I/O      ┌─────────────▼────────────┐
           │                   │  BACKGROUND SYNC WORKER   │
           │                   │  Tailscale VPN tunnel     │
           │                   │  Pushes records → server  │
           │                   └─────────────┬────────────┘
           │                                 │
┌──────────▼─────────────────────────────────▼────────────┐
│  CENTRAL SERVER / ERP  —  reporting · catalog · licensing │
└───────────────────────────────────────────────────────────┘
```

### Kiosk State Machine

```
Idle → Scanning → AwaitingPayment → ProcessingCash → DispensingChange → TransactionComplete
         ↓                                ↓
      Faulted  ←──────────────── ExactCashOnlyLockout (low-float branch)
```

---

## Project Structure

```
SelfCheckoutKiosk.sln
│
├── src/
│   ├── SelfCheckoutKiosk.Domain/              Zero dependencies — pure C#
│   │   ├── Entities/                          Product
│   │   ├── Enums/                             KioskState · ScanCategory · PaymentMethod
│   │   └── ValueObjects/                      Money · ChangeBreakdown · DispenseResult
│   │
│   ├── SelfCheckoutKiosk.Core/                Business logic — depends on Domain only
│   │   ├── Abstractions/                      ICashRecycler · IBarcodeScanner · IReceiptPrinter
│   │   │                                      HardwareEvents (event arg types)
│   │   ├── Engine/                            LLCoreLogicEngine · ILLCoreLogicEngine
│   │   │                                      HardwareAppendLog   ← NEW (Sprint 0)
│   │   │                                      RegexRouter
│   │   ├── Currency/                          DualCurrencyCalculator
│   │   │                                      LowFloatMonitor     ← IMPLEMENTED (Sprint 0)
│   │   └── Licensing/                         OfflineLicenseManager (stub — Lead scope)
│   │
│   ├── SelfCheckoutKiosk.Infrastructure/      Implements Core ports
│   │   ├── Data/                              KioskDbContext (EF Core + SQLCipher)
│   │   ├── Sync/                              TailscaleSyncWorker (stub — Sprint 1)
│   │   └── Security/                          HardwareIdProvider (TPM-backed)
│   │
│   ├── SelfCheckoutKiosk.Hal.Vendor.CashRecyclerX/      ← IMPLEMENTED (Sprint 0)
│   │   └── VendorXCashRecycler.cs             Simulation adapter · real SDK Sprint 1+
│   │
│   ├── SelfCheckoutKiosk.Hal.Vendor.DatalogicScanner/   stub
│   └── SelfCheckoutKiosk.Hal.Vendor.EpsonM30/           stub
│
├── tests/
│   ├── SelfCheckoutKiosk.Core.Tests/          Unit tests
│   │   ├── RegexRouterTests.cs
│   │   ├── LowFloatMonitorTests.cs            ← NEW (Sprint 0) — 7 cases
│   │   └── HardwareAppendLogTests.cs          ← NEW (Sprint 0) — 9 cases
│   ├── SelfCheckoutKiosk.Infrastructure.Tests/
│   └── SelfCheckoutKiosk.Integration.Tests/
│       └── CompositionSeamTests.cs            Updated — 5 integration tests
│
└── docs/
    ├── cash-recycler-integration-task.md      Systems Team task tracker (updated)
    └── SECTION4-HAL.md                        ← this file
```

---

## Team Ownership

| Layer | Team | Scope |
|---|---|---|
| `Domain` | Backend | Entities, enums, value objects — zero dependencies |
| `Core/Engine` | Backend | State machine, RegexRouter, HardwareAppendLog |
| `Core/Currency` | Backend + **Systems** | DualCurrencyCalculator, LowFloatMonitor |
| `Core/Licensing` | **Lead only** | OfflineLicenseManager — private key material |
| `Core/Abstractions` | **Systems** | ICashRecycler, IBarcodeScanner, IReceiptPrinter |
| `Infrastructure/Data` | Backend | KioskDbContext, EF migrations, SQLCipher config |
| `Infrastructure/Sync` | **Systems** | TailscaleSyncWorker (IHostedService) |
| `Infrastructure/Security` | **Lead only** | DPAPI/TPM secret sealing, HardwareIdProvider |
| `Hal.Vendor.*` | **Systems** | One adapter project per hardware SKU |
| `App/Views` · `App/Styles` | Frontend | WinUI 3 XAML, resource dictionaries |
| `App/Composition` · `App.xaml.cs` | **Lead only** | DI wiring — single source of truth |
| `deploy/Provisioning` | **Lead only** | BitLocker, Shell Launcher, USB policy |

> **Architecture Rule:** `CompositionRoot.cs` is the **only** file that may reference `Hal.Vendor.*` assemblies. No other project may import a concrete adapter.

---

## Sprint Status

### ✅ Sprint 0 — Complete (Systems Team)

| Component | File | Status | Notes |
|---|---|---|---|
| `ICashRecycler` contract review | `Core/Abstractions/ICashRecycler.cs` | ✅ Done | Surface aligned; extensions deferred to Sprint 1 |
| `HardwareAppendLog` | `Core/Engine/HardwareAppendLog.cs` | ✅ Done | **New file** — `WriteThrough` + `Flush(flushToDisk:true)` |
| `LowFloatMonitor` | `Core/Currency/LowFloatMonitor.cs` | ✅ Done | Stub replaced with full thread-safe implementation |
| `VendorXCashRecycler` | `Hal.Vendor.CashRecyclerX/VendorXCashRecycler.cs` | ✅ Done | Full simulation adapter; state machine wired |
| `LLCoreLogicEngine.InitializeAsync` | `Core/Engine/LLCoreLogicEngine.cs` | ✅ Done | HAL events wired; audit log first per blueprint |
| Unit tests — LowFloatMonitor | `Core.Tests/LowFloatMonitorTests.cs` | ✅ Done | 7 cases |
| Unit tests — HardwareAppendLog | `Core.Tests/HardwareAppendLogTests.cs` | ✅ Done | 9 cases |
| Integration seam tests | `Integration.Tests/CompositionSeamTests.cs` | ✅ Done | 5 cases (was 1) |
| Task tracker updated | `docs/cash-recycler-integration-task.md` | ✅ Done | All 5 checklist items ticked |

**Build result: 0 errors · 0 warnings · 28 tests passed**

### 🔄 Sprint 1 — Pending (Systems Team)

| Component | Owner | Blocker |
|---|---|---|
| `VendorXCashRecycler` — real vendor SDK | Systems | SDK package / DLL needed from vendor |
| `DatalogicBarcodeScanner` — serial port | Systems | `System.IO.Ports` + COM port assignment |
| `EpsonReceiptPrinter` — ESC/POS | Systems | TCP vs USB connection decision (Lead) |
| `TailscaleSyncWorker` — IHostedService | Systems | DB context (Backend) + HTTP client |
| `ICashRecycler.GetCassetteInventoryAsync` | Systems | Real SDK surface verification |

### 🔄 Sprint 1 — Pending (Other Teams)

| Component | Owner | Blocker |
|---|---|---|
| `OfflineLicenseManager` full implementation | Lead / Backend | Cryptographic key material |
| License gate in `InitializeAsync` | Systems (call site ready) | Backend must implement `EnforceFeatureAccess` |
| `KioskDbContext` — EF Core + SQLCipher | Backend | SQLCipher NuGet bundle |
| `LLCoreLogicEngine` full state machine | Backend | License gate (Lead) |
| WinUI 3 XAML Views | Frontend | WinUI 3 AOT spike result |
| PowerShell provisioning script | Lead | Physical kiosk hardware |

---

## Getting Started

### Prerequisites

| Tool | Minimum Version |
|---|---|
| .NET SDK | **10.0.110** or later |
| Visual Studio | 2022 v17.12+ |
| Windows | 11 (production) · any OS (dev builds) |

### Clone & Restore

```powershell
git clone <repository-url>
cd SelfCheckoutKiosk-V2/SelfCheckoutKiosk-V2
dotnet restore SelfCheckoutKiosk.sln
```

---

## Build & Test

All commands run from `SelfCheckoutKiosk-V2/SelfCheckoutKiosk-V2/`.

```powershell
# Build the full solution (0 errors, 0 warnings required)
dotnet build SelfCheckoutKiosk.sln --configuration Release

# Run all tests
dotnet test SelfCheckoutKiosk.sln --configuration Release

# Run with verbose test output
dotnet test SelfCheckoutKiosk.sln --configuration Release --logger "console;verbosity=normal"

# Native AOT publish (Windows x64)
dotnet publish src/SelfCheckoutKiosk.App --configuration Release --runtime win-x64 -p:PublishAot=true
```

> **`TreatWarningsAsErrors=true` is solution-wide.**
> Every compiler warning is a build failure. Never suppress a warning without Lead approval.
> Remove `#pragma warning disable` blocks only when the underlying code is fully implemented.

---

## Security Protocol

The following rules are **mandatory** for all Systems Team code. Violating them is a security defect, not a code style issue.

### 1 — Architecture Boundary Rules

| Rule | Enforcement |
|---|---|
| `Hal.Vendor.*` references `Core` only — never `Infrastructure` | Build fails if violated (`.csproj` reference guard) |
| No business logic in adapters | Code review gate |
| `LLCoreLogicEngine` is the **sole** HAL event subscriber | Architecture rule — documented in every adapter |
| ViewModels never subscribe to hardware events | Code review gate |

### 2 — Financial Audit (HardwareAppendLog)

| Rule | How |
|---|---|
| Must be **first** subscriber on `OnNoteInEscrow` | Wired before engine handler in `InitializeAsync` |
| Uses `FileOptions.WriteThrough` | Bypasses OS write cache |
| Uses `Flush(flushToDisk: true)` | Issues `FlushFileBuffers` — record survives power loss |
| Log path is **constructor-injected** | Never hard-coded — must be on BitLocker volume (Lead §5) |

### 3 — License Gate

```
InitializeAsync():
  1. _licenseManager.EnforceFeatureAccess(...)   ← Lead / Backend implements
  2. Wire HardwareAppendLog to OnNoteInEscrow     ← FIRST subscriber
  3. Wire engine's own OnNoteInEscrow handler     ← second
  4. Wire OnFault, LowFloat handlers
  5. await _cashRecycler.ConnectAsync(...)        ← NEVER before step 1 clears
```

### 4 — Secrets Policy

- SQLCipher passphrase → sealed via **DPAPI/TPM**, injected at runtime. Never in source.
- Signing keys → held by licensing service only. Never in this repository.
- BitLocker TPM binding → configured by PowerShell provisioning script (Lead). Not application code.

---

## Hardware Peripherals

| Device | SKU | Interface | Adapter Project | Status |
|---|---|---|---|---|
| Cash Recycler | CashRecyclerX | Vendor SDK / USB-Serial | `Hal.Vendor.CashRecyclerX` | ✅ Simulation adapter |
| Barcode Scanner | Datalogic | USB-COM virtual serial | `Hal.Vendor.DatalogicScanner` | 🔄 Stub |
| Receipt Printer | Epson m30 | ESC/POS — TCP or USB | `Hal.Vendor.EpsonM30` | 🔄 Stub |

**Adding a new hardware SKU:**
1. Create a new `SelfCheckoutKiosk.Hal.Vendor.<Name>/` project.
2. Implement the relevant Core interface (`ICashRecycler`, `IBarcodeScanner`, or `IReceiptPrinter`).
3. Add one `ProjectReference` in `SelfCheckoutKiosk.App.csproj`.
4. Add one line in `CompositionRoot.cs`.
5. **Core never changes for a new device.**

---

## Dependency Rules

```
SelfCheckoutKiosk.Domain
   ↑  (no references — pure C#)

SelfCheckoutKiosk.Core
   ↑  references Domain only
   ↑  NEVER references Infrastructure or Hal.Vendor.*

SelfCheckoutKiosk.Infrastructure
   ↑  references Core

SelfCheckoutKiosk.Hal.Vendor.*
   ↑  references Core only
   ↑  NEVER references Infrastructure

SelfCheckoutKiosk.App  (composition root)
   ↑  references Core + Infrastructure + all Hal.Vendor.*
      The ONLY place vendor assemblies are allowed to meet
```

These rules are **compile-time enforced** via `.csproj` `<ProjectReference>` entries.
Any disallowed reference causes an immediate build failure.

---

## Open Items for Lead / Backend

The following items are explicitly outside Systems Team scope for Sprint 0.
They are documented here so the handoff is unambiguous.

| Item | Required By | Current State |
|---|---|---|
| `OfflineLicenseManager.EnforceFeatureAccess` | Systems `InitializeAsync` (call site ready) | Throws `NotImplementedException` — Backend scope |
| Log file path policy | `HardwareAppendLog` constructor | Must be on BitLocker volume — Lead §5 provisioning |
| Per-denomination KHR threshold configuration | `LowFloatMonitor` | Currently uniform 15 — Lead decision pending |
| Real vendor SDK package | `VendorXCashRecycler` | Sprint 1 — need SDK from vendor |
| `KioskState.ExactCashOnlyLockout` state | `LLCoreLogicEngine.HandleLowFloatTriggered` | Currently uses `Faulted` as placeholder — Backend scope |

---

## Roadmap

| Feature | Category | Team |
|---|---|---|
| Card / EMV contactless | New `ICardPaymentTerminal` abstraction | Systems (adapter) + Backend |
| Age-restricted item approval | New `AwaitingAttendantApproval` state branch | Backend + Frontend |
| PLU lookup for untagged produce | `RegexRouter` PLU category + Product table | Backend |
| Loyalty / membership | `LoyaltyMemberId` on Transaction | Backend |
| Digital receipt (email / SMS) | `IDigitalReceiptDispatcher` + sync queue | Systems |
| Refund / void transaction | Attendant-PIN-gated Admin Panel flow | Backend + Frontend |
| Idle / attract screen | `IdleView` bound to `KioskState.Idle` | Frontend |
| Multi-kiosk attendant console | Companion server application | Lead — separate solution |

---

*Systems Team internal document — Sprint 0 delivery summary.*
*Next review: Sprint 1 planning session.*
