using System.Drawing;
using System.Text.Json;
using TrackAttack.Models;

namespace TrackAttack.Settings;

public class AppSettings
{
    public string MusicDirectory { get; set; } = string.Empty;
    public int DefaultSecondsPerSong { get; set; } = 30;
    public int DefaultClipDurationSeconds { get; set; } = 15;
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
    public bool WindowMaximized { get; set; }
    public bool WindowRestored { get; set; }
    public Rectangle WindowBounds { get; set; }

    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MusicQuiz", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch { }
        return new AppSettings();
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
