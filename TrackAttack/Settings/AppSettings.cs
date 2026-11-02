using System.Drawing;
using System.Text.Json;
using TrackAttack.Models;

namespace TrackAttack.Settings;

public class AppSettings
{
    public string MusicDirectory { get; set; } = string.Empty;
    public int DefaultSecondsPerSong { get; set; } = 30;
    public int DefaultClipDurationSeconds { get; set; } = 15;
    public double AudioVolume { get; set; } = 1.0;
    public bool FuzzyArtistMatching { get; set; } = true;
    public bool FuzzySongTitleMatching { get; set; } = true;
    public bool IgnorePunctuation { get; set; } = true;
    public bool SkipOnCorrectGuess { get; set; } = true;
    public bool ShowSongCountsInLists { get; set; } = true;
    public bool ExcludeRecentQuizSongs { get; set; }
    public int RecentQuizzesToExclude { get; set; } = 5;
    public bool EnableSpotifyIntegration { get; set; }
    public string SpotifyClientId { get; set; } = string.Empty;
    public string SpotifyRefreshToken { get; set; } = string.Empty;
    public string SpotifyDisplayName { get; set; } = string.Empty;
    public MetadataSuggestionProviderKind MetadataSuggestionProvider { get; set; } = MetadataSuggestionProviderKind.MusicBrainz;
    public List<LocalQuizPreset> LocalQuizPresets { get; set; } = [];
    public List<QuizProfile> Profiles { get; set; } = [];
    public string ActiveProfileName { get; set; } = "Default";
    public bool WindowMaximized { get; set; }
    public bool WindowRestored { get; set; }
    public Rectangle WindowBounds { get; set; }
    public bool NavigationCollapsed { get; set; }

    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MusicQuiz", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
                settings.EnsureProfiles();
                return settings;
            }
        }
        catch { }
        var defaults = new AppSettings();
        defaults.EnsureProfiles();
        return defaults;
    }

    public static void SaveAudioVolume(double volume)
    {
        if (!double.IsFinite(volume))
            throw new ArgumentOutOfRangeException(nameof(volume), "Volume must be a finite value.");

        var settings = Load();
        settings.AudioVolume = Math.Clamp(volume, 0.0, 1.0);
        settings.Save();
    }

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

    public QuizProfile AddProfile(string name)
    {
        name = NormalizeProfileName(name);
        EnsureProfiles(applyProfileSettings: false);
        if (Profiles.Any(profile => string.Equals(profile.Name, name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"A profile named '{name}' already exists.");

        StoreActiveProfileSettings();
        var profile = CloneProfile(GetActiveProfile());
        profile.Name = name;
        Profiles.Add(profile);
        ActiveProfileName = name;
        ApplyProfileSettings(profile);
        Save();
        return profile;
    }

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
                DefaultSecondsPerSong = DefaultSecondsPerSong,
                DefaultClipDurationSeconds = DefaultClipDurationSeconds,
                AudioVolume = AudioVolume,
                FuzzyArtistMatching = FuzzyArtistMatching,
                FuzzySongTitleMatching = FuzzySongTitleMatching,
                IgnorePunctuation = IgnorePunctuation,
                SkipOnCorrectGuess = SkipOnCorrectGuess,
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

    private void StoreActiveProfileSettings()
    {
        var profile = GetActiveProfile();
        profile.MusicDirectory = MusicDirectory;
        profile.DefaultSecondsPerSong = DefaultSecondsPerSong;
        profile.DefaultClipDurationSeconds = DefaultClipDurationSeconds;
        profile.AudioVolume = double.IsFinite(AudioVolume) ? Math.Clamp(AudioVolume, 0.0, 1.0) : 1.0;
        profile.FuzzyArtistMatching = FuzzyArtistMatching;
        profile.FuzzySongTitleMatching = FuzzySongTitleMatching;
        profile.IgnorePunctuation = IgnorePunctuation;
        profile.SkipOnCorrectGuess = SkipOnCorrectGuess;
        profile.ExcludeRecentQuizSongs = ExcludeRecentQuizSongs;
        profile.RecentQuizzesToExclude = RecentQuizzesToExclude;
    }

    private void ApplyProfileSettings(QuizProfile profile)
    {
        MusicDirectory = profile.MusicDirectory;
        DefaultSecondsPerSong = profile.DefaultSecondsPerSong;
        DefaultClipDurationSeconds = profile.DefaultClipDurationSeconds;
        AudioVolume = profile.AudioVolume;
        FuzzyArtistMatching = profile.FuzzyArtistMatching;
        FuzzySongTitleMatching = profile.FuzzySongTitleMatching;
        IgnorePunctuation = profile.IgnorePunctuation;
        SkipOnCorrectGuess = profile.SkipOnCorrectGuess;
        ExcludeRecentQuizSongs = profile.ExcludeRecentQuizSongs;
        RecentQuizzesToExclude = profile.RecentQuizzesToExclude;
    }

    private static QuizProfile CloneProfile(QuizProfile profile)
        => new()
        {
            Name = profile.Name,
            MusicDirectory = profile.MusicDirectory,
            DefaultSecondsPerSong = profile.DefaultSecondsPerSong,
            DefaultClipDurationSeconds = profile.DefaultClipDurationSeconds,
            AudioVolume = profile.AudioVolume,
            FuzzyArtistMatching = profile.FuzzyArtistMatching,
            FuzzySongTitleMatching = profile.FuzzySongTitleMatching,
            IgnorePunctuation = profile.IgnorePunctuation,
            SkipOnCorrectGuess = profile.SkipOnCorrectGuess,
            ExcludeRecentQuizSongs = profile.ExcludeRecentQuizSongs,
            RecentQuizzesToExclude = profile.RecentQuizzesToExclude
        };

    private static string NormalizeProfileName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return name.Trim();
    }

    public void Save()
    {
        EnsureProfiles(applyProfileSettings: false);
        StoreActiveProfileSettings();
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
