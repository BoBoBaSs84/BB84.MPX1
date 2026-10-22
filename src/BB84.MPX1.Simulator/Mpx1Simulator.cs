using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

using BB84.MPX1.Core.Dumps;
using BB84.MPX1.Core.Programs;
using BB84.MPX1.Core.Protocol;
using BB84.MPX1.Midi;
using BB84.MPX1.Simulator.Fixtures;

namespace BB84.MPX1.Simulator;

/// <summary>
/// An in-process MPX 1 behind <see cref="IMidiTransport"/> (SIM-001). Frames the editor sends are
/// processed by the simulated device; its answers appear on <see cref="ReadAllAsync"/>.
/// </summary>
public sealed partial class Mpx1Simulator : IMidiTransport
{
    /// <summary>The single port the simulator exposes.</summary>
    public static readonly MidiPortInfo Port = new(new MidiPortId("simulator"), "MPX 1 Simulator");

    private readonly Lock _lock = new();
    private readonly TimeProvider _time;
    private readonly SimulatorFixture _fixture;
    private readonly Dictionary<ushort, ParameterDescription> _descriptions = [];
    private readonly Dictionary<ControlAddress, ushort> _tree = [];
    private readonly Dictionary<ControlAddress, byte[]> _values = [];
    private readonly ProgramData[] _programs = new ProgramData[DatabaseDump.ProgramCount];
    private readonly Channel<(TimeSpan Delay, byte[] Frame)> _outbox = Channel.CreateUnbounded<(TimeSpan, byte[])>(new UnboundedChannelOptions { SingleReader = true });
    private Channel<MidiInput> _inbox = CreateInbox();
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _pump;
    private ProgramData _active;
    private ProgramCategories[] _database;

    /// <summary>Initializes a new instance of the <see cref="Mpx1Simulator"/> class.</summary>
    /// <param name="fixture">The device model; <see cref="DefaultFixture.CreateV110"/> when <see langword="null"/>.</param>
    /// <param name="options">Options; defaults when <see langword="null"/>.</param>
    /// <param name="time">Time source; the system clock when <see langword="null"/>.</param>
    public Mpx1Simulator(SimulatorFixture? fixture = null, SimulatorOptions? options = null, TimeProvider? time = null)
    {
        _fixture = fixture ?? DefaultFixture.CreateV110();
        Options = options ?? new SimulatorOptions();
        _time = time ?? TimeProvider.System;

        foreach (FixtureDescription d in _fixture.Descriptions)
        {
            _descriptions[d.Type] = new ParameterDescription(Options.Device, d.Type, d.Name, d.Size, (ParameterAttributes)d.Attributes, d.OptionType,
                [.. d.Units.Select(u => new UnitLimit(u.Minimum, u.Maximum, u.DisplayUnits))]);
        }
        foreach (FixtureNode node in _fixture.Nodes)
            _tree[ControlAddress.From(node.Address)] = node.Type;
        foreach (FixtureValue value in _fixture.Values)
            _values[ControlAddress.From(value.Address)] = Convert.FromHexString(value.Hex);

        for (int i = 0; i < _programs.Length; i++)
            _programs[i] = DefaultFixture.CreateProgram(i);
        foreach (FixtureProgram program in _fixture.Programs)
            _programs[program.Number] = ProgramData.FromBytes(Convert.FromHexString(program.Hex));

        _active = _programs[_fixture.ActiveProgram];
        _database = [.. _programs.Select(p => p.Categories)];
        _pump = Task.Run(PumpAsync);
    }

    /// <summary>Gets the options.</summary>
    public SimulatorOptions Options { get; }

    /// <summary>Gets the fault injection switches (SIM-004).</summary>
    public SimulatorFaults Faults { get; } = new();

    /// <summary>
    /// Gets a value indicating whether the simulated unit "crashed" because a parameter of an unloaded
    /// algorithm was written (Appendix D bug 3, SIM-005). A crashed unit no longer answers.
    /// </summary>
    public bool IsCrashed { get; private set; }

    /// <summary>Gets the number of frames received from the editor.</summary>
    public int ReceivedFrames { get; private set; }

    /// <inheritdoc/>
    public bool IsOpen { get; private set; }

    /// <summary>Gets the active (running) program.</summary>
    public ProgramData ActiveProgram
    {
        get
        {
            lock (_lock)
                return _active;
        }
    }

    /// <summary>Gets the sort flags of all programs (the Database Dump contents).</summary>
    public IReadOnlyList<ProgramCategories> Database
    {
        get
        {
            lock (_lock)
                return [.. _database];
        }
    }

    /// <summary>Gets a stored program.</summary>
    /// <param name="number">Program number 0–249.</param>
    public ProgramData GetProgram(int number)
    {
        lock (_lock)
            return _programs[number];
    }

    /// <summary>Gets the raw value stored at an address, or <see langword="null"/>.</summary>
    /// <param name="address">The control address.</param>
    public byte[]? GetValue(ControlAddress address)
    {
        lock (_lock)
            return _values.TryGetValue(address, out byte[]? value) ? value.ToArray() : null;
    }

    /// <summary>Sends an unsolicited message from the device (for example, automation or a display update).</summary>
    /// <param name="message">The message.</param>
    public void Emit(Mpx1Message message)
    {
        ArgumentNullException.ThrowIfNull(message);
        _outbox.Writer.TryWrite((TimeSpan.Zero, message.ToBytes()));
    }

    /// <inheritdoc/>
    public IReadOnlyList<MidiPortInfo> GetInputPorts() => [Port];

    /// <inheritdoc/>
    public IReadOnlyList<MidiPortInfo> GetOutputPorts() => [Port];

