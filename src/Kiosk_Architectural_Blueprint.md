# Self-Checkout Kiosk — Architectural Blueprint
### Offline-First Native C# Kiosk | Windows 11 IoT Enterprise

---

## SECTION 1 — High-Level Architecture Overview

**Conceptual data flow, physical layer → cloud:**

```
┌─────────────────────────────────────────────────────────────────────┐
│  PRESENTATION LAYER (Touchscreen)                                    │
│  WPF/WinUI 3 Views  ⇄  ViewModels (MVVM, data-bound)                 │
└───────────────────────────────┬──────────────────────────────────────┘
                                 │ events / commands (in-process)
┌───────────────────────────────▼──────────────────────────────────────┐
│  APPLICATION BRAIN                                                    │
│  LL Core Logic Engine — deterministic state machine                  │
│  • RegexRouter (EAN-13 / KHQR / Coupon classification)               │
│  • Dual-Currency Calculator (USD ledger, KHR change)                 │
│  • OfflineLicenseManager (tier gate on every feature init)           │
└──────────┬──────────────────────────────────────┬────────────────────┘
           │                                       │
┌──────────▼───────────────┐        ┌──────────────▼─────────────────┐
│  HARDWARE ABSTRACTION      │        │  PERSISTENCE LAYER              │
│  ICashRecycler              │        │  KioskDbContext (EF Core)       │
│  IBarcodeScanner (Datalogic)│        │  SQLite + SQLCipher (AES-256)   │
│  IReceiptPrinter (Epson m30)│        │  WAL + synchronous=FULL         │
│  HardwareAppendLog (raw audit)│      │  SyncStatus: Pending/Completed  │
└──────────┬───────────────┘        └──────────────┬─────────────────┘
           │ physical I/O                            │ local commit
           │                                          │
           │                          ┌───────────────▼─────────────────┐
           │                          │  BACKGROUND SYNC WORKER          │
           │                          │  Tailscale VPN tunnel (encrypted)│
           │                          │  Pushes Pending → central server │
           │                          │  Also carries tunneled RDP for   │
           │                          │  remote support/troubleshooting  │
           │                          └───────────────┬─────────────────┘
           │                                          │
┌──────────▼──────────────────────────────────────────▼─────────────────┐
│  CENTRAL SERVER / ERP — reporting, catalog master, license issuance    │
└──────────────────────────────────────────────────────────────────────┘
```

**Slide takeaway:** every layer is offline-capable on its own. The cloud is a
downstream subscriber to local truth, never a dependency for completing a sale.

---

## SECTION 2 — Unified C# Solution Folder Structure

Multi-project `.sln`, Clean/Onion-style layering, Native AOT-friendly (no
reflection-heavy frameworks, no runtime codegen in hot paths).

