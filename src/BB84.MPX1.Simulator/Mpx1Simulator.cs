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
public sealed class Mpx1Simulator : IMidiTransport
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

    private static readonly ControlAddress DisplayAddress = new(1, 8, 1);
    private static readonly ControlAddress LedsAddress = new(1, 8, 2);

    /// <summary>Gets the last Panel Button code received (PAR-007), or <see langword="null"/>.</summary>
    public byte? LastButtonCode { get; private set; }

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
                if (dump.ProgramNumber == Request.ActiveProgram)
                    _active = dump.Program;
                else if (dump.ProgramNumber < _programs.Length)
                    _programs[dump.ProgramNumber] = dump.Program;
                SignalBusy();
                break;
            case DatabaseDump database:
                _database = [.. database.Entries];
                SignalBusy();
                break;
        }
    }

    private void HandleRequest(Request request)
    {
        try
        {
            switch (request.RequestedClass)
            {
                case MessageClass.SystemConfiguration:
                    RespondSystemConfiguration();
                    break;
                case MessageClass.ParameterType:
                    RespondParameterType(request);
                    break;
                case MessageClass.ParameterDescription:
                    RespondParameterDescription(request);
                    break;
                case MessageClass.ParameterLabel:
                    RespondParameterLabel(request);
                    break;
                case MessageClass.ParameterData:
                    RespondParameterData(request);
                    break;
                case MessageClass.ParameterDisplay:
                    RespondParameterDisplay(request);
                    break;
                case MessageClass.Handshake:
                    Respond(new Handshake(Options.Device, HandshakeCommand.ImAlive));
                    break;
                case MessageClass.DatabaseDump:
                    Respond(new DatabaseDump(Options.Device, [.. _database]));
                    break;
                case MessageClass.EffectParameters:
                    RespondEffectParameters(request);
                    break;
                case MessageClass.AllEffectParameters:
                    RespondAllEffectParameters(request);
                    break;
                case MessageClass.ProgramInformation:
                    RespondProgramInformation(request);
                    break;
                case MessageClass.ProgramDump:
                    RespondProgramDump(request);
                    break;
            }
        }
        catch (Mpx1FormatException)
        {
            // Malformed request arguments: a real unit would ignore them.
        }
    }

    private void RespondSystemConfiguration()
        => Respond(new SystemConfiguration(Options.Device, _fixture.MajorVersion, _fixture.MinorVersion, _fixture.BuildTime, _fixture.BuildDate,
            _fixture.ParameterTypeCount, _fixture.BottomParameter, _fixture.MaxControlLevels));

    private void RespondParameterType(Request request)
    {
        if (_tree.TryGetValue(request.GetControlAddress(), out ushort type))
            Respond(new ParameterType(Options.Device, type));
    }

    private void RespondParameterDescription(Request request)
    {
        ushort requested = request.GetWord(0);
        if (requested < _fixture.ParameterTypeCount)
            Respond(Describe(requested));
    }

    private void RespondParameterLabel(Request request)
    {
        ControlAddress address = request.GetControlAddress();
        if (_tree.TryGetValue(address, out ushort type))
            Respond(new ParameterLabel(Options.Device, Describe(type).Name, address));
    }

    private void RespondParameterData(Request request)
    {
        ControlAddress address = request.GetControlAddress();
        if (_tree.TryGetValue(address, out ushort type) && !Describe(type).IsControlLevel)
            Respond(new ParameterData(Options.Device, ReadValue(address, type), address));
    }

    private void RespondParameterDisplay(Request request)
    {
        ControlAddress address = request.GetControlAddress();
        if (_tree.TryGetValue(address, out ushort type))
            Respond(new ParameterDisplay(Options.Device, FormatValue(address, type), address));
    }

    private void RespondEffectParameters(Request request)
    {
        EffectType effect = (EffectType)request.Arguments[0];
        byte algorithm = request.Arguments[1];
        Respond(new EffectParameters(Options.Device, effect, algorithm, EffectList(effect, algorithm)));
    }

    private void RespondAllEffectParameters(Request request)
    {
        ushort number = request.GetWord(0);
        if (ProgramOrActive(number) is not { } program)
            return;
        Respond(new AllEffectParameters(Options.Device, number,
            [.. Enumerable.Range(0, ProgramData.EffectBlockCount).Select(e => EffectList((EffectType)e, program.Algorithms[e]))]));
    }

    private void RespondProgramInformation(Request request)
    {
        ushort number = request.GetWord(0);
        if (number < _programs.Length)
            Respond(new ProgramInformation(Options.Device, number, _programs[number].Name, _programs[number].Algorithms));
    }

    private void RespondProgramDump(Request request)
    {
        ushort number = request.GetWord(0);
        if (ProgramOrActive(number) is { } program)
            Respond(new ProgramDump(Options.Device, number, program));
    }

    private void WriteParameter(ParameterData data)
    {
        if (!_tree.TryGetValue(data.Address, out ushort type) || Describe(type).IsControlLevel)
            return;

        // Appendix D bug 3: editing parameters of an algorithm that is not loaded can crash the unit.
        if (data.Address.Depth == 4 && data.Address[0] == 0 && data.Address[1] < ProgramData.EffectBlockCount
            && _active.Algorithms[data.Address[1]] != data.Address[2])
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

    // A small model of the user interface, enough for the front panel mirror (UI-007): the display reports the
    // last button event, Bypass toggles its LED, the encoder counts the right-hand digits.
    private void PressButton(byte code)
    {
        LastButtonCode = code;
        string text = code switch
        {
            PanelButtons.EncoderClockwise => "Encoder +",
            PanelButtons.EncoderCounterClockwise => "Encoder -",
            PanelButtons.TapV110 => "Tap",
            PanelButtons.ToggleABV110 => "A/B",
            _ => PanelButtons.All.FirstOrDefault(b => b.Press == code) is { } pressed ? $"{pressed.Name} press"
                : PanelButtons.All.FirstOrDefault(b => b.Hold == code) is { } held ? $"{held.Name} hold"
                : PanelButtons.All.FirstOrDefault(b => b.Release == code) is { } released ? $"{released.Name}"
                : $"Code 0x{code:X2}",
        };
        _values[DisplayAddress] = DisplayText.Encode("  MPX 1  V1.10", text);

        if (_values.TryGetValue(LedsAddress, out byte[]? leds) && leds.Length == LedState.Size)
        {
            LedState state = LedState.FromBytes(leds);
            if (code == PanelButtons.Find("Bypass").Release)
                state = state.With(PanelLed.Bypass, !state.IsOn(PanelLed.Bypass));
            _values[LedsAddress] = [.. state.Data];
        }
    }

    private ParameterDescription Describe(ushort type)
        => _descriptions.TryGetValue(type, out ParameterDescription? description)
            ? description
            : new ParameterDescription(Options.Device, type, $"Type {type:X4}", 1, ParameterAttributes.None, null, [new UnitLimit(0, 127, 0x05)]);

    private byte[] ReadValue(ControlAddress address, ushort type)
    {
        if (type == DefaultFixture.Types.PatchesDump)
            return _active.GetPatchesDump();
        if (type == DefaultFixture.Types.SoftRowDump)
            return _active.GetSoftRowDump();
        if (_values.TryGetValue(address, out byte[]? value))
            return value;
        ParameterDescription description = Describe(type);
        int size = description.Size + (description.OptionType is ushort option ? Describe(option).Size : 0);
        return new byte[size];
    }

    private string FormatValue(ControlAddress address, ushort type)
    {
        ParameterDescription description = Describe(type);
        if (description.IsControlLevel)
            return description.Name.Trim();
        byte[] value = ReadValue(address, type);
        return description.Size switch
        {
            1 => (description.Units.Length > 0 && description.Units[0].Minimum < 0 ? (int)(sbyte)value[0] : value[0]).ToString(CultureInfo.InvariantCulture),
            2 => BitConverter.ToInt16(value, 0).ToString(CultureInfo.InvariantCulture),
            _ => "Dump",
        };
    }

    private EffectParameterList EffectList(EffectType effect, byte algorithm)
    {
        ushort[] slots = new ushort[EffectParameterList.SlotCount];
        if ((int)effect >= ProgramData.EffectBlockCount)
        {
            FixtureController? controller = _fixture.Controllers.FirstOrDefault(c => c.EffectType == (byte)effect);
            if (controller is null)
                return new EffectParameterList(effect.ToString(), 0, [.. slots]);
            int used = Math.Min(controller.Types.Count, slots.Length);
            for (int i = 0; i < used; i++)
                slots[i] = controller.Types[i];
            return new EffectParameterList(controller.Name, (byte)used, [.. slots]);
        }

        ControlAddress algorithmAddress = new(0, (ushort)effect, algorithm);
        if (!_tree.TryGetValue(algorithmAddress, out ushort algorithmType))
            return new EffectParameterList(string.Empty, 0, [.. slots]);

        byte count = 0;
        while (count < slots.Length && _tree.TryGetValue(algorithmAddress.Child(count), out ushort parameterType))
            slots[count++] = parameterType;
        return new EffectParameterList(Describe(algorithmType).Name.Trim(), count, [.. slots]);
    }

    private ProgramData? ProgramOrActive(ushort number)
        => number == Request.ActiveProgram ? _active : number < _programs.Length ? _programs[number] : null;

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
