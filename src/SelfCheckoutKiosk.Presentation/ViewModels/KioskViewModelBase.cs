using SelfCheckoutKiosk.Core.Engine;
using SelfCheckoutKiosk.Domain.Enums;

namespace SelfCheckoutKiosk.Presentation.ViewModels;

/// <summary>
/// Base for all screen ViewModels. Holds the engine ABSTRACTION only — never a
/// hardware, DB, or vendor type. Mirrors engine state into a bindable property.
/// Front-End devs mock ILLCoreLogicEngine to develop + test with zero hardware.
/// </summary>
public abstract class KioskViewModelBase : ObservableObject
{
    protected ILLCoreLogicEngine Engine { get; }

    private KioskState _currentState;
    public KioskState CurrentState
    {
        get => _currentState;
        private set => SetProperty(ref _currentState, value);
    }

    protected KioskViewModelBase(ILLCoreLogicEngine engine)
    {
        Engine = engine;
        _currentState = engine.CurrentState;
        Engine.OnStateChanged += (_, e) => CurrentState = e.Current;
    }
}