```
SelfCheckoutKiosk.sln
│
├── src/
│   ├── SelfCheckoutKiosk.Domain/            [Domain layer — zero dependencies]
│   │   ├── Entities/                         Transaction, Product, LicenseConfiguration
│   │   ├── Enums/                            SyncStatus, CurrencyCode, KioskState
│   │   └── ValueObjects/                     Money, ChangeBreakdown
│   │
│   ├── SelfCheckoutKiosk.Core/               [Application/Business layer]
│   │   ├── Engine/                           LLCoreLogicEngine, RegexRouter, HardwareAppendLog
│   │   ├── Currency/                         DualCurrencyCalculator, LowFloatMonitor
│   │   ├── Licensing/                        OfflineLicenseManager
│   │   └── Abstractions/                     ICashRecycler, IBarcodeScanner, IReceiptPrinter (contracts only)
│   │
│   ├── SelfCheckoutKiosk.Infrastructure/     [Infrastructure layer]
│   │   ├── Data/                             KioskDbContext, EF migrations, SQLCipher config
│   │   ├── Sync/                             TailscaleSyncWorker, background IHostedService
│   │   └── Security/                         DPAPI/TPM secret sealing, HardwareIdProvider
│   │
│   ├── SelfCheckoutKiosk.Hal.Vendor.*/       [Hardware adapter plug-ins — one per vendor SKU]
│   │   ├── SelfCheckoutKiosk.Hal.Vendor.CashRecyclerX/
│   │   ├── SelfCheckoutKiosk.Hal.Vendor.DatalogicScanner/
│   │   └── SelfCheckoutKiosk.Hal.Vendor.EpsonM30/
│   │
│   └── SelfCheckoutKiosk.App/                [Presentation layer]
│       ├── Views/                            CartView, PaymentSelectionView, IngestionProgressView, SuccessView
│       ├── ViewModels/                       One VM per screen, MVVM
│       ├── AdminPanel/                       PIN/barcode-gated diagnostics UI
│       └── Composition/                      DI root — wires everything above
│
├── deploy/
│   └── Provisioning/                         PowerShell: Shell Launcher V2, BitLocker+TPM, USB lockdown
│
└── tests/
    ├── SelfCheckoutKiosk.Core.Tests/         Pure unit tests — currency math, RegexRouter, licensing
    └── SelfCheckoutKiosk.Infrastructure.Tests/
```

**Dependency direction:** `App` → `Infrastructure` + `Core` → `Domain`. `Domain`
references nothing. `Core` never references a vendor `Hal.Vendor.*` project directly —
only the `App`'s composition root does, at startup.

---

## SECTION 2a — Ratified: Presentation/App Split & View Ownership

*Added after Sprint 1 UI work surfaced this as an unresolved ambiguity — the
original Section 2 diagram put `Views/`, `ViewModels/`, `AdminPanel/`, and
`Composition/` all under one `App` project. In practice the codebase evolved
a cleaner split, which is hereby ratified as the intended architecture, not
a deviation from it:*

- **`SelfCheckoutKiosk.Presentation`** — ViewModels only. Deliberately
  targets plain `net10.0` (no `UseWinUI`, no Windows App SDK reference), so
  every ViewModel stays headless-testable without a UI framework or Windows
  runtime present. `SelfCheckoutKiosk.Presentation.Tests` depends on this.
  **Do not retarget this project to `net10.0-windows`/WinUI** — that would
  undo the property that makes it useful.
- **`SelfCheckoutKiosk.App`** — the only project targeting
  `net10.0-windows10.0.19041.0` with `<UseWinUI>true</UseWinUI>` and the
  `Microsoft.WindowsAppSDK` reference, because WinUI XAML `Views/` can only
  exist in a Windows-targeted, WinUI-enabled project. Owns:
  - `App/Views/` — all XAML Views and their code-behind, binding to
    ViewModels from `Presentation` via `x:Bind`/`DataContext`.
  - `App/Styles/` — shared theme resource dictionaries (brushes, control
    styles) merged into `App.xaml`, reused across all Views.
  - `App/AdminPanel/`, `App/Composition/`, `App.xaml.cs`.

**Ownership boundary clarification (supersedes any narrower reading of
`.github/CODEOWNERS` for this folder):** within `SelfCheckoutKiosk.App`,
`Views/` and `Styles/` are Front-End Developer territory — the same team
that owns `SelfCheckoutKiosk.Presentation`. `App.xaml.cs` and
`Composition/CompositionRoot.cs` remain `@project-lead`-only, since that is
where ViewModel construction and DI wiring happen (Section 6, step 6) and
where cross-cutting decisions (like registering a new service) need a single
owner. If `CODEOWNERS` currently marks the entire `SelfCheckoutKiosk.App/`
path as lead-only with no finer-grained rule, add:

```
/src/SelfCheckoutKiosk.App/Views/    @frontend-dev
/src/SelfCheckoutKiosk.App/Styles/   @frontend-dev
/src/SelfCheckoutKiosk.App/App.xaml.cs           @project-lead
/src/SelfCheckoutKiosk.App/Composition/          @project-lead
```

