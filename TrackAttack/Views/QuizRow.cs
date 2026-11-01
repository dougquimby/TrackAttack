using System.ComponentModel;
using System.Runtime.CompilerServices;
using TrackAttack.Models;

namespace TrackAttack.Views;

public sealed class QuizRow(Song song, int number) : INotifyPropertyChanged
{
    private string _title = string.Empty;
    private string _artist = string.Empty;
    private string _status = string.Empty;

    public Song Song { get; private set; } = song;
    public int Number { get; } = number;
    public string Title { get => _title; set { _title = value; Changed(); } }
    public string Artist { get => _artist; set { _artist = value; Changed(); } }
    public string Status { get => _status; set { _status = value; Changed(); } }
    public void ReplaceSong(Song song) => Song = song;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
