namespace TrackAttack.Settings;

internal static class AppDataPaths
{
    public static string DirectoryPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TrackAttack");
}
