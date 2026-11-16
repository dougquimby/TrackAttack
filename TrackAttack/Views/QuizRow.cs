using System.ComponentModel;
using System.Runtime.CompilerServices;
using TrackAttack.Models;

namespace TrackAttack.Views;

/// <summary>Provides the displayed state of one quiz song slot.</summary>
/// <param name="song">The song in the slot.</param>
/// <param name="number">The one-based slot number.</param>
public sealed class QuizRow(Song song, int number) : INotifyPropertyChanged
{
    private string _title = string.Empty;
    private string _artist = string.Empty;
    private string _status = string.Empty;

    /// <summary>Gets the song currently assigned to the slot.</summary>
    public Song Song { get; private set; } = song;
    /// <summary>Gets the one-based slot number.</summary>
    public int Number { get; } = number;
    /// <summary>Gets or sets the displayed title.</summary>
    public string Title { get => _title; set { _title = value; Changed(); } }
    /// <summary>Gets or sets the displayed artist.</summary>
    public string Artist { get => _artist; set { _artist = value; Changed(); } }
    /// <summary>Gets or sets the displayed answer status.</summary>
    public string Status { get => _status; set { _status = value; Changed(); } }
    /// <summary>Changes the song assigned to the slot.</summary>
    /// <param name="song">The replacement song.</param>
    public void ReplaceSong(Song song) => Song = song;

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
