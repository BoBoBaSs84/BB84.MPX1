using System.Diagnostics.CodeAnalysis;

namespace BB84.MPX1.Core.Protocol;

/// <summary>
/// Decodes and encodes MPX 1 SysEx frames (PRO-004, PRO-005, PRO-009).
/// </summary>
public static class Mpx1Codec
{
    /// <summary>Encodes a message as a complete SysEx frame.</summary>
    /// <param name="message">The message.</param>
    public static byte[] Encode(Mpx1Message message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return message.ToBytes();
    }

    /// <summary>Decodes a complete SysEx frame.</summary>
    /// <param name="frame">The frame, <c>F0</c> through <c>F7</c>.</param>
    /// <exception cref="Mpx1FormatException">The frame is malformed.</exception>
    public static Mpx1Message Decode(ReadOnlySpan<byte> frame)
    {
        ValidateEnvelope(frame);

        if (frame[1] is Mpx1Frame.UniversalNonRealTime or 0xFE)
            return DecodeUniversal(frame);

        return DecodeLexicon(frame);
    }

    /// <summary>Tries to decode a complete SysEx frame without throwing.</summary>
    /// <param name="frame">The frame, <c>F0</c> through <c>F7</c>.</param>
    /// <param name="message">The decoded message on success.</param>
    /// <param name="error">The error on failure.</param>
    /// <returns><see langword="true"/> when the frame was decoded.</returns>
    public static bool TryDecode(ReadOnlySpan<byte> frame, [NotNullWhen(true)] out Mpx1Message? message, [NotNullWhen(false)] out Mpx1FormatError? error)
    {
        try
        {
            message = Decode(frame);
            error = null;
            return true;
        }
        catch (Mpx1FormatException ex)
        {
            message = null;
            error = ex.Error;
            return false;
        }
    }

    private static void ValidateEnvelope(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < 3)
            throw Error(0, "Frame too short");
        if (frame[0] != Mpx1Frame.SysExStart)
            throw Error(0, "Frame does not start with F0");
        if (frame[^1] != Mpx1Frame.SysExEnd)
            throw Error(frame.Length - 1, "Frame does not end with F7");
        for (int i = 1; i < frame.Length - 1; i++)
        {
            if (frame[i] > 0x7F && !(i == 1 && frame[i] == 0xFE))
                throw Error(i, $"Status byte 0x{frame[i]:X2} inside SysEx data");
        }
    }

    private static Mpx1Message DecodeUniversal(ReadOnlySpan<byte> frame)
    {
        // Byte 1 may be 7E (MIDI spec) or FE (as misprinted in the source document, spec section 9 E1).
        if (frame.Length < 6 || frame[3] != 0x06)
            throw Error(3, "Unsupported universal message");

        DeviceId device = new(frame[2]);
        if (frame[4] == 0x01 && frame.Length == DeviceInquiry.FrameLength)
            return new DeviceInquiry(device);

        if (frame[4] == 0x02 && frame.Length == IdentityReply.FrameLength)
        {
            if (frame[5] != Mpx1Frame.LexiconId)
                throw Error(5, "Identity reply is not from a Lexicon device");
            if (frame[8] != Mpx1Frame.ProductId)
                throw Error(8, "Identity reply is not from an MPX 1");
            return new IdentityReply(device, frame[10], frame[11], frame[12]);
        }

        throw Error(4, "Unsupported universal message");
    }

    private static LexiconMessage DecodeLexicon(ReadOnlySpan<byte> frame)
    {
        if (frame[1] != Mpx1Frame.LexiconId)
            throw Error(1, "Not a Lexicon frame");
        if (frame.Length < Mpx1Frame.HeaderLength + 1)
            throw Error(0, "Frame too short");
        if (frame[2] != Mpx1Frame.ProductId)
            throw Error(2, "Not an MPX 1 frame");

        DeviceId device = new(frame[3]);
        byte rawClass = frame[4];
        ReadOnlySpan<byte> payload = frame[Mpx1Frame.HeaderLength..^1];
        MessageClass messageClass = (MessageClass)rawClass;

        if (!IsInterpreted(messageClass))
            return new RawLexiconMessage(device, rawClass, payload.ToArray());

        for (int i = 0; i < payload.Length; i++)
        {
            if (payload[i] > 0x0F)
                throw Error(Mpx1Frame.HeaderLength + i, $"Invalid nibble 0x{payload[i]:X2}");
        }

        NibbleReader reader = new(payload, Mpx1Frame.HeaderLength);
        LexiconMessage message = messageClass switch
        {
            MessageClass.SystemConfiguration => SystemConfiguration.Read(device, ref reader),
            MessageClass.ParameterData => ParameterData.Read(device, ref reader),
            MessageClass.ParameterDisplay => ParameterDisplay.Read(device, ref reader),
            MessageClass.ParameterType => ParameterType.Read(device, ref reader),
            MessageClass.ParameterDescription => ParameterDescription.Read(device, ref reader),
            MessageClass.ParameterLabel => ParameterLabel.Read(device, ref reader),
            MessageClass.Request => Request.Read(device, ref reader),
            MessageClass.Handshake => Handshake.Read(device, ref reader),
            MessageClass.DatabaseDump => DatabaseDump.Read(device, ref reader),
            MessageClass.EffectParameters => EffectParameters.Read(device, ref reader),
            MessageClass.AllEffectParameters => AllEffectParameters.Read(device, ref reader),
            MessageClass.ProgramInformation => ProgramInformation.Read(device, ref reader),
            MessageClass.ProgramDump => ProgramDump.Read(device, ref reader),
            _ => throw new System.Diagnostics.UnreachableException(),
        };
        reader.EnsureEnd();
        return message;
    }

    private static bool IsInterpreted(MessageClass messageClass)
        => messageClass is MessageClass.SystemConfiguration
            or MessageClass.ParameterData
            or MessageClass.ParameterDisplay
            or MessageClass.ParameterType
            or MessageClass.ParameterDescription
            or MessageClass.ParameterLabel
            or MessageClass.Request
            or MessageClass.Handshake
            or MessageClass.DatabaseDump
            or MessageClass.EffectParameters
            or MessageClass.AllEffectParameters
            or MessageClass.ProgramInformation
            or MessageClass.ProgramDump;

    private static Mpx1FormatException Error(int offset, string reason) => new(new Mpx1FormatError(offset, reason));
}
