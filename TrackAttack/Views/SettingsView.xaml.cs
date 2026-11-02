using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using TrackAttack.Data;
using TrackAttack.Models;
using TrackAttack.Services;
using TrackAttack.Settings;

namespace TrackAttack.Views;

public partial class SettingsView : UserControl
{
    private readonly AppSettings _settings = AppSettings.Load();
    private readonly IQuizHistoryRepository _history;
    private bool _isInitializingAudioVolume = true;
    private bool _isInitializingProfiles = true;

    public event Action? SettingsSaved;
    public event Action? ProfileChanged;

    public SettingsView(IQuizHistoryRepository history)
    {
        _history = history;
        InitializeComponent();
        ProfileCombo.ItemsSource = _settings.Profiles;
        ProfileCombo.SelectedItem = _settings.Profiles.FirstOrDefault(profile =>
            string.Equals(profile.Name, _settings.ActiveProfileName, StringComparison.OrdinalIgnoreCase));
        UpdateProfileIconPreview(ProfileCombo.SelectedItem as QuizProfile);
        ProfileNameText.Text = _settings.ActiveProfileName;
        FolderText.Text = _settings.MusicDirectory;
        SecondsText.Text = _settings.DefaultSecondsPerSong.ToString();
        ClipDurationText.Text = _settings.DefaultClipDurationSeconds.ToString();
        AudioVolumeSlider.Value = GetNormalizedAudioVolume(_settings.AudioVolume);
        UpdateAudioVolumeLabel(AudioVolumeSlider.Value);
        _isInitializingAudioVolume = false;
        FuzzyArtistCheck.IsChecked = _settings.FuzzyArtistMatching;
        FuzzyTitleCheck.IsChecked = _settings.FuzzySongTitleMatching;
        IgnorePunctuationCheck.IsChecked = _settings.IgnorePunctuation;
        SkipCorrectCheck.IsChecked = _settings.SkipOnCorrectGuess;
        ShowCountsCheck.IsChecked = _settings.ShowSongCountsInLists;
        EnableSpotifyCheck.IsChecked = _settings.EnableSpotifyIntegration;
        SpotifyClientIdText.Text = _settings.SpotifyClientId;
        SpotifyConnectionStatus.Text = SpotifyService.IsConnected
            ? $"Connected as {SpotifyService.DisplayName}"
            : string.IsNullOrWhiteSpace(_settings.SpotifyRefreshToken) ? "Not connected" : "Saved connection; reconnect when needed";
        UpdateSpotifyVisibility();
        ProviderCombo.ItemsSource = Enum.GetValues<MetadataSuggestionProviderKind>();
        ProviderCombo.SelectedItem = _settings.MetadataSuggestionProvider;
        PathsText.Text = $"Settings: {AppSettings.FilePath}{Environment.NewLine}"
            + $"Quiz history: {DatabaseInitializer.DatabasePath}";
        _isInitializingProfiles = false;
    }

