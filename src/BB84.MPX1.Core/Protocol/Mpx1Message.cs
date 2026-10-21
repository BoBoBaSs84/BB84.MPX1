namespace BB84.MPX1.Core.Protocol;

/// <summary>
/// Base type of every SysEx message the MPX 1 sends or receives.
/// </summary>
/// <param name="Device">The device ID carried in the frame.</param>
public abstract record Mpx1Message(DeviceId Device)
{
    /// <summary>Encodes the message as a complete SysEx frame (<c>F0</c> … <c>F7</c>).</summary>
    public byte[] ToBytes() => Encode();

    private protected abstract byte[] Encode();
}

/// <summary>
/// Base type of Lexicon MPX 1 messages with the header <c>F0 06 09 &lt;id&gt; &lt;class&gt;</c> and a nibble-ized payload.
/// </summary>
/// <param name="Device">The device ID carried in the frame.</param>
public abstract record LexiconMessage(DeviceId Device) : Mpx1Message(Device)
{
    /// <summary>Gets the message class (byte 5 of the header).</summary>
    public abstract MessageClass MessageClass { get; }

    internal abstract void WritePayload(NibbleWriter writer);

    private protected sealed override byte[] Encode()
    {
        NibbleWriter writer = new();
        writer.WriteRaw([Mpx1Frame.SysExStart, Mpx1Frame.LexiconId, Mpx1Frame.ProductId, Device.Value, (byte)MessageClass]);
        WritePayload(writer);
        writer.WriteRaw([Mpx1Frame.SysExEnd]);
        return writer.ToArray();
    }
}

/// <summary>
/// An MPX 1 frame with a message class this library does not interpret (PRO-023, PRO-026).
/// The payload is kept exactly as received (still nibble-ized) so it can be passed through unchanged.
/// </summary>
/// <param name="Device">The device ID carried in the frame.</param>
/// <param name="RawClass">The message class byte.</param>
/// <param name="RawPayload">The payload bytes between header and EOX, as received.</param>
public sealed record RawLexiconMessage(DeviceId Device, byte RawClass, ReadOnlyMemory<byte> RawPayload) : LexiconMessage(Device)
{
    /// <inheritdoc/>
    public override MessageClass MessageClass => (MessageClass)RawClass;

    internal override void WritePayload(NibbleWriter writer) => writer.WriteRaw(RawPayload.Span);
}

/// <summary>Constants of the MPX 1 SysEx framing (PRO-004).</summary>
public static class Mpx1Frame
{
    /// <summary>Start of SysEx.</summary>
    public const byte SysExStart = 0xF0;
    /// <summary>End of SysEx (EOX).</summary>
    public const byte SysExEnd = 0xF7;
    /// <summary>Lexicon manufacturer ID.</summary>
    public const byte LexiconId = 0x06;
    /// <summary>MPX 1 product ID.</summary>
    public const byte ProductId = 0x09;
    /// <summary>Universal Non-Real-Time SysEx ID.</summary>
    public const byte UniversalNonRealTime = 0x7E;
    /// <summary>Length of the Lexicon header (<c>F0 06 09 id class</c>).</summary>
    public const int HeaderLength = 5;
}
