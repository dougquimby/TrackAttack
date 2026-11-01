using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TrackAttack.Models;

namespace TrackAttack.Views;

public partial class SongsView : UserControl
{
    private readonly IReadOnlyList<Song> _songs;

    public SongsView(IReadOnlyList<Song> songs)
    {
        _songs = songs;
        InitializeComponent();
        SongsGrid.ItemsSource = songs;
        UpdateCount(songs.Count);
    }

    private void OnSearchClick(object sender, RoutedEventArgs e) => ApplySearch();
    private void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            ApplySearch();
        }
    }

    private void ApplySearch()
    {
        var term = SearchText.Text.Trim();
        var column = SearchColumn.SelectedIndex;
        var matches = _songs.Where(song =>
        {
            if (string.IsNullOrEmpty(term)) return true;
            var value = column switch
            {
                1 => song.Artist,
                2 => song.Album,
                3 => song.Genre,
                4 => song.Year.ToString(),
                _ => song.Title
            };
            return value.Contains(term, StringComparison.CurrentCultureIgnoreCase);
        }).ToArray();
        SongsGrid.ItemsSource = matches;
        UpdateCount(matches.Length);
    }

    private void UpdateCount(int count) => CountText.Text = $"{count:N0} song(s)";
}
