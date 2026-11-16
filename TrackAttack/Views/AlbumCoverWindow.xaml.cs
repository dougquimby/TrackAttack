using System.Windows;
using System.Windows.Media;

namespace TrackAttack.Views;

/// <summary>Displays enlarged album artwork.</summary>
public partial class AlbumCoverWindow : Window
{
    /// <summary>Creates a window for the supplied artwork.</summary>
    /// <param name="artwork">The image to display.</param>
    public AlbumCoverWindow(ImageSource artwork)
    {
        InitializeComponent();
        CoverImage.Source = artwork;
    }
}
