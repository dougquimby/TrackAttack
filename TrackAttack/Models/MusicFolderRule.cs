namespace TrackAttack.Models;

/// <summary>Specifies whether a local folder and its descendants are selected.</summary>
public sealed class MusicFolderRule
{
    /// <summary>Gets or sets the folder path to which the rule applies.</summary>
    public string Path { get; set; } = string.Empty;
    /// <summary>Gets or sets whether the folder is included.</summary>
    public bool Included { get; set; }
}
