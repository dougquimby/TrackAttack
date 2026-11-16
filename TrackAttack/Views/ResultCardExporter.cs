using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TrackAttack.Data;
using TrackAttack.Models;

namespace TrackAttack.Views;

/// <summary>Exports quiz result cards as PNG images.</summary>
public static class ResultCardExporter
{
    /// <summary>Prompts for a destination and saves a result card.</summary>
    /// <param name="owner">The view owning the save dialog.</param>
    /// <param name="session">The result to export.</param>
    /// <param name="history">The repository used to calculate leaderboard position.</param>
    public static void PromptAndSave(FrameworkElement owner, QuizSession session, IQuizHistoryRepository history)
    {
        var window = Window.GetWindow(owner);
        var dialog = new ResultCardOptionsWindow(session, history);
        if (window is null)
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        else
            dialog.Owner = window;
        dialog.ShowDialog();
    }

    /// <summary>Renders and saves a result card to a PNG file.</summary>
    /// <param name="session">The result to export.</param>
    /// <param name="path">The destination file path.</param>
    /// <param name="options">Optional sections to include in the card.</param>
    public static void SavePng(QuizSession session, string path, ResultCardOptions? options = null)
    {
        var card = new ResultCardView(session, options);
        card.Measure(new Size(card.Width, double.PositiveInfinity));
        var height = Math.Ceiling(card.DesiredSize.Height);
        if (height > 30000)
            throw new InvalidOperationException("The selected details exceed the maximum image height. Select fewer tracks or omit details.");
        var size = new Size(card.Width, height);
        card.Arrange(new Rect(size));
        card.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(card);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
