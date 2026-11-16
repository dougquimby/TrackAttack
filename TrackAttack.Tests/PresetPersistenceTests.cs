using System.IO;
using TrackAttack.Models;
using TrackAttack.Services;
using TrackAttack.Settings;

namespace TrackAttack.Tests;

public sealed class PresetPersistenceTests
{
    [Fact]
    public void SavedPresetRestoresSelectionsAndScoringRules()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"TrackAttack-tests-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "settings.json");
        try
        {
            var settings = new AppSettings
            {
                LocalQuizPresets =
                [
                    new LocalQuizPreset
                    {
                        Name = "Favorites",
                        Genres = ["Rock", "! Unknown"],
                        Decades = ["1984", "1990s", "! Unknown"],
                        UniqueArtistsOnly = true,
                        ExcludeRecentQuizSongs = true,
                        RecentQuizzesToExclude = 3,
                        Config = new QuizConfig
                        {
                            LocalMusicDirectories = [@"C:\Music"],
                            LocalFolderRules = [new MusicFolderRule { Path = @"C:\Music\Excluded", Included = false }],
                            QuizMode = QuizMode.GuessBoth,
                            QuickGuessBonusEnabled = true,
                            QuickGuessBonusSeconds = 7,
                            QuickGuessBonusPoints = 0.75,
                            NearMissFeedbackEnabled = true,
                            StarPowerEnabled = true,
                            StarPowerStreakRequired = 4,
                            StarPowerBonusPoints = 0.5,
                            HintPenalty = 0.25,
                            MaxPowerPenaltyPerSong = 0.75
                        }
                    }
                ]
            };

            settings.Save(path);
            var preset = Assert.Single(AppSettings.Load(path).LocalQuizPresets);
            Assert.Equal("Favorites", preset.Name);
            Assert.Equal(new[] { "Rock", "! Unknown" }, preset.Genres);
            Assert.Equal(new[] { "1984", "1990s", "! Unknown" }, preset.Decades);
            Assert.True(preset.UniqueArtistsOnly);
            Assert.True(preset.ExcludeRecentQuizSongs);
            Assert.Equal(3, preset.RecentQuizzesToExclude);
            Assert.Equal(@"C:\Music", Assert.Single(preset.Config.LocalMusicDirectories!));
            Assert.False(Assert.Single(preset.Config.LocalFolderRules!).Included);
            Assert.Equal(QuizMode.GuessBoth, preset.Config.QuizMode);
            Assert.True(preset.Config.QuickGuessBonusEnabled);
            Assert.Equal(7, preset.Config.QuickGuessBonusSeconds);
            Assert.Equal(0.75, preset.Config.QuickGuessBonusPoints);
            Assert.True(preset.Config.NearMissFeedbackEnabled);
            Assert.True(preset.Config.StarPowerEnabled);
            Assert.Equal(4, preset.Config.StarPowerStreakRequired);
            Assert.Equal(0.5, preset.Config.StarPowerBonusPoints);
            Assert.Equal(0.25, preset.Config.HintPenalty);
            Assert.Equal(0.75, preset.Config.MaxPowerPenaltyPerSong);
            var years = new YearFilter(preset.Decades);
            Assert.True(years.Matches(1984));
            Assert.False(years.Matches(1985));
            Assert.True(years.Matches(1997));
            Assert.True(years.Matches(0));

            var loaded = AppSettings.Load(path);
            loaded.LocalQuizPresets[0].Config.StarPowerStreakRequired = 6;
            loaded.Save(path);
            Assert.Equal(6, Assert.Single(AppSettings.Load(path).LocalQuizPresets).Config.StarPowerStreakRequired);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void InvalidSettingsAreReportedAndNotOverwritten()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"TrackAttack-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "settings.json");
        try
        {
            File.WriteAllText(path, "{ invalid");
            Assert.Throws<SettingsLoadException>(() => AppSettings.Load(path));
            Assert.Equal("{ invalid", File.ReadAllText(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ClonedSetupDoesNotShareMutableSelections()
    {
        var original = new QuizConfig
        {
            LocalMusicDirectories = [@"C:\Music"],
            LocalFolderRules = [new MusicFolderRule { Path = @"C:\Music\Live", Included = false }],
            EnabledHintTypes = [HintType.Year]
        };
        var clone = original.Clone();
        clone.LocalMusicDirectories![0] = @"D:\Other";
        clone.LocalFolderRules![0].Included = true;
        clone.EnabledHintTypes.Clear();

        Assert.Equal(@"C:\Music", Assert.Single(original.LocalMusicDirectories!));
        Assert.False(Assert.Single(original.LocalFolderRules!).Included);
        Assert.Equal(HintType.Year, Assert.Single(original.EnabledHintTypes));
    }
}
