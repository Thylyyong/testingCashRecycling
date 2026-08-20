# WinUI 3 Implementation Plan — Slideshow, Accessibility Hooks, Peripheral Simulation

Companion to [self-checkout-test-plan.md](../testing/self-checkout-test-plan.md). Snippets target
.NET 6/7 + WinUI 3, compatible with the existing `SelfCheckoutKiosk.App` / `.Presentation` split
(MVVM, `ObservableObject`, `IUiDispatcher`).

## 1. AutomationProperties on top-bar buttons

Apply names/help text to every interactive control so WinAppDriver/Narrator can address them.
Add to [WelcomeView.xaml](../../src/SelfCheckoutKiosk.App/Views/WelcomeView.xaml) top bar:

```xml
<StackPanel Orientation="Horizontal" Spacing="6"
            AutomationProperties.Name="Online status"
            AutomationProperties.HelpText="Shows kiosk network connectivity"
            AutomationProperties.AutomationId="TopBar.Online">
    <FontIcon Glyph="&#xE897;" FontSize="16" Foreground="White" AutomationProperties.AccessibilityView="Raw" />
    <TextBlock x:Name="OnlineLabel" Text="Online" Foreground="White" FontSize="14" />
</StackPanel>

<Button x:Name="AdminButton"
        AutomationProperties.Name="Admin"
        AutomationProperties.HelpText="Opens the attendant admin panel, PIN required"
        AutomationProperties.AutomationId="TopBar.Admin"
        MinWidth="44" MinHeight="44"
        Click="OnAdminClick">
    <StackPanel Orientation="Horizontal" Spacing="6">
        <FontIcon Glyph="&#xE72E;" FontSize="16" AutomationProperties.AccessibilityView="Raw" />
        <TextBlock Text="Admin" FontSize="14" />
    </StackPanel>
</Button>
```

Rules applied uniformly: give every `Button`/interactive `StackPanel` an `AutomationProperties.Name`
and `AutomationId`; mark purely decorative `FontIcon`s as `AccessibilityView="Raw"` so Narrator
doesn't double-announce icon + label; enforce `MinWidth`/`MinHeight` 44 via a shared style:

```xml
<Style x:Key="KioskTouchButtonStyle" TargetType="Button">
    <Setter Property="MinWidth" Value="44" />
    <Setter Property="MinHeight" Value="44" />
</Style>
```

## 2. Idle slideshow overlay + rotation

```xml
<!-- IdleSlideshowOverlay.xaml -->
<Grid x:Name="IdleOverlay"
      Visibility="Collapsed"
      Background="Black"
      AutomationProperties.Name="Promotional slideshow"
      PointerPressed="OnAnyInput" Tapped="OnAnyInput" KeyDown="OnAnyInput">
    <Grid Width="1800" Height="1000" HorizontalAlignment="Center" VerticalAlignment="Center">
        <Image x:Name="SlideImageA" Stretch="Uniform" Opacity="1" />
        <Image x:Name="SlideImageB" Stretch="Uniform" Opacity="0" />
    </Grid>
</Grid>
```

```csharp
public sealed class IdleSlideshowController
{
    private readonly DispatcherTimer _idleTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    private readonly DispatcherTimer _slideTimer = new() { Interval = TimeSpan.FromSeconds(8) };
    private readonly IReadOnlyList<SlideAsset> _slides;
    private int _index;
    private bool _showingA = true;

    public IdleSlideshowController(IReadOnlyList<SlideAsset> slides) => _slides = slides;

    public event Action<SlideAsset>? SlideChanged;
    public event Action? OverlayShown;
    public event Action? OverlayHidden;

    public void RegisterInputActivity()
    {
        _idleTimer.Stop();
        _idleTimer.Start();
        if (_slideTimer.IsEnabled) StopSlideshow();
    }

    public void Start()
    {
        _idleTimer.Tick += (_, _) => { _idleTimer.Stop(); StartSlideshow(); };
        _idleTimer.Start();
    }

    private void StartSlideshow()
    {
        OverlayShown?.Invoke();
        _index = 0;
        ShowCurrentSlide();
        _slideTimer.Tick += (_, _) => Advance();
        _slideTimer.Start();
    }

    private void StopSlideshow()
    {
        _slideTimer.Stop();
        OverlayHidden?.Invoke();
    }

    private void Advance()
    {
        _index = (_index + 1) % _slides.Count;
        ShowCurrentSlide();
    }

    private void ShowCurrentSlide() => SlideChanged?.Invoke(_slides[_index]);
}
```

Crossfade (600ms) in code-behind using `Storyboard`/`DoubleAnimation` on `SlideImageA`/`B`
opacity, swapping which `Image` is "active" each tick — omitted here for brevity, standard
WinUI opacity-crossfade pattern.

Wire input detection at the window level so *any* touch/key cancels idle immediately:

```csharp
// MainWindow.xaml.cs
this.Content.PointerPressed += (_, _) => _idleController.RegisterInputActivity();
this.Content.KeyDown += (_, _) => _idleController.RegisterInputActivity();
```

## 3. Active-session slide area (non-blocking, pausable)

```xml
<!-- Placed inside CartView, docked so it never overlaps cart controls -->
<Border Grid.Column="1" Width="Auto" MaxWidth="{Binding ActiveSlideMaxWidth}"
        AutomationProperties.Name="Promotional content"
        AutomationProperties.AccessibilityView="Raw"
        Visibility="{Binding IsSlideAreaVisible}">
    <Image Source="{Binding CurrentActiveSlide.ImageSource}" Stretch="Uniform" />
</Border>
```

