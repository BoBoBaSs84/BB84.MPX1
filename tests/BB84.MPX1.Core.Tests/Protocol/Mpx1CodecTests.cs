using System.Collections.Immutable;

using BB84.MPX1.Core.Programs;
using BB84.MPX1.Core.Protocol;

namespace BB84.MPX1.Core.Tests.Protocol;

[TestClass]
public sealed class Mpx1CodecTests
{
    private static readonly DeviceId Device = TestData.Device;

    public static IEnumerable<object[]> AllMessages()
    {
        yield return [new SystemConfiguration(Device, 1, 10, "17:51:03", "May 10 1996", 0x01C0, 0x0164, 5)];
        yield return [new ParameterData(Device, new byte[] { 0x0A, 0x01 }, new ControlAddress(0, 0, 1, 2))];
        yield return [new ParameterDisplay(Device, "\u0001Gain +3dB", new ControlAddress(0, 2, 1, 2))];
        yield return [new ParameterType(Device, 0x014D)];
        yield return [new ParameterDescription(Device, 0x002D, "Mix  ", 1, ParameterAttributes.Patchable | ParameterAttributes.Automation, null,
            [new UnitLimit(0, 100, 0x00)])];
        yield return [new ParameterDescription(Device, 0x0010, "Rate ", 2, ParameterAttributes.Patchable, 0x0042,
            [new UnitLimit(0, 5000, 0x05), new UnitLimit(-1, -2, 0x06)])];
        yield return [new ParameterLabel(Device, "1-Band (M)", new ControlAddress(0, 2, 1))];
        yield return [Request.ParameterType(Device, new ControlAddress(1, 2, 10))];
        yield return [Request.ProgramDump(Device, Request.ActiveProgram)];
        yield return [new Handshake(Device, HandshakeCommand.Ready)];
        yield return [new DatabaseDump(Device, [.. Enumerable.Range(0, DatabaseDump.ProgramCount)
            .Select(i => new ProgramCategories((EffectCategories)(i * 7 & 0x0FFF), (SourceCategories)i))])];
        yield return [new EffectParameters(Device, EffectType.Chorus, 4, ParameterList("Chorus 4", 3))];
        yield return [new AllEffectParameters(Device, Request.ActiveProgram,
            [.. Enumerable.Range(0, ProgramData.EffectBlockCount).Select(i => ParameterList($"Effect {i}", (byte)(i + 1)))])];
        yield return [new ProgramInformation(Device, 208, "My Program  ", [1, 2, 3, 4, 5, 6])];
        yield return [new ProgramDump(Device, 249, TestData.Program())];
        yield return [new DeviceInquiry(Device)];
        yield return [new IdentityReply(Device, 1, 10, 0)];
    }

    [TestMethod]
    [DynamicData(nameof(AllMessages))]
    [Description("AC-010, PRO-004, PRO-006, PRO-009, PRO-012, PRO-013, PRO-014, PRO-015: every message class decodes back to identical bytes")]
    public void EncodeDecodeEncode_IsByteIdentical(Mpx1Message message)
    {
        byte[] first = message.ToBytes();
        Mpx1Message decoded = Mpx1Codec.Decode(first);

        Assert.AreEqual(message.GetType(), decoded.GetType());
        Assert.AreEqual(message.Device, decoded.Device);
        CollectionAssert.AreEqual(first, decoded.ToBytes());
    }

    [TestMethod]
    [Description("AC-008, PRO-011")]
    public void Decode_DocumentedParameterDataResponse()
    {
        byte[] frame = Hex.Parse("F0 06 09 00 01 01 00 00 00 00 00 04 00 00 00 00 00 00 00 02 00 00 00 01 00 00 00 02 00 00 00 F7");

        ParameterData data = (ParameterData)Mpx1Codec.Decode(frame);

        CollectionAssert.AreEqual(new byte[] { 0x00 }, data.Data.ToArray());
        Assert.AreEqual(new ControlAddress(0, 2, 1, 2), data.Address);
    }

    [TestMethod]
    [Description("PRO-017, spec section 9 E2: single-nibble handshake from the document's example")]
    public void Decode_ShortHandshake_IsAccepted()
    {
        Handshake handshake = (Handshake)Mpx1Codec.Decode(Hex.Parse("F0 06 09 00 12 01 F7"));
        Assert.AreEqual(HandshakeCommand.AreYouThere, handshake.Command);
    }

    [TestMethod]
    [Description("PRO-024, spec section 9 E1: identity reply with misprinted FE header")]
    public void Decode_IdentityReply_AcceptsFeHeader()
    {
        IdentityReply reply = (IdentityReply)Mpx1Codec.Decode(Hex.Parse("F0 FE 00 06 02 06 00 00 09 00 01 0A 00 00 F7"));
        Assert.AreEqual((byte)1, reply.MajorVersion);
        Assert.AreEqual((byte)10, reply.MinorVersion);
    }

    [TestMethod]
    [Description("PRO-023, PRO-026: unknown classes are passed through unchanged")]
    public void Decode_CompactProgramDump_IsPassedThroughRaw()
    {
        byte[] frame = Hex.Parse("F0 06 09 00 1C 01 02 7F 03 F7");

        RawLexiconMessage raw = (RawLexiconMessage)Mpx1Codec.Decode(frame);

        Assert.AreEqual(MessageClass.CompactProgramDump, raw.MessageClass);
        CollectionAssert.AreEqual(frame, raw.ToBytes());
    }

