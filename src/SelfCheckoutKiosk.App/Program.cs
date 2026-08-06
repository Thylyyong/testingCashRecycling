using SelfCheckoutKiosk.App;
using SelfCheckoutKiosk.App.Composition;

if (args.Contains("--verify-hardware"))
{
    await HardwareVerificationHarness.RunAsync();
    return;
}

// Sprint 0 entry point: proves Category 1/2/3 wire together through the
// composition root and the full object graph resolves. Replaced by the WinUI 3
// application bootstrap once the presentation framework is ratified.
var services = CompositionRoot.Build();

Console.WriteLine("[SelfCheckoutKiosk] Composition root built successfully.");
Console.WriteLine($"[SelfCheckoutKiosk] Engine resolved; initial state = {services.Engine.CurrentState}.");
Console.WriteLine("[SelfCheckoutKiosk] Sprint 0 scaffold — fill stubs per work-package ownership.");
Console.WriteLine("\n[Tip] To run real-hardware verification tests over USB, run: dotnet run --project src/SelfCheckoutKiosk.App -- --verify-hardware");
