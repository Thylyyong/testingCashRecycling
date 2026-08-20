namespace SelfCheckoutKiosk.Presentation.Services;

/// <summary>
/// Marshals a callback onto the UI thread. Engine events (hardware polling,
/// state transitions) fire on background threads — every ViewModel property
/// update reachable from an engine event handler MUST go through this.
///
/// Framework-agnostic on purpose (mirrors <c>ObservableObject</c>): the App
/// project supplies a WinUI 3 <c>DispatcherQueue</c>-backed implementation;
/// tests supply an inline "run synchronously" fake, so ViewModels stay
/// unit-testable with no UI framework loaded.
///
/// CONTRACT for any real-UI-framework implementation (unlike a test fake):
/// every call MUST always queue a new dispatcher message, even when made
/// from the UI thread itself — never run the callback inline as an
/// "optimization." An engine event can be raised synchronously from a
/// UI-thread call stack that is still inside the engine's internal lock
/// and/or inside a live modal dialog's await continuation; running the UI
/// reaction inline in that case mutates the visual tree while that frame is
/// still unwinding, which can wedge the UI thread. See
/// <c>SelfCheckoutKiosk.App.Services.WinUiDispatcher</c> for the incident
/// this used to cause.
/// </summary>
public interface IUiDispatcher
{
    void Post(Action action);
}
