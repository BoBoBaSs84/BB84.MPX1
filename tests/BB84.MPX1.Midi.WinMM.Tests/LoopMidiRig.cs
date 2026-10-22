using BB84.MPX1.Simulator;

namespace BB84.MPX1.Midi.WinMM.Tests;

/// <summary>
/// An editor-side WinMM transport wired to a <see cref="Mpx1Simulator"/> through two loopMIDI ports: the editor
/// reads "loopMIDI Port Editor" and writes "loopMIDI Port Simulator"; the simulator side does the reverse. Frames
/// cross the real Windows MIDI stack in both directions.
/// </summary>
internal sealed class LoopMidiRig : IAsyncDisposable
{
    private const string EditorPort = "loopMIDI Port Editor";
    private const string SimulatorPort = "loopMIDI Port Simulator";

    // A freshly opened loopMIDI port pair sometimes delivers nothing in one direction for as long as it stays open.
    // The rig therefore checks both directions and reopens until frames flow.
    private const int MaxOpenAttempts = 16;

    private readonly WinMMMidiTransport _device;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _toSimulator;
    private readonly Task _fromSimulator;

    private LoopMidiRig(WinMMMidiTransport editor, WinMMMidiTransport device, Mpx1Simulator simulator)
    {
        Editor = editor;
        _device = device;
        Simulator = simulator;
        _toSimulator = Task.Run(() => PumpToSimulatorAsync(_stop.Token));
        _fromSimulator = Task.Run(() => PumpFromSimulatorAsync(_stop.Token));
    }

    /// <summary>Gets the transport the editor side (session, librarian) uses.</summary>
    public WinMMMidiTransport Editor { get; }

    /// <summary>Gets the simulated unit on the far side of the ports.</summary>
    public Mpx1Simulator Simulator { get; }

    /// <summary>Opens the rig, or ends the test as inconclusive when the two loopMIDI ports do not exist.</summary>
    /// <param name="options">Simulator options; defaults when <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task<LoopMidiRig> OpenAsync(SimulatorOptions? options, CancellationToken cancellationToken)
    {
        WinMMMidiTransport editor = new();
        WinMMMidiTransport device = new();
        MidiPortInfo? editorIn = Find(editor.GetInputPorts(), EditorPort);
        MidiPortInfo? editorOut = Find(editor.GetOutputPorts(), SimulatorPort);
        MidiPortInfo? deviceIn = Find(device.GetInputPorts(), SimulatorPort);
        MidiPortInfo? deviceOut = Find(device.GetOutputPorts(), EditorPort);
        if (editorIn is null || editorOut is null || deviceIn is null || deviceOut is null)
        {
            await editor.DisposeAsync();
            await device.DisposeAsync();
            Assert.Inconclusive($"The loopMIDI ports '{EditorPort}' and '{SimulatorPort}' are not both present.");
        }

        Mpx1Simulator simulator = new(options: options ?? new SimulatorOptions { BusyDuration = TimeSpan.FromMilliseconds(1) });
        await simulator.OpenAsync(Mpx1Simulator.Port.Id, Mpx1Simulator.Port.Id, cancellationToken);
        int attempts = 0;
        bool linked = false;
        while (!linked && attempts < MaxOpenAttempts)
        {
            attempts++;
            await editor.CloseAsync(cancellationToken);
            await device.CloseAsync(cancellationToken);
            await editor.OpenAsync(editorIn!.Id, editorOut!.Id, cancellationToken);
            await device.OpenAsync(deviceIn!.Id, deviceOut!.Id, cancellationToken);
            linked = await LinkWorksAsync(editor, device, cancellationToken) && await LinkWorksAsync(device, editor, cancellationToken);
        }
        if (!linked)
        {
            await editor.DisposeAsync();
            await device.DisposeAsync();
            await simulator.DisposeAsync();
            Assert.Inconclusive($"No working path through '{EditorPort}' and '{SimulatorPort}' after {attempts} attempts.");
        }
        return new LoopMidiRig(editor, device, simulator);
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        await Editor.DisposeAsync();
        await _device.DisposeAsync();
        await Simulator.DisposeAsync();
        await Task.WhenAll(_toSimulator, _fromSimulator);
        _stop.Dispose();
    }

    private static async Task<bool> LinkWorksAsync(WinMMMidiTransport from, WinMMMidiTransport to, CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(250));
        Task<bool> reader = Task.Run(async () =>
        {
            try
            {
                await foreach (MidiInput input in to.ReadAllAsync(timeout.Token))
                {
                    if (input is SysExInput)
                        return true;
                }
            }
            catch (OperationCanceledException)
            {
                // Nothing arrived in time.
            }
            return false;
        }, cancellationToken);
        await Task.Delay(50, cancellationToken);
        await from.SendSysExAsync(new byte[] { 0xF0, 0x06, 0x09, 0x7F, 0x12, 0x00, 0x00, 0xF7 }, cancellationToken);
        return await reader;
    }

    private static MidiPortInfo? Find(IReadOnlyList<MidiPortInfo> ports, string name)
        => ports.FirstOrDefault(p => p.Name == name);

    // Frames the editor wrote to the simulator port reach the simulated unit.
    private async Task PumpToSimulatorAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (MidiInput input in _device.ReadAllAsync(cancellationToken))
            {
                if (input is SysExInput sysEx)
                    await Simulator.SendSysExAsync(sysEx.Frame, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown.
        }
    }

    // The unit's answers go out of the editor port.
    private async Task PumpFromSimulatorAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (MidiInput input in Simulator.ReadAllAsync(cancellationToken))
            {
                if (input is SysExInput sysEx)
                    await _device.SendSysExAsync(sysEx.Frame, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown.
        }
    }
}