    [TestMethod]
    [Description("AC-009, PRO-005, SEC-001")]
    public void TryDecode_ByteCountLargerThanFrame_ReturnsErrorWithOffset()
    {
        // Parameter Data claiming 0xFF00 data bytes.
        byte[] frame = Hex.Parse("F0 06 09 00 01 00 00 00 0F 00 00 F7");

        bool ok = Mpx1Codec.TryDecode(frame, out Mpx1Message? message, out Mpx1FormatError? error);

        Assert.IsFalse(ok);
        Assert.IsNull(message);
        Assert.IsNotNull(error);
        Assert.AreEqual(9, error.Offset);
    }

    [TestMethod]
    [DataRow("F0 06 09 00 03 00 00 00 F7", 5, DisplayName = "truncated word")]
    [DataRow("F0 06 09 00 03 00 00 00 00 00 F7", 9, DisplayName = "trailing nibble")]
    [DataRow("F0 06 09 00 03 10 00 00 00 F7", 5, DisplayName = "invalid nibble")]
    [DataRow("F0 06 0A 00 03 00 00 00 00 F7", 2, DisplayName = "wrong product")]
    [DataRow("F0 07 09 00 03 00 00 00 00 F7", 1, DisplayName = "wrong manufacturer")]
    [DataRow("F0 06 09 00 03 00 00 00 00", 8, DisplayName = "missing EOX")]
    [DataRow("F0 06 09 00 01 00 00 00 00 0F 0F 00 00 F7", 9, DisplayName = "control address too deep")]
    [Description("PRO-003, PRO-005")]
    public void TryDecode_MalformedFrames_ReportOffset(string hex, int offset)
    {
        bool ok = Mpx1Codec.TryDecode(Hex.Parse(hex), out _, out Mpx1FormatError? error);

        Assert.IsFalse(ok);
        Assert.AreEqual(offset, error!.Offset, error.Reason);
    }

    [TestMethod]
    [Description("PRO-007: custom LCD characters 0x00-0x07 are preserved, not treated as terminators")]
    public void ParameterDisplay_PreservesCustomCharacters()
    {
        ParameterDisplay display = new(Device, "A\0B\u0007", ControlAddress.Root);

        ParameterDisplay decoded = (ParameterDisplay)Mpx1Codec.Decode(display.ToBytes());

        Assert.AreEqual("A\0B\u0007", decoded.Text);
    }

    [TestMethod]
    [Description("PRO-008: limits are signed words")]
    public void ParameterDescription_SignedLimits()
    {
        ParameterDescription description = new(Device, 1, "Level", 1, ParameterAttributes.None, null, [new UnitLimit(-96, 0, 0x01)]);

        ParameterDescription decoded = (ParameterDescription)Mpx1Codec.Decode(description.ToBytes());

        Assert.AreEqual((short)-96, decoded.Units[0].Minimum);
        Assert.IsNull(decoded.OptionType);
    }

    [TestMethod]
    [Description("PRO-022: Program Dump frame is 844 bytes")]
    public void ProgramDump_FrameLength()
    {
        Assert.HasCount(844, new ProgramDump(Device, 0, TestData.Program()).ToBytes());
        Assert.HasCount(ProgramDump.FrameLength, new ProgramDump(Device, 0, TestData.Program()).ToBytes());
    }

    [TestMethod]
    [Description("PRO-018: Database Dump frame is 1506 bytes")]
    public void DatabaseDump_FrameLength()
        => Assert.HasCount(1506, new DatabaseDump(Device, [.. Enumerable.Repeat(default(ProgramCategories), DatabaseDump.ProgramCount)]).ToBytes());

    [TestMethod]
    [Description("PRO-019, PRO-020: frame lengths 154 and 874")]
    public void EffectParameterFrames_Lengths()
    {
        Assert.HasCount(154, new EffectParameters(Device, EffectType.Pitch, 0, ParameterList("x", 1)).ToBytes());
        Assert.HasCount(874, new AllEffectParameters(Device, 0, [.. Enumerable.Repeat(ParameterList("x", 1), 6)]).ToBytes());
    }

    [TestMethod]
    [Description("PRO-010, PRO-021: frame lengths 60 and 46")]
    public void InfoFrames_Lengths()
    {
        Assert.HasCount(60, new SystemConfiguration(Device, 1, 0, "", "", 0, 0, 0).ToBytes());
        Assert.HasCount(46, new ProgramInformation(Device, 0, "", [0, 0, 0, 0, 0, 0]).ToBytes());
    }

    [TestMethod]
    [Description("PRO-019")]
    public void EffectParameterList_ParameterTypes_UsesCount()
    {
        EffectParameterList list = ParameterList("Detune (M)", 4);
        Assert.HasCount(4, list.ParameterTypes);
        Assert.AreEqual((ushort)0x0100, list.ParameterTypes[0]);
    }

    private static EffectParameterList ParameterList(string name, byte count)
        => new(name, count, [.. Enumerable.Range(0, EffectParameterList.SlotCount).Select(i => (ushort)(0x0100 + i))]);
}
