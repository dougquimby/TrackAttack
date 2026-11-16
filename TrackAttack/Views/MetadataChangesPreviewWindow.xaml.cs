using System.Windows;
using TrackAttack.Models;

namespace TrackAttack.Views;

/// <summary>Shows the complete staged batch before metadata is written.</summary>
public partial class MetadataChangesPreviewWindow : Window
{
    /// <summary>Creates a review of the modified entries.</summary>
    /// <param name="entries">The modified files and their tag changes.</param>
    public MetadataChangesPreviewWindow(IReadOnlyList<MetadataFileChange> entries)
    {
        InitializeComponent();
        ChangesGrid.RowHeight = double.NaN;
        var changes = entries.SelectMany(entry => entry.Changes.Select(change => new
        {
            entry.FilePath,
            change.Field,
            Before = FormatValues(change.Before),
            After = FormatValues(change.After)
        })).ToArray();
        SummaryText.Text = $"Review {changes.Length:N0} field change(s) across {entries.Count:N0} file(s)";
        ChangesGrid.ItemsSource = changes;
        Loaded += (_, _) => CancelButton.Focus();
    }

    private static string FormatValues(IReadOnlyList<string> values)
    {
        if (values.Count == 0)
            return "(empty)";
        if (values.Count == 1)
            return FormatValue(values[0]);
        return string.Join(Environment.NewLine, values.Select((value, index) =>
            $"{index + 1}. {FormatValue(value)}"));
    }

    private static string FormatValue(string value) => string.IsNullOrEmpty(value) ? "(empty)" : $"\"{value}\"";

    private void OnApplyClick(object sender, RoutedEventArgs e) => DialogResult = true;
}
