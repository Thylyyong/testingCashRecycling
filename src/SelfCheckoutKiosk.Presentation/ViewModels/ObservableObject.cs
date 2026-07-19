using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SelfCheckoutKiosk.Presentation.ViewModels;

/// <summary>
/// Minimal framework-agnostic INotifyPropertyChanged base so ViewModels build
/// and unit-test with no UI framework present. Replace with CommunityToolkit.Mvvm
/// ObservableObject on the connected machine if preferred.
/// </summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }
}
