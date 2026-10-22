using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Channels;

using static BB84.MPX1.Midi.WinMM.NativeMethods;

namespace BB84.MPX1.Midi.WinMM;

/// <summary>
/// <see cref="IMidiTransport"/> over the Windows Multimedia MIDI API (TRN-002 to TRN-006).
/// </summary>
/// <remarks>
/// Port IDs are the device names, with <c> (2)</c>, <c> (3)</c> … appended to duplicates, so they stay
/// stable when device indices shift. MIDI clock (<c>F8</c>) and active sensing (<c>FE</c>) are dropped.
/// </remarks>
public sealed class WinMMMidiTransport : IMidiTransport, IDisposable
{
    private const int InputBufferCount = 8;
    private const int InputBufferSize = 4096;
    private static readonly uint HeaderSize = GetHeaderSize();

    private readonly TimeProvider _time;
    private readonly Lock _assemblerLock = new();
    private readonly SysExAssembler _assembler = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly Lock _openLock = new();

    private Channel<MidiInput> _inbox = CreateInbox();
    private Channel<nint>? _requeue;
    private Task? _requeueWorker;
    private GCHandle _self;
    private nint _inHandle;
    private nint _outHandle;
    private nint[] _inputHeaders = [];
    private MidiPortId? _openInput;
    private MidiPortId? _openOutput;
    private volatile bool _closing;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="WinMMMidiTransport"/> class.</summary>
    /// <param name="time">Time source for timestamps; the system clock when <see langword="null"/>.</param>
    public WinMMMidiTransport(TimeProvider? time = null)
        => _time = time ?? TimeProvider.System;

    /// <inheritdoc/>
    public bool IsOpen => _inHandle != 0 && _outHandle != 0;

    /// <inheritdoc/>
    public unsafe IReadOnlyList<MidiPortInfo> GetInputPorts()
    {
        uint count = midiInGetNumDevs();
        List<string> names = new((int)count);
        for (uint i = 0; i < count; i++)
        {
            MidiInCaps caps;
            names.Add(midiInGetDevCapsW(i, &caps, (uint)sizeof(MidiInCaps)) == MMSYSERR_NOERROR
                ? new string(caps.szPname).TrimEnd('\0')
                : $"MIDI In {i}");
        }
        return ToPorts(names);
    }

    /// <inheritdoc/>
    public unsafe IReadOnlyList<MidiPortInfo> GetOutputPorts()
    {
        uint count = midiOutGetNumDevs();
        List<string> names = new((int)count);
        for (uint i = 0; i < count; i++)
        {
            MidiOutCaps caps;
            names.Add(midiOutGetDevCapsW(i, &caps, (uint)sizeof(MidiOutCaps)) == MMSYSERR_NOERROR
                ? new string(caps.szPname).TrimEnd('\0')
                : $"MIDI Out {i}");
        }
        return ToPorts(names);
    }

    /// <summary>Makes port names unique by appending a counter to duplicates (TRN-006).</summary>
    /// <param name="names">Device names in index order.</param>
    internal static IReadOnlyList<MidiPortInfo> ToPorts(IReadOnlyList<string> names)
    {
        Dictionary<string, int> seen = new(StringComparer.Ordinal);
        List<MidiPortInfo> ports = new(names.Count);
        foreach (string name in names)
        {
            int n = seen.TryGetValue(name, out int previous) ? previous + 1 : 1;
            seen[name] = n;
            string id = n == 1 ? name : $"{name} ({n})";
            ports.Add(new MidiPortInfo(new MidiPortId(id), id));
        }
        return ports;
    }

