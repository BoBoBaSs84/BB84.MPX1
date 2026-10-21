using BB84.MPX1.Core.Protocol;

namespace BB84.MPX1.Core.Database;

/// <summary>
/// Identifies one firmware build. A learned database is valid only for the build it was learned from (LRN-004).
/// </summary>
/// <param name="MajorVersion">Major software version.</param>
/// <param name="MinorVersion">Minor software version.</param>
/// <param name="BuildDate">Build date string from System Configuration.</param>
/// <param name="BuildTime">Build time string from System Configuration.</param>
public sealed record DatabaseKey(byte MajorVersion, byte MinorVersion, string BuildDate, string BuildTime)
{
    /// <summary>Creates the key for the firmware that sent <paramref name="configuration"/>.</summary>
    /// <param name="configuration">The System Configuration message.</param>
    public static DatabaseKey From(SystemConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return new(configuration.MajorVersion, configuration.MinorVersion, configuration.BuildDate.Trim(), configuration.BuildTime.Trim());
    }

    /// <summary>Gets a file-name-safe identifier, for example <c>V1.10_Mar-30-1998_17-51-03</c>.</summary>
    public string FileStem
    {
        get
        {
            static string Clean(string value) => string.Concat(value.Select(c => char.IsLetterOrDigit(c) ? c : '-'));
            return $"V{MajorVersion}.{MinorVersion:D2}_{Clean(BuildDate)}_{Clean(BuildTime)}";
        }
    }

    /// <inheritdoc/>
    public override string ToString() => $"V{MajorVersion}.{MinorVersion:D2} ({BuildDate} {BuildTime})";
}
