using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using TrackAttack.Data;
using TrackAttack.Models;
using TrackAttack.Settings;

namespace TrackAttack.Views;

public partial class SettingsView : UserControl
{
    private readonly AppSettings _settings = AppSettings.Load();
    private readonly IQuizHistoryRepository _history;

    public event Action? SettingsSaved;

    public SettingsView(IQuizHistoryRepository history)
    {
        _history = history;
        InitializeComponent();
        FolderText.Text = _settings.MusicDirectory;
        SecondsText.Text = _settings.DefaultSecondsPerSong.ToString();
        ClipDurationText.Text = _settings.DefaultClipDurationSeconds.ToString();
        FuzzyArtistCheck.IsChecked = _settings.FuzzyArtistMatching;
        FuzzyTitleCheck.IsChecked = _settings.FuzzySongTitleMatching;
        IgnorePunctuationCheck.IsChecked = _settings.IgnorePunctuation;
        SkipCorrectCheck.IsChecked = _settings.SkipOnCorrectGuess;
        ShowCountsCheck.IsChecked = _settings.ShowSongCountsInLists;
        ProviderCombo.ItemsSource = Enum.GetValues<MetadataSuggestionProviderKind>();
        ProviderCombo.SelectedItem = _settings.MetadataSuggestionProvider;
        PathsText.Text = $"Settings: {AppSettings.FilePath}{Environment.NewLine}Quiz history: {DatabaseInitializer.DatabasePath}";
    }

    private void OnBrowseClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Select your music library", FolderName = FolderText.Text };
        if (dialog.ShowDialog() == true)
            FolderText.Text = dialog.FolderName;
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(SecondsText.Text, out var seconds) || seconds is < 10 or > 600
            || !int.TryParse(ClipDurationText.Text, out var clipDuration) || clipDuration is < 3 or > 120)
        {
            MessageBox.Show("Seconds per song must be 10–600 and clip duration must be 3–120.",
                "Invalid settings", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _settings.MusicDirectory = FolderText.Text.Trim();
        _settings.DefaultSecondsPerSong = seconds;
        _settings.DefaultClipDurationSeconds = clipDuration;
        _settings.FuzzyArtistMatching = FuzzyArtistCheck.IsChecked == true;
        _settings.FuzzySongTitleMatching = FuzzyTitleCheck.IsChecked == true;
        _settings.IgnorePunctuation = IgnorePunctuationCheck.IsChecked == true;
        _settings.SkipOnCorrectGuess = SkipCorrectCheck.IsChecked == true;
        _settings.ShowSongCountsInLists = ShowCountsCheck.IsChecked == true;
        if (ProviderCombo.SelectedItem is MetadataSuggestionProviderKind provider)
            _settings.MetadataSuggestionProvider = provider;

        try
        {
            _settings.Save();
            SettingsSaved?.Invoke();
            MessageBox.Show("Settings saved.", "Settings", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not save settings:\n{ex.Message}", "Settings save failed",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void OnClearHistoryClick(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("Permanently delete all quiz history? This cannot be undone.", "Clear quiz history",
                MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
            return;
        try
        {
            await _history.ClearAllAsync();
            MessageBox.Show("Quiz history cleared.", "History cleared", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not clear quiz history:\n{ex.Message}", "History clear failed",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