so path-based rules resolve unambiguously (`CODEOWNERS` uses last-match-wins
per path, so the more specific rules must appear after the general one).

---

## SECTION 3 — Category 1 Work Package: Core Brain & Data Persistence
### (Backend Team)

**Ownership boundary:** `SelfCheckoutKiosk.Domain`, `SelfCheckoutKiosk.Core`,
`SelfCheckoutKiosk.Infrastructure/Data`

- **`LLCoreLogicEngine` state machine**
  - States: `Idle → Scanning → AwaitingPayment → ProcessingCash → DispensingChange → TransactionComplete`, with a parallel `ExactCashOnlyLockout` and `Faulted` branch reachable from any state.
  - Single subscriber to all HAL events (cash + scanner) — no other class touches hardware event streams directly.
  - Owns `RegexRouter`: classifies every scan as EAN-13 product / KHQR profile / `VCH-` offline coupon before dispatch.

- **Dual-currency math logic**
  - Input: USD transaction total + USD tendered (running total as notes are accepted).
  - Step 1 — Balance: `remaining = max(0, total − tendered)`.
  - Step 2 — Overpayment split: whole-dollar portion dispensed as USD notes; sub-dollar fractional remainder converted at live exchange rate into KHR.
  - Step 3 — KHR rounding: always rounds **down** to the nearest dispensable note denomination — the kiosk must never manufacture money it doesn't have in the cassette.

- **`OfflineLicenseManager`**
  - Verifies a signed token (asymmetric key pair; public key embedded in the AOT binary, private key held only by the licensing/signing service).
  - Validates: signature authenticity → expiration → node-lock (`HardwareId` match) → tier bracket consistency.
  - Enforces brackets: **Lite** (`MaxKiosks ≤ 2`, cash module + AI module forced off) / **Pro** (`MaxKiosks ≤ 5`, cash + AI camera telemetry unlockable) / **Enterprise** (unlimited nodes, custom ERP sync hooks).
  - Exposes a runtime gate (`EnforceFeatureAccess`) that every gated subsystem must call before initializing — not just a startup check.

- **`KioskDbContext` (EF Core + SQLCipher)**
  - AES-256 encryption at rest via the SQLCipher native bundle.
  - `OnConfiguring` raw-connection hook executes, before EF touches anything else:
    - `PRAGMA journal_mode=WAL;` — power-cut resilience, readers never block writers.
    - `PRAGMA synchronous=FULL;` — blocks until the OS confirms the write is physically on disk.
  - Tables: `Transactions` (`SyncStatus`: Pending/Completed), `Products`, `LicenseConfiguration`.

---

## SECTION 4 — Category 2 Work Package: Hardware Abstraction Layer & Connectivity
### (Systems Team)

**Ownership boundary:** `SelfCheckoutKiosk.Core/Abstractions`, all
`SelfCheckoutKiosk.Hal.Vendor.*` projects, `SelfCheckoutKiosk.Infrastructure/Sync`

- **Strategy Pattern contracts**
  - `ICashRecycler` — connect, arm/disarm acceptance, note-inserted/escrow/jam events, programmatic dispense + reject commands.
  - `IBarcodeScanner` — USB-COM virtual serial connect, background `OnBarcodeScanned` raw-string event.
  - `IReceiptPrinter` — raw ESC/POS byte-stream send to the Epson m30, paper-present check, job-status events.
  - Core business logic depends on these interfaces only; a new vendor SKU is a new adapter project, never a change to `Core`.

- **Low-float safeguard monitor**
  - `LowFloatMonitor` holds a live per-denomination KHR note count, updated from cassette inventory reads and dispense events.
  - `LowFloatThreshold = 15`. The instant any tracked KHR denomination's count drops below 15, fires `LowFloatStateTriggered`.
  - Engine reaction: immediately calls `StopAcceptingCashAsync()` and transitions the UI to an "Exact Cash / Digital Payments Only" lock screen.

