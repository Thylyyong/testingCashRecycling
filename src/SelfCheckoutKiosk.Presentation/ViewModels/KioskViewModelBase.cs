using SelfCheckoutKiosk.Core.Engine;
using SelfCheckoutKiosk.Domain.Enums;
using SelfCheckoutKiosk.Presentation.Services;

namespace SelfCheckoutKiosk.Presentation.ViewModels;

/// <summary>
/// Base for all screen ViewModels. Holds the engine ABSTRACTION only — never a
/// hardware, DB, or vendor type. Mirrors engine state into a bindable property.
/// Front-End devs mock ILLCoreLogicEngine to develop + test with zero hardware.
///
/// Every engine event handler fires on whatever background thread the HAL
/// adapter raised it from, so every property write reachable from one is
/// routed through <see cref="Dispatcher"/> onto the UI thread.
/// </summary>
public abstract class KioskViewModelBase : ObservableObject
{
    protected ILLCoreLogicEngine Engine { get; }
    protected IUiDispatcher Dispatcher { get; }

    private KioskState _currentState;
    public KioskState CurrentState
    {
        get => _currentState;
        private set => SetProperty(ref _currentState, value);
    }

    private bool _hasFault;
    public bool HasFault
    {
        get => _hasFault;
        private set => SetProperty(ref _hasFault, value);
    }

    private string? _faultMessage;
    public string? FaultMessage
    {
        get => _faultMessage;
        private set => SetProperty(ref _faultMessage, value);
    }

    protected KioskViewModelBase(ILLCoreLogicEngine engine, IUiDispatcher dispatcher)
    {
        Engine = engine;
        Dispatcher = dispatcher;
        _currentState = engine.CurrentState;
        Engine.OnStateChanged += (_, e) => Dispatcher.Post(() => CurrentState = e.Current);
        Engine.OnHardwareFault += (_, e) => Dispatcher.Post(() =>
        {
            HasFault = true;
            FaultMessage = $"{e.Device}: {e.Message}";
        });
    }
}
