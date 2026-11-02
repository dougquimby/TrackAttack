using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.IO;
using TrackAttack.Settings;

namespace TrackAttack.Views;

internal static class ProfileIconFiles
{
    private const int MaximumIconDimension = 256;
    private const long MaximumSourceFileSize = 10 * 1024 * 1024;
    private const long MaximumSourcePixelCount = 40_000_000;

    public static string SaveResizedPng(string sourcePath)
    {
        var sourceInfo = new FileInfo(sourcePath);
        if (!sourceInfo.Exists)
            throw new FileNotFoundException("The selected profile image could not be found.", sourcePath);
        if (sourceInfo.Length > MaximumSourceFileSize)
            throw new InvalidDataException("Profile images must be 10 MB or smaller.");

        using var sourceStream = File.OpenRead(sourcePath);
        var decoder = BitmapDecoder.Create(
            sourceStream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        if (decoder.Frames.Count == 0)
            throw new InvalidDataException("The selected file does not contain a readable image.");

        var frame = decoder.Frames[0];
        if (frame.PixelWidth <= 0 || frame.PixelHeight <= 0
            || frame.PixelWidth > 20_000 || frame.PixelHeight > 20_000
            || (long)frame.PixelWidth * frame.PixelHeight > MaximumSourcePixelCount)
            throw new InvalidDataException("Profile images must be no larger than 40 megapixels.");

        var scale = Math.Min(
            1.0,
            MaximumIconDimension / (double)Math.Max(frame.PixelWidth, frame.PixelHeight));
        var resized = new TransformedBitmap(frame, new ScaleTransform(scale, scale));
        resized.Freeze();

        var directory = Path.Combine(AppDataPaths.DirectoryPath, "ProfileIcons");
        Directory.CreateDirectory(directory);
        var fileName = $"{Guid.NewGuid():N}.png";
        var destinationPath = Path.Combine(directory, fileName);
        var temporaryPath = destinationPath + ".tmp";
        try
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(resized));
            using (var destination = File.Create(temporaryPath))
                encoder.Save(destination);
            File.Move(temporaryPath, destinationPath);
        }
        catch
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
            throw;
        }

        return fileName;
    }

    public static string? GetExistingPath(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)
            || !string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal)
            || !string.Equals(Path.GetExtension(fileName), ".png", StringComparison.OrdinalIgnoreCase))
            return null;

        var path = Path.Combine(AppDataPaths.DirectoryPath, "ProfileIcons", fileName);
        return File.Exists(path) ? path : null;
    }

    public static void Delete(string? fileName)
    {
        var path = GetExistingPath(fileName);
        if (path is not null)
            File.Delete(path);
    }

    public static BitmapSource LoadBitmapSource(string path)
    {
        using var stream = File.OpenRead(path);
        var decoder = BitmapDecoder.Create(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        if (decoder.Frames.Count == 0)
            throw new InvalidDataException("The saved profile image is unreadable.");

        var frame = decoder.Frames[0];
        frame.Freeze();
        return frame;
    }

    public static string GetInitials(string? profileName)
    {
        var parts = (profileName ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            return "?";
        if (parts.Length == 1)
            return parts[0][..Math.Min(2, parts[0].Length)].ToUpperInvariant();
        return $"{char.ToUpperInvariant(parts[0][0])}{char.ToUpperInvariant(parts[^1][0])}";
    }
}
