using BB84.MPX1.Core.Protocol;

namespace BB84.MPX1.Simulator;

/// <summary>Configuration of an <see cref="Mpx1Simulator"/>.</summary>
public sealed record SimulatorOptions
{
    /// <summary>Gets the simulated device ID. Default 0.</summary>
    public DeviceId Device { get; init; } = new(0);

    /// <summary>Gets the delay before each response. Default 0.</summary>
    public TimeSpan ResponseLatency { get; init; } = TimeSpan.Zero;

    /// <summary>Gets how long the device stays BUSY after a bulk write (SIM-003). Default 50 ms.</summary>
    public TimeSpan BusyDuration { get; init; } = TimeSpan.FromMilliseconds(50);
}

/// <summary>
/// Fault injection switches (SIM-004). Counters are decremented as faults are applied; set them at any time.
/// </summary>
public sealed class SimulatorFaults
{
    private int _dropResponses;
    private int _corruptResponses;

    /// <summary>Gets or sets the number of upcoming responses to drop silently.</summary>
    public int DropResponses
    {
        get => Volatile.Read(ref _dropResponses);
        set => Volatile.Write(ref _dropResponses, value);
    }

    /// <summary>Gets or sets the number of upcoming responses to corrupt (an invalid nibble is inserted).</summary>
    public int CorruptResponses
    {
        get => Volatile.Read(ref _corruptResponses);
        set => Volatile.Write(ref _corruptResponses, value);
    }

    /// <summary>Gets or sets a value indicating whether READY is never sent after BUSY.</summary>
    public bool SuppressReady { get; set; }

    /// <summary>Gets or sets an extra delay added to every response.</summary>
    public TimeSpan ExtraLatency { get; set; }

    internal bool TryConsumeDrop() => TryConsume(ref _dropResponses);

    internal bool TryConsumeCorrupt() => TryConsume(ref _corruptResponses);

    private static bool TryConsume(ref int counter)
    {
        while (true)
        {
            int current = Volatile.Read(ref counter);
            if (current <= 0)
                return false;
            if (Interlocked.CompareExchange(ref counter, current - 1, current) == current)
                return true;
        }
    }
}
