namespace BB84.MPX1.Midi;

/// <summary>Tunable timing of an <see cref="Mpx1Session"/>.</summary>
public sealed record Mpx1SessionOptions
{
    /// <summary>Gets the time to wait for each response attempt (SES-003). Default 1000 ms.</summary>
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromMilliseconds(1000);

    /// <summary>Gets the number of retries after the first attempt times out (SES-003). Default 2.</summary>
    public int Retries { get; init; } = 2;

    /// <summary>Gets the time to wait for READY after the device reported BUSY (SES-005). Default 3000 ms.</summary>
    public TimeSpan ReadyTimeout { get; init; } = TimeSpan.FromMilliseconds(3000);

    /// <summary>Gets the time to wait for a BUSY handshake after a bulk write before assuming the device is ready. Default 100 ms.</summary>
    public TimeSpan BusyGracePeriod { get; init; } = TimeSpan.FromMilliseconds(100);

    /// <summary>Gets the minimum gap between two outgoing SysEx frames (SES-008). Default 0.</summary>
    public TimeSpan InterFrameGap { get; init; } = TimeSpan.Zero;
}
