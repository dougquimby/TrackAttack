using System.Drawing;
using System.Text.Json;
using TrackAttack.Models;

namespace TrackAttack.Settings;

/// <summary>Stores global preferences and the active profile's settings.</summary>
public class AppSettings
{
    /// <summary>Gets or sets the active profile's primary library folder.</summary>
    public string MusicDirectory { get; set; } = string.Empty;
    /// <summary>Gets or sets additional library folders for the active profile.</summary>
    public List<string> AdditionalMusicDirectories { get; set; } = [];
    /// <summary>Gets or sets the profile's legacy default seconds per song.</summary>
    public int DefaultSecondsPerSong { get; set; } = 30;
    /// <summary>Gets or sets the profile's legacy default clip length in seconds.</summary>
    public int DefaultClipDurationSeconds { get; set; } = 15;
    /// <summary>Gets or sets the profile's normalized playback volume.</summary>
    public double AudioVolume { get; set; } = 1.0;
    /// <summary>Gets or sets the profile's default fuzzy artist matching preference.</summary>
    public bool FuzzyArtistMatching { get; set; } = true;
    /// <summary>Gets or sets the profile's default fuzzy title matching preference.</summary>
    public bool FuzzySongTitleMatching { get; set; } = true;
    /// <summary>Gets or sets the profile's default punctuation matching preference.</summary>
    public bool IgnorePunctuation { get; set; } = true;
    /// <summary>Gets or sets whether correct answers advance immediately by default.</summary>
    public bool SkipOnCorrectGuess { get; set; } = true;
    /// <summary>Gets or sets whether song counts are shown in filter lists.</summary>
    public bool ShowSongCountsInLists { get; set; } = true;
    /// <summary>Gets or sets whether recent quiz songs are excluded by default.</summary>
    public bool ExcludeRecentQuizSongs { get; set; }
    /// <summary>Gets or sets the default number of recent quizzes considered for exclusion.</summary>
    public int RecentQuizzesToExclude { get; set; } = 5;
    public bool EnableSpotifyIntegration { get; set; }
    public string SpotifyClientId { get; set; } = string.Empty;
    public string SpotifyRefreshToken { get; set; } = string.Empty;
    public string SpotifyDisplayName { get; set; } = string.Empty;
    /// <summary>Gets or sets the selected metadata catalog.</summary>
    public MetadataSuggestionProviderKind MetadataSuggestionProvider { get; set; } = MetadataSuggestionProviderKind.MusicBrainz;
    /// <summary>Gets or sets the saved named quiz setups.</summary>
    public List<LocalQuizPreset> LocalQuizPresets { get; set; } = [];
    /// <summary>Gets or sets the saved player profiles.</summary>
    public List<QuizProfile> Profiles { get; set; } = [];
    /// <summary>Gets or sets the active profile's name.</summary>
    public string ActiveProfileName { get; set; } = "Default";
    /// <summary>Gets or sets whether the window was maximized.</summary>
    public bool WindowMaximized { get; set; }
    /// <summary>Gets or sets whether normal window bounds were saved.</summary>
    public bool WindowRestored { get; set; }
    /// <summary>Gets or sets the saved normal window bounds.</summary>
    public Rectangle WindowBounds { get; set; }
    /// <summary>Gets or sets whether the navigation sidebar is collapsed.</summary>
    public bool NavigationCollapsed { get; set; }
    /// <summary>Gets or sets the active profile's dark-mode preference.</summary>
    public bool DarkModeEnabled { get; set; }

    /// <summary>Gets the path to the user settings JSON file.</summary>
    public static string FilePath => Path.Combine(AppDataPaths.DirectoryPath, "settings.json");

    /// <summary>Gets the configured library folders, omitting blank and duplicate paths.</summary>
    /// <returns>The primary and additional library folders in order.</returns>
    public List<string> GetMusicDirectories()
        => new[] { MusicDirectory }.Concat(AdditionalMusicDirectories ?? [])
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>Sets the primary and additional library folders.</summary>
    /// <param name="directories">The folders in order; the first becomes the primary folder.</param>
    public void SetMusicDirectories(IEnumerable<string> directories)
    {
        var folders = directories.ToList();
        MusicDirectory = folders.FirstOrDefault() ?? string.Empty;
        AdditionalMusicDirectories = folders.Skip(1).ToList();
    }

