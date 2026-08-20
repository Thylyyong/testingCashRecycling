using SelfCheckoutKiosk.Core.Engine;
using SelfCheckoutKiosk.Domain.Enums;
using SelfCheckoutKiosk.Presentation.Services;

namespace SelfCheckoutKiosk.Presentation.ViewModels;

/// <summary>
/// Navigation host — the one place that maps <see cref="KioskState"/> onto a
/// <see cref="KioskScreen"/> and drives the shell's ContentControl/Frame.
/// Subscribes to <see cref="ILLCoreLogicEngine.OnStateChanged"/> (background
/// thread) and marshals the resulting navigation onto the UI thread.
/// </summary>
public sealed class MainShellViewModel : KioskViewModelBase
{
    public MainShellViewModel(ILLCoreLogicEngine engine, IUiDispatcher dispatcher)
        : base(engine, dispatcher)
    {
        ActiveScreen = MapToScreen(engine.CurrentState);
        Engine.OnStateChanged += (_, e) => Dispatcher.Post(() =>
        {
            // Any real engine transition supersedes a UI-local guest-session
            // start (see BeginGuestSession) — in particular this is what
            // makes Idle show WelcomeView again for the next customer after
            // a transaction completes/cancels back to Idle, rather than
            // reopening straight into CartView's empty state.
            _guestSessionStarted = false;
            ActiveScreen = MapToScreen(e.Current);
        });
    }

    /// <summary>Presentation-layer-only flag: the engine's <see cref="KioskState"/>
    /// stays <see cref="KioskState.Idle"/> both before AND after "Shop as
    /// Guest" is tapped (it only changes on the first real
    /// <c>SubmitScanAsync</c>), so this can't be derived from engine state
    /// alone. Set by <see cref="BeginGuestSession"/>, never by the engine —
    /// the engine never learns a guest session exists, it just eventually
    /// sees a scan like any other. Reset in the constructor's OnStateChanged
    /// handler above.</summary>
    private bool _guestSessionStarted;

    private KioskScreen _activeScreen;
    public KioskScreen ActiveScreen
    {
        get => _activeScreen;
        private set
        {
            bool changed = SetProperty(ref _activeScreen, value);
            System.Diagnostics.Debug.WriteLine($"[CancelTrace] 5. ActiveScreen setter: value={value}, PropertyChanged raised={changed}, thread={Environment.CurrentManagedThreadId}");
        }
    }

    /// <summary>"Shop as Guest" hook (see WelcomeView). Purely a presentation-layer
    /// navigation decision — does not call the engine, does not attach any
    /// member context. Switches Idle's screen from WelcomeView to CartView's
    /// documented empty "scan to begin" state, which was previously
    /// unreachable because MapToScreen only ever routed Idle to
    /// <see cref="KioskScreen.Idle"/>.</summary>
    public void BeginGuestSession()
    {
        if (CurrentState != KioskState.Idle)
            return;

        _guestSessionStarted = true;
        ActiveScreen = KioskScreen.Cart;
    }

    /// <summary>Explicit counterpart to <see cref="BeginGuestSession"/>, called
    /// when Cancel fires (see CartView.OnCancelClicked). Needed because
    /// canceling before the first scan calls <c>ResetToIdleAsync</c> while the
    /// engine is already <see cref="KioskState.Idle"/> — a no-op transition
    /// (see <c>LLCoreLogicEngine.TransitionTo</c>'s early-return), so
    /// <c>OnStateChanged</c> never fires and the constructor's reset never
    /// runs. Reads <see cref="KioskViewModelBase.Engine"/> directly rather
    /// than the (possibly not-yet-dispatched) cached <c>CurrentState</c>
    /// property, so this is correct regardless of dispatcher timing relative
    /// to the caller's own engine call.</summary>
    public void EndGuestSession()
    {
        System.Diagnostics.Debug.WriteLine($"[CancelTrace] 4a. EndGuestSession entered, _guestSessionStarted={_guestSessionStarted}, Engine.CurrentState={Engine.CurrentState}, ActiveScreen(before)={ActiveScreen}, thread={Environment.CurrentManagedThreadId}");
        _guestSessionStarted = false;
        KioskScreen computed = MapToScreen(Engine.CurrentState);
        System.Diagnostics.Debug.WriteLine($"[CancelTrace] 4b. Computed ActiveScreen={computed}, about to assign");
        ActiveScreen = computed;
        System.Diagnostics.Debug.WriteLine($"[CancelTrace] 4c. ActiveScreen assigned, current value={ActiveScreen}");
    }

    /// <summary>NOTE: <see cref="KioskState.AwaitingPayment"/> is the resting
    /// state both immediately after choosing KHQR (waiting on the digital
    /// confirmation) AND between individual cash notes (waiting on the next
    /// one) — the engine's public surface doesn't expose which, so it routes
    /// to the combined Payment screen. <see cref="KioskState.ExactCashOnlyLockout"/>
    /// is unambiguous (cash-only by definition) and routes straight to
    /// Ingestion, same as the mid-tender <see cref="KioskState.ProcessingCash"/>
    /// and <see cref="KioskState.DispensingChange"/> states.</summary>
    private KioskScreen MapToScreen(KioskState state) => state switch
    {
        KioskState.Idle => _guestSessionStarted ? KioskScreen.Cart : KioskScreen.Idle,
        KioskState.Scanning => KioskScreen.Cart,
        KioskState.AwaitingPayment => KioskScreen.Payment,
        KioskState.ProcessingCash or KioskState.DispensingChange or KioskState.ExactCashOnlyLockout => KioskScreen.Ingestion,
        KioskState.TransactionComplete => KioskScreen.Success,
        KioskState.Faulted => KioskScreen.Faulted,
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Unmapped kiosk state."),
    };
}
