using BB84.MPX1.Core.Protocol;

namespace BB84.MPX1.Core.Tests.Protocol;

/// <summary>Byte vectors taken from the MPX 1 MIDI Implementation document (spec section 5).</summary>
[TestClass]
public sealed class RequestEncodingTests
{
    private static readonly DeviceId Device0 = new(0);
    private static readonly ControlAddress EqGain = new(0, 2, 1, 2);

    private static void AssertBytes(string expected, Mpx1Message message)
        => Assert.AreEqual(expected, Hex.Format(message.ToBytes()));

    [TestMethod]
    [Description("PRO-025, spec 9 E6: the undocumented Memory Tool request class 10 has no message class, so it can never be sent")]
    public void MemoryTool_IsNeverRequested()
    {
        Assert.IsFalse(Enum.IsDefined((MessageClass)0x10));
        Assert.IsFalse(Enum.GetValues<MessageClass>().Contains((MessageClass)0x10));
    }

    [TestMethod]
    [Description("AC-002, PRO-016")]
    public void ParameterType_EqGain()
        => AssertBytes("F0 06 09 00 06 03 00 04 00 00 00 00 00 00 00 02 00 00 00 01 00 00 00 02 00 00 00 F7",
            Request.ParameterType(Device0, EqGain));

    [TestMethod]
    [Description("PRO-016: Parameter Data request example")]
    public void ParameterData_EqGain()
        => AssertBytes("F0 06 09 00 06 01 00 04 00 00 00 00 00 00 00 02 00 00 00 01 00 00 00 02 00 00 00 F7",
            Request.ParameterData(Device0, EqGain));

    [TestMethod]
    [Description("PRO-016: Parameter Display request example")]
    public void ParameterDisplay_EqGain()
        => AssertBytes("F0 06 09 00 06 02 00 04 00 00 00 00 00 00 00 02 00 00 00 01 00 00 00 02 00 00 00 F7",
            Request.ParameterDisplay(Device0, EqGain));

    [TestMethod]
    [Description("PRO-016: Parameter Label request example (A:0 B:2 C:1)")]
    public void ParameterLabel_OneBandEq()
        => AssertBytes("F0 06 09 00 06 05 00 03 00 00 00 00 00 00 00 02 00 00 00 01 00 00 00 F7",
            Request.ParameterLabel(Device0, new ControlAddress(0, 2, 1)));

    [TestMethod]
    [Description("PRO-016: Display dump request example (A:1 B:8 C:1)")]
    public void ParameterData_DisplayDump()
        => AssertBytes("F0 06 09 00 06 01 00 03 00 00 00 01 00 00 00 08 00 00 00 01 00 00 00 F7",
            Request.ParameterData(Device0, new ControlAddress(1, 8, 1)));

    [TestMethod]
    [Description("PRO-016: root Parameter Type request example")]
    public void ParameterType_Root()
        => AssertBytes("F0 06 09 00 06 03 00 00 00 00 00 F7", Request.ParameterType(Device0, ControlAddress.Root));

    [TestMethod]
    [Description("AC-003, PRO-016")]
    public void ParameterDescription_0x0125()
        => AssertBytes("F0 06 09 00 06 04 00 05 02 01 00 F7", Request.ParameterDescription(Device0, 0x0125));

    [TestMethod]
    [Description("AC-004, PRO-016")]
    public void SystemConfiguration()
        => AssertBytes("F0 06 09 00 06 00 00 00 00 00 00 00 00 F7", Request.SystemConfiguration(Device0));

    [TestMethod]
    [Description("PRO-016: Database Dump request example")]
    public void DatabaseDump()
        => AssertBytes("F0 06 09 00 06 06 01 00 00 00 00 00 00 F7", Request.DatabaseDump(Device0));

    [TestMethod]
    [Description("AC-005, PRO-016")]
    public void ProgramInformation_Program8()
        => AssertBytes("F0 06 09 00 06 0A 01 08 00 00 00 00 00 F7", Request.ProgramInformation(Device0, 8));

    [TestMethod]
    [Description("AC-005a, PRO-016")]
    public void ProgramDump_Program8()
        => AssertBytes("F0 06 09 00 06 0B 01 08 00 00 00 00 00 F7", Request.ProgramDump(Device0, 8));

    [TestMethod]
    [Description("AC-006, PRO-016")]
    public void EffectParameters_ChorusAlgorithm4()
        => AssertBytes("F0 06 09 00 06 08 01 01 00 04 00 00 00 F7", Request.EffectParameters(Device0, EffectType.Chorus, 4));

    [TestMethod]
    [Description("PRO-016: Report All Effect Parameters, program 1 and active program")]
    public void AllEffectParameters_Examples()
    {
        AssertBytes("F0 06 09 00 06 09 01 00 00 00 00 00 00 F7", Request.AllEffectParameters(Device0, 0));
        AssertBytes("F0 06 09 00 06 09 01 0F 0F 0F 0F 00 00 F7", Request.AllEffectParameters(Device0, Request.ActiveProgram));
    }

    [TestMethod]
    [Description("AC-007, PRO-017")]
    public void Handshake_AreYouThere()
        => AssertBytes("F0 06 09 00 12 01 00 F7", new Handshake(Device0, HandshakeCommand.AreYouThere));

    [TestMethod]
    [Description("PRO-024")]
    public void DeviceInquiry()
        => AssertBytes("F0 7E 00 06 01 F7", new DeviceInquiry(Device0));

    [TestMethod]
    [Description("PRO-016")]
    public void GetControlAddress_RoundTripsFactoryAddress()
        => Assert.AreEqual(EqGain, Request.ParameterType(Device0, EqGain).GetControlAddress());

    [TestMethod]
    [Description("PRO-016")]
    public void GetWord_ReadsProgramNumber()
        => Assert.AreEqual((ushort)0xFFFF, Request.ProgramDump(Device0, Request.ActiveProgram).GetWord(0));
}
