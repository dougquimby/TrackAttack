# TrackAttack — agent handoff

Use this repository map before exploring. Treat the code as authoritative when behavior changes; update this guide when architecture or workflows change.

## Scope

- TrackAttack is a Windows desktop music-guessing game. Local-file quizzes are the default; Spotify playlist quizzes are also a supported source, disabled by default and controlled by `EnableSpotifyIntegration`. Preserve the feature switch and keep source-specific behavior appropriately gated, while considering both sources for shared quiz features.
- Prefer presentation-independent quiz rules and services. Keep WPF controls and lifecycle concerns in `Views/`; avoid adding WPF dependencies to `Engine/`, `Models/`, or reusable `Services/`.
- TrackAttack stores its settings, quiz history, and metadata backups under `%APPDATA%\TrackAttack`.

## Project and commands

- Windows desktop music-guessing game: C# WPF, `net10.0-windows`, nullable reference types and implicit usings enabled.
- One app project: `TrackAttack/TrackAttack.csproj`; solution: `TrackAttack.slnx`.
- Build: `dotnet build TrackAttack/TrackAttack.csproj`
- Run: `dotnet run --project TrackAttack/TrackAttack.csproj`
- No test project currently exists. Build code changes and report that automated tests are unavailable.
- Main dependencies: MetaBrainz.MusicBrainz and MetaBrainz.MusicBrainz.CoverArt, TheAudioDB catalog suggestions, Microsoft.Data.Sqlite, NAudio, SQLitePCLRaw, TagLibSharp, and the optional Spotify Web API packages.

## Application map

```text
TrackAttack/App.xaml
  └─ MainWindow                         composition root / embedded navigation
       ├─ HomeView
       ├─ QuizSetupView ── QuizView ── QuizEngine
       │                    ├─ IPlaybackService / NAudioPlaybackService or SpotifyPlaybackService
       │                    └─ ResultsView
       ├─ MetadataView ── LocalMetadataManager
       ├─ SettingsView
       ├─ HistoryView
       └─ SongsView
```

- `MainWindow.xaml.cs` creates the shared local music source and history repository, owns the current `UserControl` and navigation stack, and guards navigation when a quiz is active. The shared header displays the current page title; avoid repeating it inside individual views. Quiz completion replaces the quiz with `ResultsView`; abandoning a quiz calls `Abandon()` and disposes it. It selects Spotify playback only for a Spotify quiz config.
- Each screen is a WPF `UserControl` with XAML in `Views/*View.xaml` and behavior in its `.xaml.cs` code-behind. Follow these existing patterns; there is no separate ViewModel layer currently.
- `QuizSetupView` raises `QuizRequested` with its config, selected songs, and mulligan reserve. `QuizView` owns the quiz engine and playback service, maps engine events to UI, and disposes resources on unload.
- `Engine/QuizEngine.cs` is the quiz state machine. Keep scoring, rotation, timer, and power rules there rather than in view code.
- `MetadataView` presents and stages edits in `MetadataEditEntry` models. `LocalMetadataManager` scans files, validates and writes tags, and creates/restores recovery snapshots. Keep TagLib and filesystem mutation out of the view.
- `Models/` holds quiz, song, result, profile, preset, and metadata models. `Services/` contains local source/playback, answer matching, and catalog suggestion providers. `Data/` contains SQLite initialization and the async history repository. `Settings/AppSettings.cs` owns settings serialization.
- `MainWindow` persists the sidebar collapsed preference plus normal window bounds and maximized state in `AppSettings`; it restores saved placement only when the bounds intersect the current virtual desktop.

## Local library, quiz setup, and presets

