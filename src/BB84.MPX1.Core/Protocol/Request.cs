using System.Buffers.Binary;
using System.Collections.Immutable;

namespace BB84.MPX1.Core.Protocol;

/// <summary>
/// Request message, class <c>06</c> (PRO-016). Asks the device to answer with a message of
/// <see cref="RequestedClass"/>. Use the static factory methods to build valid requests (spec 4.4).
/// </summary>
/// <param name="Device">The device ID.</param>
/// <param name="RequestedClass">The message class the device shall answer with.</param>
/// <param name="Arguments">The un-nibble-ized argument bytes (words are little-endian).</param>
public sealed record Request(DeviceId Device, MessageClass RequestedClass, ImmutableArray<byte> Arguments) : LexiconMessage(Device)
{
    /// <summary>Program number that addresses the active (running) program.</summary>
    public const ushort ActiveProgram = 0xFFFF;

    /// <inheritdoc/>
    public override MessageClass MessageClass => MessageClass.Request;

    /// <summary>Requests the System Configuration (<c>00</c>).</summary>
    /// <param name="device">The device ID.</param>
    public static Request SystemConfiguration(DeviceId device)
        => new(device, MessageClass.SystemConfiguration, [0, 0, 0]);

    /// <summary>Requests the Parameter Data (<c>01</c>) at an address.</summary>
    /// <param name="device">The device ID.</param>
    /// <param name="address">The control address.</param>
    public static Request ParameterData(DeviceId device, ControlAddress address)
        => new(device, MessageClass.ParameterData, AddressArguments(address));

    /// <summary>Requests the Parameter Display (<c>02</c>) text at an address.</summary>
    /// <param name="device">The device ID.</param>
    /// <param name="address">The control address.</param>
    public static Request ParameterDisplay(DeviceId device, ControlAddress address)
        => new(device, MessageClass.ParameterDisplay, AddressArguments(address));

    /// <summary>Requests the Parameter Type (<c>03</c>) at an address.</summary>
    /// <param name="device">The device ID.</param>
    /// <param name="address">The control address.</param>
    public static Request ParameterType(DeviceId device, ControlAddress address)
        => new(device, MessageClass.ParameterType, AddressArguments(address));

    /// <summary>Requests the Parameter Description (<c>04</c>) of a parameter type.</summary>
    /// <param name="device">The device ID.</param>
    /// <param name="parameterType">The parameter type number.</param>
    public static Request ParameterDescription(DeviceId device, ushort parameterType)
        => new(device, MessageClass.ParameterDescription, Word(parameterType));

    /// <summary>Requests the Parameter Label (<c>05</c>) at an address.</summary>
    /// <param name="device">The device ID.</param>
    /// <param name="address">The control address.</param>
    public static Request ParameterLabel(DeviceId device, ControlAddress address)
        => new(device, MessageClass.ParameterLabel, AddressArguments(address));

    /// <summary>Requests a Handshake (<c>12</c>) answer.</summary>
    /// <param name="device">The device ID.</param>
    /// <param name="command">The handshake command.</param>
    public static Request Handshake(DeviceId device, HandshakeCommand command)
        => new(device, MessageClass.Handshake, [(byte)command]);

    /// <summary>Requests the Database Dump (<c>16</c>).</summary>
    /// <param name="device">The device ID.</param>
    public static Request DatabaseDump(DeviceId device)
        => new(device, MessageClass.DatabaseDump, [0, 0, 0]);

    /// <summary>Requests Report Effect Parameters (<c>18</c>).</summary>
    /// <param name="device">The device ID.</param>
    /// <param name="effectType">The effect or controller type.</param>
    /// <param name="algorithm">The algorithm number (ignored for controller types 6–16).</param>
    public static Request EffectParameters(DeviceId device, EffectType effectType, byte algorithm)
        => new(device, MessageClass.EffectParameters, [(byte)effectType, algorithm, 0]);

    /// <summary>Requests Report All Effect Parameters (<c>19</c>) of a program.</summary>
    /// <param name="device">The device ID.</param>
    /// <param name="program">Program number 0–249 or <see cref="ActiveProgram"/>.</param>
    public static Request AllEffectParameters(DeviceId device, ushort program)
        => new(device, MessageClass.AllEffectParameters, ProgramArguments(program));

