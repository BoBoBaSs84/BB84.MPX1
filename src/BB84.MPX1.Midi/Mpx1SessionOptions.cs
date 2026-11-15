namespace BB84.MPX1.Midi;

/// <summary>Tunable timing of an <see cref="Mpx1Session"/>.</summary>
public sealed record Mpx1SessionOptions
{
    /// <summary>
    /// Gets the time to wait for each response attempt (SES-003). Default 2000 ms: about three times the slowest reply
    /// measured on a V1.00 unit (Database Dump, 630 ms, mostly MIDI wire time), leaving room for slower transmit speeds.
    /// </summary>
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromMilliseconds(2000);

    /// <summary>Gets the number of retries after the first attempt times out (SES-003). Default 2.</summary>
    public int Retries { get; init; } = 2;

    /// <summary>Gets the time to wait for READY after the device reported BUSY (SES-005). Default 3000 ms.</summary>
    public TimeSpan ReadyTimeout { get; init; } = TimeSpan.FromMilliseconds(3000);

    /// <summary>
    /// Gets the time to wait for a BUSY handshake after a bulk write before assuming the device is ready. Default 100 ms.
    /// The wait starts once the frame has had time to cross the MIDI cable (see <see cref="GetBusyWait"/>).
    /// </summary>
    public TimeSpan BusyGracePeriod { get; init; } = TimeSpan.FromMilliseconds(100);

    /// <summary>MIDI 1.0 serial rate in bits per second; each byte takes 10 bits (start, 8 data, stop).</summary>
    public const int MidiBaudRate = 31250;

    /// <summary>
    /// Returns how long to wait for BUSY after sending a bulk frame: the frame's time on the MIDI cable plus
    /// <see cref="BusyGracePeriod"/> (SES-005). USB MIDI interfaces report a send as complete long before the
    /// device has received the frame; a V1.00 unit sends BUSY about 330 ms after an 844-byte Program Dump starts.
    /// </summary>
    /// <param name="frameLength">The frame length in bytes.</param>
    public TimeSpan GetBusyWait(int frameLength)
        => TimeSpan.FromSeconds(frameLength * 10.0 / MidiBaudRate) + BusyGracePeriod;

    /// <summary>Gets the minimum gap between two outgoing SysEx frames (SES-008). Default 0.</summary>
    public TimeSpan InterFrameGap { get; init; } = TimeSpan.Zero;
}
