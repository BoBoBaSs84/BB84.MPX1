using BB84.MPX1.Midi;
using BB84.MPX1.Midi.WinMM;
using BB84.MPX1.Simulator;

namespace BB84.MPX1.App.Services;

/// <summary>A port offered in the MIDI setup: a hardware port or the built-in simulator.</summary>
/// <param name="Key">Stable key for settings; prefixed so a hardware port can never collide with the simulator.</param>
/// <param name="Name">Display name.</param>
/// <param name="Port">The underlying transport port.</param>
/// <param name="IsSimulator">Whether this is the simulator pseudo-port.</param>
public sealed record MidiPortChoice(string Key, string Name, MidiPortInfo Port, bool IsSimulator)
{
    /// <inheritdoc/>
    public override string ToString() => Name;
}

/// <summary>Lists ports and creates the transport for a chosen input/output pair.</summary>
public interface IMidiPortCatalog
{
    /// <summary>Lists the input ports, simulator first.</summary>
    IReadOnlyList<MidiPortChoice> GetInputs();

    /// <summary>Lists the output ports, simulator first.</summary>
    IReadOnlyList<MidiPortChoice> GetOutputs();

    /// <summary>Creates an unopened transport for the pair.</summary>
    /// <param name="input">The input port.</param>
    /// <param name="output">The output port.</param>
    /// <exception cref="ArgumentException">The simulator was combined with a hardware port.</exception>
    IMidiTransport CreateTransport(MidiPortChoice input, MidiPortChoice output);
}

/// <summary>
/// Default catalog: the "MPX 1 Simulator" pseudo-port plus every WinMM port. Hardware enumeration failures
/// (for example on a machine without MIDI drivers) leave only the simulator.
/// </summary>
/// <param name="createHardware">Creates a hardware transport; <see langword="null"/> for none (tests).</param>
/// <param name="createSimulator">Creates the simulator; a default-fixture simulator when <see langword="null"/>.</param>
public sealed class MidiPortCatalog(Func<IMidiTransport>? createHardware, Func<Mpx1Simulator>? createSimulator = null) : IMidiPortCatalog
{
    private const string SimulatorPrefix = "sim:";
    private const string HardwarePrefix = "midi:";

    private readonly Func<Mpx1Simulator> _createSimulator = createSimulator ?? (() => new Mpx1Simulator());

    /// <summary>Creates the catalog used by the application on this OS.</summary>
    public static MidiPortCatalog CreateDefault()
        => new(OperatingSystem.IsWindows() ? CreateWinMM : null);

    private static WinMMMidiTransport CreateWinMM()
        => OperatingSystem.IsWindows() ? new WinMMMidiTransport() : throw new PlatformNotSupportedException("WinMM is only available on Windows.");

    /// <summary>The simulator pseudo-port.</summary>
    public static MidiPortChoice Simulator { get; } = new(SimulatorPrefix + Mpx1Simulator.Port.Id.Value, Mpx1Simulator.Port.Name, Mpx1Simulator.Port, IsSimulator: true);

    /// <inheritdoc/>
    public IReadOnlyList<MidiPortChoice> GetInputs() => List(static t => t.GetInputPorts());

    /// <inheritdoc/>
    public IReadOnlyList<MidiPortChoice> GetOutputs() => List(static t => t.GetOutputPorts());

    /// <inheritdoc/>
    public IMidiTransport CreateTransport(MidiPortChoice input, MidiPortChoice output)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        if (input.IsSimulator != output.IsSimulator)
            throw new ArgumentException("The simulator must be used for both input and output.", nameof(output));
        if (input.IsSimulator)
            return _createSimulator();
        return createHardware?.Invoke() ?? throw new InvalidOperationException("No MIDI hardware transport is available on this system.");
    }

    private List<MidiPortChoice> List(Func<IMidiTransport, IReadOnlyList<MidiPortInfo>> ports)
    {
        List<MidiPortChoice> result = [Simulator];
        if (createHardware is null)
            return result;

        IMidiTransport? transport = null;
        try
        {
            transport = createHardware();
            result.AddRange(ports(transport).Select(p => new MidiPortChoice(HardwarePrefix + p.Id.Value, p.Name, p, IsSimulator: false)));
        }
        catch (Exception ex) when (ex is InvalidOperationException or PlatformNotSupportedException or DllNotFoundException or EntryPointNotFoundException)
        {
            // No usable MIDI subsystem: the simulator stays available.
        }
        finally
        {
            // Port listing is synchronous; avoid blocking on DisposeAsync when the transport can close synchronously.
            if (transport is IDisposable disposable)
                disposable.Dispose();
            else
                transport?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        return result;
    }
}