    private void OnBrowseClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Select your music library", FolderName = FolderText.Text };
        if (dialog.ShowDialog() == true)
            FolderText.Text = dialog.FolderName;
    }

    private void OnProfileSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializingProfiles || ProfileCombo.SelectedItem is not QuizProfile profile
            || string.Equals(profile.Name, _settings.ActiveProfileName, StringComparison.OrdinalIgnoreCase))
            return;

        if (!TryCaptureProfileSettings())
        {
            SelectActiveProfile();
            return;
        }

        try
        {
            _settings.Save();
            _settings.ActivateProfile(profile.Name);
            RefreshProfileChoices(_settings.ActiveProfileName);
            PopulateProfileSettings();
            SettingsSaved?.Invoke();
            ProfileChanged?.Invoke();
        }
        catch (Exception exception)
        {
            SelectActiveProfile();
            MessageBox.Show($"Could not switch profiles:\n{exception.Message}",
                "Profile switch failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnChooseProfileIconClick(object sender, RoutedEventArgs e)
    {
        if (ProfileCombo.SelectedItem is not QuizProfile profile)
            return;

        var dialog = new OpenFileDialog
        {
            Title = "Choose a profile image",
            Filter = "Image files (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp",
            CheckFileExists = true
        };
        if (dialog.ShowDialog() != true)
            return;

        var previousIconFileName = profile.IconFileName;
        string? newIconFileName = null;
        try
        {
            newIconFileName = ProfileIconFiles.SaveResizedPng(dialog.FileName);
            profile.IconFileName = newIconFileName;
            _settings.Save();
        }
        catch (Exception exception)
        {
            profile.IconFileName = previousIconFileName;
            if (newIconFileName is not null)
            {
                try
                {
                    ProfileIconFiles.Delete(newIconFileName);
                }
                catch (Exception cleanupException)
                {
                    MessageBox.Show(
                        $"Could not save the profile image:\n{exception.Message}\n\n"
                        + $"The unused resized image could not be removed:\n{cleanupException.Message}",
                        "Profile image failed", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }

            MessageBox.Show($"Could not save the profile image:\n{exception.Message}",
                "Profile image failed", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        UpdateProfileIconPreview(profile);
        ProfileChanged?.Invoke();
        try
        {
            DeleteProfileIconIfUnused(previousIconFileName, profile);
        }
        catch (Exception exception)
        {
            MessageBox.Show($"The new profile image was saved, but the previous image could not be removed:\n{exception.Message}",
                "Profile image cleanup failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnRemoveProfileIconClick(object sender, RoutedEventArgs e)
    {
        if (ProfileCombo.SelectedItem is not QuizProfile profile
            || string.IsNullOrWhiteSpace(profile.IconFileName))
            return;

        var previousIconFileName = profile.IconFileName;
        try
        {
            profile.IconFileName = null;
            _settings.Save();
        }
        catch (Exception exception)
        {
            profile.IconFileName = previousIconFileName;
            UpdateProfileIconPreview(profile);
            MessageBox.Show($"Could not remove the profile image:\n{exception.Message}",
                "Profile image failed", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        UpdateProfileIconPreview(profile);
        ProfileChanged?.Invoke();
        try
        {
            DeleteProfileIconIfUnused(previousIconFileName, profile);
        }
        catch (Exception exception)
        {
            MessageBox.Show($"The profile image was removed, but its file could not be deleted:\n{exception.Message}",
                "Profile image cleanup failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void UpdateProfileIconPreview(QuizProfile? profile)
    {
        ProfileIconInitials.Text = ProfileIconFiles.GetInitials(profile?.Name);
        ProfileIconInitials.ToolTip = null;
        ProfileIconInitials.Visibility = Visibility.Visible;
        ProfileIconPreview.Source = null;
        ProfileIconPreview.Visibility = Visibility.Collapsed;
        RemoveProfileIconButton.IsEnabled = !string.IsNullOrWhiteSpace(profile?.IconFileName);

        var path = ProfileIconFiles.GetExistingPath(profile?.IconFileName);
        if (path is null)
            return;

        try
        {
            ProfileIconPreview.Source = ProfileIconFiles.LoadBitmapSource(path);
            ProfileIconPreview.Visibility = Visibility.Visible;
            ProfileIconInitials.Visibility = Visibility.Collapsed;
        }
        catch (Exception exception) when (
            exception is IOException or NotSupportedException or System.IO.FileFormatException)
        {
            ProfileIconInitials.ToolTip = $"The saved profile image could not be loaded: {exception.Message}";
        }
    }

    private void DeleteProfileIconIfUnused(string? fileName, QuizProfile owner)
    {
        if (_settings.Profiles.Any(profile => !ReferenceEquals(profile, owner)
                && string.Equals(profile.IconFileName, fileName, StringComparison.OrdinalIgnoreCase)))
            return;

        ProfileIconFiles.Delete(fileName);
    }

    private void OnAddProfileClick(object sender, RoutedEventArgs e)
    {
        if (!TryCaptureProfileSettings())
            return;
        try
        {
            var profile = _settings.AddProfile(ProfileNameText.Text);
            RefreshProfileChoices(profile.Name);
            ProfileNameText.Text = profile.Name;
            PopulateProfileSettings();
            SettingsSaved?.Invoke();
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Could not add profile",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnRenameProfileClick(object sender, RoutedEventArgs e)
    {
        if (!TryCaptureProfileSettings())
            return;
        try
        {
            _settings.RenameActiveProfile(ProfileNameText.Text);
            ProfileNameText.Text = _settings.ActiveProfileName;
            RefreshProfileChoices(_settings.ActiveProfileName);
            SettingsSaved?.Invoke();
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Could not rename profile",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnDeleteProfileClick(object sender, RoutedEventArgs e)
    {
        if (_settings.Profiles.Count <= 1)
        {
            MessageBox.Show("At least one profile must remain.", "Cannot delete profile",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (MessageBox.Show($"Delete profile '{_settings.ActiveProfileName}'? Existing quiz history will keep its profile label.",
                "Delete profile", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No)
            != MessageBoxResult.Yes)
            return;

        if (!TryCaptureProfileSettings())
            return;
        var deletedProfile = _settings.Profiles.First(profile =>
            string.Equals(profile.Name, _settings.ActiveProfileName, StringComparison.OrdinalIgnoreCase));
        try
        {
            _settings.DeleteActiveProfile();
            RefreshProfileChoices(_settings.ActiveProfileName);
            ProfileNameText.Text = _settings.ActiveProfileName;
            PopulateProfileSettings();
            SettingsSaved?.Invoke();
            ProfileChanged?.Invoke();
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Could not delete profile",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        try
        {
            DeleteProfileIconIfUnused(deletedProfile.IconFileName, deletedProfile);
        }
        catch (Exception exception)
        {
            MessageBox.Show($"The profile was deleted, but its image could not be removed:\n{exception.Message}",
                "Profile image cleanup failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private bool TryCaptureProfileSettings()
    {
        if (!int.TryParse(SecondsText.Text, out var seconds) || seconds is < 10 or > 600
            || !int.TryParse(ClipDurationText.Text, out var clipDuration) || clipDuration is < 3 or > 120)
        {
            MessageBox.Show("Seconds per song must be 10–600 and clip duration must be 3–120.",
                "Invalid settings", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        _settings.MusicDirectory = FolderText.Text.Trim();
        _settings.DefaultSecondsPerSong = seconds;
        _settings.DefaultClipDurationSeconds = clipDuration;
        _settings.AudioVolume = AudioVolumeSlider.Value;
        _settings.FuzzyArtistMatching = FuzzyArtistCheck.IsChecked == true;
        _settings.FuzzySongTitleMatching = FuzzyTitleCheck.IsChecked == true;
        _settings.IgnorePunctuation = IgnorePunctuationCheck.IsChecked == true;
        _settings.SkipOnCorrectGuess = SkipCorrectCheck.IsChecked == true;
        return true;
    }

    private void PopulateProfileSettings()
    {
        FolderText.Text = _settings.MusicDirectory;
        SecondsText.Text = _settings.DefaultSecondsPerSong.ToString();
        ClipDurationText.Text = _settings.DefaultClipDurationSeconds.ToString();
        _isInitializingAudioVolume = true;
        AudioVolumeSlider.Value = GetNormalizedAudioVolume(_settings.AudioVolume);
        UpdateAudioVolumeLabel(AudioVolumeSlider.Value);
        _isInitializingAudioVolume = false;
        FuzzyArtistCheck.IsChecked = _settings.FuzzyArtistMatching;
        FuzzyTitleCheck.IsChecked = _settings.FuzzySongTitleMatching;
        IgnorePunctuationCheck.IsChecked = _settings.IgnorePunctuation;
        SkipCorrectCheck.IsChecked = _settings.SkipOnCorrectGuess;
        ProfileNameText.Text = _settings.ActiveProfileName;
        UpdateProfileIconPreview(ProfileCombo.SelectedItem as QuizProfile);
    }

    private void SelectActiveProfile()
    {
        _isInitializingProfiles = true;
        ProfileCombo.SelectedItem = _settings.Profiles.FirstOrDefault(profile =>
            string.Equals(profile.Name, _settings.ActiveProfileName, StringComparison.OrdinalIgnoreCase));
        _isInitializingProfiles = false;
    }

    private void RefreshProfileChoices(string selectedProfileName)
    {
        _isInitializingProfiles = true;
        ProfileCombo.ItemsSource = _settings.Profiles;
        ProfileCombo.SelectedItem = _settings.Profiles.FirstOrDefault(profile =>
            string.Equals(profile.Name, selectedProfileName, StringComparison.OrdinalIgnoreCase));
        _isInitializingProfiles = false;
        UpdateProfileIconPreview(ProfileCombo.SelectedItem as QuizProfile);
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
        _settings.EnableSpotifyIntegration = EnableSpotifyCheck.IsChecked == true;
        _settings.SpotifyClientId = SpotifyClientIdText.Text.Trim();
        RefreshSpotifyCredentials();
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

    private void OnAudioVolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isInitializingAudioVolume)
            return;

        UpdateAudioVolumeLabel(e.NewValue);
        _settings.AudioVolume = e.NewValue;
        try
        {
            AppSettings.SaveAudioVolume(e.NewValue);
        }
        catch (Exception exception)
        {
            AudioVolumeValueText.Text = "Save failed";
            AudioVolumeValueText.ToolTip = $"Could not save audio volume: {exception.Message}";
        }
    }

    private void UpdateAudioVolumeLabel(double volume)
    {
        AudioVolumeValueText.Text = $"{volume:P0}";
        AudioVolumeValueText.ToolTip = null;
    }

    private static double GetNormalizedAudioVolume(double volume)
        => double.IsFinite(volume) ? Math.Clamp(volume, 0.0, 1.0) : 1.0;

    private void OnSpotifyFeatureChanged(object sender, RoutedEventArgs e)
        => UpdateSpotifyVisibility();

    private void UpdateSpotifyVisibility()
        => SpotifySettingsPanel.Visibility = EnableSpotifyCheck.IsChecked == true
            ? Visibility.Visible
            : Visibility.Collapsed;

    private async void OnConnectSpotifyClick(object sender, RoutedEventArgs e)
    {
        var clientId = SpotifyClientIdText.Text.Trim();
        if (string.IsNullOrWhiteSpace(clientId))
        {
            MessageBox.Show("Enter the Spotify client ID from your Spotify developer app.",
                "Client ID required", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        SpotifyConnectionStatus.Text = "Waiting for Spotify authorization…";
        try
        {
            _settings.EnableSpotifyIntegration = EnableSpotifyCheck.IsChecked == true;
            _settings.SpotifyClientId = clientId;
            RefreshSpotifyCredentials();
            _settings.Save();
            await SpotifyService.ConnectWithBrowserAsync(clientId);
            SpotifyConnectionStatus.Text = $"Connected as {SpotifyService.DisplayName}";
        }
        catch (Exception exception)
        {
            SpotifyConnectionStatus.Text = "Connection failed.";
            MessageBox.Show($"Could not connect to Spotify:\n{exception.Message}",
                "Spotify connection failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RefreshSpotifyCredentials()
    {
        var storedSettings = AppSettings.Load();
        if (!string.Equals(storedSettings.SpotifyClientId, SpotifyClientIdText.Text.Trim(),
                StringComparison.Ordinal))
        {
            if (SpotifyService.IsConnected)
                SpotifyService.Disconnect();
            _settings.SpotifyRefreshToken = string.Empty;
            _settings.SpotifyDisplayName = string.Empty;
            return;
        }

        _settings.SpotifyRefreshToken = storedSettings.SpotifyRefreshToken;
        _settings.SpotifyDisplayName = storedSettings.SpotifyDisplayName;
    }

    private void OnDisconnectSpotifyClick(object sender, RoutedEventArgs e)
    {
        try
        {
            SpotifyService.Disconnect();
            _settings.SpotifyRefreshToken = string.Empty;
            _settings.SpotifyDisplayName = string.Empty;
            SpotifyConnectionStatus.Text = "Not connected";
        }
        catch (Exception exception)
        {
            MessageBox.Show($"Could not disconnect Spotify:\n{exception.Message}",
                "Spotify disconnect failed", MessageBoxButton.OK, MessageBoxImage.Error);
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