```csharp
public sealed class ActiveSessionSlideController
{
    private readonly DispatcherTimer _timer = new();
    private readonly Random _rng = new();
    public bool IsPaused { get; private set; }

    public void Pause() { IsPaused = true; _timer.Stop(); }
    public void Resume()
    {
        if (IsPaused)
        {
            IsPaused = false;
            _timer.Interval = TimeSpan.FromSeconds(_rng.Next(6, 11));
            _timer.Start();
        }
    }
}
```

Pause/resume hooks tie into existing view-model state
(`PaymentSelectionViewModel`, modal dialog open/close) — call `Pause()` when payment entry
starts or any modal opens, `Resume()` on close. Error/payment overlays must always render above
the slide area (`Canvas.ZIndex` higher, or simply place slide `Border` earlier in z-order than
modal `ContentDialog`s, which WinUI already renders on top).

Max width binding:

```csharp
public double ActiveSlideMaxWidth => ActualWindowWidth * 0.30; // 30% cap per spec
```

## 4. Peripheral simulation stubs (for QA without real hardware)

```csharp
public interface IBarcodeScanner
{
    event Action<string>? BarcodeScanned;
    bool IsConnected { get; }
}

public interface ICardReaderDevice
{
    event Action<CardResult>? TransactionCompleted;
    Task<bool> BeginTransactionAsync(decimal amount, TimeSpan timeout);
}

public interface IReceiptPrinterDevice
{
    bool IsPaperAvailable { get; }
    bool IsConnected { get; }
    Task<bool> PrintReceiptAsync(ReceiptPayload payload);
}

public sealed record CardResult(bool Approved, string? DeclineReason);
public sealed record ReceiptPayload(string TransactionId, IReadOnlyList<string> Lines);

public sealed class PeripheralSimulator
{
    public sealed class ScannerSim : IBarcodeScanner
    {
        public bool IsConnected { get; private set; } = true;
        public event Action<string>? BarcodeScanned;
        public void SetConnected(bool connected) => IsConnected = connected;
        public void SimulateScan(string upc) => BarcodeScanned?.Invoke(upc);
    }

    public sealed class CardReaderSim : ICardReaderDevice
    {
        public bool IsConnected { get; private set; } = true;
        public bool ForceDecline { get; set; }
        public event Action<CardResult>? TransactionCompleted;
        public void SetConnected(bool connected) => IsConnected = connected;

        public async Task<bool> BeginTransactionAsync(decimal amount, TimeSpan timeout)
        {
            if (!IsConnected) return false;
            await Task.Delay(300);
            var result = new CardResult(!ForceDecline, ForceDecline ? "DO_NOT_HONOR" : null);
            TransactionCompleted?.Invoke(result);
            return result.Approved;
        }
    }

    public sealed class CashAcceptorSim
    {
        public event Action<CashFault>? FaultRaised;
        public event Action? FaultCleared;
        public void RaiseFault(CashFault fault) => FaultRaised?.Invoke(fault);
        public void ClearFault() => FaultCleared?.Invoke();
    }

    public sealed class PrinterSim : IReceiptPrinterDevice
    {
        public bool IsConnected { get; private set; } = true;
        public bool IsPaperAvailable { get; private set; } = true;
        public void SetConnected(bool connected) => IsConnected = connected;
        public void SetPaperOut(bool paperOut) => IsPaperAvailable = !paperOut;

        public Task<bool> PrintReceiptAsync(ReceiptPayload payload) =>
            Task.FromResult(IsConnected && IsPaperAvailable);
    }
}

public enum CashFault { Jam, SensorFault, LowFloat }
```

Register simulator implementations behind the same interfaces used by
`SelfCheckoutKiosk.Hal.Vendor.*` real drivers in `CompositionRoot`, gated by a QA/debug build flag
— never ship the simulator wired in a release configuration.

## 5. InternalsVisibleTo for test hooks

```xml
<!-- SelfCheckoutKiosk.Presentation.csproj -->
<ItemGroup>
  <InternalsVisibleTo Include="SelfCheckoutKiosk.Integration.Tests" />
</ItemGroup>
```

```csharp
[assembly: InternalsVisibleTo("SelfCheckoutKiosk.Integration.Tests")]

internal sealed class IdleSlideshowTestHooks
{
    internal static void ForceIdleTimeout(IdleSlideshowController controller) =>
        controller.GetType().GetMethod("StartSlideshow", BindingFlags.NonPublic | BindingFlags.Instance);
}
```

Prefer exposing an `internal` method directly over reflection where possible; reflection shown
only as a fallback when a third-party base class hides the member.

## 6. Slide asset metadata schema

```json
{
  "id": "promo-2026-summer-01",
  "title": "Summer Snacks Sale",
  "alt_text": "Chips and soda display, 2 for $5 promotion",
  "duration_seconds": 8,
  "priority": 5,
  "start_date": "2026-06-01",
  "end_date": "2026-08-31",
  "language": "en-US",
  "format": "webp",
  "max_size_mb": 5,
  "placement": ["idle", "active_session"]
}
```

`priority` (integer, higher wins ties in rotation order); `placement` distinguishes idle-only vs.
shared assets. Error/payment overlays are not slides — they are handled by the pause/z-order rule
in §3, not by priority within this schema.
