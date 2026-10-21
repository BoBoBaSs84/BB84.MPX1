using System.Runtime.CompilerServices;
using System.Threading.Channels;

using BB84.MPX1.Midi;

namespace BB84.MPX1.Simulator.Recording;

/// <summary>
/// Plays back a recorded session deterministically (TRN-007): each frame the editor sends must match the next
/// recorded <c>out</c> frame, and releases the recorded <c>in</c> frames that followed it. Timing is ignored.
/// </summary>
public sealed class ReplayMidiTransport : IMidiTransport
{
    /// <summary>The single port the replay transport exposes.</summary>
    public static readonly MidiPortInfo Port = new(new MidiPortId("replay"), "Replay");

    private readonly IReadOnlyList<RecordedFrame> _frames;
    private readonly Lock _lock = new();
    private readonly TimeProvider _time;
    private Channel<MidiInput> _inbox = CreateInbox();
    private int _position;

    /// <summary>Initializes a new instance of the <see cref="ReplayMidiTransport"/> class.</summary>
    /// <param name="frames">The recorded frames in order.</param>
    /// <param name="strict">When <see langword="true"/>, a sent frame that differs from the recording throws.</param>
    /// <param name="time">Time source for timestamps; the system clock when <see langword="null"/>.</param>
    public ReplayMidiTransport(IReadOnlyList<RecordedFrame> frames, bool strict = true, TimeProvider? time = null)
    {
        _frames = frames ?? throw new ArgumentNullException(nameof(frames));
        Strict = strict;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Gets a value indicating whether mismatching sent frames throw.</summary>
    public bool Strict { get; }

    /// <summary>Gets a value indicating whether every recorded frame has been played.</summary>
    public bool IsComplete
    {
        get
        {
            lock (_lock)
                return _position >= _frames.Count;
        }
    }

    /// <inheritdoc/>
    public bool IsOpen { get; private set; }

    /// <inheritdoc/>
    public IReadOnlyList<MidiPortInfo> GetInputPorts() => [Port];

    /// <inheritdoc/>
    public IReadOnlyList<MidiPortInfo> GetOutputPorts() => [Port];

    /// <inheritdoc/>
    public ValueTask OpenAsync(MidiPortId input, MidiPortId output, CancellationToken cancellationToken = default)
    {
        if (IsOpen)
            return ValueTask.CompletedTask;

        _inbox = CreateInbox();
        IsOpen = true;
        lock (_lock)
            ReleaseIncoming();
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask CloseAsync(CancellationToken cancellationToken = default)
    {
        IsOpen = false;
        _inbox.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    /// <exception cref="InvalidOperationException">In strict mode, the frame differs from the recording.</exception>
    public ValueTask SendSysExAsync(ReadOnlyMemory<byte> frame, CancellationToken cancellationToken = default)
    {
        if (!IsOpen)
            throw new InvalidOperationException("The transport is not open.");

        lock (_lock)
        {
            string hex = Convert.ToHexString(frame.Span);
            if (_position < _frames.Count && _frames[_position].Dir == RecordedFrame.Out)
            {
                RecordedFrame expected = _frames[_position];
                if (Strict && !string.Equals(expected.Hex, hex, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Replay mismatch at frame {_position}: expected {expected.Hex}, got {hex}.");
                _position++;
                ReleaseIncoming();
            }
            else if (Strict)
            {
                throw new InvalidOperationException($"Replay has no more outgoing frames; got {hex}.");
            }
        }
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask SendShortAsync(int message, CancellationToken cancellationToken = default)
        => ValueTask.CompletedTask;

    /// <inheritdoc/>
    public async IAsyncEnumerable<MidiInput> ReadAllAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Channel<MidiInput> inbox = _inbox;
        await foreach (MidiInput input in inbox.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            yield return input;
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync() => CloseAsync();

    private void ReleaseIncoming()
    {
        while (_position < _frames.Count && _frames[_position].Dir == RecordedFrame.In)
        {
            _inbox.Writer.TryWrite(new SysExInput(_time.GetUtcNow(), _frames[_position].ToBytes()));
            _position++;
        }
    }

    private static Channel<MidiInput> CreateInbox()
        => Channel.CreateUnbounded<MidiInput>(new UnboundedChannelOptions { SingleReader = true });
}
