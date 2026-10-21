using System.Text.Json;

using BB84.MPX1.Simulator.Fixtures;

namespace BB84.MPX1.Simulator.Recording;

/// <summary>
/// One SysEx frame of a recorded session, stored as a JSON line <c>{"t":…,"dir":"out","hex":"F0…F7"}</c> (spec 4.7).
/// </summary>
/// <param name="T">Milliseconds since the recording started.</param>
/// <param name="Dir"><see cref="Out"/> (editor to device) or <see cref="In"/> (device to editor).</param>
/// <param name="Hex">The frame as hex.</param>
public sealed record RecordedFrame(long T, string Dir, string Hex)
{
    /// <summary>Direction value for frames sent to the device.</summary>
    public const string Out = "out";

    /// <summary>Direction value for frames received from the device.</summary>
    public const string In = "in";

    /// <summary>Gets the frame bytes.</summary>
    public byte[] ToBytes() => Convert.FromHexString(Hex);

    /// <summary>Formats the frame as one JSON line (without a line break).</summary>
    public string ToJsonLine()
        => JsonSerializer.Serialize(this, CompactContext.RecordedFrame);

    /// <summary>Reads all frames from JSON-lines text. Blank lines are ignored.</summary>
    /// <param name="reader">The text source.</param>
    /// <exception cref="JsonException">A line is not a valid frame.</exception>
    public static IReadOnlyList<RecordedFrame> ReadAll(TextReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        List<RecordedFrame> frames = [];
        while (reader.ReadLine() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            frames.Add(JsonSerializer.Deserialize(line, CompactContext.RecordedFrame)
                ?? throw new JsonException("Empty frame line."));
        }
        return frames;
    }

    private static SimulatorJsonContext CompactContext { get; } = new(new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    });
}