- **`HardwareAppendLog`**
  - Fires synchronously off `ICashRecycler.OnNoteInEscrow`, before any other processing of that note.
  - Unbuffered write: opens the file with `FileOptions.WriteThrough`, writes, and forces `Flush(flushToDisk: true)` — bypasses OS write caching so the record survives an immediate power loss.
  - Logs a paired resolution event (`COMMITTED_TO_VAULT` / `REJECTED`) so the log is a closed audit loop; startup reconciliation scans for orphaned escrow entries with no resolution.

- **Tailscale VPN background sync worker**
  - Runs as a hosted background service inside the Tailscale-tunneled overlay network — no public-facing ports.
  - Polls `Transactions` where `SyncStatus = Pending`, pushes to the central server, marks `Completed` on ack (idempotent via `TransactionGuid`).
  - Same tunnel doubles as the secure channel for tunneled RDP remote troubleshooting — no separate VPN/firewall exception needed.

---

## SECTION 5 — Category 3 Work Package: Touchscreen Interface & OS Hardening
### (UI/Desktop Team)

**Ownership boundary:** `SelfCheckoutKiosk.App`, `deploy/Provisioning`

- **XAML view composition (WPF or WinUI 3, MVVM)**
  - `CartView` — scanned items, running USD total, remove/void line.
  - `PaymentSelectionView` — cash vs. KHQR digital, routes into `AwaitingPayment` state.
  - `IngestionProgressView` — live note-by-note escrow feedback, balance countdown.
  - `SuccessView` — receipt confirmation, change-dispensed summary, reset-to-idle timer.
  - Each view binds to exactly one ViewModel; ViewModels never reference hardware types directly — only `LLCoreLogicEngine`'s public events/state.

- **Admin Diagnostics Panel**
  - Hidden entry point: fixed PIN pad combination *or* a reserved barcode (outside the EAN-13/KHQR/coupon pattern space) scanned in sequence.
  - Surfaces: HAL device link states, cassette inventory counts, unresolved `HardwareAppendLog` entries, license tier/expiration, last sync timestamp.
  - Runs in a separate, elevated-access ViewModel gated by `OfflineLicenseManager`/local role check — never reachable from the customer-facing shell.

- **MVVM real-time dual-currency binding**
  - `LLCoreLogicEngine` state changes and balance updates raised as .NET events on the engine's owning thread; ViewModels marshal to the UI thread via the dispatcher.
  - "Zero-latency shared memory space" in practice: engine and ViewModel live in the same process/AppDomain — no IPC, no serialization boundary between balance calculation and UI refresh, just direct event-to-property-update binding.

- **PowerShell provisioning script (conceptual blueprint)**
  1. Enable and configure **Shell Launcher V2** — set the kiosk app as the shell for the dedicated kiosk local account, replacing `explorer.exe`.
  2. Enable **BitLocker** on the OS volume, bind the unlock key to **TPM 2.0** (no recovery password left on-site).
  3. Disable **USB AutoPlay/AutoRun** system-wide via policy, and restrict USB mass-storage device classes while leaving HID (scanner) and vendor cash-recycler USB-serial classes explicitly allow-listed.
  4. Lock down Windows Update to a maintenance window; disable unnecessary services and Store access.
  5. Register the app + Shell Launcher config as an idempotent script re-runnable during imaging.

---

## SECTION 6 — The Clean Integration Protocol

Step-by-step merge of Category 1 (Backend), Category 2 (Systems), and Category 3 (UI)
via `IServiceCollection` at the `SelfCheckoutKiosk.App` composition root:

1. **Register Domain/Core services first, hardware-independent.**
   `DualCurrencyCalculator`, `LowFloatMonitor`, `HardwareAppendLog`, `OfflineLicenseManager` as singletons — none of these touch a physical device at construction time.

2. **Validate the license before anything hardware-related is registered.**
   Call `OfflineLicenseManager.LoadAndValidateAsync` synchronously during host startup. Treat failure as a hard stop, not a degraded mode.

