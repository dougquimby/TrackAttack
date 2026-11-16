using System.Windows;
using System.Windows.Controls;
using System.Collections.ObjectModel;
using System.Reflection;
using Microsoft.Win32;
using TrackAttack.Data;
using TrackAttack.Models;
using TrackAttack.Services;
using TrackAttack.Settings;

namespace TrackAttack.Views;

/// <summary>Displays and saves profile, library, appearance, and application settings.</summary>
public partial class SettingsView : UserControl
{
    private AppSettings _settings = AppSettings.Load();
    private readonly IQuizHistoryRepository _history;
    private bool _isInitializingAudioVolume = true;
    private bool _isInitializingProfiles = true;
    private int _folderCheckVersion;
    private readonly ObservableCollection<string> _musicFolders = [];
    private Dictionary<Control, object?> _savedInputs = [];
    private string[] _savedMusicFolders = [];
    private static readonly HashSet<string> ProfileInputNames =
    [
        "AudioVolumeSlider", "DarkModeCheck"
    ];

    /// <summary>Occurs after settings have been saved.</summary>
    public event Action? SettingsSaved;
    /// <summary>Occurs after the active profile changes.</summary>
    public event Action? ProfileChanged;
    /// <summary>Occurs when the player requests metadata editing.</summary>
    public event Action? MetadataRequested;

    /// <summary>Creates the settings view.</summary>
    /// <param name="history">The repository used for history management.</param>
    public SettingsView(IQuizHistoryRepository history)
    {
        _history = history;
        InitializeComponent();
        var assembly = typeof(SettingsView).Assembly;
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
            ?? throw new InvalidOperationException("The application version is missing.");
        var buildDate = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(attribute => attribute.Key == "BuildDateUtc").Value
            ?? throw new InvalidOperationException("The application build date is missing.");
        BuildInfoText.Text = $"Version {version} · Built {buildDate} (UTC)";
        ProfileCombo.ItemsSource = _settings.Profiles;
        ProfileCombo.SelectedItem = _settings.Profiles.FirstOrDefault(profile =>
            string.Equals(profile.Name, _settings.ActiveProfileName, StringComparison.OrdinalIgnoreCase));
        UpdateProfileIconPreview(ProfileCombo.SelectedItem as QuizProfile);
        ProfileNameText.Text = _settings.ActiveProfileName;
        MusicFoldersList.ItemsSource = _musicFolders;
        LoadMusicFolders();
        AudioVolumeSlider.Value = GetNormalizedAudioVolume(_settings.AudioVolume);
        UpdateAudioVolumeLabel(AudioVolumeSlider.Value);
        _isInitializingAudioVolume = false;
        ShowCountsCheck.IsChecked = _settings.ShowSongCountsInLists;
        ShowRecordCelebrationsCheck.IsChecked = _settings.ShowRecordCelebrations;
        DarkModeCheck.IsChecked = _settings.DarkModeEnabled;
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
        RememberSavedInputs();
        Loaded += (_, _) =>
        {
            var storedSettings = AppSettings.Load();
            var profileChanged = !string.Equals(
                _settings.ActiveProfileName, storedSettings.ActiveProfileName, StringComparison.OrdinalIgnoreCase);
            var themeChanged = _settings.DarkModeEnabled != storedSettings.DarkModeEnabled;
            var volumeChanged = _settings.AudioVolume != storedSettings.AudioVolume;
            _settings = storedSettings;
            RefreshProfileChoices(_settings.ActiveProfileName);
            if (profileChanged)
            {
                PopulateProfileSettings();
                RememberSavedInputs(profileOnly: true);
            }
            else
            {
                if (themeChanged)
                {
                    DarkModeCheck.IsChecked = _settings.DarkModeEnabled;
                    _savedInputs[DarkModeCheck] = DarkModeCheck.IsChecked;
                }
                if (volumeChanged)
                {
                    _isInitializingAudioVolume = true;
                    AudioVolumeSlider.Value = GetNormalizedAudioVolume(_settings.AudioVolume);
                    UpdateAudioVolumeLabel(AudioVolumeSlider.Value);
                    _isInitializingAudioVolume = false;
                    _savedInputs[AudioVolumeSlider] = AudioVolumeSlider.Value;
                }
            }
        };
        Unloaded += (_, _) => _folderCheckVersion++;
    }

