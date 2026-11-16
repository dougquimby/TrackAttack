using System.Windows;
using System.Windows.Controls;

namespace TrackAttack.Views;

/// <summary>Provides access to local quiz challenges.</summary>
public partial class SocialView : UserControl
{
    /// <summary>Occurs when a challenge file is requested.</summary>
    public event Action? OpenChallengeRequested;
    /// <summary>Occurs when quiz setup is requested.</summary>
    public event Action? CreateChallengeRequested;
    /// <summary>Occurs when quiz history is requested.</summary>
    public event Action? HistoryRequested;

    /// <summary>Creates the social view.</summary>
    public SocialView() => InitializeComponent();

    private void OnOpenChallengeClick(object sender, RoutedEventArgs e) => OpenChallengeRequested?.Invoke();
    private void OnCreateChallengeClick(object sender, RoutedEventArgs e) => CreateChallengeRequested?.Invoke();
    private void OnHistoryClick(object sender, RoutedEventArgs e) => HistoryRequested?.Invoke();
}
