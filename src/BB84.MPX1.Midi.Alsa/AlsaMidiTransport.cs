using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Channels;

using static BB84.MPX1.Midi.Alsa.NativeMethods;

namespace BB84.MPX1.Midi.Alsa;

/// <summary>
/// <see cref="IMidiTransport"/> over the ALSA sequencer API (TRN-008).
/// </summary>
/// <remarks>
/// The transport is one sequencer client, <c>BB84.MPX1</c>, with one input and one output port that it subscribes to the
/// chosen ports. Port IDs are <c>client name:port name</c>, with <c> (2)</c>, <c> (3)</c> … appended to duplicates, so they
/// stay stable when client numbers change. SysEx goes out in chunks of <see cref="SysExChunkSize"/> bytes and is
/// reassembled on the way in. MIDI clock (<c>F8</c>) and active sensing (<c>FE</c>) are dropped.
/// </remarks>
public sealed class AlsaMidiTransport : IMidiTransport, IDisposable
{
    /// <summary>Largest SysEx event sent to the sequencer; small enough for its default buffers.</summary>
    internal const int SysExChunkSize = 256;

    private const string ClientName = "BB84.MPX1";
    private const int ShortMessageBufferSize = 16;

    private readonly TimeProvider _time;
    private readonly SysExAssembler _assembler = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly Lock _openLock = new();

    private Channel<MidiInput> _inbox = CreateInbox();
    private nint _seq;
    private int _clientId;
    private int _inPort;
    private int _outPort;
    private nint _encoder;
    private nint _decoder;
    private Thread? _reader;
    private int _wakeFd = -1;
    private (int Client, int Port)? _source;
    private (int Client, int Port)? _destination;
    private MidiPortId? _openInput;
    private MidiPortId? _openOutput;
    private volatile bool _open;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="AlsaMidiTransport"/> class.</summary>
    /// <param name="time">Time source for timestamps and send timeouts; the system clock when <see langword="null"/>.</param>
    /// <remarks>The sequencer client is opened on first use, so constructing the transport never fails.</remarks>
    public AlsaMidiTransport(TimeProvider? time = null)
        => _time = time ?? TimeProvider.System;

    /// <inheritdoc/>
    public bool IsOpen => _open;

    /// <inheritdoc/>
    /// <exception cref="AlsaException">The ALSA sequencer is not available.</exception>
    public IReadOnlyList<MidiPortInfo> GetInputPorts() => ListPorts(input: true).Ports;

    /// <inheritdoc/>
    /// <exception cref="AlsaException">The ALSA sequencer is not available.</exception>
    public IReadOnlyList<MidiPortInfo> GetOutputPorts() => ListPorts(input: false).Ports;

    /// <summary>Builds the port list from client and port names in enumeration order (TRN-006).</summary>
    /// <param name="names">Client and port names.</param>
    internal static IReadOnlyList<MidiPortInfo> ToPorts(IEnumerable<(string Client, string Port)> names)
        => MidiPortNames.MakeUnique([.. names.Select(n => $"{n.Client}:{n.Port}")]);

    /// <summary>Splits a SysEx frame into the pieces sent as separate sequencer events.</summary>
    /// <param name="frame">The frame.</param>
    /// <param name="size">The largest piece.</param>
    internal static IEnumerable<ReadOnlyMemory<byte>> Chunk(ReadOnlyMemory<byte> frame, int size)
    {
        for (int offset = 0; offset < frame.Length; offset += size)
            yield return frame.Slice(offset, Math.Min(size, frame.Length - offset));
    }

