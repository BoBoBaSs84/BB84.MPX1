using System.Text.Json;
using System.Text.Json.Serialization;

using BB84.MPX1.Core.Database;
using BB84.MPX1.Midi;

namespace BB84.MPX1.App.Services;

/// <summary>Persisted application settings (spec 4.7): ports, device ID and session timing.</summary>
public sealed record AppSettings
{
    /// <summary>Current schema version.</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>Gets the schema version of the file.</summary>
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    /// <summary>Gets the key of the last input port (see <see cref="MidiPortChoice.Key"/>), or <see langword="null"/>.</summary>
    public string? InputPort { get; init; }

    /// <summary>Gets the key of the last output port, or <see langword="null"/>.</summary>
    public string? OutputPort { get; init; }

    /// <summary>Gets the device ID, 0–127.</summary>
    public byte DeviceId { get; init; }

    /// <summary>Gets the request timeout in milliseconds (SES-003).</summary>
    public int RequestTimeoutMs { get; init; } = 1000;

    /// <summary>Gets the retry count (SES-003).</summary>
    public int Retries { get; init; } = 2;

    /// <summary>Gets the READY timeout in milliseconds (SES-005).</summary>
    public int ReadyTimeoutMs { get; init; } = 3000;

    /// <summary>Gets the minimum gap between outgoing frames in milliseconds (SES-008).</summary>
    public int InterFrameGapMs { get; init; }

    /// <summary>Gets the firmware build of the last learned database, used for offline editing (UI-010).</summary>
    public DatabaseKey? LastDatabase { get; init; }

    /// <summary>Gets a value indicating whether the MIDI monitor panel is expanded.</summary>
    public bool MonitorVisible { get; init; } = true;

    /// <summary>Builds session options from the timing settings, clamped to sane ranges.</summary>
    public Mpx1SessionOptions ToSessionOptions() => new()
    {
        RequestTimeout = TimeSpan.FromMilliseconds(Math.Clamp(RequestTimeoutMs, 50, 30_000)),
        Retries = Math.Clamp(Retries, 0, 10),
        ReadyTimeout = TimeSpan.FromMilliseconds(Math.Clamp(ReadyTimeoutMs, 100, 60_000)),
        InterFrameGap = TimeSpan.FromMilliseconds(Math.Clamp(InterFrameGapMs, 0, 1000)),
    };
}

/// <summary>Source-generated JSON metadata for <see cref="AppSettings"/> (CON-003).</summary>
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class AppSettingsJsonContext : JsonSerializerContext;

/// <summary>Loads and saves <see cref="AppSettings"/> as JSON.</summary>
/// <param name="path">The settings file.</param>
public sealed class SettingsStore(string path)
{
    /// <summary>Gets the settings file path.</summary>
    public string Path { get; } = path ?? throw new ArgumentNullException(nameof(path));

    /// <summary>Loads the settings. A missing or corrupt file yields defaults (SEC-002).</summary>
    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(Path))
                return new AppSettings();
            return JsonSerializer.Deserialize(File.ReadAllText(Path), AppSettingsJsonContext.Default.AppSettings) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new AppSettings();
        }
    }

    /// <summary>Saves the settings, replacing the file atomically.</summary>
    /// <param name="settings">The settings.</param>
    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        string? directory = System.IO.Path.GetDirectoryName(Path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        string temp = Path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, AppSettingsJsonContext.Default.AppSettings));
        File.Move(temp, Path, overwrite: true);
    }
}

/// <summary>Per-user locations of settings and the learned-database cache.</summary>
public static class AppPaths
{
    private const string AppFolder = "BB84.MPX1";

    /// <summary>Gets the settings file, under the roaming application data folder.</summary>
    public static string SettingsFile { get; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppFolder, "settings.json");

    /// <summary>Gets the learned-database cache folder (LRN-004), under the local application data folder.</summary>
    public static string DatabaseDirectory { get; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppFolder, "databases");
}
