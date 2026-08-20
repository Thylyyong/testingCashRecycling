# Proposal (for Lead review) — Graceful Degradation When a HAL Adapter Is Unreachable

**Status:** Proposal only. Not implemented. Front-end scope for this pass was
`Presentation/` + `App/Views/` only; this crosses into `Core`/`Hal.Vendor.*`,
which is Lead/Back-End territory per `.github/CODEOWNERS` and
`Kiosk_Architectural_Blueprint.md` Section 2a.

## What triggered this

Testing Cancel-from-Cart on a real device with the CashDevice-RestAPI bridge
(`localhost:5055`) not running surfaced two related problems:

1. `LLCoreLogicEngine.ResetToIdleAsync()` calls `_cashRecycler.DisarmAcceptanceAsync()`
   unconditionally and lets any exception (e.g. `HttpRequestException` — the
   bridge process isn't listening) propagate all the way to the UI layer,
   where it's caught generically and shown as a "Hardware Error" dialog. The
   whole Cancel action fails outright — the transaction is never actually
   reset to Idle, even though disarming acceptance on an unreachable device is
   arguably not something Cancel should be blocked by at all.
2. A second identical Cancel tap (while the bridge was still unreachable)
   crashed the process with a raw access violation instead of surfacing the
   same caught exception the first tap got. A front-end mitigation (disabling
   the Cancel button for the duration of the async call, see the paired PR)
   closes off the most likely *trigger* — a second overlapping call into the
   same code path — but does not address why an unreachable HTTP dependency
   can escalate to an AV instead of a clean managed exception. That needs
   investigation with an actual crash dump / native debugger on the device;
   it isn't diagnosable from source alone. One concrete lead worth checking:
   `ItlRestCashRecycler`'s background `PollingLoopAsync` (started once from
   `ConnectAsync`) is *also* hitting the same unreachable bridge every 250ms
   the entire time, and on every failed poll it fires `OnFault` — which every
   `KioskViewModelBase`-derived ViewModel forwards through
   `IUiDispatcher.Post`. That means a continuous background stream of
   dispatcher posts is already in flight from the ambient unreachable-bridge
   condition alone, before any Cancel tap — worth ruling in or out as a
   contributing factor to dispatcher/native pressure at the moment of the
   second tap. See the related finding below for a prior confirmed incident
   in this exact area.

## Related finding — a prior confirmed heap-corruption incident in the same subsystem

`App.xaml.cs:170-192` (`OnMainWindowClosed`, subscribed at `App.xaml.cs:114`)
documents an already-confirmed, already-fixed native heap corruption bug
involving this same `ItlRestCashRecycler` background polling loop:

> Stops every background thread the composition root started BEFORE the
> process tears down. Missing this was a real bug (not hypothetical):
> `ItlRestCashRecycler`'s status-polling loop (started from `ConnectAsync`
> via a bare `Task.Run`) kept running after the window closed — still firing
> HAL events, which still called into `WinUiDispatcher.Post`, which still
> enqueued onto a `DispatcherQueue` that was itself mid-shutdown. Enqueuing
> against a dying WinRT dispatcher from a background thread during process
> teardown is exactly the kind of thing that corrupts the native heap
> (`STATUS_HEAP_CORRUPTION` / `0xC0000374`) rather than throwing a normal
> managed exception — and why it only ever showed up AT EXIT.

That specific mechanism (polling loop → `OnFault`/HAL event → `Post` against
a dispatcher that's no longer in a valid state to receive it) is already
mitigated for the *shutdown* case by disposing `CashRecycler` in
`OnMainWindowClosed`. It is **not** obviously mitigated for the *"bridge
unreachable while the app is fully running"* case this proposal is about —
the polling loop keeps firing `OnFault` every ~250ms the whole time the
bridge is down, which is a different trigger for the same class of
dispatcher/native interaction, just without the "window is closing" part of
the original bug. Given today's second-Cancel-tap access violation happened
under exactly the "bridge unreachable, polling loop firing continuously"
condition, this prior incident is the most relevant precedent to compare a
crash dump against — same subsystem, same `Post`-against-dispatcher shape,
different lifecycle phase. Not claiming it's the same bug; claiming it's the
first place to look.

(Correction for the record: an earlier version of this investigation
mis-cited this as living in `MainWindow.xaml.cs`. It has always been in
`App.xaml.cs` — confirmed by direct re-read, not assumption.)

## Why this matters beyond this one bug

A self-checkout kiosk where a customer-facing action (Cancel) can be blocked,
or — worse — crash the entire process, because a peripheral's local REST
bridge is briefly unreachable is a real production risk: bridges/USB
peripherals restart, hang, or lag in the field, and Cancel is exactly the
action a customer or attendant reaches for when something is already going
wrong. It shouldn't have a hardware-availability dependency at all for the
part of its job that's purely software state (clearing the transaction ledger
and returning to Idle).

## Proposed direction (not prescriptive — Lead's call)

1. **Decouple "reset the transaction" from "tell the device to stop
   accepting."** `ResetToIdleAsync()` currently does both in one all-or-
   nothing sequence. Consider: always perform the transaction-state reset
   (`_transaction = new Transaction(); LastChangeBreakdown = null;
   TransitionTo(KioskState.Idle);`) regardless of whether
   `DisarmAcceptanceAsync()` succeeds — treat a failed disarm call as a
   `HardwareFaultEventArgs` to surface (the device might still be accepting
   notes physically, which the attendant needs to know), not as a reason to
   leave the transaction stuck.
2. **Give `ICashRecycler` adapters (or the engine's calls into them) a
   bounded timeout + single retry** for calls made from user-interactive
   paths (Cancel, Pay Now) specifically, so an unreachable bridge fails fast
   with a clear, recoverable error rather than hanging on the default
   `HttpClient` timeout (100s) or however the current behavior manifests.
3. **Audit every other engine method that calls into `ICashRecycler`/
   `IBarcodeScanner`/`IReceiptPrinter`** (`ArmAcceptanceAsync` in whichever
   method arms it, `DispenseAsync`, etc.) for the same all-or-nothing
   coupling between "hardware call" and "transaction state transition" —
   Cancel is unlikely to be the only path with this shape.
4. **Investigate the AV separately from the graceful-degradation fix** — it's
   a stability bug in its own right (an unreachable peripheral should never
   be able to crash the whole kiosk process, independent of whether Cancel
   *also* gets more resilient). Suggest attaching a native debugger / enabling
   crash dumps for the next repro, since managed-only tracing (what the
   front-end can add) won't show a heap corruption's actual origin.

## What's explicitly NOT proposed here

Not proposing the front-end fabricate a "successful" cancel when hardware
calls fail, nor proposing any change to `ICashRecycler`'s public interface
shape without Lead/Back-End sign-off — this is a description of the problem
and a direction, not a diff.
