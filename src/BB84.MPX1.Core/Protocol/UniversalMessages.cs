namespace BB84.MPX1.Core.Protocol;

/// <summary>
/// Universal Non-Real-Time Device Inquiry: <c>F0 7E &lt;id&gt; 06 01 F7</c> (PRO-024).
/// </summary>
/// <param name="Device">The device ID (MIDI "channel" field), or <see cref="DeviceId.All"/>.</param>
public sealed record DeviceInquiry(DeviceId Device) : Mpx1Message(Device)
{
    internal const int FrameLength = 6;

    private protected override byte[] Encode()
        => [Mpx1Frame.SysExStart, Mpx1Frame.UniversalNonRealTime, Device.Value, 0x06, 0x01, Mpx1Frame.SysExEnd];
}

/// <summary>
/// Identity Reply to a <see cref="DeviceInquiry"/> (PRO-024):
/// <c>F0 7E &lt;id&gt; 06 02 06 00 00 09 00 &lt;major&gt; &lt;minor&gt; &lt;phase&gt; 00 F7</c>.
/// </summary>
/// <param name="Device">The device ID.</param>
/// <param name="MajorVersion">Major software version.</param>
/// <param name="MinorVersion">Minor software version.</param>
/// <param name="DevelopmentPhase">Software development phase.</param>
public sealed record IdentityReply(DeviceId Device, byte MajorVersion, byte MinorVersion, byte DevelopmentPhase) : Mpx1Message(Device)
{
    internal const int FrameLength = 15;

    private protected override byte[] Encode()
        =>
        [
            Mpx1Frame.SysExStart, Mpx1Frame.UniversalNonRealTime, Device.Value, 0x06, 0x02,
            Mpx1Frame.LexiconId, 0x00, 0x00, Mpx1Frame.ProductId, 0x00,
            MajorVersion, MinorVersion, DevelopmentPhase, 0x00, Mpx1Frame.SysExEnd,
        ];
}
