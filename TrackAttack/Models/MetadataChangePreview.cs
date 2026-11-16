namespace TrackAttack.Models;

/// <summary>Describes a changed tag before and after a staged edit.</summary>
/// <param name="Field">The edited metadata field.</param>
/// <param name="Before">The values read during the last scan.</param>
/// <param name="After">The values to be written.</param>
public sealed record MetadataFieldChange(string Field, IReadOnlyList<string> Before, IReadOnlyList<string> After);

/// <summary>Describes all staged tag changes for a file.</summary>
/// <param name="FilePath">The path of the file.</param>
/// <param name="Changes">The fields to be changed.</param>
public sealed record MetadataFileChange(string FilePath, IReadOnlyList<MetadataFieldChange> Changes);
