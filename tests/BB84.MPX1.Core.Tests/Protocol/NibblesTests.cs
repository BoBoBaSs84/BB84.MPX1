using BB84.MPX1.Core.Protocol;

namespace BB84.MPX1.Core.Tests.Protocol;

[TestClass]
public sealed class NibblesTests
{
    [TestMethod]
    [Description("AC-001, PRO-001")]
    public void Encode_Byte_LowNibbleFirst()
        => CollectionAssert.AreEqual(Hex.Parse("05 0A"), Nibbles.Encode([0xA5]));

    [TestMethod]
    [Description("AC-001, PRO-002")]
    public void EncodeWord_LeastSignificantNibbleFirst()
        => CollectionAssert.AreEqual(Hex.Parse("05 02 01 00"), Nibbles.EncodeWord(0x0125));

    [TestMethod]
    [Description("Spec section 9: parameter type count 0x01C0")]
    public void EncodeWord_ParameterTypeCount()
        => CollectionAssert.AreEqual(Hex.Parse("00 0C 01 00"), Nibbles.EncodeWord(0x01C0));

    [TestMethod]
    [Description("PRO-001")]
    public void Decode_RoundTripsAllByteValues()
    {
        byte[] all = [.. Enumerable.Range(0, 256).Select(i => (byte)i)];
        CollectionAssert.AreEqual(all, Nibbles.Decode(Nibbles.Encode(all)));
    }

    [TestMethod]
    [Description("PRO-003")]
    public void Decode_InvalidNibble_ReportsOffset()
    {
        Mpx1FormatException ex = Assert.ThrowsExactly<Mpx1FormatException>(() => Nibbles.Decode(Hex.Parse("01 02 10 00")));
        Assert.AreEqual(2, ex.Error.Offset);
    }

    [TestMethod]
    public void Decode_OddLength_Throws()
        => Assert.ThrowsExactly<Mpx1FormatException>(() => Nibbles.Decode(Hex.Parse("01 02 03")));
}