    /// <summary>Requests Program Information (<c>1A</c>).</summary>
    /// <param name="device">The device ID.</param>
    /// <param name="program">Program number 0–249.</param>
    public static Request ProgramInformation(DeviceId device, ushort program)
        => new(device, MessageClass.ProgramInformation, ProgramArguments(program));

    /// <summary>Requests a Program Dump (<c>1B</c>).</summary>
    /// <param name="device">The device ID.</param>
    /// <param name="program">Program number 0–249 or <see cref="ActiveProgram"/>.</param>
    public static Request ProgramDump(DeviceId device, ushort program)
        => new(device, MessageClass.ProgramDump, ProgramArguments(program));

    /// <summary>Interprets the arguments as a control address (classes 01, 02, 03, 05).</summary>
    /// <exception cref="Mpx1FormatException">The arguments do not form a control address.</exception>
    public ControlAddress GetControlAddress()
    {
        NibbleWriter writer = new();
        writer.WriteBytes(Arguments.AsSpan());
        byte[] nibbles = writer.ToArray();
        NibbleReader reader = new(nibbles, Mpx1Frame.HeaderLength + 2);
        ControlAddress address = reader.ReadControlAddress();
        reader.EnsureEnd();
        return address;
    }

    /// <summary>Reads the little-endian word at the given argument byte offset.</summary>
    /// <param name="offset">Byte offset into <see cref="Arguments"/>.</param>
    public ushort GetWord(int offset)
    {
        if (offset < 0 || offset + 2 > Arguments.Length)
            throw new Mpx1FormatException(new Mpx1FormatError(Mpx1Frame.HeaderLength + 2 + (offset * 2), "Request argument word out of range"));
        return BinaryPrimitives.ReadUInt16LittleEndian(Arguments.AsSpan(offset, 2));
    }

    internal override void WritePayload(NibbleWriter writer)
    {
        writer.WriteByte((byte)RequestedClass);
        writer.WriteBytes(Arguments.AsSpan());
    }

    internal static Request Read(DeviceId device, ref NibbleReader reader)
    {
        MessageClass requested = (MessageClass)reader.ReadByte();
        if (reader.RemainingNibbles % 2 != 0)
            throw new Mpx1FormatException(new Mpx1FormatError(reader.FrameOffset, "Odd number of argument nibbles"));
        byte[] arguments = reader.ReadBytes(reader.RemainingNibbles / 2);
        return new(device, requested, [.. arguments]);
    }

    private static ImmutableArray<byte> Word(ushort value) => [(byte)value, (byte)(value >> 8)];

    private static ImmutableArray<byte> ProgramArguments(ushort program) => [(byte)program, (byte)(program >> 8), 0];

    private static ImmutableArray<byte> AddressArguments(ControlAddress address)
    {
        ImmutableArray<byte>.Builder builder = ImmutableArray.CreateBuilder<byte>(2 + (address.Depth * 2));
        builder.Add((byte)address.Depth);
        builder.Add((byte)(address.Depth >> 8));
        foreach (ushort level in address.Levels)
        {
            builder.Add((byte)level);
            builder.Add((byte)(level >> 8));
        }
        return builder.MoveToImmutable();
    }
}

/// <summary>Handshake message, class <c>12</c> (PRO-017).</summary>
/// <param name="Device">The device ID.</param>
/// <param name="Command">The handshake command.</param>
public sealed record Handshake(DeviceId Device, HandshakeCommand Command) : LexiconMessage(Device)
{
    /// <inheritdoc/>
    public override MessageClass MessageClass => MessageClass.Handshake;

    internal override void WritePayload(NibbleWriter writer) => writer.WriteByte((byte)Command);

    internal static Handshake Read(DeviceId device, ref NibbleReader reader)
    {
        // The source document's example sends only the low nibble (spec section 9, E2); accept it.
        byte command = reader.RemainingNibbles == 1 ? reader.ReadNibble() : reader.ReadByte();
        return new(device, (HandshakeCommand)command);
    }
}
