using BB84.MPX1.Core.Protocol;

namespace BB84.MPX1.Midi;

/// <summary>
/// Talks to one MPX 1 over an <see cref="IMidiTransport"/> (SES-001): matches responses to requests,
/// applies timeouts and retries, honours BUSY/READY flow control and publishes unsolicited traffic.
/// </summary>
/// <remarks>
/// Events are raised on the receive loop's thread; UI code must marshal them to its own thread.
/// </remarks>
public sealed class Mpx1Session : IAsyncDisposable
{
    private readonly IMidiTransport _transport;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _requestLock = new(1, 1);
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly Lock _stateLock = new();
    private readonly CancellationTokenSource _stop = new();

    private Task? _receiveLoop;
    private PendingRequest? _pending;
    private TaskCompletionSource? _readyGate;
    private TaskCompletionSource? _busyObserved;
    private long _lastSendTimestamp;
    private bool _hasSent;

    /// <summary>Initializes a new instance of the <see cref="Mpx1Session"/> class.</summary>
    /// <param name="transport">An open transport.</param>
    /// <param name="device">The device ID to talk to.</param>
    /// <param name="options">Timing options; defaults when <see langword="null"/>.</param>
    /// <param name="time">Time source; the system clock when <see langword="null"/>.</param>
    public Mpx1Session(IMidiTransport transport, DeviceId device, Mpx1SessionOptions? options = null, TimeProvider? time = null)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        Device = device;
        Options = options ?? new Mpx1SessionOptions();
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Gets the device ID this session talks to.</summary>
    public DeviceId Device { get; }

    /// <summary>Gets the timing options.</summary>
    public Mpx1SessionOptions Options { get; }

    /// <summary>Gets a value indicating whether the device currently reports BUSY.</summary>
    public bool IsDeviceBusy
    {
        get
        {
            lock (_stateLock)
                return _readyGate is not null;
        }
    }

    /// <summary>Raised for every SysEx frame sent or received (UI-009).</summary>
    public event EventHandler<FrameEventArgs>? FrameTraffic;

    /// <summary>Raised for decoded messages that did not answer a pending request (SES-009).</summary>
    public event EventHandler<MessageEventArgs>? UnsolicitedMessage;

    /// <summary>Raised for short MIDI messages.</summary>
    public event EventHandler<ShortMessageEventArgs>? ShortMessageReceived;

    /// <summary>Starts the receive loop. The transport must be open.</summary>
    public void Start()
    {
        if (!_transport.IsOpen)
            throw new InvalidOperationException("The transport is not open.");
        _receiveLoop ??= Task.Run(() => ReceiveLoopAsync(_stop.Token));
    }

    /// <summary>
    /// Sends a request and waits for the matching response (SES-002, SES-003).
    /// </summary>
    /// <typeparam name="TResponse">The expected response type.</typeparam>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="Mpx1TimeoutException">No matching response after all retries.</exception>
    public async Task<TResponse> RequestAsync<TResponse>(Request request, CancellationToken cancellationToken = default)
        where TResponse : LexiconMessage
    {
        ArgumentNullException.ThrowIfNull(request);
        Func<Mpx1Message, bool> matcher = ResponseMatcher.For(request);
        Mpx1Message response = await ExchangeAsync(request, m => m is TResponse && matcher(m), cancellationToken).ConfigureAwait(false);
        return (TResponse)response;
    }

    /// <summary>
    /// Sends a message and waits for the first received message accepted by <paramref name="match"/>,
    /// retrying on timeout (SES-003, SES-004).
    /// </summary>
    /// <param name="message">The message to send.</param>
    /// <param name="match">Response predicate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="Mpx1TimeoutException">No matching response after all retries.</exception>
    public async Task<Mpx1Message> ExchangeAsync(Mpx1Message message, Func<Mpx1Message, bool> match, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(match);
        EnsureStarted();

        byte[] frame = message.ToBytes();
        int attempts = 0;

        await _requestLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            for (int attempt = 0; attempt <= Options.Retries; attempt++)
            {
                PendingRequest pending = new(match);
                lock (_stateLock)
                    _pending = pending;

                try
                {
                    await SendFrameAsync(frame, message, cancellationToken).ConfigureAwait(false);
                    attempts++;

                    Task completed = await Task.WhenAny(pending.Completion.Task, Task.Delay(Options.RequestTimeout, _time, cancellationToken)).ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (completed == pending.Completion.Task)
                        return await pending.Completion.Task.ConfigureAwait(false);
                }
                finally
                {
                    lock (_stateLock)
                    {
                        if (ReferenceEquals(_pending, pending))
                            _pending = null;
                    }
                }
            }
        }
        finally
        {
            _requestLock.Release();
        }

