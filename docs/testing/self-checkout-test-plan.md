# Self-Checkout Kiosk — Test Plan & Test Scripts

Scope: WelcomeView top bar, cash banner, hero/membership, guest CTA, idle slideshow (planned),
active-session slide area (planned), and the five physical peripherals (touchscreen, barcode
scanner, card reader, cash acceptor, receipt printer). Platform: WinUI 3, .NET 6/7, 1920x1080.

Related files: [WelcomeView.xaml](../../src/SelfCheckoutKiosk.App/Views/WelcomeView.xaml),
[MainShellViewModel.cs](../../src/SelfCheckoutKiosk.Presentation/ViewModels/MainShellViewModel.cs),
[LLCoreLogicEngine.cs](../../src/SelfCheckoutKiosk.Core/Engine/LLCoreLogicEngine.cs),
[HardwareEvents.cs](../../src/SelfCheckoutKiosk.Core/Abstractions/HardwareEvents.cs).

## Assumptions (confirm with product/hardware owner before sign-off)

- Idle timeout 30s; idle slide duration 8s; crossfade 600ms.
- Active-session slide area ≤30% width, rotates 6–10s, pauses during payment entry/modals.
- Touch targets ≥44x44px; safe content area 1800x1000 centered on 1920x1080.
- Slide assets: PNG/WebP/MP4 (H.264), ≤5MB each.
- No confirmed vendor SDK for scanner/card reader/cash acceptor/printer in this repo yet
  beyond the `ICashRecycler` abstraction and `SelfCheckoutKiosk.Hal.Vendor.*` projects — hardware
  tests below assume the simulator stubs in
  [WinUI3 Implementation Plan §4](../implementation/winui3-implementation-plan.md) until real
  HAL drivers are wired through `CompositionRoot`.

---

## 1. Top Function Bar (Online, Admin, Reprint, Price Check, Recall)

| Test ID | Label | Preconditions | Steps | Expected | Pass/Fail | Severity |
|---|---|---|---|---|---|---|
| TB-01 | Online indicator reflects connectivity | Kiosk idle, network up | 1. Observe Online icon/label. 2. Disconnect network (disable NIC or unplug). 3. Wait ≤5s. 4. Reconnect. | Icon+label present at 44x44 min hit target; on disconnect, icon changes state (e.g., strike-through / red) within 5s; on reconnect, reverts within 5s. Fail if state doesn't change within 5s or button has no accessible name. | Icon state transition ≤5s both directions | High |
| TB-02 | Admin opens authenticated panel | Idle screen, admin PIN known | 1. Tap "Admin". 2. Enter valid PIN. 3. Enter invalid PIN (separate run). | Valid PIN → Admin/diagnostics view opens ≤1s. Invalid PIN → error shown, no navigation, attempt logged. | — | Critical (security) |
| TB-03 | Reprint retrieves last receipt | A completed sale exists in session/local store | 1. Tap "Reprint". 2. Confirm/select transaction if prompted. | Printer HAL invoked with last receipt payload; printer emits paper within timeout (see PR-02); UI shows "Printing…" then success/failure toast. | — | Medium |
| TB-04 | Price Check scans without adding to cart | Idle or mid-session | 1. Tap "Price Check". 2. Scan item barcode. | Price + description shown in a check-only panel; item NOT added to cart/basket total. Closing panel returns to prior screen unchanged. | — | Medium |
| TB-05 | Recall restores a suspended/parked transaction | A transaction was previously parked | 1. Tap "Recall". 2. Select parked transaction ID. | Cart restored with identical line items/total as when parked; audit log entry written. | — | High |
| TB-06 | Top bar buttons meet touch target & AutomationProperties | Any screen | 1. Measure each button's hit rectangle. 2. Inspect `AutomationProperties.Name`/`HelpText` via Inspect.exe or WinAppDriver. | Each control ≥44x44px; each has non-empty, unique `Name`; `HelpText` describes action in ≤10 words. | — | High (accessibility) |

## 2. Cash-Accepted Banner

| Test ID | Label | Preconditions | Steps | Expected | Severity |
|---|---|---|---|---|---|
| CB-01 | Banner reflects cash acceptor ready state | Cash acceptor connected & idle | Observe banner on load. | Green "CASH ACCEPTED HERE" banner visible when acceptor reports Ready. | High |
| CB-02 | Banner hides/changes on acceptor fault | Acceptor connected | Simulate fault (jam/offline) via `PeripheralSimulator.CashAcceptor.RaiseFault()`. | Banner switches to neutral/hidden state within 2s of fault event; no stale "accepted" claim shown while acceptor is down. | Critical (payment reliability) |
| CB-03 | Banner recovers after fault clear | Following CB-02 | Clear fault via simulator. | Banner returns to green "accepted" state within 2s of recovery event. | High |

## 3. Welcome / Hero / Membership / Guest