3. **Register the concrete HAL adapters from the vendor projects.**
   `services.AddSingleton<ICashRecycler, VendorXCashRecycler>()`, etc. — this is the *only* place a `Hal.Vendor.*` assembly is referenced.

4. **Register `KioskDbContext` via a factory delegate**, not a direct singleton — EF Core contexts are cheap to create and shouldn't be held open for the process lifetime.

5. **Register `LLCoreLogicEngine` last among backend services**, injecting the interfaces (not concretions) from steps 1–4. This is where Category 1 and Category 2 formally meet — the engine never knows which vendor adapter it received.

6. **Register ViewModels and the Tailscale sync worker as hosted/scoped services**, each depending only on `LLCoreLogicEngine` and `KioskDbContext` — never on `ICashRecycler`/`IBarcodeScanner` directly.

7. **Call `LLCoreLogicEngine.InitializeAsync()` once, after the host is built.**
   This is where cash-module initialization is itself license-gated (`EnforceFeatureAccess(LicensedFeature.CashRecycler)`) before `ICashRecycler.ConnectAsync()` is ever called.

8. **Wire UI updates purely through engine events**, never through direct polling:
   `OnStateChanged` → drives view navigation (Cart → Payment → Ingestion → Success).
   `OnProductAdded` → updates the cart line list and running total.
   `LowFloatStateTriggered` / `LowFloatStateCleared` → toggles the lock-screen overlay.
   `OnHardwareFault` → routes to a fault view or the Admin Diagnostics Panel.

9. **Smoke-test the seam, not just the parts.**
   Integration test: simulate a scan → escrow → dispense sequence with a mock `ICashRecycler`/`IBarcodeScanner`, and assert the UI ViewModel's bound state reaches `TransactionComplete` with the correct change breakdown — this is the one test that proves Category 1/2/3 are actually wired correctly, not just individually correct.

---

*This document maps directly onto the codebase already scaffolded in
`SelfCheckoutKiosk.Core` / `SelfCheckoutKiosk.App` — Sections 3–5 describe the same
modules already implemented (`LLCoreLogicEngine`, `DualCurrencyCalculator`,
`OfflineLicenseManager`, `KioskDbContext`, the HAL interfaces, `HardwareAppendLog`),
reframed here as team-facing work packages rather than source files.*

---

## SECTION 7 — Category 4 Work Package: Supermarket-Standard Gap Roadmap
### (Cross-Team — not blocking Sprint 0)

Sections 1–6 cover the kiosk's core transaction engine, which is intentionally
ahead of most generic self-checkout blueprints on offline resilience and
dual-currency handling. The items below are gaps against what a supermarket
buyer treats as baseline, identified by comparing this blueprint to current
industry-standard self-checkout deployments. None of these block Sprint 0;
they are staged here as a Sprint 1+ roadmap so scope is visible and estimable
rather than discovered mid-integration.

- **Card/EMV or contactless payment**
  - Currently: cash + KHQR digital only.
  - Add `ICardPaymentTerminal` to `SelfCheckoutKiosk.Core/Abstractions`,
    mirroring the `ICashRecycler` strategy pattern — a new
    `SelfCheckoutKiosk.Hal.Vendor.*` adapter project per terminal vendor, no
    change to `Core` business logic. `PaymentMethod` enum gains `Card`.
  - Owner: Category 2 (Systems Team) for the adapter; Category 1 (Backend) for
    routing it through `LLCoreLogicEngine.SelectPaymentMethodAsync`.

- **Age-restricted item approval flow**
  - Currently: no concept of a restricted item; every EAN-13 scan is treated
    identically.
  - Requires: a restricted-item flag on `Product`, a new
    `AwaitingAttendantApproval` branch in the `KioskState` machine (reachable
    from `Scanning`), and a UI notification surfaced to the Admin Diagnostics
    Panel / a remote attendant console (see below) rather than resolved
    on-device.
  - Owner: Category 1 (state machine + `Product` entity), Category 3 (UI
    notification + attendant-approval screen).

