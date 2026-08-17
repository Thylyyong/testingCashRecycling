using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace SelfCheckoutKiosk.App.Views;

/// <summary>
/// Blocking failure screen — see FaultView.xaml for the "why".
/// Navigated to instead of the normal Home/Cart flow when
/// LLCoreLogicEngine.InitializeAsync() throws.
/// </summary>
public sealed partial class FaultView : Page
{
    public FaultView()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        var reason = e.Parameter as string;
        ReasonTextBlock.Text = string.IsNullOrWhiteSpace(reason)
            ? "An unknown error occurred during startup."
            : reason;
    }
}
