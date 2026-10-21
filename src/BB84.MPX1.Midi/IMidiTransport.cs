namespace BB84.MPX1.Midi;

/// <summary>
/// Stable identifier of a MIDI port. Implementations choose the format; callers treat it as opaque.
/// </summary>
/// <param name="Value">The identifier.</param>
public readonly record struct MidiPortId(string Value)
{
    /// <inheritdoc/>
    public override string ToString() => Value;
}

/// <summary>A MIDI port as shown to the user.</summary>
/// <param name="Id">Stable identifier.</param>
/// <param name="Name">Display name.</param>
public sealed record MidiPortInfo(MidiPortId Id, string Name);

/// <summary>Something received from a MIDI input port.</summary>
/// <param name="Timestamp">Time of receipt.</param>
public abstract record MidiInput(DateTimeOffset Timestamp);

/// <summary>A complete SysEx frame, <c>F0</c> through <c>F7</c>.</summary>
/// <param name="Timestamp">Time of receipt.</param>
/// <param name="Frame">The frame bytes.</param>
public sealed record SysExInput(DateTimeOffset Timestamp, ReadOnlyMemory<byte> Frame) : MidiInput(Timestamp);

/// <summary>A short (channel or system) message packed as in WinMM: status in the low byte.</summary>
/// <param name="Timestamp">Time of receipt.</param>
/// <param name="Message">The packed message.</param>
public sealed record ShortInput(DateTimeOffset Timestamp, int Message) : MidiInput(Timestamp);

/// <summary>
/// The only boundary to operating-system MIDI APIs (TRN-001). Implemented by the WinMM adapter,
/// the simulator, loopback and record/replay transports (TRN-007).
/// </summary>
public interface IMidiTransport : IAsyncDisposable
{
    /// <summary>Gets a value indicating whether ports are open.</summary>
    bool IsOpen { get; }

    /// <summary>Lists the input ports.</summary>
    IReadOnlyList<MidiPortInfo> GetInputPorts();

    /// <summary>Lists the output ports.</summary>
    IReadOnlyList<MidiPortInfo> GetOutputPorts();

    /// <summary>Opens one input and one output port. Calling it again with the same ports does nothing (TRN-006).</summary>
    /// <param name="input">The input port.</param>
    /// <param name="output">The output port.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask OpenAsync(MidiPortId input, MidiPortId output, CancellationToken cancellationToken = default);

    /// <summary>Closes the ports. Safe to call when already closed (TRN-006).</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask CloseAsync(CancellationToken cancellationToken = default);

    /// <summary>Sends one complete SysEx frame, <c>F0</c> through <c>F7</c>.</summary>
    /// <param name="frame">The frame.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask SendSysExAsync(ReadOnlyMemory<byte> frame, CancellationToken cancellationToken = default);

    /// <summary>Sends a packed short message.</summary>
    /// <param name="message">The message, status in the low byte.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask SendShortAsync(int message, CancellationToken cancellationToken = default);

    /// <summary>Reads everything received from the input port until the transport is closed or disposed.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    IAsyncEnumerable<MidiInput> ReadAllAsync(CancellationToken cancellationToken = default);
}