- The supported source is local MP3 and FLAC files. `LocalFileMusicSource.ScanDirectory` scans recursively and reads tags with TagLibSharp; when title or artist is missing, it falls back to a filename shaped like `Artist - Title`. Unreadable files are skipped in quiz scans.
- `QuizSetupView` scans the configured folder, builds genre and decade filters, supports the `! Unknown` option for blank genre or nonpositive year, applies inclusive duration filters and optional unique-artist selection, and shuffles eligible songs. It selects the requested song count and retains extras as the mulligan reserve. `SongsView` shows songs matching the current filters.
- Recent-quiz exclusion is optional and off by default. When enabled, it excludes songs from the configured number of recent quizzes for the active profile and current source/library; history matches new rows by file path and can fall back to title and artist for older rows.
- Quiz profiles are named players or groups selected on Home and managed in Settings. Each profile owns its music folder and quiz defaults (timer/clip duration, volume, matching, skip-on-correct, and recent-quiz exclusion); source credentials, metadata provider, navigation, and window preferences remain global. Existing settings migrate into a single `Default` profile without losing those quiz preferences. Quiz history stores the profile name, keeps it after a profile is renamed or deleted, and can be filtered by any profile or across all profiles.
- Quiz history can replay a selected quiz using its saved config and recorded track list, or delete an individual quiz and its song results. Replay resolves current playback metadata from the original local library or Spotify playlist and uses other currently eligible tracks for the saved mulligan reserve; missing original tracks stop replay with an explanation. In full-blur mode, title/artist are blurred and guesses from other profiles are blurred; other spoiler modes preserve their title/artist behavior. The leaderboard compares the best result per profile for quizzes with the same title, source type, answer mode, and ordered track identities.
- Named quiz presets are saved in `AppSettings.LocalQuizPresets`. They store the source folder, filters, quiz/timer/clip settings, matching options, title/artist point values, hint scrambling mode, powers and penalties, and recent-quiz options. Selecting one restores its settings and rescans when its folder differs; its name is prefilled so saving updates it without retyping, while changing the name saves under a different name. The song-count display preference is also persisted in settings.
- Trace a new quiz option end-to-end: `QuizSetupView` control and preset handling → `QuizConfig` → engine or service behavior → result/history persistence and display.

## Optional Spotify quizzes

- Spotify integration is off by default. `SettingsView` persists the `EnableSpotifyIntegration` switch; when disabled, Spotify controls are hidden and new quizzes require the local library. Changing the switch in Settings updates an already-open quiz setup view when returning to it.
- When enabled, `QuizSetupView` reuses shared quiz options, powers, and presets. Spotify playlists saved to the connected user's account are the selection source; genre filtering is hidden, while decade and duration filters and optional unique-artist/recent-quiz exclusion remain available. Track years come from Spotify album release dates.
- `SpotifyService` uses browser PKCE authorization. Configure a Spotify developer app and register `http://127.0.0.1:5543/callback`; credentials are stored with the existing settings in `%APPDATA%\TrackAttack\settings.json`. A playlist must be available in the connected account's playlist library.
- `SpotifyMusicSource` pages playlist tracks and maps track URIs for stable history identity. Preview URLs play locally through NAudio when available; tracks without previews use Spotify Connect playback, with the selected output device shown in the quiz. For Connect playback, use the selected playlist as `context_uri` and the selected track URI as the request offset, pass the selected device ID (which activates that device), and verify Spotify reports the requested track playing before starting quiz progress. Serialize Connect start commands and ignore stale song requests so rapid grid selection cannot leave an older request as the final track. A playback failure must leave the quiz usable: show retry guidance, allow Replay or another song selection, and do not pause the engine without a playback recovery path. Connect audio comes from that Spotify device, not the app's local audio stream. Spotify Web API playback control requires Premium and an available playback device. Runtime API/device behavior requires a connected Spotify account and is not covered by the project build.
- Keep playlist APIs and OAuth in `Services/`, source-specific controls gated in `Views/`, and quiz/source metadata in `QuizConfig` and `Song`. Do not change the local-file path when modifying optional Spotify behavior.

## Metadata repair and lookup

- `MetadataView` scans MP3/FLAC files through `LocalMetadataManager`, stages edits to Title, Artist, Album, Genre, and Year, and asks for confirmation before writing. Unreadable rows are marked non-editable. Before applying changes, the manager saves a recovery snapshot under `%APPDATA%\TrackAttack\MetadataBackups`; **Undo Last Batch** restores the latest snapshot.
- Catalog lookup uses `IMetadataSuggestionProvider`, with the provider selection persisted in settings: MusicBrainz or TheAudioDB. Both providers retry common punctuation/spacing variants of the artist name, and MusicBrainz falls back to a title/album query when artist-filtered searches return nothing. Keep provider-specific API requests behind this interface and feed suggestions into the same staged-edit workflow.
- `MetadataMatchScorer` ranks candidates using title (20%), artist (15%), album (20%), and duration (45%). Neither provider scores genre; album/year/genre details may be unavailable.
- Bulk lookup operates on selected readable rows, processes requests sequentially, and stages matches for review; it does not write files. For a single selected row, the candidate grid lets users choose which title, artist, album, genre, and year fields to stage; all field checkboxes start checked. Multi-row bulk lookup stages confident top matches automatically, while the one-request-per-album option stages year/genre to matching readable tracks. Manual candidate staging can also propagate its checked genre/year fields to tracks with the same original album and artist. Cancellation stops further work while retaining already staged candidates. The existing apply confirmation, recovery snapshot, and undo flow still governs file writes.
- Keep API calls and matching/mapping rules out of WPF code where possible; preserve the provider contract's independence from any one catalog or UI.

