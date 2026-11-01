using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TrackAttack.Views;

public sealed class FilterChoice(string value, int count, bool showSongCount = true) : INotifyPropertyChanged
{
    private bool _isChecked;

    public string Value { get; } = value;
    public int Count { get; } = count;
    public bool ShowSongCount { get; } = showSongCount;
    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (_isChecked == value)
                return;
            _isChecked = value;
            OnPropertyChanged();
        }
    }
    public string Display => ShowSongCount ? $"{Value} ({Count})" : Value;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
