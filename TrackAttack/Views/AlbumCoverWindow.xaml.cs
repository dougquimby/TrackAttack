using System.Windows;
using System.Windows.Media;

namespace TrackAttack.Views;

public partial class AlbumCoverWindow : Window
{
    public AlbumCoverWindow(ImageSource artwork)
    {
        InitializeComponent();
        CoverImage.Source = artwork;
    }
}
