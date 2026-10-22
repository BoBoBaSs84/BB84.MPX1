using System.Globalization;

using BB84.MPX1.Core.Programs;
using BB84.MPX1.Core.Protocol;
using BB84.MPX1.Simulator.Fixtures;

namespace BB84.MPX1.Simulator;

// Answers to Request messages and the parameter lookups they need.
public sealed partial class Mpx1Simulator
{
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
        => (int)effect >= ProgramData.EffectBlockCount ? ControllerList(effect) : AlgorithmList(effect, algorithm);

    private EffectParameterList ControllerList(EffectType effect)
    {
        ushort[] slots = new ushort[EffectParameterList.SlotCount];
        FixtureController? controller = _fixture.Controllers.FirstOrDefault(c => c.EffectType == (byte)effect);
        if (controller is null)
            return new EffectParameterList(effect.ToString(), 0, [.. slots]);

        int used = Math.Min(controller.Types.Count, slots.Length);
        for (int i = 0; i < used; i++)
            slots[i] = controller.Types[i];
        return new EffectParameterList(controller.Name, (byte)used, [.. slots]);
    }

    private EffectParameterList AlgorithmList(EffectType effect, byte algorithm)
    {
        ushort[] slots = new ushort[EffectParameterList.SlotCount];
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
}
