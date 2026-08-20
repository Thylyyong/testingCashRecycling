# Self-Checkout Kiosk — Blueprint Addendum
### Section 2b — Open / Provisional Items (Not Yet Ratified)

*This addendum is meant to be read alongside `Kiosk_Architectural_Blueprint.md`.
It does not modify Sections 1–7. Unlike Section 2a — which ratified a
practice that had already settled — this section is a running log of
decisions made inside individual Claude Code sessions that deviate from or
extend the literal text of the blueprint, and have NOT yet been reviewed or
ratified by the project owner. Nothing here is authoritative architecture.
It exists so deviations stay visible and don't quietly become de facto
architecture through repeated sessions. Each entry should eventually be
resolved — ratified into the main sections, reverted to match the literal
blueprint text, or explicitly rejected.*

---

## Open Item 1 — LowFloatMonitor: per-denomination threshold vs. flat 15

- **Blueprint text (Section 4):** `LowFloatThreshold = 15` — one flat
  threshold applied uniformly across all KHR denominations.
- **What was implemented instead** (per `backend_completion_prompt.md`,
  Task 3): `IReadOnlyDictionary<int, int>` denomination → threshold,
  defaulting every denomination to 15 if unconfigured, but overridable
  per-denomination. Framed in the prompt as a "review finding" rather than
  a blueprint change.
- **Status:** PROVISIONAL. Implemented as described. Not yet folded into
  Section 4 as ratified text.
- **Resolution needed:** confirm whether per-denomination override is
  wanted long-term (e.g. because low-value KHR notes deplete faster than
  high-value ones), or whether behavior should be constrained back to a
  single flat threshold. Until resolved, treat the dictionary-based
  implementation as the working default, not settled architecture.

## Open Item 2 — Media caching / catalog sync / local-override conflict model

- **Blueprint text:** no mention anywhere in Sections 1–7 of
  `WelcomeMediaItem`, a pulled catalog/media manifest, or an
  `IsLocalOverride`/conflict-pending sync model. Section 1 describes the
  central server strictly for reporting, catalog master, and license
  issuance. Section 4 describes `TailscaleSyncWorker` as push-only
  (`Transactions` where `SyncStatus = Pending` → central, marked
  `Completed` on ack).
- **What's proposed** (per `kiosk_backend_media_sync_prompt.md`): pulling a
  catalog/media manifest down from the central server, caching and
  checksumming media files locally, and a conflict-hold precedence model
  for locally-overridden `Product`/`WelcomeMediaItem` records.
- **Status:** NOT YET RUN. The prompt is instructed to stop and ask,
  in-session, before implementing this if it isn't already blueprint-
  ratified — per direction, that check happens live in the Claude Code
  session rather than being pre-ratified here.
- **Resolution needed:** once that session surfaces the question, decide
  whether to (a) ratify a new blueprint section covering catalog/media
  sync, (b) let it proceed provisionally with an explicit provisional
  marker carried through to code comments and docs, or (c) descope it
  entirely for now.

## Open Item 3 — ItlRestCashRecycler vendor adapter (REST bridge, not direct SDK)

- **Blueprint text (Section 4):** `ICashRecycler` adapters described as
  direct vendor-SDK-style implementations, one per vendor SKU
  (`Hal.Vendor.CashRecyclerX` is the only cash-recycler adapter named).
- **What exists in the repo:** a second cash-recycler adapter,
  `SelfCheckoutKiosk.Hal.Vendor.ItlRestCashRecycler`, which talks to a
  same-machine local REST bridge service (`http://localhost:5055/`) rather
  than a direct SDK/serial integration. Wired into `CompositionRoot.cs` as
  the active `ICashRecycler` implementation.
- **Status:** IN USE, but undocumented — not mentioned anywhere in
  Sections 1–7, and (until this pass) not listed in `.github/CODEOWNERS`.
- **Resolution needed:** confirm whether the REST-bridge pattern is the
  intended long-term integration approach for this vendor (vs. `CashRecyclerX`
  as a direct-SDK adapter) and, if so, fold a short note into Section 4
  describing the pattern so future adapters know which style to follow.
  `CODEOWNERS` has been updated to declare ownership in the meantime.

## Open Item 4 — IProductCatalog abstraction

- **Blueprint text:** `Core/Abstractions` is scoped to `ICashRecycler`,
  `IBarcodeScanner`, `IReceiptPrinter` (Section 2, Section 4).
- **What exists in the repo:** `IProductCatalog`/`EfProductCatalog`, feeding
  `LLCoreLogicEngine` for EAN-13 lookups after `RegexRouter` classification.
  A reasonable, arguably necessary addition — the engine needs a product
  lookup path from somewhere — but not named in the blueprint's abstraction
  list.
- **Status:** IN USE, undocumented.
- **Resolution needed:** low priority; likely just needs a one-line
  addition to Section 4's abstraction list next time the blueprint is
  revised. Not blocking anything.

---

**How to use this log:** when either item above gets resolved, move its
entry into the appropriate numbered section of the main blueprint (as
Section 2a did for the Presentation/App split) and delete it from here.
This file should stay short — it's a holding pen, not a permanent home.