| Test ID | Label | Preconditions | Steps | Expected | Severity |
|---|---|---|---|---|---|
| WM-01 | Hero renders at 1920x1080 | Fresh idle screen | Load WelcomeView, screenshot. | "Welcome to CA!" hero fully visible, no clipping/overlap, safe area respected (content within 1800x1000 centered box). | Low |
| WM-02 | Membership buttons are placeholders (no crash) | Idle screen | Tap each of "CA Rewards App", "CA Membership Card", "Linked Card". | No unhandled exception; since no backend wired yet (see TODO in WelcomeView.xaml:102-104), button may no-op — flag as FAIL only if it throws or navigates incorrectly, not for "does nothing." | Low (tracked as known gap) |
| WM-03 | Guest CTA does not block barcode-driven start | Idle screen | 1. Do NOT tap "Shop as Guest". 2. Scan a product barcode directly. | Per `LLCoreLogicEngine.HandleProductScan`, state transitions Idle→Scanning and navigates to CartView automatically — confirms guest button is not required to start a session. | Medium |
| WM-04 | First scan transition timing | Idle screen | Scan barcode, measure time to CartView render. | Navigation completes ≤1s after scan event received by engine. | Medium |

## 4. Peripheral Tests

### 4.1 Barcode Scanner

| Test ID | Preconditions | Steps | Expected | Severity |
|---|---|---|---|---|
| BS-01 Success scan | Idle/cart screen, known-good UPC | Scan barcode | Item resolved, added to cart within 500ms, audible/visual beep+flash confirmation | Critical |
| BS-02 Unknown SKU | — | Scan unregistered/garbage barcode | UI shows "Item not found" dialog; no cart mutation; error logged with raw barcode payload | High |
| BS-03 Scanner disconnected | Physically unplug or `PeripheralSimulator.Scanner.SetConnected(false)` | Attempt scan | UI shows scanner-offline banner within 3s; Price Check/BS flows disabled or route to manual entry fallback | Critical |
| BS-04 Rapid double-scan (bounce) | — | Scan same item twice within 200ms | Only one line added, or explicit quantity-2 confirmation per product config — not silent duplicate defect | High |

### 4.2 Card Reader

| Test ID | Preconditions | Steps | Expected | Severity |
|---|---|---|---|---|
| CR-01 Approved payment | Cart with balance due, test card configured for approval | Insert/tap card | "Approved" within 8s timeout; receipt flow triggers; slide rotation paused during entry (see Active Session Slides) | Critical |
| CR-02 Declined payment | Test card configured for decline | Insert/tap card | "Declined" shown, cart preserved, user offered retry/alternate payment; no funds captured client-side | Critical |
| CR-03 Reader timeout | No card presented | Wait 8s (assumption; confirm actual HAL timeout) | UI reverts to payment-method selection with timeout message; reader reset issued | High |
| CR-04 Reader offline | `PeripheralSimulator.CardReader.SetConnected(false)` | Attempt payment | Card option disabled/hidden or shows "temporarily unavailable"; cash-only fallback offered if acceptor is up | Critical |

### 4.3 Cash Acceptor

| Test ID | Preconditions | Steps | Expected | Severity |
|---|---|---|---|---|
| CA-01 Accepted note | Cart with balance due | Insert valid note | Running total decrements correctly within 1s; banner remains "accepted" | Critical |
| CA-02 Rejected note | — | Insert crumpled/foreign note | Note returned, no total change, "note not accepted, try again" message | High |
| CA-03 Jam simulation | `PeripheralSimulator.CashAcceptor.RaiseFault(Jam)` | Trigger jam mid-insert | UI blocks further cash entry, shows attendant-required message, CB-02 banner behavior confirmed | Critical |
| CA-04 Low float / can't make change | Configure recycler float below change threshold ([LowFloatMonitor.cs](../../src/SelfCheckoutKiosk.Core/Currency/LowFloatMonitor.cs)) | Attempt cash payment requiring change | Cash option disabled or "exact change only" messaging shown before insertion, not after | High |

### 4.4 Receipt Printer

| Test ID | Preconditions | Steps | Expected | Severity |
|---|---|---|---|---|
| PR-01 Successful print | Completed sale | Trigger print (auto or Reprint) | Paper emitted within 5s (assumption; confirm vendor spec, e.g. Epson M30 driver timing); on-screen "Receipt printed" confirmation | Critical |
| PR-02 Out of paper | Simulate `PeripheralSimulator.Printer.SetPaperOut(true)` | Trigger print | UI shows "Printer needs attention", offers e-receipt/skip fallback, does not block sale completion | High |
| PR-03 Printer offline | `PeripheralSimulator.Printer.SetConnected(false)` | Trigger print | Same fallback as PR-02 within 3s detection | High |

## 5. Network / Offline Mode

