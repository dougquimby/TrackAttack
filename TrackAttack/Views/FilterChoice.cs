using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TrackAttack.Views;

/// <summary>Represents a selectable filter value and its matching-song count.</summary>
/// <param name="value">The filter value.</param>
/// <param name="count">The number of matching songs.</param>
/// <param name="showSongCount">Whether to include the count in the display text.</param>
public sealed class FilterChoice(string value, int count, bool showSongCount = true) : INotifyPropertyChanged
{
    private bool _isChecked;

    /// <summary>Gets the filter value.</summary>
    public string Value { get; } = value;
    /// <summary>Gets the matching-song count.</summary>
    public int Count { get; } = count;
    /// <summary>Gets whether the count appears in the display text.</summary>
    public bool ShowSongCount { get; } = showSongCount;
    /// <summary>Gets or sets whether the filter is selected.</summary>
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
    /// <summary>Gets the display text, including an indication when no tracks match.</summary>
    public string Display => Count == 0 ? $"{Value} (no tracks)" : ShowSongCount ? $"{Value} ({Count})" : Value;

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
