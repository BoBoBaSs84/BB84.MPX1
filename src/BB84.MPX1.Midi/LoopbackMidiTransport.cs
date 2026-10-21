using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace BB84.MPX1.Midi;

/// <summary>
/// In-memory transport. <see cref="CreatePair"/> returns two connected endpoints: whatever one sends,
/// the other receives. Used for tests and as the wire between the session and the simulator.
/// </summary>
public sealed class LoopbackMidiTransport : IMidiTransport
{
    /// <summary>The single port both endpoints expose.</summary>
    public static readonly MidiPortInfo Port = new(new MidiPortId("loopback"), "Loopback");

    private readonly Channel<MidiInput> _inbox = Channel.CreateUnbounded<MidiInput>(new UnboundedChannelOptions { SingleReader = true });
    private readonly TimeProvider _time;
    private LoopbackMidiTransport? _peer;
    private bool _disposed;

    private LoopbackMidiTransport(TimeProvider time) => _time = time;

    /// <summary>Creates two connected endpoints.</summary>
    /// <param name="time">Time source for timestamps; defaults to the system clock.</param>
    public static (LoopbackMidiTransport Left, LoopbackMidiTransport Right) CreatePair(TimeProvider? time = null)
    {
        LoopbackMidiTransport left = new(time ?? TimeProvider.System);
        LoopbackMidiTransport right = new(time ?? TimeProvider.System);
        left._peer = right;
        right._peer = left;
        return (left, right);
    }

    /// <inheritdoc/>
    public bool IsOpen { get; private set; }

    /// <summary>Raised for every frame this endpoint sends (for monitors and tests).</summary>
    public event EventHandler<SysExInput>? SysExSent;

    /// <inheritdoc/>
    public IReadOnlyList<MidiPortInfo> GetInputPorts() => [Port];

    /// <inheritdoc/>
    public IReadOnlyList<MidiPortInfo> GetOutputPorts() => [Port];

    /// <inheritdoc/>
    public ValueTask OpenAsync(MidiPortId input, MidiPortId output, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        IsOpen = true;
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask CloseAsync(CancellationToken cancellationToken = default)
    {
        IsOpen = false;
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask SendSysExAsync(ReadOnlyMemory<byte> frame, CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        SysExInput input = new(_time.GetUtcNow(), frame.ToArray());
        SysExSent?.Invoke(this, input);
        _peer!._inbox.Writer.TryWrite(input);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask SendShortAsync(int message, CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        _peer!._inbox.Writer.TryWrite(new ShortInput(_time.GetUtcNow(), message));
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<MidiInput> ReadAllAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (MidiInput input in _inbox.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            yield return input;
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        _disposed = true;
        IsOpen = false;
        _inbox.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }

    private void EnsureOpen()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsOpen)
            throw new InvalidOperationException("The transport is not open.");
    }
}
