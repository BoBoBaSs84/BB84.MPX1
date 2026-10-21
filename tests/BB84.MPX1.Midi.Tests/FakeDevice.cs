using System.Collections.Concurrent;

using BB84.MPX1.Core.Protocol;

namespace BB84.MPX1.Midi.Tests;

/// <summary>
/// Scriptable device on the far end of a loopback pair. The responder returns the frames to send back,
/// each with an optional delay before it.
/// </summary>
internal sealed class FakeDevice : IAsyncDisposable
{
    private readonly LoopbackMidiTransport _transport;
    private readonly Func<Mpx1Message, IEnumerable<(TimeSpan Delay, Mpx1Message Message)>> _responder;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;

    public FakeDevice(LoopbackMidiTransport transport, Func<Mpx1Message, IEnumerable<(TimeSpan Delay, Mpx1Message Message)>> responder)
    {
        _transport = transport;
        _responder = responder;
        _transport.OpenAsync(LoopbackMidiTransport.Port.Id, LoopbackMidiTransport.Port.Id).AsTask().Wait();
        _loop = Task.Run(LoopAsync);
    }

    public ConcurrentQueue<(DateTimeOffset Time, Mpx1Message Message)> Received { get; } = new();

    public Task SendAsync(Mpx1Message message) => _transport.SendSysExAsync(message.ToBytes()).AsTask();

    private async Task LoopAsync()
    {
        try
        {
            await foreach (MidiInput input in _transport.ReadAllAsync(_stop.Token))
            {
                if (input is not SysExInput sysEx)
                    continue;
                Mpx1Message message = Mpx1Codec.Decode(sysEx.Frame.Span);
                Received.Enqueue((DateTimeOffset.UtcNow, message));
                foreach ((TimeSpan delay, Mpx1Message response) in _responder(message))
                {
                    if (delay > TimeSpan.Zero)
                        await Task.Delay(delay, _stop.Token);
                    await _transport.SendSysExAsync(response.ToBytes(), _stop.Token);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        await _loop;
        await _transport.DisposeAsync();
        _stop.Dispose();
    }
}
