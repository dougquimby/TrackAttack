using System.Globalization;
using System.Reflection;
using TrackAttack.Views;

namespace TrackAttack.Tests;

public sealed class AppBuildMetadataTests
{
    [Fact]
    public void AppAssemblyContainsVersionAndUtcBuildDate()
    {
        var assembly = typeof(SettingsView).Assembly;
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        Assert.False(string.IsNullOrWhiteSpace(version));

        var buildDate = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(attribute => attribute.Key == "BuildDateUtc").Value;
        Assert.True(DateOnly.TryParseExact(buildDate, "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var date));
        Assert.True(date <= DateOnly.FromDateTime(DateTime.UtcNow));
    }
}
