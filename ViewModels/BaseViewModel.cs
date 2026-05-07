using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Plantagotchi.ViewModels;

/// <summary>
/// A base class for all ViewModels to handle property change notifications.
/// This allows the UI to update automatically when data changes.
/// </summary>
public abstract class BaseViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Notifies the UI that a specific property has changed.
    /// The [CallerMemberName] attribute automatically picks up the name of the property calling this method.
    /// </summary>
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}