    /// <inheritdoc/>
    /// <exception cref="AlsaException">The sequencer is not available, a port was not found, or a subscription failed.</exception>
    public ValueTask OpenAsync(MidiPortId input, MidiPortId output, CancellationToken cancellationToken = default)
    {
        lock (_openLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_open && _openInput == input && _openOutput == output)
                return ValueTask.CompletedTask;

            CloseCore();
            (int Client, int Port) source = Resolve(input, isInput: true);
            (int Client, int Port) destination = Resolve(output, isInput: false);

            _inbox = CreateInbox();
            _assembler.Reset();
            try
            {
                Check(snd_seq_connect_from(_seq, _inPort, source.Client, source.Port), "snd_seq_connect_from");
                _source = source;
                Check(snd_seq_connect_to(_seq, _outPort, destination.Client, destination.Port), "snd_seq_connect_to");
                _destination = destination;
                StartReader();

                _openInput = input;
                _openOutput = output;
                _open = true;
            }
            catch
            {
                CloseCore();
                throw;
            }
        }
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask CloseAsync(CancellationToken cancellationToken = default)
    {
        lock (_openLock)
            CloseCore();
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public async ValueTask SendSysExAsync(ReadOnlyMemory<byte> frame, CancellationToken cancellationToken = default)
    {
        if (frame.Length < 2 || frame.Span[0] != 0xF0 || frame.Span[^1] != 0xF7)
            throw new ArgumentException("A SysEx frame must start with F0 and end with F7.", nameof(frame));

        await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            nint seq = RequireOpen();

            // 31250 baud ≈ 3125 bytes/s; allow twice the wire time plus one second, as the WinMM adapter does.
            DateTimeOffset deadline = _time.GetUtcNow() + TimeSpan.FromSeconds(1 + (frame.Length * 2.0 / 3125));
            foreach (ReadOnlyMemory<byte> chunk in Chunk(frame, SysExChunkSize))
            {
                // A full kernel pool answers -EAGAIN (the client is non-blocking); wait for it to drain.
                while (!TryOutputSysEx(seq, chunk.Span))
                {
                    if (_time.GetUtcNow() >= deadline)
                        throw new AlsaException("Timed out while sending SysEx.");
                    await Task.Delay(TimeSpan.FromMilliseconds(2), _time, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            ReleaseSendLock();
        }
    }

    /// <inheritdoc/>
    public async ValueTask SendShortAsync(int message, CancellationToken cancellationToken = default)
    {
        await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            OutputShort(RequireOpen(), message);
        }
        finally
        {
            ReleaseSendLock();
        }
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<MidiInput> ReadAllAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Channel<MidiInput> inbox = _inbox;
        await foreach (MidiInput input in inbox.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            yield return input;
    }

    /// <summary>Closes the ports and the sequencer client. Safe to call more than once.</summary>
    public void Dispose()
    {
        lock (_openLock)
        {
            if (_disposed)
                return;
            _disposed = true;
            CloseCore();

            // A send still running uses the client; leaking it is safer than closing it under the sender.
            if (_sendLock.Wait(TimeSpan.FromSeconds(5)))
                CloseClient();
        }
        _sendLock.Dispose();
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    /// <summary>Handles one received SysEx event: a frame or a piece of one (TRN-004).</summary>
    /// <param name="chunk">The event's bytes.</param>
    internal void OnSysEx(ReadOnlySpan<byte> chunk)
    {
        IReadOnlyList<byte[]> frames = _assembler.Feed(chunk);
        DateTimeOffset now = _time.GetUtcNow();
        foreach (byte[] frame in frames)
            _inbox.Writer.TryWrite(new SysExInput(now, frame));
    }

    /// <summary>Handles one received short message as raw MIDI bytes (TRN-005).</summary>
    /// <param name="message">Status byte and data bytes.</param>
    internal void OnShort(ReadOnlySpan<byte> message)
    {
        if (message.IsEmpty || message[0] is 0xF8 or 0xFE)
            return;
        int packed = message[0];
        for (int i = 1; i < message.Length && i < 3; i++)
            packed |= message[i] << (8 * i);
        _inbox.Writer.TryWrite(new ShortInput(_time.GetUtcNow(), packed));
    }

    /// <summary>Opens the sequencer client and its two ports on first use.</summary>
    private unsafe void EnsureClient()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_seq != 0)
            return;

        nint seq;
        int result = snd_seq_open(&seq, "default", SND_SEQ_OPEN_DUPLEX, SND_SEQ_NONBLOCK);
        if (result < 0)
        {
            throw new AlsaException(
                $"The ALSA sequencer is not available ({ErrorText(result)}). Load the snd-seq kernel module and check access to /dev/snd/seq.",
                result);
        }

        _seq = seq;
        try
        {
            Check(snd_seq_set_client_name(seq, ClientName), "snd_seq_set_client_name");
            _clientId = Check(snd_seq_client_id(seq), "snd_seq_client_id");
            _inPort = Check(snd_seq_create_simple_port(seq, "MPX 1 In", SND_SEQ_PORT_CAP_WRITE | SND_SEQ_PORT_CAP_SUBS_WRITE,
                SND_SEQ_PORT_TYPE_MIDI_GENERIC | SND_SEQ_PORT_TYPE_APPLICATION), "snd_seq_create_simple_port");
            _outPort = Check(snd_seq_create_simple_port(seq, "MPX 1 Out", SND_SEQ_PORT_CAP_READ | SND_SEQ_PORT_CAP_SUBS_READ,
                SND_SEQ_PORT_TYPE_MIDI_GENERIC | SND_SEQ_PORT_TYPE_APPLICATION), "snd_seq_create_simple_port");

            nint parser;
            Check(snd_midi_event_new(ShortMessageBufferSize, &parser), "snd_midi_event_new");
            _encoder = parser;
            Check(snd_midi_event_new(ShortMessageBufferSize, &parser), "snd_midi_event_new");
            _decoder = parser;
            snd_midi_event_no_status(_decoder, 1);
        }
        catch
        {
            CloseClient();
            throw;
        }
    }

    private void CloseClient()
    {
        if (_encoder != 0)
            snd_midi_event_free(_encoder);
        if (_decoder != 0)
            snd_midi_event_free(_decoder);
        if (_seq != 0)
            _ = snd_seq_close(_seq);
        _encoder = 0;
        _decoder = 0;
        _seq = 0;
    }

    private (IReadOnlyList<MidiPortInfo> Ports, List<(int Client, int Port)> Addresses) ListPorts(bool input)
    {
        lock (_openLock)
        {
            EnsureClient();
            List<(string Client, string Port, int ClientId, int PortId)> found = Enumerate(input
                ? SND_SEQ_PORT_CAP_READ | SND_SEQ_PORT_CAP_SUBS_READ
                : SND_SEQ_PORT_CAP_WRITE | SND_SEQ_PORT_CAP_SUBS_WRITE);
            return (ToPorts(found.Select(f => (f.Client, f.Port))), [.. found.Select(f => (f.ClientId, f.PortId))]);
        }
    }

    /// <summary>Lists the ports of other clients that have every capability in <paramref name="capabilities"/>.</summary>
    private unsafe List<(string Client, string Port, int ClientId, int PortId)> Enumerate(uint capabilities)
    {
        List<(string, string, int, int)> result = [];
        nint client = 0;
        nint port = 0;
        try
        {
            Check(snd_seq_client_info_malloc(&client), "snd_seq_client_info_malloc");
            Check(snd_seq_port_info_malloc(&port), "snd_seq_port_info_malloc");
            snd_seq_client_info_set_client(client, -1);
            while (snd_seq_query_next_client(_seq, client) >= 0)
            {
                int clientId = snd_seq_client_info_get_client(client);
                if (clientId == SND_SEQ_CLIENT_SYSTEM || clientId == _clientId)
                    continue;
                string clientName = Marshal.PtrToStringUTF8(snd_seq_client_info_get_name(client)) ?? $"Client {clientId}";

                snd_seq_port_info_set_client(port, clientId);
                snd_seq_port_info_set_port(port, -1);
                while (snd_seq_query_next_port(_seq, port) >= 0)
                {
                    uint caps = snd_seq_port_info_get_capability(port);
                    if ((caps & capabilities) != capabilities || (caps & SND_SEQ_PORT_CAP_NO_EXPORT) != 0)
                        continue;
                    int portId = snd_seq_port_info_get_port(port);
                    string portName = Marshal.PtrToStringUTF8(snd_seq_port_info_get_name(port)) ?? $"Port {portId}";
                    result.Add((clientName, portName, clientId, portId));
                }
            }
        }
        finally
        {
            if (client != 0)
                snd_seq_client_info_free(client);
            if (port != 0)
                snd_seq_port_info_free(port);
        }
        return result;
    }

    private (int Client, int Port) Resolve(MidiPortId id, bool isInput)
    {
        (IReadOnlyList<MidiPortInfo> ports, List<(int Client, int Port)> addresses) = ListPorts(isInput);
        for (int i = 0; i < ports.Count; i++)
        {
            if (ports[i].Id == id)
                return addresses[i];
        }
        throw new AlsaException($"MIDI {(isInput ? "input" : "output")} port '{id}' not found.");
    }

    private unsafe void StartReader()
    {
        int wake = eventfd(0, EFD_CLOEXEC);
        if (wake < 0)
            throw new AlsaException($"eventfd failed with errno {Marshal.GetLastPInvokeError()}.");
        _wakeFd = wake;

        int count = Check(snd_seq_poll_descriptors_count(_seq, POLLIN), "snd_seq_poll_descriptors_count");
        PollFd[] fds = new PollFd[count + 1];
        int filled;
        fixed (PollFd* first = fds)
            filled = Check(snd_seq_poll_descriptors(_seq, first, (uint)count, POLLIN), "snd_seq_poll_descriptors");
        // Never poll an unfilled entry: its zero descriptor would be stdin.
        if (filled < count)
            Array.Resize(ref fds, filled + 1);
        fds[^1] = new PollFd { Fd = wake, Events = POLLIN };

        nint seq = _seq;
        _reader = new Thread(() => ReadLoop(seq, fds)) { IsBackground = true, Name = "ALSA MIDI input" };
        _reader.Start();
    }

    /// <summary>Waits for sequencer input until the wake descriptor (the last one) signals a close.</summary>
    private unsafe void ReadLoop(nint seq, PollFd[] fds)
    {
        fixed (PollFd* first = fds)
        {
            while (true)
            {
                for (int i = 0; i < fds.Length; i++)
                    first[i].Revents = 0;
                if (poll(first, (nuint)fds.Length, -1) < 0)
                {
                    if (Marshal.GetLastPInvokeError() == EINTR)
                        continue;
                    return;
                }
                if (first[fds.Length - 1].Revents != 0)
                    return;
                Drain(seq);
            }
        }
    }

    private unsafe void Drain(nint seq)
    {
        while (true)
        {
            SndSeqEvent* ev;
            int result = snd_seq_event_input(seq, &ev);
            if (result == -ENOSPC)
            {
                // Input overrun: the kernel dropped events, so a frame in progress is incomplete.
                _assembler.Reset();
                continue;
            }
            if (result < 0 || ev == null)
                return;
            OnEvent(ev);
        }
    }

    private unsafe void OnEvent(SndSeqEvent* ev)
    {
        switch (ev->Type)
        {
            case SND_SEQ_EVENT_SYSEX:
                OnSysEx(new ReadOnlySpan<byte>((void*)ev->ExtPointer, (int)ev->ExtLength));
                break;
            case SND_SEQ_EVENT_CLOCK:
            case SND_SEQ_EVENT_SENSING:
                break;
            default:
                byte* buffer = stackalloc byte[ShortMessageBufferSize];
                // Negative for events that are not MIDI messages (subscriptions, client announcements).
                nint length = snd_midi_event_decode(_decoder, buffer, ShortMessageBufferSize, ev);
                if (length > 0)
                    OnShort(new ReadOnlySpan<byte>(buffer, (int)length));
                break;
        }
    }

    private unsafe bool TryOutputSysEx(nint seq, ReadOnlySpan<byte> chunk)
    {
        fixed (byte* data = chunk)
        {
            SndSeqEvent ev = default;
            ev.Type = SND_SEQ_EVENT_SYSEX;
            ev.Flags = SND_SEQ_EVENT_LENGTH_VARIABLE;
            ev.ExtLength = (uint)chunk.Length;
            ev.ExtPointer = (nint)data;
            Address(ref ev);
            int result = snd_seq_event_output_direct(seq, &ev);
            if (result == -EAGAIN)
                return false;
            Check(result, "snd_seq_event_output_direct");
            return true;
        }
    }

    private unsafe void OutputShort(nint seq, int message)
    {
        byte* bytes = stackalloc byte[3];
        bytes[0] = (byte)message;
        bytes[1] = (byte)(message >> 8);
        bytes[2] = (byte)(message >> 16);

        SndSeqEvent ev = default;
        snd_midi_event_reset_encode(_encoder);
        if (snd_midi_event_encode(_encoder, bytes, 3, &ev) <= 0 || ev.Type == SND_SEQ_EVENT_NONE)
            throw new ArgumentException($"0x{message:X6} is not a complete MIDI short message.", nameof(message));
        Address(ref ev);
        Check(snd_seq_event_output_direct(seq, &ev), "snd_seq_event_output_direct");
    }

    /// <summary>Sends an event from the output port to its subscribers, bypassing the queues.</summary>
    private void Address(ref SndSeqEvent ev)
    {
        ev.Queue = SND_SEQ_QUEUE_DIRECT;
        ev.SourcePort = (byte)_outPort;
        ev.DestClient = SND_SEQ_ADDRESS_SUBSCRIBERS;
        ev.DestPort = SND_SEQ_ADDRESS_UNKNOWN;
    }

    private unsafe void CloseCore()
    {
        _open = false;
        if (_wakeFd >= 0)
        {
            ulong one = 1;
            _ = write(_wakeFd, &one, sizeof(ulong));
            // A reader that does not stop still polls the descriptor; leaking it is safer than closing it under the reader.
            if (_reader?.Join(TimeSpan.FromSeconds(2)) ?? true)
                _ = close(_wakeFd);
            _wakeFd = -1;
        }
        _reader = null;

        if (_seq != 0)
        {
            if (_source is { } source)
                _ = snd_seq_disconnect_from(_seq, _inPort, source.Client, source.Port);
            if (_destination is { } destination)
                _ = snd_seq_disconnect_to(_seq, _outPort, destination.Client, destination.Port);
        }
        _source = null;
        _destination = null;
        _openInput = null;
        _openOutput = null;
        _inbox.Writer.TryComplete();
    }

    private nint RequireOpen()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        nint seq = _seq;
        return _open && seq != 0 ? seq : throw new InvalidOperationException("The transport is not open.");
    }

    private void ReleaseSendLock()
    {
        try
        {
            _sendLock.Release();
        }
        catch (ObjectDisposedException)
        {
            // The transport was disposed while the send was in flight.
        }
    }

    private static Channel<MidiInput> CreateInbox()
        => Channel.CreateUnbounded<MidiInput>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });

    private static int Check(int result, string function)
        => result >= 0 ? result : throw new AlsaException($"{function} failed: {ErrorText(result)}", result);
}
