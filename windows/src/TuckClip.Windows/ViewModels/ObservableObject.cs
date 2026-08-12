using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TuckClip.Windows.ViewModels;

/// <summary>
/// Minimal observable base used by the Windows UI. Keeping this local avoids a
/// dependency on a general-purpose MVVM framework for a small desktop client.
/// </summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetProperty<T>(
        ref T storage,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(storage, value))
        {
            return false;
        }

        storage = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
