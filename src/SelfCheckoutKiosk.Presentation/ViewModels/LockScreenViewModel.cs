using SelfCheckoutKiosk.Core.Engine;
using SelfCheckoutKiosk.Presentation.Services;

namespace SelfCheckoutKiosk.Presentation.ViewModels;

/// <summary>
/// Overlays the "Exact Cash / Digital Payments Only" notice whenever the
/// engine's <see cref="LowFloatMonitor"/> reports a KHR denomination below
/// threshold. Purely an overlay flag — the underlying screen keeps rendering
/// underneath; it does not itself navigate.
/// </summary>
public sealed class LockScreenViewModel : KioskViewModelBase
{
    public LockScreenViewModel(ILLCoreLogicEngine engine, IUiDispatcher dispatcher)
        : base(engine, dispatcher)
    {
        Engine.LowFloatStateTriggered += (_, _) => Dispatcher.Post(() => IsLocked = true);
        Engine.LowFloatStateCleared += (_, _) => Dispatcher.Post(() => IsLocked = false);
    }

    private bool _isLocked;
    public bool IsLocked
    {
        get => _isLocked;
        private set => SetProperty(ref _isLocked, value);
    }

    public string Message =>
        "Physical KHR notes are running low. Exact cash or digital (KHQR) payments only until restocked.";
}
