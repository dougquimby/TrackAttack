using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TrackAttack.Views;

internal sealed class YearFilterNode : INotifyPropertyChanged
{
    private readonly YearFilterNode? _parent;
    private readonly Action _selectionChanged;
    private bool _isChecked;
    private bool _isExpanded;

    public string Value { get; }
    public List<YearFilterNode> Children { get; } = [];
    public string Display { get; }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value) return;
            _isExpanded = value;
            OnPropertyChanged();
        }
    }

    public bool? IsChecked
    {
        get => Children.Count == 0 ? _isChecked
            : Children.All(child => child.IsChecked == true) ? true
            : Children.All(child => child.IsChecked == false) ? false : null;
        set
        {
            if (!value.HasValue)
                return;
            SetChecked(value.Value);
            _parent?.OnPropertyChanged(nameof(IsChecked));
            _selectionChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public YearFilterNode(string value, int count, bool showSongCount,
        bool isChecked, Action selectionChanged, YearFilterNode? parent = null)
    {
        Value = value;
        Display = count == 0 ? $"{value} (no tracks)" : showSongCount ? $"{value} ({count})" : value;
        _isChecked = isChecked;
        _selectionChanged = selectionChanged;
        _parent = parent;
    }

    public IEnumerable<string> SelectedValues()
        => Children.Count == 0
            ? _isChecked ? [Value] : []
            : Children.SelectMany(child => child.SelectedValues());

    private void SetChecked(bool value)
    {
        _isChecked = value;
        foreach (var child in Children)
            child.SetChecked(value);
        OnPropertyChanged(nameof(IsChecked));
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
