using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TrackAttack.Models;
using TrackAttack.Views;
using TrackAttack.Services;

namespace TrackAttack.Tests;

public sealed class ResultCardTests
{
    [Fact]
    public async Task ExportsPngWithAccurateSummaryAndNoSongDetails()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var path = Path.Combine(Path.GetTempPath(), $"TrackAttack-result-{Guid.NewGuid():N}.png");
            try
            {
                var session = new QuizSession
                {
                    ProfileName = "Player",
                    Date = new DateTime(2026, 11, 16),
                    Duration = TimeSpan.FromSeconds(125),
                    Config = new QuizConfig { QuizTitle = "Rock quiz", QuizMode = QuizMode.GuessBoth },
                    Results =
                    [
                        new SongResult
                        {
                            Song = new Song { Title = "Secret song", Artist = "Secret artist", SourcePath = @"C:\private\song.mp3" },
                            IsCorrect = true,
                            ArtistCorrect = true
                        }
                    ]
                };
                var card = new ResultCardView(session);
                card.Measure(new Size(card.Width, card.Height));
                card.Arrange(new Rect(new Size(card.Width, card.Height)));
                card.UpdateLayout();
                var text = AllText(card).ToArray();
                Assert.Contains("Rock quiz", text);
                Assert.Contains("Player", text);
                Assert.Contains("100%", text);
                Assert.Contains($"{2.0:F1} / {2.0:F1} points", text);
                Assert.Contains("Title + artist", text);
                Assert.Contains("2:05", text);
                Assert.DoesNotContain(text, value => value.Contains("Secret", StringComparison.OrdinalIgnoreCase)
                    || value.Contains("private", StringComparison.OrdinalIgnoreCase));

                ResultCardExporter.SavePng(session, path);
                using var stream = File.OpenRead(path);
                var image = new PngBitmapDecoder(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
                Assert.Equal(1200, image.PixelWidth);
                Assert.Equal(630, image.PixelHeight);
                var pixels = new byte[image.PixelWidth * image.PixelHeight * 4];
                image.CopyPixels(pixels, image.PixelWidth * 4, 0);
                Assert.Equal(255, pixels[3]);
                Assert.NotEqual(pixels.Take(4).ToArray(), pixels.Skip((315 * 1200 + 300) * 4).Take(4).ToArray());
                completion.SetResult();
            }
            catch (Exception exception) { completion.SetException(exception); }
            finally { File.Delete(path); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await completion.Task;
    }

    [Fact]
    public async Task LegacyScoresAndMissingNamesUseClearLabels()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var session = new QuizSession
                {
                    ProfileName = "",
                    Config = new QuizConfig { QuizTitle = "", QuizMode = QuizMode.GuessArtist },
                    Results = [new SongResult { IsCorrect = true }, new SongResult()]
                };
                var card = new ResultCardView(session);
                Assert.Equal("1 / 2 correct", ((TextBlock)card.FindName("ScoreText")).Text);
                Assert.Equal("50%", ((TextBlock)card.FindName("PercentageText")).Text);
                Assert.Equal("Artist", ((TextBlock)card.FindName("ModeText")).Text);
                Assert.Equal("Track Attack Quiz", ((TextBlock)card.FindName("TitleText")).Text);
                Assert.Equal("Default", ((TextBlock)card.FindName("ProfileText")).Text);
                Assert.Equal("Date unavailable", ((TextBlock)card.FindName("DateText")).Text);
                completion.SetResult();
            }
            catch (Exception exception) { completion.SetException(exception); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await completion.Task;
    }

    [Fact]
    public async Task SelectedDetailsIncludeOnlyScoredTracksAndRecordedGuesses()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var path = Path.Combine(Path.GetTempPath(), $"TrackAttack-details-{Guid.NewGuid():N}.png");
            try
            {
                var session = new QuizSession
                {
                    Config = new QuizConfig { QuizMode = QuizMode.GuessBoth, HintCount = 1 },
                    Results =
                    [
                        new SongResult
                        {
                            Song = new Song { Title = "Earned", Artist = "First" },
                            IsCorrect = true, UserGuess = "Accepted title",
                            MulliganSongs = [new MulliganSong("Replaced", "Old artist", 0)]
                        },
                        new SongResult { Song = new Song { Title = "Missed", Artist = "Second" } },
                        new SongResult
                        {
                            Song = new Song { Title = "Penalty", Artist = "Third" },
                            IsCorrect = true, UserGuess = "Penalty guess", PowerPenalty = 2
                        }
                    ]
                };
                session.Config.MaxPowerPenaltyPerSong = 2;
                var options = new ResultCardOptions
                {
                    Tracks = ResultCardTracks.PointsEarned, IncludeGuesses = true, IncludeSettings = true,
                    Rank = new ResultCardRank(2, 4)
                };
                var card = new ResultCardView(session, options);
                card.Measure(new Size(card.Width, double.PositiveInfinity));
                card.Arrange(new Rect(card.DesiredSize));
                card.UpdateLayout();
                var text = AllText(card).ToArray();
                Assert.Contains(text, value => value.Contains("Earned"));
                Assert.Contains(text, value => value.Contains("Accepted title"));
                Assert.Contains(text, value => value.Contains("Artist guess: Not recorded"));
                Assert.Contains(text, value => value.Contains("#2 of 4"));
                Assert.Contains("QUIZ SETTINGS", text);
                Assert.Contains(text, value => value.StartsWith("Timer:"));
                Assert.DoesNotContain(text, value => value.Contains("Missed — Second")
                    || value.Contains("Penalty — Third") || value.Contains("Replaced — Old artist"));
                ResultCardExporter.SavePng(session, path, options);
                using var stream = File.OpenRead(path);
                var image = new PngBitmapDecoder(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
                Assert.Equal(1200, image.PixelWidth);
                Assert.True(image.PixelHeight > 630);
                completion.SetResult();
            }
            catch (Exception exception) { completion.SetException(exception); }
            finally { File.Delete(path); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await completion.Task;
    }

    [Fact]
    public async Task AllTracksIncludeMulligansWithoutGuessesUnlessSelected()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var session = new QuizSession
                {
                    Results = [new SongResult
                    {
                        Song = new Song { Title = "Final", Artist = "Artist" },
                        IsCorrect = true, UserGuess = "Accepted guess",
                        MulliganSongs = [new MulliganSong("Replaced", "Old artist", 0)]
                    }]
                };
                var card = new ResultCardView(session, new ResultCardOptions { Tracks = ResultCardTracks.All });
                card.Measure(new Size(card.Width, double.PositiveInfinity));
                card.Arrange(new Rect(card.DesiredSize));
                card.UpdateLayout();
                var text = AllText(card).ToArray();
                Assert.Contains(text, value => value.Contains("Final"));
                Assert.Contains(text, value => value.Contains("Replaced"));
                Assert.Contains("Mulligan", text);
                Assert.DoesNotContain(text, value => value.Contains("Accepted guess"));
                completion.SetResult();
            }
            catch (Exception exception) { completion.SetException(exception); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await completion.Task;
    }

