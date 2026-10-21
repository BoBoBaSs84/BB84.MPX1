using System.Runtime.CompilerServices;

using BB84.MPX1.Midi;

namespace BB84.MPX1.Simulator.Recording;

/// <summary>
/// Wraps another transport and writes every SysEx frame in both directions as JSON lines (TRN-007, spec 4.7).
/// The writer is not disposed by this transport.
/// </summary>
/// <param name="inner">The transport that does the actual I/O.</param>
/// <param name="writer">Destination of the JSON lines.</param>
/// <param name="time">Time source; the system clock when <see langword="null"/>.</param>
public sealed class RecordingMidiTransport(IMidiTransport inner, TextWriter writer, TimeProvider? time = null) : IMidiTransport
{
    private readonly IMidiTransport _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly TextWriter _writer = writer ?? throw new ArgumentNullException(nameof(writer));
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly long _start = (time ?? TimeProvider.System).GetTimestamp();
    private readonly Lock _writeLock = new();

    /// <inheritdoc/>
    public bool IsOpen => _inner.IsOpen;

    /// <inheritdoc/>
    public IReadOnlyList<MidiPortInfo> GetInputPorts() => _inner.GetInputPorts();

    /// <inheritdoc/>
    public IReadOnlyList<MidiPortInfo> GetOutputPorts() => _inner.GetOutputPorts();

    /// <inheritdoc/>
    public ValueTask OpenAsync(MidiPortId input, MidiPortId output, CancellationToken cancellationToken = default)
        => _inner.OpenAsync(input, output, cancellationToken);

    /// <inheritdoc/>
    public ValueTask CloseAsync(CancellationToken cancellationToken = default)
        => _inner.CloseAsync(cancellationToken);

    /// <inheritdoc/>
    public ValueTask SendSysExAsync(ReadOnlyMemory<byte> frame, CancellationToken cancellationToken = default)
    {
        Write(RecordedFrame.Out, frame.Span);
        return _inner.SendSysExAsync(frame, cancellationToken);
    }

    /// <inheritdoc/>
    public ValueTask SendShortAsync(int message, CancellationToken cancellationToken = default)
        => _inner.SendShortAsync(message, cancellationToken);

    /// <inheritdoc/>
    public async IAsyncEnumerable<MidiInput> ReadAllAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (MidiInput input in _inner.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            if (input is SysExInput sysEx)
                Write(RecordedFrame.In, sysEx.Frame.Span);
            yield return input;
        }
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        lock (_writeLock)
            _writer.Flush();
        return _inner.DisposeAsync();
    }

    private void Write(string direction, ReadOnlySpan<byte> frame)
    {
        RecordedFrame record = new((long)_time.GetElapsedTime(_start).TotalMilliseconds, direction, Convert.ToHexString(frame));
        lock (_writeLock)
            _writer.WriteLine(record.ToJsonLine());
    }
}
