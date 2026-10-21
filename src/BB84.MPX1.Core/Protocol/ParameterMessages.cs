using System.Collections.Immutable;

namespace BB84.MPX1.Core.Protocol;

/// <summary>System Configuration message, class <c>00</c> (PRO-010).</summary>
/// <param name="Device">The device ID.</param>
/// <param name="MajorVersion">Software major version (left of the decimal point).</param>
/// <param name="MinorVersion">Software minor version (right of the decimal point).</param>
/// <param name="BuildTime">8-character build time, <c>hh:mm:ss</c>.</param>
/// <param name="BuildDate">11-character build date, for example <c>May 10 1996</c>.</param>
/// <param name="ParameterTypeCount">Total number of parameter types.</param>
/// <param name="BottomParameter">The "bottom" parameter type number.</param>
/// <param name="MaxControlLevels">Maximum number of control levels used.</param>
public sealed record SystemConfiguration(
    DeviceId Device,
    byte MajorVersion,
    byte MinorVersion,
    string BuildTime,
    string BuildDate,
    ushort ParameterTypeCount,
    ushort BottomParameter,
    ushort MaxControlLevels) : LexiconMessage(Device)
{
    internal const int BuildTimeLength = 8;
    internal const int BuildDateLength = 11;

    /// <inheritdoc/>
    public override MessageClass MessageClass => MessageClass.SystemConfiguration;

    internal override void WritePayload(NibbleWriter writer)
    {
        writer.WriteByte(MajorVersion);
        writer.WriteByte(MinorVersion);
        writer.WriteFixedString(BuildTime, BuildTimeLength);
        writer.WriteFixedString(BuildDate, BuildDateLength);
        writer.WriteWord(ParameterTypeCount);
        writer.WriteWord(BottomParameter);
        writer.WriteWord(MaxControlLevels);
    }

    internal static SystemConfiguration Read(DeviceId device, ref NibbleReader reader)
        => new(device, reader.ReadByte(), reader.ReadByte(), reader.ReadString(BuildTimeLength),
            reader.ReadString(BuildDateLength), reader.ReadWord(), reader.ReadWord(), reader.ReadWord());
}

/// <summary>Parameter Data message, class <c>01</c> (PRO-011). Carries parameter bytes followed by option bytes.</summary>
/// <param name="Device">The device ID.</param>
/// <param name="Data">Parameter data followed by option data, if any.</param>
/// <param name="Address">Control address of the parameter.</param>
public sealed record ParameterData(DeviceId Device, ReadOnlyMemory<byte> Data, ControlAddress Address) : LexiconMessage(Device)
{
    /// <inheritdoc/>
    public override MessageClass MessageClass => MessageClass.ParameterData;

    internal override void WritePayload(NibbleWriter writer)
    {
        writer.WriteWord(checked((ushort)Data.Length));
        writer.WriteBytes(Data.Span);
        writer.WriteControlAddress(Address);
    }

    internal static ParameterData Read(DeviceId device, ref NibbleReader reader)
    {
        ushort count = reader.ReadWord();
        byte[] data = reader.ReadBytes(count);
        return new(device, data, reader.ReadControlAddress());
    }
}

/// <summary>Parameter Display message, class <c>02</c> (PRO-012): the formatted text of a parameter value.</summary>
/// <param name="Device">The device ID.</param>
/// <param name="Text">The display text.</param>
/// <param name="Address">Control address of the parameter.</param>
public sealed record ParameterDisplay(DeviceId Device, string Text, ControlAddress Address) : LexiconMessage(Device)
{
    /// <inheritdoc/>
    public override MessageClass MessageClass => MessageClass.ParameterDisplay;

    internal override void WritePayload(NibbleWriter writer)
    {
        writer.WriteWord(checked((ushort)Text.Length));
        writer.WriteString(Text);
        writer.WriteControlAddress(Address);
    }

    internal static ParameterDisplay Read(DeviceId device, ref NibbleReader reader)
    {
        ushort count = reader.ReadWord();
        string text = reader.ReadString(count);
        return new(device, text, reader.ReadControlAddress());
    }
}

/// <summary>Parameter Label message, class <c>05</c> (PRO-015): the name of the parameter at an address.</summary>
/// <param name="Device">The device ID.</param>
/// <param name="Label">The label text.</param>
/// <param name="Address">Control address of the parameter.</param>
public sealed record ParameterLabel(DeviceId Device, string Label, ControlAddress Address) : LexiconMessage(Device)
{
    /// <inheritdoc/>
    public override MessageClass MessageClass => MessageClass.ParameterLabel;

