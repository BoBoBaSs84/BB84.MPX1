using BB84.MPX1.Core.Programs;
using BB84.MPX1.Core.Protocol;

namespace BB84.MPX1.Core.Tests.Programs;

[TestClass]
public sealed class ProgramDataTests
{
    [TestMethod]
    [Description("PRG-002: raw program bytes round trip")]
    public void ToBytes_FromBytes_RoundTrip()
    {
        ProgramData program = TestData.Program();

        byte[] bytes = program.ToBytes();

        Assert.HasCount(ProgramData.Size, bytes);
        CollectionAssert.AreEqual(bytes, ProgramData.FromBytes(bytes).ToBytes());
        Assert.AreEqual("Test Program", ProgramData.FromBytes(bytes).Name);
    }

    [TestMethod]
    public void FromBytes_WrongLength_Throws()
        => Assert.ThrowsExactly<Mpx1FormatException>(() => ProgramData.FromBytes(new byte[10]));

    [TestMethod]
    [Description("Spec 4.5: program name (frame bytes 492-515) starts 241 bytes into the program data")]
    public void ToBytes_NameOffset()
    {
        byte[] bytes = TestData.Program().ToBytes();
        // Program data nibbles start at frame byte 10, so frame byte 492 is data byte (492 - 10) / 2 = 241.
        Assert.AreEqual((byte)'T', bytes[241]);
    }
}
