# Self-Checkout Kiosk — Solution Scaffold (Sprint 0)

Offline-first self-checkout kiosk for Windows 11 IoT Enterprise. This scaffold is
the Sprint-0 skeleton: enforced architecture boundaries, Native-AOT-ready build
configuration, and the interface contracts that unblock parallel work on Day 1.

## What is verified
Built and run with the **.NET 10 SDK (10.0.110)**:

- All product projects compile under **nullable-enabled, warnings-as-errors**.
- Clean Architecture boundaries are **compile-enforced** via project references.
- The App **Native-AOT-publishes** to a self-contained native binary, and the
  composition root resolves the full Core + Infrastructure + HAL graph at runtime
  (engine reports `Idle`).

> Test projects (xUnit) and any external NuGet packages restore on your connected
> machine — this offline build environment can't reach nuget.org.

## Structure
```
src/
  SelfCheckoutKiosk.Domain            references NOTHING (pure C#)
  SelfCheckoutKiosk.Core              -> Domain            (engine, abstractions, currency, licensing)
  SelfCheckoutKiosk.Infrastructure    -> Core              (KioskDbContext, sync, security)
  SelfCheckoutKiosk.Hal.Vendor.*      -> Core              (one adapter project per device SKU)
  SelfCheckoutKiosk.App               -> Core + Infrastructure + Hal.Vendor.*   (composition root + UI)
tests/
  *.Core.Tests / *.Infrastructure.Tests / *.Integration.Tests
deploy/Provisioning/                  Lead-owned OS hardening + provisioning
```

## Dependency rules (enforced by project references)
- **Domain** depends on nothing.
- **Core** depends only on Domain. It never references Infrastructure or any HAL.
- **Hal.Vendor.\*** depend only on Core and are referenced **only** by the App.
- **App** is the single composition root — the only place vendor assemblies meet.

A new hardware SKU is a **new** `Hal.Vendor.*` project implementing a Core
abstraction, plus one line in the composition root. Core never changes.

## Native AOT
`Directory.Build.props` sets `IsAotCompatible=true` on every product project, so the
trim/AOT analyzers fail the build on AOT-hostile code the moment it's written.
`PublishAot=true` lives on the App entry executable, which pulls the whole graph
into the native image.

```
dotnet build   SelfCheckoutKiosk.sln -c Release
dotnet test    SelfCheckoutKiosk.sln
dotnet publish src/SelfCheckoutKiosk.App -c Release -r win-x64 -p:PublishAot=true
```

## Retarget notes (post AOT-spike)
- `App` and `Hal.Vendor.*` are on `net10.0` so the scaffold builds on any OS today.
  Move them to `net10.0-windows10.0.26100.0` when the WinUI 3 markup and the real
  vendor SDKs are introduced. Domain/Core/Infrastructure stay platform-neutral.
- The composition root uses **manual composition** (AOT-friendliest). The equivalent
  `Microsoft.Extensions.DependencyInjection` wiring is included as a comment block in
  `CompositionRoot.cs` if you prefer container DI.

## Ownership
- **Lead:** Directory.Build.props, composition root, licensing/secret material,
  provisioning, CI, Tailscale ACLs.
- **Back-End:** Domain, Core (engine + abstractions + currency + licensing logic),
  Infrastructure (EF Core + SQLCipher), HAL adapter internals.
- **Front-End:** ViewModels binding to `ILLCoreLogicEngine` (mock it on Day 1);
  XAML views once the framework is ratified.
- **Flutter:** separate back-office repo (catalog, coupons, reporting).

## Immediate next steps
1. Back-End: fill `RegexRouter`, `DualCurrencyCalculator`, `LowFloatMonitor` + tests.
2. Back-End: add EF Core + SQLCipher to `KioskDbContext` (WAL + synchronous=FULL).
3. Lead: run the WinUI 3 AOT spike; ratify or fall back per findings.
4. Front-End: build ViewModels + headless test harness against a mock engine.

Stubs throw `NotImplementedException` with `TODO(owner)` tags. Constructors never
throw, so the DI graph resolves before behaviour is implemented.