    internal override void WritePayload(NibbleWriter writer)
    {
        writer.WriteWord(checked((ushort)Label.Length));
        writer.WriteString(Label);
        writer.WriteControlAddress(Address);
    }

    internal static ParameterLabel Read(DeviceId device, ref NibbleReader reader)
    {
        ushort count = reader.ReadWord();
        string label = reader.ReadString(count);
        return new(device, label, reader.ReadControlAddress());
    }
}

/// <summary>Parameter Type message, class <c>03</c> (PRO-013): the parameter type at the requested address.</summary>
/// <param name="Device">The device ID.</param>
/// <param name="Type">The parameter type number.</param>
public sealed record ParameterType(DeviceId Device, ushort Type) : LexiconMessage(Device)
{
    /// <inheritdoc/>
    public override MessageClass MessageClass => MessageClass.ParameterType;

    internal override void WritePayload(NibbleWriter writer) => writer.WriteWord(Type);

    internal static ParameterType Read(DeviceId device, ref NibbleReader reader) => new(device, reader.ReadWord());
}

/// <summary>One unit/limit triple of a parameter description.</summary>
/// <param name="Minimum">Minimum value (signed word; cast to 8 bits for 1-byte parameters, PRO-008).</param>
/// <param name="Maximum">Maximum value (signed word).</param>
/// <param name="DisplayUnits">Display units type (Appendix B).</param>
public readonly record struct UnitLimit(short Minimum, short Maximum, ushort DisplayUnits);

/// <summary>Parameter Description message, class <c>04</c> (PRO-014).</summary>
/// <param name="Device">The device ID.</param>
/// <param name="Type">The parameter type number.</param>
/// <param name="Name">The parameter name (not null-terminated, often space padded).</param>
/// <param name="Size">Size of the parameter in bytes, excluding option bytes.</param>
/// <param name="Flags">Control flags.</param>
/// <param name="OptionType">The option parameter type, or <see langword="null"/> when there is none (<c>0xFFFF</c>).</param>
/// <param name="Units">Unit/limit triples; more than one when the option selects the unit.</param>
public sealed record ParameterDescription(
    DeviceId Device,
    ushort Type,
    string Name,
    ushort Size,
    ParameterAttributes Flags,
    ushort? OptionType,
    ImmutableArray<UnitLimit> Units) : LexiconMessage(Device)
{
    /// <summary>Option type value meaning "no option".</summary>
    public const ushort NoOption = 0xFFFF;

    /// <inheritdoc/>
    public override MessageClass MessageClass => MessageClass.ParameterDescription;

    /// <summary>Gets a value indicating whether the node is a branch with children.</summary>
    public bool IsControlLevel => Flags.HasFlag(ParameterAttributes.ControlLevel);

    internal override void WritePayload(NibbleWriter writer)
    {
        writer.WriteWord(Type);
        writer.WriteByte(checked((byte)Name.Length));
        writer.WriteString(Name);
        writer.WriteWord(Size);
        writer.WriteByte((byte)Flags);
        writer.WriteWord(OptionType ?? NoOption);
        writer.WriteByte(checked((byte)Units.Length));
        foreach (UnitLimit unit in Units)
        {
            writer.WriteWord(unchecked((ushort)unit.Minimum));
            writer.WriteWord(unchecked((ushort)unit.Maximum));
            writer.WriteWord(unit.DisplayUnits);
        }
    }

    internal static ParameterDescription Read(DeviceId device, ref NibbleReader reader)
    {
        ushort type = reader.ReadWord();
        byte nameLength = reader.ReadByte();
        string name = reader.ReadString(nameLength);
        ushort size = reader.ReadWord();
        ParameterAttributes flags = (ParameterAttributes)reader.ReadByte();
        ushort option = reader.ReadWord();
        byte unitCount = reader.ReadByte();
        ImmutableArray<UnitLimit>.Builder units = ImmutableArray.CreateBuilder<UnitLimit>(unitCount);
        for (int i = 0; i < unitCount; i++)
            units.Add(new UnitLimit(unchecked((short)reader.ReadWord()), unchecked((short)reader.ReadWord()), reader.ReadWord()));
        return new(device, type, name, size, flags, option == NoOption ? null : option, units.MoveToImmutable());
    }
}
