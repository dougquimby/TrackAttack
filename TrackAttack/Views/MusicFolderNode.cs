using System.ComponentModel;
using System.Runtime.CompilerServices;
using TrackAttack.Models;
using TrackAttack.Services;

namespace TrackAttack.Views;

internal sealed class MusicFolderNode : INotifyPropertyChanged
{
    private bool _included;
    private bool _isExpanded;

    public string Path { get; }
    public string Name { get; }
    public string DisplayName => IsAvailable ? Name : $"{Name} (unavailable)";
    public bool IsAvailable { get; private set; } = true;
    public List<MusicFolderNode> Children { get; } = [];
    public MusicFolderNode? Parent { get; }
    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? SelectionChanged;

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
        get => Children.All(child => child.IsChecked == _included) ? _included : null;
        set
        {
            if (!value.HasValue)
                return;
            SetIncluded(value.Value);
            Parent?.NotifyAncestors();
            SelectionChanged?.Invoke();
        }
    }

    private MusicFolderNode(string path, MusicFolderNode? parent, IReadOnlyList<MusicFolderRule>? rules)
    {
        Path = path;
        Parent = parent;
        Name = parent is null ? path : System.IO.Path.GetFileName(path);
        _included = MusicFolderSelection.IsIncluded(path, rules);
    }

    public static MusicFolderNode Create(string path, IReadOnlyList<MusicFolderRule>? rules,
        ICollection<LibraryScanIssue> issues)
    {
        var fullPath = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(path));
        return CreateNode(fullPath, null, rules, issues);
    }

    private static MusicFolderNode CreateNode(
        string path, MusicFolderNode? parent, IReadOnlyList<MusicFolderRule>? rules,
        ICollection<LibraryScanIssue> issues)
    {
        var node = new MusicFolderNode(path, parent, rules);
        try
        {
            foreach (var child in Directory.EnumerateDirectories(path).OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0)
                        node.Children.Add(CreateNode(child, node, rules, issues));
                }
                catch (Exception exception) when (LocalFileMusicSource.IsFileAccessFailure(exception))
                {
                    issues.Add(new LibraryScanIssue(child, exception.Message));
                }
            }
        }
        catch (Exception exception) when (LocalFileMusicSource.IsFileAccessFailure(exception))
        {
            node.IsAvailable = false;
            issues.Add(new LibraryScanIssue(path, exception.Message));
        }
        return node;
    }

    public IEnumerable<string> Paths()
        => new[] { Path }.Concat(Children.SelectMany(child => child.Paths()));

    public void AddRules(List<MusicFolderRule> rules, bool inherited = true)
    {
        if (_included != inherited)
            rules.Add(new MusicFolderRule { Path = Path, Included = _included });
        foreach (var child in Children)
            child.AddRules(rules, _included);
    }

    private void SetIncluded(bool value)
    {
        _included = value;
        foreach (var child in Children)
            child.SetIncluded(value);
        OnPropertyChanged(nameof(IsChecked));
    }

    private void NotifyAncestors()
    {
        OnPropertyChanged(nameof(IsChecked));
        Parent?.NotifyAncestors();
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