| Test ID | Steps | Expected | Severity |
|---|---|---|---|
| NW-01 Offline sale completion | Disable network, complete a full cash sale | Sale completes locally, queued for sync (see [TailscaleSyncWorker.cs](../../src/SelfCheckoutKiosk.Infrastructure/Sync/TailscaleSyncWorker.cs)); Online indicator reflects offline | Critical |
| NW-02 Reconnect sync | Re-enable network after NW-01 | Queued transaction(s) sync within worker's poll interval; no duplicate submission | Critical |
| NW-03 License/offline validation | Network down, licensed device | Kiosk continues operating per `OfflineLicenseManager` grace period; no forced lockout mid-grace | High |

## 6. Accessibility

| Test ID | Steps | Expected | Severity |
|---|---|---|---|
| AX-01 High contrast | Enable Windows High Contrast theme, reload app | All text/icons remain legible (WCAG AA contrast); no invisible controls | High |
| AX-02 Screen reader labels | Enable Narrator, Tab/swipe through top bar and hero controls | Each control announces its `AutomationProperties.Name`; decorative icons are not double-announced | High |
| AX-03 Touch target audit | Automated measurement pass (WinAppDriver `.Size`) | All interactive elements ≥44x44px | Medium |

---

## Automated Harness Selectors (WinAppDriver / Appium)

```csharp
// Example: locate top bar buttons by AutomationProperties.Name
var onlineBtn = session.FindElementByAccessibilityId("TopBar.Online");
var adminBtn  = session.FindElementByName("Admin");
var reprintBtn = session.FindElementByName("Reprint");

Assert.IsTrue(onlineBtn.Size.Width >= 44 && onlineBtn.Size.Height >= 44);
adminBtn.Click();
```

Suggested harness command (WinAppDriver root session), run from PowerShell:

```powershell
Start-Process WinAppDriver.exe
# then point Appium/WinAppDriver client at the packaged app's AUMID
```

---

## Operator Checklist (printable)

```
SELF-CHECKOUT KIOSK — SHIFT-START CHECK        Date: _______  Lane: ___

[ ] PASS  [ ] FAIL   Online indicator shows connected
[ ] PASS  [ ] FAIL   Admin button opens with correct PIN
[ ] PASS  [ ] FAIL   Reprint produces last receipt
[ ] PASS  [ ] FAIL   Price Check does not add item to cart
[ ] PASS  [ ] FAIL   Recall restores a parked transaction
[ ] PASS  [ ] FAIL   "CASH ACCEPTED HERE" banner is lit green
[ ] PASS  [ ] FAIL   Barcode scanner beeps and reads test item
[ ] PASS  [ ] FAIL   Card reader accepts test tap/insert
[ ] PASS  [ ] FAIL   Cash acceptor takes a test note, returns correct change
[ ] PASS  [ ] FAIL   Receipt printer has paper and prints test receipt
[ ] PASS  [ ] FAIL   Touchscreen responds across all four corners

If ANY item is FAIL: stop using the lane, tag "OUT OF SERVICE",
notify support with the failed item numbers above.

Operator signature: _____________________
```

---

## Developer Report Template (fill per failure)

```
Test ID:        <e.g. CA-03>
Component:      <e.g. Cash Acceptor>
Failure summary: <one sentence>
Repro steps:     <numbered>
Actual result:   <observed>
Expected result: <from test plan>
Logs/screenshot: <path/ref>
Root cause (if known):
Remediation:
Estimated effort: <S/M/L or hours>
Severity:        <critical|high|medium|low>
Blocking release: <yes/no>
```

## Example JSON Test Result Entries

**Passing example**
```json
{
  "test_id": "BS-01",
  "component": "BarcodeScanner",
  "action": "Scan known-good UPC 012345678905",
  "expected": "Item resolved and added to cart within 500ms with beep/flash confirmation",
  "actual": "Item added in 210ms, beep and flash observed",
  "status": "PASS",
  "timestamp": "2026-07-30T14:02:11Z",
  "screenshot_ref": "artifacts/BS-01_20260730.png",
  "log": "logs/scanner_20260730.log#L118-L124",
  "severity": "critical"
}
```

**Failing example**
```json
{
  "test_id": "CA-04",
  "component": "CashAcceptor",
  "action": "Attempt $7.35 cash payment when recycler float is below change threshold",
  "expected": "Cash option disabled or 'exact change only' shown before note insertion",
  "actual": "Cash option remained enabled; note accepted, then error surfaced only after insertion with no change dispensed",
  "status": "FAIL",
  "timestamp": "2026-07-30T14:15:47Z",
  "screenshot_ref": "artifacts/CA-04_20260730.png",
  "log": "logs/lowfloatmonitor_20260730.log#L40-L58",
  "severity": "critical"
}
```

See [test-result-schema.json](test-result-schema.json) for the full machine-readable schema and a batch example array.
