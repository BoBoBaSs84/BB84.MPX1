using BB84.MPX1.Core.Protocol;
using BB84.MPX1.Core.Values;

namespace BB84.MPX1.Core.Tests.Values;

[TestClass]
public sealed class ValueTests
{
    private static readonly DeviceId Device = new(0);

    private static ParameterDescription Describe(ushort size, ushort? option, params UnitLimit[] units)
        => new(Device, 0x0100, "Test ", size, ParameterAttributes.None, option, [.. units]);

    [TestMethod]
    [Description("AC-016, PAR-004")]
    public void Rate_RatioAndFrequency()
    {
        Assert.AreEqual("3:4", RateValue.FromRaw(0x8304).ToString());
        Assert.AreEqual("1.00 Hz", RateValue.FromRaw(0x0064).ToString());
        Assert.AreEqual((ushort)0x8304, RateValue.FromRatio(3, 4).ToRaw());
        Assert.AreEqual((ushort)5000, RateValue.FromCentihertz(5000).ToRaw());
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => RateValue.FromCentihertz(5001));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => RateValue.FromRatio(25, 1));
    }

    [TestMethod]
    [Description("PAR-006")]
    public void MidiSpeed_OnlyFourValues()
    {
        CollectionAssert.AreEqual(new byte[] { 0x01, 0x02, 0x04, 0x40 }, MidiSpeed.AllowedValues.ToArray());
        Assert.IsTrue(MidiSpeed.IsValid(0x40));
        Assert.IsFalse(MidiSpeed.IsValid(0x03));
        Assert.AreEqual("Medium Slow", MidiSpeed.Name(0x04));
    }

    [TestMethod]
    [Description("PRO-008, PAR-001: signed 1-byte value")]
    public void Decode_SignedByte()
    {
        ParameterDescription level = Describe(1, null, new UnitLimit(-96, 0, 0x80));

        ParameterValue value = ParameterValueCodec.Decode(level, null, [0xF4]);

        Assert.AreEqual(-12, value.Value);
        Assert.AreEqual("-12dB", DisplayUnitFormatter.Format(level, value));
        CollectionAssert.AreEqual(new byte[] { 0xF4 }, ParameterValueCodec.Encode(level, null, -12));
    }

    [TestMethod]
    [Description("PAR-002, PAR-008: option byte follows the parameter and selects the unit")]
    public void Decode_OptionSelectsUnit()
    {
        ParameterDescription delay = Describe(2, 0x0101, new UnitLimit(0, 1000, 0x3B), new UnitLimit(0, 0x7FFF, 0x0F));
        ParameterDescription option = Describe(1, null, new UnitLimit(0, 1, 0x14));

        ParameterValue ms = ParameterValueCodec.Decode(delay, option, [0xF4, 0x01, 0x00]);
        ParameterValue ratio = ParameterValueCodec.Decode(delay, option, [0x04, 0x03, 0x01]);

        Assert.AreEqual(500, ms.Value);
        Assert.AreEqual("500 ms", DisplayUnitFormatter.Format(delay, ms));
        Assert.AreEqual(1, ratio.Option);
        Assert.AreEqual("3:4", DisplayUnitFormatter.Format(delay, ratio));
        CollectionAssert.AreEqual(new byte[] { 0xF4, 0x01, 0x00 }, ParameterValueCodec.Encode(delay, option, 500, 0));
    }

    [TestMethod]
    [Description("PAR-005: a zero-size option lives in the MSB of the parameter")]
    public void Decode_ZeroSizeOption_UsesMsb()
    {
        ParameterDescription rate = Describe(2, 0x0102, new UnitLimit(0, 5000, 0x17), new UnitLimit(0, 0x1818, 0x08));
        ParameterDescription units = Describe(0, null, new UnitLimit(0, 1, 0x13));

        ParameterValue hz = ParameterValueCodec.Decode(rate, units, [0x64, 0x00]);
        ParameterValue ratio = ParameterValueCodec.Decode(rate, units, [0x04, 0x83]);

        Assert.AreEqual(0, hz.Option);
        Assert.AreEqual("1.00 Hz", DisplayUnitFormatter.Format(rate, hz));
        Assert.AreEqual(1, ratio.Option);
        Assert.AreEqual(0x0304, ratio.Value);
        Assert.AreEqual("3:4", DisplayUnitFormatter.Format(rate, ratio));
        CollectionAssert.AreEqual(new byte[] { 0x04, 0x83 }, ParameterValueCodec.Encode(rate, units, 0x0304, 1));
    }

    [TestMethod]
    [Description("PAR-001: values outside the limits are rejected")]
    public void Encode_OutOfRange_Throws()
    {
        ParameterDescription mix = Describe(1, null, new UnitLimit(0, 100, 0x00));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ParameterValueCodec.Encode(mix, null, 101));
        CollectionAssert.AreEqual(new byte[] { 100 }, ParameterValueCodec.Encode(mix, null, 100));
    }

    [TestMethod]
    [Description("PRO-008: unsigned 1-byte limits are read as bytes")]
    public void Encode_UnsignedByteLimits()
    {
        ParameterDescription value = Describe(1, null, new UnitLimit(0, -1, 0x01));
        CollectionAssert.AreEqual(new byte[] { 200 }, ParameterValueCodec.Encode(value, null, 200));
    }

    [TestMethod]
    [Description("PAR-003: Appendix B formatting")]
    [DataRow((ushort)0x00, 50, 50, "50%")]
    [DataRow((ushort)0x07, 0, 0, "None")]
    [DataRow((ushort)0x0A, 15, 15, "1.5 Q")]
    [DataRow((ushort)0x18, 0, 0, "1")]
    [DataRow((ushort)0x18, 16, 16, "Off")]
    [DataRow((ushort)0x18, 17, 17, "Omni")]
    [DataRow((ushort)0x26, 120, 120, "120 BPM")]
    [DataRow((ushort)0x28, 0, 0, "None")]
    [DataRow((ushort)0x28, 38, 38, "CC7")]
    [DataRow((ushort)0x35, 3, 3, "5.5 M")]
    [DataRow((ushort)0x3A, 25, 25, "50%")]
    [DataRow((ushort)0x3E, 10, 10, "-6")]
    [DataRow((ushort)0x3E, 20, 20, "+4")]
    [DataRow((ushort)0x40, 2, 2, "150 ms")]
    [DataRow((ushort)0x44, 0, 0, "Dump")]
    [DataRow((ushort)0x81, -150, -150, "-1.50")]
    [DataRow((ushort)0x84, 0, 0, "C")]
    [DataRow((ushort)0x84, -20, -20, "20L")]
    [DataRow((ushort)0x84, 20, 20, "20R")]
    [DataRow((ushort)0x86, 0xFF, 0xFF, "None")]
    [DataRow((ushort)0x86, 7, 7, "CC7")]
    [DataRow((ushort)0x2F, 3, 3, "3")]
    [DataRow((ushort)0x01, 7, 7, "7")]
    [DataRow((ushort)0x03, 7, 7, "7%")]
    [DataRow((ushort)0x08, 0x0304, 0x8304, "3:4")]
    [DataRow((ushort)0x09, 60, 60, "60Hz")]
    [DataRow((ushort)0x0E, 90, 90, "90°")]
    [DataRow((ushort)0x13, 0, 0x8000, "cycles:beat")]
    [DataRow((ushort)0x13, 0, 0, "Hz")]
    [DataRow((ushort)0x17, 250, 250, "2.50 Hz")]
    [DataRow((ushort)0x18, 20, 20, "20")]
    [DataRow((ushort)0x28, 200, 200, "200")]
    [DataRow((ushort)0x2A, 4, 4, "4 Beats")]
    [DataRow((ushort)0x2B, 9, 9, "9")]
    [DataRow((ushort)0x3B, 12, 12, "12 ms")]
    [DataRow((ushort)0x45, 3, 3, "3 Feet")]
    [DataRow((ushort)0x46, 3, 3, "3 Meters")]
    [DataRow((ushort)0x4B, 0, 0, "Name")]
    [DataRow((ushort)0x82, -5, -5, "-5%")]
    [DataRow((ushort)0x87, -45, -45, "-45°")]
    [DataRow((ushort)0x89, 3, 3, "3dB")]
    [DataRow((ushort)0x85, -300, -300, "-300")]
    public void Format_DisplayUnits(ushort units, int value, int raw, string expected)
        => Assert.AreEqual(expected, DisplayUnitFormatter.Format(new UnitLimit(0, 127, units), value, raw, null, "Name"));

    [TestMethod]
    [Description("PAR-003: level 'Off' at the minimum; string-table units are not 'supported'")]
    public void Format_LevelOffAndSupport()
    {
        Assert.AreEqual("Off", DisplayUnitFormatter.Format(new UnitLimit(-96, 0, 0x80), -96, 0xA0, null, "Level"));
        Assert.IsTrue(DisplayUnitFormatter.IsSupported(0x80));
        Assert.IsFalse(DisplayUnitFormatter.IsSupported(0x2F));
        Assert.AreEqual("AB", DisplayUnitFormatter.FormatText("AB"u8));
    }
}