- **PLU lookup for produce (no barcode)**
  - Currently: `RegexRouter` classifies EAN-13 / KHQR / `VCH-` coupon only.
  - Add a `PluCode` category (4–5 digit numeric, distinct pattern from
    EAN-13) and a produce lookup path against `Product` keyed by PLU instead
    of EAN-13.
  - Owner: Category 1 (Backend) — `RegexRouter` + `Product` lookup only, no
    HAL change needed.

- **Loyalty/membership integration**
  - Currently: no concept of a customer identity on a transaction.
  - Requires a `LoyaltyMemberId` on `Transaction`, an entry point (scan a
    membership barcode or manual entry), and — if discounts apply — a hook
    into the dual-currency total calculation before `DualCurrencyCalculator`
    runs.
  - Owner: Category 1 (Backend); ERP-side membership lookup itself is likely
    a central-server concern, not on-kiosk, given the offline-first design —
    flag for architecture decision on whether membership data caches locally
    or requires a network round-trip (and what happens offline).

- **Digital receipt (email/SMS)**
  - Currently: `IReceiptPrinter` is print-only (raw ESC/POS to the Epson
    m30).
  - Add a separate `IDigitalReceiptDispatcher` abstraction (not a change to
    `IReceiptPrinter` — printing and digital dispatch are different
    concerns with different failure modes, especially offline). Digital
    dispatch likely queues through the same `SyncStatus`-style
    pending/completed pattern as `TailscaleSyncWorker`, since email/SMS
    require connectivity the kiosk may not have at transaction time.
  - Owner: Category 2 (Infrastructure/Sync).

- **Refund / void-whole-transaction**
  - Currently: `CartView` supports per-line void only; no path back from
    `AwaitingPayment`/`ProcessingCash` to cancel the whole transaction, and
    no post-completion refund flow at all.
  - Requires: a `VoidTransaction` action from any pre-completion state
    (distinct from the existing per-line void), and — for post-completion
    refunds — an attendant-PIN-gated flow through the Admin Diagnostics
    Panel, since refunding cash already dispensed has real loss-prevention
    implications.
  - Owner: Category 1 (state machine) + Category 3 (attendant-gated UI).

- **Idle/attract screen**
  - Currently: the view flow begins at `CartView`; `KioskState.Idle` exists
    in the enum but has no dedicated view — nothing distinguishes "waiting
    for the first scan" from mid-transaction.
  - Add an `IdleView`/`IdleViewModel` bound to `KioskState.Idle`, shown
    before the first scan and after `ResetToIdleAsync` — this is also the
    natural place for store branding/promotions and the reserved
    admin-entry barcode sequence (Section 5) to live.
  - Owner: Category 3 (UI/Desktop Team).

- **Centralized multi-kiosk attendant console**
  - Currently: the central server (Section 1) exists for reporting, catalog
    master, and license issuance — not for live per-kiosk attendant
    assistance. Each kiosk's Admin Diagnostics Panel is local-only, gated by
    PIN/barcode on that device.
  - This is very likely **out of scope for the kiosk codebase itself** — a
    live "assist queue" spanning many kiosks is a separate central-server
    application, not a `SelfCheckoutKiosk.App` concern. Flagged here so it's
    an explicit architecture decision (build a companion attendant app? defer
    to store-provided POS supervisor tooling?) rather than an assumed gap in
    this repo.
  - Owner: Project Lead — scoping decision, likely a separate solution.

**Sequencing note:** these are independent of each other and of Sections 3–6;
none require changes to the `LLCoreLogicEngine` constructor signature or
`CompositionRoot.cs` beyond what's already anticipated, except the age-restricted
approval flow and refund/void, which both add new `KioskState` branches and
should be scoped together in one pass to avoid two separate state-machine
changes landing back-to-back.
