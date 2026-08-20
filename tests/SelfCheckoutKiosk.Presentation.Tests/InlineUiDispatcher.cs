using SelfCheckoutKiosk.Presentation.Services;

namespace SelfCheckoutKiosk.Presentation.Tests;

/// <summary>Test fake — runs posted callbacks synchronously, no UI thread.</summary>
public sealed class InlineUiDispatcher : IUiDispatcher
{
    public void Post(Action action) => action();
}