    [Fact]
    public void LeaderboardUsesHistoryOrderingAndMatchingQuizIdentity()
    {
        QuizSession Attempt(int id, bool correct, DateTime date, string path) => new()
        {
            Id = id, Date = date, Config = new QuizConfig { QuizTitle = "Quiz" },
            Results = [new SongResult { IsCorrect = correct, Song = new Song { SourcePath = path } }]
        };
        var earlier = Attempt(1, true, new DateTime(2026, 1, 1), @"C:\music\track.mp3");
        var later = Attempt(2, true, new DateTime(2026, 1, 2), @"C:\music\track.mp3");
        var missed = Attempt(3, false, new DateTime(2026, 1, 1), @"C:\music\track.mp3");
        var other = Attempt(4, true, new DateTime(2026, 1, 1), @"C:\music\other.mp3");
        Assert.Equal(new[] { 1, 2, 3 }, QuizLeaderboard.Rank([missed, later, other, earlier], later).Select(x => x.Id));
    }

    [Fact]
    public async Task MaximumQuizKeepsAllTrackDetailsWithinImageLimit()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var path = Path.Combine(Path.GetTempPath(), $"TrackAttack-large-result-{Guid.NewGuid():N}.png");
            try
            {
                var session = new QuizSession
                {
                    Config = new QuizConfig { QuizMode = QuizMode.GuessBoth },
                    Results = Enumerable.Range(1, 1000).Select(index => new SongResult
                    {
                        Song = new Song { Title = $"Track {index}", Artist = $"Artist {index}" },
                        IsCorrect = true, ArtistCorrect = true, UserGuess = "Title", ArtistGuess = "Artist"
                    }).ToList()
                };
                var options = new ResultCardOptions { Tracks = ResultCardTracks.All, IncludeGuesses = true };
                var card = new ResultCardView(session, options);
                card.Measure(new Size(card.Width, double.PositiveInfinity));
                Assert.InRange(card.DesiredSize.Height, 631, 30000);
                card.Arrange(new Rect(card.DesiredSize));
                card.UpdateLayout();
                var text = AllText(card).ToArray();
                Assert.Contains(text, value => value.Contains("1. Track 1 — Artist 1"));
                Assert.Contains(text, value => value.Contains("1000. Track 1000 — Artist 1000"));
                ResultCardExporter.SavePng(session, path, options);
                using var stream = File.OpenRead(path);
                var image = new PngBitmapDecoder(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
                Assert.Equal((int)Math.Ceiling(card.DesiredSize.Height), image.PixelHeight);
                var last = FindText(card, "1000. Track 1000 — Artist 1000")!;
                var origin = last.TransformToAncestor(card).Transform(new Point(0, 0));
                var region = new Int32Rect((int)origin.X, (int)origin.Y,
                    (int)Math.Ceiling(last.ActualWidth), (int)Math.Ceiling(last.ActualHeight));
                var stride = region.Width * 4;
                var pixels = new byte[stride * region.Height];
                image.CopyPixels(region, pixels, stride, 0);
                Assert.Contains(Enumerable.Range(0, pixels.Length / 4), index =>
                    pixels[index * 4] > 200 && pixels[index * 4 + 1] > 200 && pixels[index * 4 + 2] > 200);
                completion.SetResult();
            }
            catch (Exception exception) { completion.SetException(exception); }
            finally { File.Delete(path); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await completion.Task;
    }

    private static IEnumerable<string> AllText(DependencyObject parent)
    {
        if (parent is TextBlock block)
            yield return block.Text;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
            foreach (var value in AllText(VisualTreeHelper.GetChild(parent, index)))
                yield return value;
    }

    private static TextBlock? FindText(DependencyObject parent, string value)
    {
        if (parent is TextBlock block && block.Text == value)
            return block;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
            if (FindText(VisualTreeHelper.GetChild(parent, index), value) is { } found)
                return found;
        return null;
    }
}
