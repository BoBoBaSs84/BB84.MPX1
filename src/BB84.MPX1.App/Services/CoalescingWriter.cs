namespace BB84.MPX1.App.Services;

/// <summary>
/// Sends live edits without flooding the MIDI line: while a value changes quickly, at most one write per
/// <c>interval</c> goes out, always with the latest value. Once no change arrived for <c>settleDelay</c>, the
/// <c>settled</c> callback runs once (used to read back the device's display text).
/// </summary>
/// <typeparam name="T">The value type.</typeparam>
/// <param name="write">Sends one value.</param>
/// <param name="settled">Runs after the last write of a burst.</param>
/// <param name="onError">Receives write or settle failures; the pending value is dropped.</param>
/// <param name="time">Time source.</param>
/// <param name="interval">Minimum time between writes.</param>
/// <param name="settleDelay">Quiet time after the last write before <paramref name="settled"/> runs.</param>
public sealed class CoalescingWriter<T>(
    Func<T, CancellationToken, Task> write,
    Func<CancellationToken, Task> settled,
    Action<Exception> onError,
    TimeProvider time,
    TimeSpan interval,
    TimeSpan settleDelay) : IDisposable
{
    /// <summary>Default minimum time between live writes.</summary>
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromMilliseconds(30);

    /// <summary>Default quiet time before reading back.</summary>
    public static readonly TimeSpan DefaultSettleDelay = TimeSpan.FromMilliseconds(150);

    private readonly Lock _lock = new();
    private bool _hasPending;
    private T? _pending;
    private bool _active;
    private bool _disposed;
    private int _generation;
    private Task _running = Task.CompletedTask;
    private CancellationTokenSource _cancel = new();

    /// <summary>Gets the task of the current burst; completed when idle.</summary>
    public Task Completion
    {
        get
        {
            lock (_lock)
                return _running;
        }
    }

    /// <summary>Queues a value. Replaces any value that has not been sent yet.</summary>
    /// <param name="value">The value.</param>
    public void Submit(T value)
    {
        lock (_lock)
        {
            if (_disposed)
                return;
            _pending = value;
            _hasPending = true;
            if (!_active)
            {
                _active = true;
                _running = RunAsync(_generation, _cancel.Token);
            }
        }
    }

    /// <summary>Drops any unsent value and stops the current burst; later values are ignored.</summary>
    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
                return;
            _disposed = true;
            _hasPending = false;
            _active = false;
            _generation++;
            _cancel.Cancel();
            _cancel.Dispose();
        }
    }

    /// <summary>Drops any unsent value and stops the current burst.</summary>
    public void Cancel()
    {
        lock (_lock)
        {
            if (_disposed)
                return;
            _hasPending = false;
            _active = false;
            _generation++;
            _cancel.Cancel();
            _cancel.Dispose();
            _cancel = new CancellationTokenSource();
        }
    }

    private async Task RunAsync(int generation, CancellationToken cancellationToken)
    {
        // Leave the caller's lock and stack before the first write.
        await Task.Yield();
        try
        {
            while (true)
            {
                while (TryTake(generation, out T value))
                {
                    await write(value, cancellationToken).ConfigureAwait(true);
                    await Task.Delay(interval, time, cancellationToken).ConfigureAwait(true);
                }

                await Task.Delay(settleDelay, time, cancellationToken).ConfigureAwait(true);
                if (HasPending())
                    continue;
                await settled(cancellationToken).ConfigureAwait(true);
                if (TryFinish(generation))
                    return;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Cancelled by the owner; Cancel already reset the state.
        }
        catch (Exception ex)
        {
            lock (_lock)
            {
                if (generation == _generation)
                {
                    _hasPending = false;
                    _active = false;
                }
            }
            onError(ex);
        }
    }

    private bool HasPending()
    {
        lock (_lock)
            return _hasPending;
    }

    private bool TryFinish(int generation)
    {
        lock (_lock)
        {
            if (_hasPending && generation == _generation)
                return false;
            if (generation == _generation)
                _active = false;
            return true;
        }
    }

    private bool TryTake(int generation, out T value)
    {
        lock (_lock)
        {
            value = _pending!;
            if (!_hasPending || generation != _generation)
                return false;
            _hasPending = false;
            return true;
        }
    }
}