        throw new Mpx1TimeoutException($"No response to {message.GetType().Name} after {attempts} attempt(s).", frame, attempts);
    }

    /// <summary>
    /// Sends a message without waiting for a response. Bulk writes (Program Dump, Database Dump, multi-byte
    /// Parameter Data) honour BUSY/READY flow control (SES-005).
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="Mpx1TimeoutException">The device reported BUSY and did not send READY in time.</exception>
    public async Task SendAsync(Mpx1Message message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        EnsureStarted();

        byte[] frame = message.ToBytes();
        if (!IsBulkWrite(message))
        {
            await SendFrameAsync(frame, message, cancellationToken).ConfigureAwait(false);
            return;
        }

        TaskCompletionSource busyObserved = new(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_stateLock)
            _busyObserved = busyObserved;

        try
        {
            await SendFrameAsync(frame, message, cancellationToken).ConfigureAwait(false);
            await Task.WhenAny(busyObserved.Task, Task.Delay(Options.BusyGracePeriod, _time, cancellationToken)).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            await WaitUntilReadyAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            lock (_stateLock)
            {
                if (ReferenceEquals(_busyObserved, busyObserved))
                    _busyObserved = null;
            }
        }
    }

    /// <summary>Checks whether the device answers ARE_YOU_THERE with IM_ALIVE (SES-006).</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> when the device answered.</returns>
    public async Task<bool> PingAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await ExchangeAsync(new Handshake(Device, HandshakeCommand.AreYouThere),
                m => m is Handshake { Command: HandshakeCommand.ImAlive } && DeviceMatches(m), cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Mpx1TimeoutException)
        {
            return false;
        }
    }

    /// <summary>
    /// Detects the firmware version: Device Inquiry first, then System Configuration (SES-007).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="Mpx1TimeoutException">The device did not answer the System Configuration request.</exception>
    public async Task<DeviceVersion> DetectVersionAsync(CancellationToken cancellationToken = default)
    {
        IdentityReply? identity = null;
        try
        {
            identity = (IdentityReply)await ExchangeAsync(new DeviceInquiry(Device),
                m => m is IdentityReply && DeviceMatches(m), cancellationToken).ConfigureAwait(false);
        }
        catch (Mpx1TimeoutException)
        {
            // Not fatal: System Configuration carries the version as well.
        }

        SystemConfiguration configuration = await RequestAsync<SystemConfiguration>(Request.SystemConfiguration(Device), cancellationToken).ConfigureAwait(false);
        return new DeviceVersion(configuration.MajorVersion, configuration.MinorVersion, identity, configuration);
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        if (_receiveLoop is not null)
        {
            try
            {
                await _receiveLoop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown.
            }
        }
        lock (_stateLock)
            _readyGate?.TrySetCanceled();
        _stop.Dispose();
        _requestLock.Dispose();
        _sendLock.Dispose();
    }

    private static bool IsBulkWrite(Mpx1Message message)
        => message is ProgramDump or DatabaseDump or ParameterData { Data.Length: > 2 };

    private bool DeviceMatches(Mpx1Message message)
        => Device == DeviceId.All || message.Device == Device;

    private void EnsureStarted()
    {
        if (_receiveLoop is null)
            throw new InvalidOperationException("Call Start() before sending.");
    }

    private async Task SendFrameAsync(byte[] frame, Mpx1Message message, CancellationToken cancellationToken)
    {
        await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await WaitUntilReadyAsync(cancellationToken).ConfigureAwait(false);

            if (_hasSent && Options.InterFrameGap > TimeSpan.Zero)
            {
                TimeSpan remaining = Options.InterFrameGap - _time.GetElapsedTime(_lastSendTimestamp);
                if (remaining > TimeSpan.Zero)
                    await Task.Delay(remaining, _time, cancellationToken).ConfigureAwait(false);
            }

            await _transport.SendSysExAsync(frame, cancellationToken).ConfigureAwait(false);
            _lastSendTimestamp = _time.GetTimestamp();
            _hasSent = true;
        }
        finally
        {
            _sendLock.Release();
        }

        FrameTraffic?.Invoke(this, new FrameEventArgs(FrameDirection.Sent, _time.GetUtcNow(), frame, message, null));
    }

    private async Task WaitUntilReadyAsync(CancellationToken cancellationToken)
    {
        Task? gate;
        lock (_stateLock)
            gate = _readyGate?.Task;
        if (gate is null)
            return;

        Task completed = await Task.WhenAny(gate, Task.Delay(Options.ReadyTimeout, _time, cancellationToken)).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (completed != gate)
        {
            lock (_stateLock)
            {
                // Give up on this BUSY so later sends are not blocked forever.
                _readyGate = null;
            }
            throw new Mpx1TimeoutException($"Device reported BUSY and did not send READY within {Options.ReadyTimeout.TotalMilliseconds} ms.");
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        await foreach (MidiInput input in _transport.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            switch (input)
            {
                case SysExInput sysEx:
                    HandleFrame(sysEx);
                    break;
                case ShortInput shortInput:
                    ShortMessageReceived?.Invoke(this, new ShortMessageEventArgs(shortInput.Timestamp, shortInput.Message));
                    break;
            }
        }
    }

    private void HandleFrame(SysExInput input)
    {
        if (!Mpx1Codec.TryDecode(input.Frame.Span, out Mpx1Message? message, out Mpx1FormatError? error))
        {
            FrameTraffic?.Invoke(this, new FrameEventArgs(FrameDirection.Received, input.Timestamp, input.Frame, null, error));
            return;
        }

        FrameTraffic?.Invoke(this, new FrameEventArgs(FrameDirection.Received, input.Timestamp, input.Frame, message, null));

        if (message is Handshake handshake && DeviceMatches(handshake))
            UpdateFlowControl(handshake.Command);

        PendingRequest? pending;
        lock (_stateLock)
            pending = _pending;

        if (pending is not null && pending.Match(message) && pending.Completion.TrySetResult(message))
            return;

        UnsolicitedMessage?.Invoke(this, new MessageEventArgs(message));
    }

    private void UpdateFlowControl(HandshakeCommand command)
    {
        lock (_stateLock)
        {
            switch (command)
            {
                case HandshakeCommand.Busy:
                    _readyGate ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    _busyObserved?.TrySetResult();
                    break;
                case HandshakeCommand.Ready:
                    _readyGate?.TrySetResult();
                    _readyGate = null;
                    break;
            }
        }
    }

    private sealed class PendingRequest(Func<Mpx1Message, bool> match)
    {
        public Func<Mpx1Message, bool> Match { get; } = match;

        public TaskCompletionSource<Mpx1Message> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
