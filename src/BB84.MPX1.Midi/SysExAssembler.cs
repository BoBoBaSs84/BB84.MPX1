namespace BB84.MPX1.Midi;

/// <summary>
/// Reassembles SysEx frames that arrive split across several driver buffers (TRN-004).
/// Bytes outside <c>F0</c> … <c>F7</c> are discarded; a new <c>F0</c> restarts the frame.
/// </summary>
public sealed class SysExAssembler
{
    /// <summary>Largest accepted frame (SEC-001).</summary>
    public const int MaxFrameLength = 64 * 1024;

    private readonly List<byte> _buffer = new(1024);
    private bool _inFrame;

    /// <summary>Gets the number of frames dropped because they exceeded <see cref="MaxFrameLength"/>.</summary>
    public int DroppedFrames { get; private set; }

    /// <summary>Feeds a chunk of received bytes and returns every frame it completes.</summary>
    /// <param name="chunk">The received bytes.</param>
    public IReadOnlyList<byte[]> Feed(ReadOnlySpan<byte> chunk)
    {
        List<byte[]>? frames = null;
        foreach (byte value in chunk)
        {
            if (value == 0xF0)
            {
                _buffer.Clear();
                _buffer.Add(value);
                _inFrame = true;
                continue;
            }

            if (!_inFrame)
                continue;

            // Real-time messages (F8-FF) may be interleaved with SysEx data; they are not part of the frame.
            if (value >= 0xF8)
                continue;

            _buffer.Add(value);

            if (value == 0xF7)
            {
                (frames ??= []).Add([.. _buffer]);
                Reset();
            }
            else if (value > 0x7F)
            {
                // Any other status byte aborts the frame.
                Reset();
            }
            else if (_buffer.Count > MaxFrameLength)
            {
                DroppedFrames++;
                Reset();
            }
        }
        return frames ?? (IReadOnlyList<byte[]>)[];
    }

    /// <summary>Discards a partially received frame.</summary>
    public void Reset()
    {
        _buffer.Clear();
        _inFrame = false;
    }
}
