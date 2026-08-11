# Cash Recycler Integration Task

## Goal
Implement the cash recycler adapter and wire it into the core engine flow for the current branch task.

## Current repo status
- The current branch is `feature/cash-recycler-integration`.
- The repo is buildable and testable after restoring the missing Domain boundary for this checkout.
- The adapter entry point is in `src/SelfCheckoutKiosk.Hal.Vendor.CashRecyclerX/VendorXCashRecycler.cs`.
- The engine contract is in `src/SelfCheckoutKiosk.Core/Engine/LLCoreLogicEngine.cs`.

## Scope
- Define the adapter behavior expected by `ICashRecycler`.
- Keep vendor SDK details isolated in the HAL adapter project.
- Do not move business logic into the adapter.
- Use the core engine contract as the integration boundary.

## Task checklist
- [x] Review `ICashRecycler` and align the adapter surface.
- [x] Implement `VendorXCashRecycler` methods as a safe stub or first-pass SDK wrapper.
  - Implemented as a full simulation adapter with device state machine (Disconnected/Connected/Armed/Stopped).
  - `SimulateNoteInserted` / `SimulateFault` are `internal` test helpers (InternalsVisibleTo integration tests only).
  - Real vendor SDK wiring is Sprint 1+ (no SDK package yet).
- [x] Validate the engine can construct with the adapter without breaking the seam.
  - `LLCoreLogicEngine.InitializeAsync` implemented: wires `HardwareAppendLog` FIRST to `OnNoteInEscrow`, then engine handler, low-float handlers, fault handler; then calls `ConnectAsync`.
  - Existing `Engine_ConstructsAndStartsIdle_WithMockHal` test still passes.
- [x] Add or update tests for the expected cash flow behavior.
  - `LowFloatMonitorTests.cs` — 7 unit tests (threshold boundary, re-entrancy, multi-denomination, GetAllCounts).
  - `HardwareAppendLogTests.cs` — 9 unit tests (escrow write, commit, reject, ScanOrphans reconciliation).
  - `CompositionSeamTests.cs` — 4 integration tests (construct+idle, InitializeAsync connects, note→log, low-float→stop, fault→Faulted state).
- [x] Keep architecture boundaries enforced: Core depends only on Domain, HAL depends on Core.
  - No reference additions made. All adapters still reference Core only.
  - `HardwareAppendLog` lives in Core/Engine — no new project dependencies.

## Notes
- **HardwareAppendLog** was missing from the codebase — created as the highest priority item (Blueprint §4: must fire synchronously FIRST on every OnNoteInEscrow, using FileOptions.WriteThrough + Flush(flushToDisk:true)).
- **LowFloatMonitor** stub replaced with full thread-safe implementation.
- **License gate** (`EnforceFeatureAccess`) is marked as a TODO comment in `InitializeAsync` — Backend/Lead implement this when `OfflineLicenseManager` is ready. The exact call site is clearly marked.
- **No BitLocker code** added — log file path is constructor-injected. OS volume policy is a Lead/§5 (OS Hardening) concern, not this task.

## Open items for Lead / Backend (not Systems Team scope)
- Implement `OfflineLicenseManager.EnforceFeatureAccess` and uncomment the license gate in `InitializeAsync`.
- Decide log file path policy (BitLocker-protected OS volume — §5 provisioning script).
- Decide per-denomination thresholds (Blueprint §4 TODO: currently uniform 15).
- Acquire real vendor SDK for `VendorXCashRecycler` (Sprint 1).
