namespace BB84.MPX1.Core.Protocol;

/// <summary>
/// Splits raw SysEx data (for example, the contents of a <c>.syx</c> file) into frames.
/// </summary>
public static class SysExFrames
{
    /// <summary>Returns every complete <c>F0</c> … <c>F7</c> frame; bytes outside frames are skipped.</summary>
    /// <param name="data">The raw data.</param>
    public static IReadOnlyList<byte[]> Split(ReadOnlySpan<byte> data)
    {
        List<byte[]> frames = [];
        int start = -1;
        for (int i = 0; i < data.Length; i++)
        {
            if (data[i] == Mpx1Frame.SysExStart)
                start = i;
            else if (data[i] == Mpx1Frame.SysExEnd && start >= 0)
            {
                frames.Add(data[start..(i + 1)].ToArray());
                start = -1;
            }
        }
        return frames;
    }

    /// <summary>Concatenates messages into raw SysEx data.</summary>
    /// <param name="messages">The messages.</param>
    public static byte[] Join(IEnumerable<Mpx1Message> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);
        using MemoryStream stream = new();
        foreach (Mpx1Message message in messages)
            stream.Write(message.ToBytes());
        return stream.ToArray();
    }
}
