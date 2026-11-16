using System.ComponentModel;
using SpotifyAPI.Web;

namespace TrackAttack.Views;

internal sealed class SpotifyPlaylistChoice(FullPlaylist playlist, bool isAccessDenied = false) : INotifyPropertyChanged
{
    private bool _isAccessDenied = isAccessDenied;

    public FullPlaylist Playlist { get; } = playlist;

    public bool IsAccessDenied
    {
        get => _isAccessDenied;
        set
        {
            if (_isAccessDenied == value)
                return;
            _isAccessDenied = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsAccessDenied)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Display)));
        }
    }

    public string Display
    {
        get
        {
            var owner = Playlist.Owner?.DisplayName;
            if (string.IsNullOrWhiteSpace(owner))
                owner = Playlist.Owner?.Id;
            if (string.IsNullOrWhiteSpace(owner))
                owner = "Unknown owner";
            return $"{Playlist.Name} — {owner}{(IsAccessDenied ? " (access denied)" : "")}";
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