## Engine and playback behavior to preserve

- Quiz modes are title, artist, and both. The setup view allows configuring the points awarded for each answer type: title-only and artist-only default to 2 points, while Guess Both defaults to 1.5 for title and 0.5 for artist. Partial credit is retained. Legacy quiz history without configured weights keeps the former scoring (1 point for single-answer modes and 1.5/0.5 for Guess Both). Power penalties are stored per song and capped by `MaxPowerPenaltyPerSong` and earned points so a song never contributes negative points.
- Timer modes are whole-quiz countdown and per-song reset. Per-song expiry passes the current song and resets its timer; it does not end the quiz. Per-song resets invalidate queued ticks from the previous song so stale timer callbacks cannot drain the new song's time. Clip start can be fixed or random; the random start assigned to a song remains the same on retries.
- Playback modes are loop, play once and stop, and play once then auto-advance. The playback service raises `ClipEnded`; the engine marshals timer and clip callbacks to its captured synchronization context and prevents them from advancing state while paused.
- Quiz pause pauses the engine countdown and playback together and excludes paused time from quiz duration. Clip-only pause affects playback without pausing quiz time.
- `IPlaybackService.Volume` is normalized per-stream gain and persists in the active profile's `AppSettings.AudioVolume`; the Settings and Quiz sliders use the same value, and NAudio playback must not change Windows master volume. `SeekClip` seeks relative to the designated clip start, clamps to clip duration, and re-arms clip-end handling. Progress UI shows clip-relative and full-song progress; use the service's clip/song position and duration properties rather than seeking the source track directly.
- Correct guesses normally advance immediately; with `SkipOnCorrectGuess` disabled, the clip finishes its cycle first. Pass advances, and the song grid can jump to an unfinished song. Tab passes during a running quiz.
- Mulligan replaces the current slot using the preselected reserve. Extender doubles a song's clip duration and remains in effect if the song returns in rotation. Hints randomly reveal enabled, applicable first-letter, scrambled title/artist, genre, exact-year, or album-cover clues; a clue type is shown at most once per song and revealed clues are retained. Album names are shown with cover art only when guessing the song title, not when artist is part of the answer. Album art tries valid Spotify artwork, then a tagged MusicBrainz release ID or high-confidence release search, then TheAudioDB album artwork; each candidate is decoded before use, and unavailable or unsupported images fall back to another available hint.
- Scrambled title and artist hints use the quiz setup's `ScrambleHintsPerWord` setting. It defaults to scrambling each word independently; when disabled, letters are shuffled across the entire title or artist while punctuation and spacing stay in place.
- Ending a quiz fills in unrecorded songs as incorrect, orders all results by their original quiz slot (including mulligan replacements), and raises `QuizEnded`. Keep scoring and rotation out of view code. Dispose timers, playback, and engine resources along the existing `QuizView` lifecycle.

## Persistence

- Settings JSON: `%APPDATA%\TrackAttack\settings.json` (`AppSettings.FilePath`), including profile settings, active profile, global settings, sidebar collapsed preference, and quiz defaults.
- History SQLite database: `%APPDATA%\TrackAttack\history.db` (`DatabaseInitializer.DatabasePath`); `DatabaseInitializer.Initialize()` creates and migrates the schema, including the profile-name field. Metadata recovery snapshots are stored under `%APPDATA%\TrackAttack\MetadataBackups`.
- `IQuizHistoryRepository` exposes async save/list/detail/recent-song/clear operations. Quiz results are saved through the history repository; abandoning a running quiz saves partial results.
- `QuizConfig` is serialized as JSON in `QuizHistory.ConfigJson`; individual song outcome fields are stored in `QuizSongResult`.

## Change guidance

- Keep async repository and catalog calls async from views; use the existing error reporting patterns rather than swallowing failures.
- Keep XAML layout and its code-behind in sync. Unhook or dispose timers, playback, and event subscriptions along the existing lifecycle.
- For settings/history changes, keep all TrackAttack data under `%APPDATA%\TrackAttack`.
- Follow the repository's existing project and UI conventions. Update this guide when architecture, commands, or important workflows change.