    /// <inheritdoc/>
    public ValueTask OpenAsync(MidiPortId input, MidiPortId output, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_openLock)
        {
            if (IsOpen && _openInput == input && _openOutput == output)
                return ValueTask.CompletedTask;

            CloseCore();

            uint inIndex = IndexOf(GetInputPorts(), input, "input");
            uint outIndex = IndexOf(GetOutputPorts(), output, "output");

            _closing = false;
            _inbox = CreateInbox();
            _assembler.Reset();
            _self = GCHandle.Alloc(this);

            try
            {
                OpenInput(inIndex);
                OpenOutput(outIndex);

                _openInput = input;
                _openOutput = output;
            }
            catch
            {
                CloseCore();
                throw;
            }
        }
        return ValueTask.CompletedTask;
    }

    private unsafe void OpenInput(uint index)
    {
        nint handle;
        delegate* unmanaged<nint, uint, nint, nint, nint, void> callback = &MidiInProc;
        Check(midiInOpen(&handle, index, (nint)callback, GCHandle.ToIntPtr(_self), CALLBACK_FUNCTION), "midiInOpen", input: true);
        _inHandle = handle;

        _requeue = Channel.CreateUnbounded<nint>(new UnboundedChannelOptions { SingleReader = true });
        _requeueWorker = Task.Run(RequeueLoopAsync, CancellationToken.None);

        AllocateInputBuffers();
        Check(midiInStart(_inHandle), "midiInStart", input: true);
    }

    private unsafe void AllocateInputBuffers()
    {
        _inputHeaders = new nint[InputBufferCount];
        for (int i = 0; i < InputBufferCount; i++)
        {
            MidiHdr* header = (MidiHdr*)NativeMemory.AllocZeroed((nuint)sizeof(MidiHdr));
            header->lpData = (nint)NativeMemory.AllocZeroed(InputBufferSize);
            header->dwBufferLength = InputBufferSize;
            _inputHeaders[i] = (nint)header;
            Check(midiInPrepareHeader(_inHandle, header, HeaderSize), "midiInPrepareHeader", input: true);
            Check(midiInAddBuffer(_inHandle, header, HeaderSize), "midiInAddBuffer", input: true);
        }
    }

    private unsafe void OpenOutput(uint index)
    {
        nint handle;
        Check(midiOutOpen(&handle, index, 0, 0, CALLBACK_NULL), "midiOutOpen", input: false);
        _outHandle = handle;
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
        nint handle = 0;
        nint header = 0;
        try
        {
            handle = RequireOutput();
            header = AllocOutputHeader(frame.Span);
            PrepareAndSend(handle, header);

            // 31250 baud ≈ 3125 bytes/s; allow twice the wire time plus one second.
            TimeSpan timeout = TimeSpan.FromSeconds(1 + (frame.Length * 2.0 / 3125));
            long start = _time.GetTimestamp();
            while (!IsDone(header))
            {
                if (_time.GetElapsedTime(start) > timeout)
                    throw new WinMMException("Timed out while sending SysEx.");
                await Task.Delay(1, cancellationToken).ConfigureAwait(false);
            }

            Unprepare(handle, header);
        }
        finally
        {
            ReleaseOutputHeader(handle, header);
            ReleaseSendLock();
        }
    }

    /// <summary>
    /// Takes a header back from the driver and frees it. A header the driver still owns (cancelled or timed-out send)
    /// is returned by a reset first, because freeing memory the driver is reading from is undefined behavior.
    /// </summary>
    private static unsafe void ReleaseOutputHeader(nint handle, nint header)
    {
        if (header == 0)
            return;
        if (handle != 0)
        {
            if (!IsDone(header))
                _ = midiOutReset(handle);
            // Harmless when the header was never prepared or is already unprepared.
            _ = midiOutUnprepareHeader(handle, (MidiHdr*)header, HeaderSize);
        }
        FreeHeader(header);
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

    private static unsafe uint GetHeaderSize() => (uint)sizeof(MidiHdr);

    private static unsafe nint AllocOutputHeader(ReadOnlySpan<byte> frame)
    {
        MidiHdr* header = (MidiHdr*)NativeMemory.AllocZeroed((nuint)sizeof(MidiHdr));
        header->lpData = (nint)NativeMemory.Alloc((nuint)frame.Length);
        header->dwBufferLength = (uint)frame.Length;
        frame.CopyTo(new Span<byte>((void*)header->lpData, frame.Length));
        return (nint)header;
    }

    private static unsafe void PrepareAndSend(nint handle, nint header)
    {
        Check(midiOutPrepareHeader(handle, (MidiHdr*)header, HeaderSize), "midiOutPrepareHeader", input: false);
        Check(midiOutLongMsg(handle, (MidiHdr*)header, HeaderSize), "midiOutLongMsg", input: false);
    }

    private static unsafe bool IsDone(nint header)
        => (Volatile.Read(ref ((MidiHdr*)header)->dwFlags) & MHDR_DONE) != 0;

    private static unsafe void Unprepare(nint handle, nint header)
        => Check(midiOutUnprepareHeader(handle, (MidiHdr*)header, HeaderSize), "midiOutUnprepareHeader", input: false);

    private static unsafe void FreeHeader(nint header)
    {
        if (header == 0)
            return;
        NativeMemory.Free((void*)((MidiHdr*)header)->lpData);
        NativeMemory.Free((void*)header);
    }
    /// <inheritdoc/>
    public ValueTask SendShortAsync(int message, CancellationToken cancellationToken = default)
    {
        Check(midiOutShortMsg(RequireOutput(), (uint)message), "midiOutShortMsg", input: false);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<MidiInput> ReadAllAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Channel<MidiInput> inbox = _inbox;
        await foreach (MidiInput input in inbox.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            yield return input;
    }

    /// <summary>Closes the ports and releases the transport. Safe to call more than once.</summary>
    public void Dispose()
    {
        lock (_openLock)
        {
            if (_disposed)
                return;
            _disposed = true;
            CloseCore();
        }
        _sendLock.Dispose();
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    [UnmanagedCallersOnly]
    private static void MidiInProc(nint handle, uint message, nint instance, nint param1, nint param2)
    {
        try
        {
            if (GCHandle.FromIntPtr(instance).Target is WinMMMidiTransport transport)
                transport.OnInput(message, param1);
        }
        catch (Exception)
        {
            // Never let an exception cross the native boundary.
        }
    }

    private unsafe void OnInput(uint message, nint param1)
    {
        switch (message)
        {
            case MIM_DATA:
                int packed = (int)param1;
                byte status = (byte)packed;
                if (status is not (0xF8 or 0xFE))
                    _inbox.Writer.TryWrite(new ShortInput(_time.GetUtcNow(), packed));
                break;

            case MIM_LONGDATA:
            case MIM_LONGERROR:
                MidiHdr* header = (MidiHdr*)param1;
                uint recorded = header->dwBytesRecorded;
                if (recorded > 0)
                {
                    ReadOnlySpan<byte> data = new((void*)header->lpData, (int)recorded);
                    IReadOnlyList<byte[]> frames;
                    lock (_assemblerLock)
                    {
                        if (message == MIM_LONGERROR)
                            _assembler.Reset();
                        frames = _assembler.Feed(data);
                    }
                    DateTimeOffset now = _time.GetUtcNow();
                    foreach (byte[] frame in frames)
                        _inbox.Writer.TryWrite(new SysExInput(now, frame));
                }

                // midiInAddBuffer must not be called from the callback; hand the buffer to the worker (TRN-003).
                if (!_closing)
                    _requeue?.Writer.TryWrite(param1);
                break;
        }
    }

    private async Task RequeueLoopAsync()
    {
        Channel<nint>? requeue = _requeue;
        if (requeue is null)
            return;

        await foreach (nint pointer in requeue.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            if (_closing)
                continue;
            AddBuffer(_inHandle, pointer);
        }
    }

    private static unsafe void AddBuffer(nint handle, nint pointer)
    {
        MidiHdr* header = (MidiHdr*)pointer;
        header->dwBytesRecorded = 0;
        _ = midiInAddBuffer(handle, header, HeaderSize);
    }

    private void CloseCore()
    {
        _closing = true;

        bool inputClosed = CloseInput();

        // A driver that failed to close may still call back and write into the buffers, so leaking them
        // and the callback's GCHandle is safer than freeing them.
        if (inputClosed)
        {
            foreach (nint header in _inputHeaders)
                FreeHeader(header);
            if (_self.IsAllocated)
                _self.Free();
        }
        _inputHeaders = [];

        CloseOutput();

        _openInput = null;
        _openOutput = null;
        _inbox.Writer.TryComplete();
    }

    /// <summary>Stops the input, ends the requeue worker and closes the port.</summary>
    /// <returns><see langword="true"/> when the driver no longer owns the buffers.</returns>
    private unsafe bool CloseInput()
    {
        if (_inHandle != 0)
        {
            _ = midiInStop(_inHandle);
            _ = midiInReset(_inHandle);
        }

        _requeue?.Writer.TryComplete();
        _requeueWorker?.Wait(TimeSpan.FromSeconds(2));
        _requeue = null;
        _requeueWorker = null;

        if (_inHandle == 0)
            return true;

        foreach (nint header in _inputHeaders)
        {
            if (header != 0)
                _ = midiInUnprepareHeader(_inHandle, (MidiHdr*)header, HeaderSize);
        }

        bool closed = midiInClose(_inHandle) == MMSYSERR_NOERROR;
        if (!closed)
        {
            // The driver still holds a buffer (MIDIERR_STILLPLAYING): give it back and try once more.
            _ = midiInReset(_inHandle);
            closed = midiInClose(_inHandle) == MMSYSERR_NOERROR;
        }
        _inHandle = 0;
        return closed;
    }

    private void CloseOutput()
    {
        if (_outHandle == 0)
            return;

        _ = midiOutReset(_outHandle);
        if (midiOutClose(_outHandle) != MMSYSERR_NOERROR)
        {
            _ = midiOutReset(_outHandle);
            _ = midiOutClose(_outHandle);
        }
        _outHandle = 0;
    }

    private nint RequireOutput()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        nint handle = _outHandle;
        return handle != 0 ? handle : throw new InvalidOperationException("The transport is not open.");
    }

    private static uint IndexOf(IReadOnlyList<MidiPortInfo> ports, MidiPortId id, string kind)
    {
        for (int i = 0; i < ports.Count; i++)
        {
            if (ports[i].Id == id)
                return (uint)i;
        }
        throw new WinMMException($"MIDI {kind} port '{id}' not found.");
    }

    private static Channel<MidiInput> CreateInbox()
        => Channel.CreateUnbounded<MidiInput>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });

    private static unsafe void Check(uint result, string function, bool input)
    {
        if (result == MMSYSERR_NOERROR)
            return;

        char* text = stackalloc char[256];
        uint textResult = input ? midiInGetErrorTextW(result, text, 256) : midiOutGetErrorTextW(result, text, 256);
        string detail = textResult == MMSYSERR_NOERROR ? new string(text) : $"MMRESULT {result}";
        throw new WinMMException($"{function} failed: {detail}", result);
    }
}
