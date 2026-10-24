using System.Globalization;
using System.Text;

using BB84.MPX1.App.Services;
using BB84.MPX1.Midi;

namespace BB84.MPX1.App.ViewModels;

/// <summary>One line of the MIDI monitor.</summary>
/// <param name="Time">Local time of the frame.</param>
/// <param name="Direction">Arrow: sent or received.</param>
/// <param name="Hex">The frame as hex, shortened for long frames.</param>
/// <param name="Summary">Decoded summary.</param>
/// <param name="IsError">Whether the frame failed to decode.</param>
public sealed record MonitorEntry(string Time, string Direction, string Hex, string Summary, bool IsError)
{
    /// <summary>Longest frame shown in full.</summary>
    public const int MaxHexBytes = 40;

    /// <summary>Creates an entry from session traffic.</summary>
    /// <param name="frame">The traffic event.</param>
    public static MonitorEntry From(FrameEventArgs frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ReadOnlySpan<byte> bytes = frame.Frame.Span;
        StringBuilder hex = new(MaxHexBytes * 3);
        int shown = Math.Min(bytes.Length, MaxHexBytes);
        for (int i = 0; i < shown; i++)
        {
            if (i > 0)
                hex.Append(' ');
            hex.Append(bytes[i].ToString("X2", CultureInfo.InvariantCulture));
        }
        if (bytes.Length > shown)
            hex.Append(CultureInfo.InvariantCulture, $" … ({bytes.Length} bytes)");

        return new MonitorEntry(
            frame.Timestamp.ToLocalTime().ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture),
            frame.Direction == FrameDirection.Sent ? "→" : "←",
            hex.ToString(),
            MessageSummary.Describe(frame.Message, frame.Error),
            frame.Message is null);
    }
}
