using System.Collections.Immutable;
using System.Globalization;

using BB84.MPX1.Core.Protocol;

namespace BB84.MPX1.Core.Files;

/// <summary>The MPX 1 messages found in a <c>.syx</c> file, plus what was skipped and why.</summary>
/// <param name="Messages">Decoded MPX 1 messages in file order.</param>
/// <param name="Warnings">One entry per skipped frame (other devices, invalid data).</param>
public sealed record SyxContent(ImmutableArray<Mpx1Message> Messages, ImmutableArray<string> Warnings)
{
    /// <summary>Gets the program dumps.</summary>
    public IEnumerable<ProgramDump> Programs => Messages.OfType<ProgramDump>();

    /// <summary>Gets the first Database Dump, or <see langword="null"/>.</summary>
    public DatabaseDump? Database => Messages.OfType<DatabaseDump>().FirstOrDefault();

    /// <summary>Gets the Parameter Data messages (system dumps).</summary>
    public IEnumerable<ParameterData> Dumps => Messages.OfType<ParameterData>();
}

/// <summary>
/// Reads and writes raw <c>.syx</c> files: concatenated SysEx frames, interoperable with generic SysEx tools
/// (PRG-006, spec 4.7). File contents are untrusted (SEC-002): malformed frames become warnings.
/// </summary>
public static class SyxFile
{
    /// <summary>Largest accepted file. A full backup is about 230 KB.</summary>
    public const int MaxSize = 16 * 1024 * 1024;

    /// <summary>Parses file contents.</summary>
    /// <param name="data">The file bytes.</param>
    /// <exception cref="ArgumentException">The file is larger than <see cref="MaxSize"/>.</exception>
    public static SyxContent Read(ReadOnlySpan<byte> data)
    {
        if (data.Length > MaxSize)
            throw new ArgumentException($"The file is larger than {MaxSize / (1024 * 1024)} MB and is not a SysEx file for the MPX 1.", nameof(data));

        var messages = ImmutableArray.CreateBuilder<Mpx1Message>();
        var warnings = ImmutableArray.CreateBuilder<string>();
        IReadOnlyList<byte[]> frames = SysExFrames.Split(data);
        for (int i = 0; i < frames.Count; i++)
        {
            byte[] frame = frames[i];
            string where = (i + 1).ToString(CultureInfo.InvariantCulture);
            if (!IsMpx1(frame))
            {
                warnings.Add($"Frame {where}: not an MPX 1 frame ({Header(frame)}); ignored.");
                continue;
            }
            if (Mpx1Codec.TryDecode(frame, out Mpx1Message? message, out Mpx1FormatError? error))
                messages.Add(message!);
            else
                warnings.Add($"Frame {where}: {error}; ignored.");
        }
        if (frames.Count == 0)
            warnings.Add("The file contains no SysEx frames.");
        return new SyxContent(messages.ToImmutable(), warnings.ToImmutable());
    }

    /// <summary>Writes messages as file contents.</summary>
    /// <param name="messages">The messages.</param>
    public static byte[] Write(IEnumerable<Mpx1Message> messages) => SysExFrames.Join(messages);

    private static bool IsMpx1(ReadOnlySpan<byte> frame)
        => frame.Length >= 3 && frame[1] == Mpx1Frame.LexiconId && frame[2] == Mpx1Frame.ProductId
            || frame.Length >= 6 && frame[1] == Mpx1Frame.UniversalNonRealTime && frame[3] == 0x06;

    private static string Header(ReadOnlySpan<byte> frame)
        => Convert.ToHexString(frame[..Math.Min(frame.Length, 4)]);
}
