namespace TrackAttack.Services;

internal static class LocalArtistResolver
{
    public static string? FromTags(TagLib.Tag tag)
        => FirstArtist(tag.Performers ?? []) ?? FirstArtist(tag.AlbumArtists ?? []);

    public static string? UsableArtist(string? value)
    {
        var artist = value?.Trim();
        return string.IsNullOrEmpty(artist) || artist.Equals("Various Artists", StringComparison.OrdinalIgnoreCase)
            ? null
            : artist;
    }

    private static string? FirstArtist(IEnumerable<string> values)
        => values.Select(UsableArtist).FirstOrDefault(value => value is not null);
}