    /// <summary>Loads user settings, or creates defaults when the file is absent.</summary>
    /// <returns>The loaded settings with an active profile.</returns>
    /// <exception cref="SettingsLoadException">The settings file is invalid or cannot be read.</exception>
    public static AppSettings Load()
    {
        var filePath = FilePath;
        try
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(filePath))
                ?? throw new JsonException("The settings file contains no settings object.");
            settings.EnsureProfiles();
            return settings;
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            var defaults = new AppSettings();
            defaults.EnsureProfiles();
            return defaults;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            throw new SettingsLoadException(
                $"Could not load settings from {filePath}:\n{exception.Message}\nThe file has not been changed.", exception);
        }
    }

    /// <summary>Persists the active profile's playback volume.</summary>
    /// <param name="volume">A finite normalized volume, clamped to the range 0 to 1.</param>
    /// <exception cref="ArgumentOutOfRangeException">The volume is not finite.</exception>
    /// <exception cref="SettingsLoadException">Existing settings cannot be loaded.</exception>
    public static void SaveAudioVolume(double volume)
    {
        if (!double.IsFinite(volume))
            throw new ArgumentOutOfRangeException(nameof(volume), "Volume must be a finite value.");

        var settings = Load();
        settings.AudioVolume = Math.Clamp(volume, 0.0, 1.0);
        settings.Save();
    }

    /// <summary>Activates and saves an existing profile.</summary>
    /// <param name="name">The profile name, matched without regard to case.</param>
    /// <exception cref="ArgumentException">The name is blank or does not identify a profile.</exception>
    public void ActivateProfile(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        EnsureProfiles();
        StoreActiveProfileSettings();
        var profile = Profiles.FirstOrDefault(item =>
            string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"Profile '{name}' does not exist.", nameof(name));
        ActiveProfileName = profile.Name;
        ApplyProfileSettings(profile);
        Save();
    }

    /// <summary>Creates, activates, and saves a profile based on the current profile.</summary>
    /// <param name="name">The new profile name.</param>
    /// <returns>The newly created profile.</returns>
    /// <exception cref="ArgumentException">The name is blank.</exception>
    /// <exception cref="InvalidOperationException">A profile with the same name already exists.</exception>
    public QuizProfile AddProfile(string name)
    {
        name = NormalizeProfileName(name);
        EnsureProfiles(applyProfileSettings: false);
        if (Profiles.Any(profile => string.Equals(profile.Name, name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"A profile named '{name}' already exists.");

        StoreActiveProfileSettings();
        var profile = CloneProfile(GetActiveProfile());
        profile.Name = name;
        profile.IconFileName = null;
        Profiles.Add(profile);
        ActiveProfileName = name;
        ApplyProfileSettings(profile);
        Save();
        return profile;
    }

    /// <summary>Renames and saves the active profile.</summary>
    /// <param name="name">The new profile name.</param>
    /// <exception cref="ArgumentException">The name is blank.</exception>
    /// <exception cref="InvalidOperationException">Another profile has the same name.</exception>
    public void RenameActiveProfile(string name)
    {
        name = NormalizeProfileName(name);
        EnsureProfiles(applyProfileSettings: false);
        if (Profiles.Any(profile => !string.Equals(profile.Name, ActiveProfileName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(profile.Name, name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"A profile named '{name}' already exists.");

        var profile = GetActiveProfile();
        profile.Name = name;
        ActiveProfileName = name;
        StoreActiveProfileSettings();
        Save();
    }

    /// <summary>Deletes the active profile, activates another profile, and saves the settings.</summary>
    /// <exception cref="InvalidOperationException">The active profile is the last profile.</exception>
    public void DeleteActiveProfile()
    {
        EnsureProfiles(applyProfileSettings: false);
        if (Profiles.Count <= 1)
            throw new InvalidOperationException("At least one profile must remain.");

        var profile = GetActiveProfile();
        Profiles.Remove(profile);
        var replacement = Profiles[0];
        ActiveProfileName = replacement.Name;
        ApplyProfileSettings(replacement);
        Save();
    }

    private void EnsureProfiles(bool applyProfileSettings = true)
    {
        Profiles ??= [];
        if (Profiles.Count == 0)
        {
            Profiles.Add(new QuizProfile
            {
                Name = string.IsNullOrWhiteSpace(ActiveProfileName) ? "Default" : ActiveProfileName.Trim(),
                MusicDirectory = MusicDirectory,
                AdditionalMusicDirectories = [.. (AdditionalMusicDirectories ?? [])],
                DefaultSecondsPerSong = DefaultSecondsPerSong,
                DefaultClipDurationSeconds = DefaultClipDurationSeconds,
                AudioVolume = AudioVolume,
                FuzzyArtistMatching = FuzzyArtistMatching,
                FuzzySongTitleMatching = FuzzySongTitleMatching,
                IgnorePunctuation = IgnorePunctuation,
                SkipOnCorrectGuess = SkipOnCorrectGuess,
                DarkModeEnabled = DarkModeEnabled,
                ExcludeRecentQuizSongs = ExcludeRecentQuizSongs,
                RecentQuizzesToExclude = RecentQuizzesToExclude
            });
        }

        Profiles = Profiles
            .Where(profile => profile is not null && !string.IsNullOrWhiteSpace(profile.Name))
            .DistinctBy(profile => profile.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (Profiles.Count == 0)
            Profiles.Add(new QuizProfile());

        foreach (var profile in Profiles)
            profile.DarkModeEnabled ??= DarkModeEnabled;

        var activeProfile = Profiles.FirstOrDefault(profile =>
            string.Equals(profile.Name, ActiveProfileName, StringComparison.OrdinalIgnoreCase));
        activeProfile ??= Profiles[0];
        ActiveProfileName = activeProfile.Name;
        if (applyProfileSettings)
            ApplyProfileSettings(activeProfile);
    }

    private QuizProfile GetActiveProfile()
        => Profiles.First(profile =>
            string.Equals(profile.Name, ActiveProfileName, StringComparison.OrdinalIgnoreCase));

    /// <summary>Gets the active profile's last started quiz setup.</summary>
    /// <returns>The saved setup, or null if no setup was recorded.</returns>
    public LocalQuizPreset? GetLastQuizSetup() => GetActiveProfile().LastQuizSetup;

    /// <summary>Sets the active profile's last started quiz setup in memory.</summary>
    /// <param name="setup">The setup to retain; call <see cref="Save"/> to persist it.</param>
    public void SetLastQuizSetup(LocalQuizPreset setup) => GetActiveProfile().LastQuizSetup = setup;

    private void StoreActiveProfileSettings()
    {
        var profile = GetActiveProfile();
        profile.MusicDirectory = MusicDirectory;
        profile.AdditionalMusicDirectories = [.. (AdditionalMusicDirectories ?? [])];
        profile.DefaultSecondsPerSong = DefaultSecondsPerSong;
        profile.DefaultClipDurationSeconds = DefaultClipDurationSeconds;
        profile.AudioVolume = double.IsFinite(AudioVolume) ? Math.Clamp(AudioVolume, 0.0, 1.0) : 1.0;
        profile.FuzzyArtistMatching = FuzzyArtistMatching;
        profile.FuzzySongTitleMatching = FuzzySongTitleMatching;
        profile.IgnorePunctuation = IgnorePunctuation;
        profile.SkipOnCorrectGuess = SkipOnCorrectGuess;
        profile.DarkModeEnabled = DarkModeEnabled;
        profile.ExcludeRecentQuizSongs = ExcludeRecentQuizSongs;
        profile.RecentQuizzesToExclude = RecentQuizzesToExclude;
    }

    private void ApplyProfileSettings(QuizProfile profile)
    {
        MusicDirectory = profile.MusicDirectory;
        AdditionalMusicDirectories = [.. (profile.AdditionalMusicDirectories ?? [])];
        DefaultSecondsPerSong = profile.DefaultSecondsPerSong;
        DefaultClipDurationSeconds = profile.DefaultClipDurationSeconds;
        AudioVolume = profile.AudioVolume;
        FuzzyArtistMatching = profile.FuzzyArtistMatching;
        FuzzySongTitleMatching = profile.FuzzySongTitleMatching;
        IgnorePunctuation = profile.IgnorePunctuation;
        SkipOnCorrectGuess = profile.SkipOnCorrectGuess;
        DarkModeEnabled = profile.DarkModeEnabled ?? DarkModeEnabled;
        ExcludeRecentQuizSongs = profile.ExcludeRecentQuizSongs;
        RecentQuizzesToExclude = profile.RecentQuizzesToExclude;
    }

    private static QuizProfile CloneProfile(QuizProfile profile)
        => new()
        {
            Name = profile.Name,
            IconFileName = profile.IconFileName,
            MusicDirectory = profile.MusicDirectory,
            AdditionalMusicDirectories = [.. (profile.AdditionalMusicDirectories ?? [])],
            DefaultSecondsPerSong = profile.DefaultSecondsPerSong,
            DefaultClipDurationSeconds = profile.DefaultClipDurationSeconds,
            AudioVolume = profile.AudioVolume,
            FuzzyArtistMatching = profile.FuzzyArtistMatching,
            FuzzySongTitleMatching = profile.FuzzySongTitleMatching,
            IgnorePunctuation = profile.IgnorePunctuation,
            SkipOnCorrectGuess = profile.SkipOnCorrectGuess,
            DarkModeEnabled = profile.DarkModeEnabled,
            ExcludeRecentQuizSongs = profile.ExcludeRecentQuizSongs,
            RecentQuizzesToExclude = profile.RecentQuizzesToExclude
        };

    private static string NormalizeProfileName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return name.Trim();
    }

    /// <summary>Persists settings using a temporary file before replacing the existing file.</summary>
    public void Save()
    {
        EnsureProfiles(applyProfileSettings: false);
        StoreActiveProfileSettings();
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        var filePath = FilePath;
        var directory = Path.GetDirectoryName(filePath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".settings-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }
}

/// <summary>Indicates that existing user settings could not be read or parsed.</summary>
/// <param name="message">The user-facing description of the failure.</param>
/// <param name="innerException">The underlying read or parse failure.</param>
public sealed class SettingsLoadException(string message, Exception innerException)
    : IOException(message, innerException);