    private static IEnumerable<Control> GetSettingsInputs(DependencyObject parent)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
        {
            if (child is Control control && !string.IsNullOrEmpty(control.Name)
                && control.Name != "ProfileNameText"
                && control is TextBox or CheckBox or ComboBox or Slider)
                yield return control;

            foreach (var input in GetSettingsInputs(child))
                yield return input;
        }
    }

    private static object? GetInputValue(Control input) => input switch
    {
        TextBox textBox => textBox.Text,
        CheckBox checkBox => checkBox.IsChecked,
        ComboBox comboBox => comboBox.SelectedItem,
        Slider slider => slider.Value,
        _ => throw new InvalidOperationException("Unsupported settings input.")
    };

    private void OnMetadataClick(object sender, RoutedEventArgs e) => MetadataRequested?.Invoke();

    private void RememberSavedInputs(bool profileOnly = false)
    {
        foreach (var input in GetSettingsInputs(SettingsTabs))
        {
            if (!profileOnly || ProfileInputNames.Contains(input.Name))
                _savedInputs[input] = GetInputValue(input);
        }
        _savedMusicFolders = _musicFolders.ToArray();
    }

    /// <summary>Tests whether staged settings differ from the last saved inputs.</summary>
    /// <returns>Whether there are unsaved changes.</returns>
    public bool HasUnsavedChanges()
        => !_savedMusicFolders.SequenceEqual(_musicFolders)
           || _savedInputs.Any(entry => !Equals(entry.Value, GetInputValue(entry.Key)));

    private void OnAddMusicFolderClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Add a music library folder" };
        if (dialog.ShowDialog() != true)
            return;

        var path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dialog.FolderName));
        var overlappingFolder = _musicFolders.FirstOrDefault(folder =>
            MusicFolderSelection.IsSameOrDescendant(path, folder)
            || MusicFolderSelection.IsSameOrDescendant(folder, path));
        if (overlappingFolder is not null)
        {
            MessageBox.Show($"Library folders cannot overlap.\n\n{path}\n{overlappingFolder}",
                "Overlapping folders",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        _musicFolders.Add(path);
    }

    private void OnRemoveMusicFolderClick(object sender, RoutedEventArgs e)
    {
        if (MusicFoldersList.SelectedItem is string folder)
            _musicFolders.Remove(folder);
    }

    private async void OnRemoveMissingMusicFoldersClick(object sender, RoutedEventArgs e)
    {
        var folders = _musicFolders.ToArray();
        var profileName = _settings.ActiveProfileName;
        var checkVersion = _folderCheckVersion;
        RemoveMissingFoldersButton.IsEnabled = false;
        try
        {
            var (missing, errors) = await Task.Run(() => FindMissingMusicFolders(folders));
            if (checkVersion != _folderCheckVersion)
                return;
            if (!string.Equals(profileName, _settings.ActiveProfileName, StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("The active profile changed while folders were being checked. No folders were removed.",
                    "Folder check canceled", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (errors.Count > 0)
                MessageBox.Show("These folders could not be checked and will not be removed:\n\n"
                    + FormatFolderList(errors), "Folder check incomplete", MessageBoxButton.OK, MessageBoxImage.Warning);

            var toRemove = missing.Where(_musicFolders.Contains).ToArray();
            if (toRemove.Length == 0)
            {
                if (errors.Count == 0)
                    MessageBox.Show("No missing music folders were found.", "Music folders",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (MessageBox.Show($"{toRemove.Length} music folder(s) are missing or unavailable:\n\n"
                    + FormatFolderList(toRemove)
                    + "\n\nOffline drives or network folders may become available again. Remove these folders from the list?"
                    + "\nReview the list and save Settings to keep the change.",
                    "Remove missing folders", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No)
                != MessageBoxResult.Yes)
                return;

            foreach (var folder in toRemove)
                _musicFolders.Remove(folder);
        }
        finally
        {
            RemoveMissingFoldersButton.IsEnabled = true;
        }
    }

    private static (List<string> Missing, List<string> Errors) FindMissingMusicFolders(IEnumerable<string> folders)
    {
        var missing = new List<string>();
        var errors = new List<string>();
        foreach (var folder in folders)
        {
            try
            {
                if ((File.GetAttributes(folder) & FileAttributes.Directory) == 0)
                    missing.Add(folder);
            }
            catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
            {
                missing.Add(folder);
            }
            catch (Exception exception) when (LocalFileMusicSource.IsFileAccessFailure(exception)
                                               || exception is ArgumentException or NotSupportedException)
            {
                errors.Add($"{folder}\n  {exception.Message}");
            }
        }
        return (missing, errors);
    }

    private static string FormatFolderList(IReadOnlyCollection<string> folders)
    {
        var remaining = folders.Count > 10 ? $"\n...and {folders.Count - 10} more." : string.Empty;
        return string.Join("\n", folders.Take(10)) + remaining;
    }

    private void LoadMusicFolders()
    {
        _musicFolders.Clear();
        foreach (var folder in _settings.GetMusicDirectories())
            _musicFolders.Add(folder);
    }

    private void OnProfileSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializingProfiles || ProfileCombo.SelectedItem is not QuizProfile profile
            || string.Equals(profile.Name, _settings.ActiveProfileName, StringComparison.OrdinalIgnoreCase))
            return;

        CaptureProfileSettings();

        try
        {
            _settings.Save();
            _settings.ActivateProfile(profile.Name);
            RefreshProfileChoices(_settings.ActiveProfileName);
            PopulateProfileSettings();
            RememberSavedInputs(profileOnly: true);
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
        CaptureProfileSettings();
        try
        {
            var profile = _settings.AddProfile(ProfileNameText.Text);
            RefreshProfileChoices(profile.Name);
            ProfileNameText.Text = profile.Name;
            PopulateProfileSettings();
            RememberSavedInputs(profileOnly: true);
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
        CaptureProfileSettings();
        try
        {
            _settings.RenameActiveProfile(ProfileNameText.Text);
            ThemeManager.Apply(_settings.DarkModeEnabled);
            ProfileNameText.Text = _settings.ActiveProfileName;
            RefreshProfileChoices(_settings.ActiveProfileName);
            RememberSavedInputs(profileOnly: true);
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

        CaptureProfileSettings();
        var deletedProfile = _settings.Profiles.First(profile =>
            string.Equals(profile.Name, _settings.ActiveProfileName, StringComparison.OrdinalIgnoreCase));
        try
        {
            _settings.DeleteActiveProfile();
            RefreshProfileChoices(_settings.ActiveProfileName);
            ProfileNameText.Text = _settings.ActiveProfileName;
            PopulateProfileSettings();
            RememberSavedInputs(profileOnly: true);
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

    private void CaptureProfileSettings()
    {
        _settings.SetMusicDirectories(_musicFolders);
        _settings.AudioVolume = AudioVolumeSlider.Value;
        _settings.DarkModeEnabled = DarkModeCheck.IsChecked == true;
    }

    private void PopulateProfileSettings()
    {
        LoadMusicFolders();
        _isInitializingAudioVolume = true;
        AudioVolumeSlider.Value = GetNormalizedAudioVolume(_settings.AudioVolume);
        UpdateAudioVolumeLabel(AudioVolumeSlider.Value);
        _isInitializingAudioVolume = false;
        DarkModeCheck.IsChecked = _settings.DarkModeEnabled;
        ThemeManager.Apply(_settings.DarkModeEnabled);
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
        => TrySaveSettings(showConfirmation: true);

    /// <summary>Attempts to save staged settings and apply the chosen appearance.</summary>
    /// <param name="showConfirmation">Whether to display a confirmation after saving.</param>
    /// <returns>Whether the settings were saved successfully.</returns>
    public bool TrySaveSettings(bool showConfirmation = false)
    {
        _settings.SetMusicDirectories(_musicFolders);
        _settings.ShowSongCountsInLists = ShowCountsCheck.IsChecked == true;
        _settings.ShowRecordCelebrations = ShowRecordCelebrationsCheck.IsChecked == true;
        _settings.DarkModeEnabled = DarkModeCheck.IsChecked == true;
        _settings.EnableSpotifyIntegration = EnableSpotifyCheck.IsChecked == true;
        _settings.SpotifyClientId = SpotifyClientIdText.Text.Trim();
        RefreshSpotifyCredentials();
        if (ProviderCombo.SelectedItem is MetadataSuggestionProviderKind provider)
            _settings.MetadataSuggestionProvider = provider;

        try
        {
            _settings.Save();
            ThemeManager.Apply(_settings.DarkModeEnabled);
            RememberSavedInputs();
            SettingsSaved?.Invoke();
            if (showConfirmation)
                MessageBox.Show("Settings saved.", "Settings", MessageBoxButton.OK, MessageBoxImage.Information);
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not save settings:\n{ex.Message}", "Settings save failed",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
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
            _savedInputs[AudioVolumeSlider] = e.NewValue;
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