    /// <inheritdoc/>
    public ValueTask OpenAsync(MidiPortId input, MidiPortId output, CancellationToken cancellationToken = default)
    {
        if (!IsOpen)
        {
            _inbox = CreateInbox();
            IsOpen = true;
        }
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask CloseAsync(CancellationToken cancellationToken = default)
    {
        if (IsOpen)
        {
            IsOpen = false;
            _inbox.Writer.TryComplete();
        }
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask SendSysExAsync(ReadOnlyMemory<byte> frame, CancellationToken cancellationToken = default)
    {
        if (!IsOpen)
            throw new InvalidOperationException("The transport is not open.");

        lock (_lock)
        {
            ReceivedFrames++;
            if (IsCrashed || !Mpx1Codec.TryDecode(frame.Span, out Mpx1Message? message, out _))
                return ValueTask.CompletedTask;
            if (message.Device != Options.Device && message.Device != DeviceId.All)
                return ValueTask.CompletedTask;

            Handle(message);
        }
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask SendShortAsync(int message, CancellationToken cancellationToken = default)
        => IsOpen ? ValueTask.CompletedTask : throw new InvalidOperationException("The transport is not open.");

    /// <inheritdoc/>
    public async IAsyncEnumerable<MidiInput> ReadAllAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Channel<MidiInput> inbox = _inbox;
        await foreach (MidiInput input in inbox.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            yield return input;
    }

    /// <summary>Exports the current device state as a fixture.</summary>
    public SimulatorFixture ToFixture()
    {
        lock (_lock)
        {
            return _fixture with
            {
                Values = [.. _values.Select(kv => new FixtureValue([.. kv.Key.Levels], Convert.ToHexString(kv.Value)))],
                Programs = [.. _programs.Select((p, i) => new FixtureProgram((ushort)i, Convert.ToHexString(p.ToBytes())))],
            };
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await CloseAsync().ConfigureAwait(false);
        _outbox.Writer.TryComplete();
        await _stop.CancelAsync().ConfigureAwait(false);
        try
        {
            await _pump.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown.
        }
        _stop.Dispose();
    }

    private void Handle(Mpx1Message message)
    {
        switch (message)
        {
            case DeviceInquiry:
                Respond(new IdentityReply(Options.Device, _fixture.MajorVersion, _fixture.MinorVersion, _fixture.DevelopmentPhase));
                break;
            case Handshake { Command: HandshakeCommand.AreYouThere }:
                Respond(new Handshake(Options.Device, HandshakeCommand.ImAlive));
                break;
            case Request request:
                HandleRequest(request);
                break;
            case ParameterData data:
                WriteParameter(data);
                break;
            case ProgramDump dump:
                StoreProgram(dump);
                SignalBusy();
                break;
            case DatabaseDump database:
                _database = [.. database.Entries];
                SignalBusy();
                break;
        }
    }

    private void StoreProgram(ProgramDump dump)
    {
        if (dump.ProgramNumber == Request.ActiveProgram)
            _active = dump.Program;
        else if (dump.ProgramNumber < _programs.Length)
            _programs[dump.ProgramNumber] = dump.Program;
    }

    // Appendix D bug 3: editing parameters of an algorithm that is not loaded can crash the unit.
    private bool IsUnloadedAlgorithmWrite(ControlAddress address)
        => address.Depth == 4 && address[0] == 0 && address[1] < ProgramData.EffectBlockCount
            && _active.Algorithms[address[1]] != address[2];

    private void WriteParameter(ParameterData data)
    {
        if (!_tree.TryGetValue(data.Address, out ushort type) || Describe(type).IsControlLevel)
            return;

        if (IsUnloadedAlgorithmWrite(data.Address))
        {
            IsCrashed = true;
            return;
        }

        switch (type)
        {
            // Dumps of the active program edit the running program itself (DMP-009, DMP-010).
            case DefaultFixture.Types.PatchesDump when data.Data.Length == DumpLayouts.Patches.Size:
                _active = _active.WithPatchesDump(data.Data.Span);
                break;
            case DefaultFixture.Types.SoftRowDump when data.Data.Length == DumpLayouts.SoftRow.Size:
                _active = _active.WithSoftRowDump(data.Data.Span);
                break;
            case DefaultFixture.Types.PanelButton when data.Data.Length >= 1:
                PressButton(data.Data.Span[0]);
                return;
            default:
                _values[data.Address] = data.Data.ToArray();
                break;
        }
        if (data.Data.Length > 2)
            SignalBusy();
    }

    private void SignalBusy()
    {
        Respond(new Handshake(Options.Device, HandshakeCommand.Busy));
        if (!Faults.SuppressReady)
            Enqueue(Options.BusyDuration, new Handshake(Options.Device, HandshakeCommand.Ready).ToBytes());
    }

    private void Respond(Mpx1Message message)
    {
        if (Faults.TryConsumeDrop())
            return;

        byte[] frame = message.ToBytes();
        if (Faults.TryConsumeCorrupt() && frame.Length > Mpx1Frame.HeaderLength + 1)
            frame[Mpx1Frame.HeaderLength] = 0x10;

        Enqueue(Options.ResponseLatency + Faults.ExtraLatency, frame);
    }

    private void Enqueue(TimeSpan delay, byte[] frame)
        => _outbox.Writer.TryWrite((delay, frame));

    private async Task PumpAsync()
    {
        await foreach ((TimeSpan delay, byte[] frame) in _outbox.Reader.ReadAllAsync(_stop.Token).ConfigureAwait(false))
        {
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, _time, _stop.Token).ConfigureAwait(false);
            _inbox.Writer.TryWrite(new SysExInput(_time.GetUtcNow(), frame));
        }
    }

    private static Channel<MidiInput> CreateInbox()
        => Channel.CreateUnbounded<MidiInput>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
}
