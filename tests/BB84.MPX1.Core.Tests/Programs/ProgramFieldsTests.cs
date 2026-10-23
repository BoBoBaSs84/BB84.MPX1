using BB84.MPX1.Core.Programs;
using BB84.MPX1.Core.Protocol;

namespace BB84.MPX1.Core.Tests.Programs;

[TestClass]
public sealed class ProgramFieldsTests
{
    [TestMethod]
    [Description("PRO-007: names are printable ASCII, 12 characters, space padded")]
    public void NormalizeName_PadsTruncatesAndReplaces()
    {
        Assert.AreEqual("            ", ProgramFields.NormalizeName(null));
        Assert.AreEqual("Lead        ", ProgramFields.NormalizeName("Lead"));
        Assert.AreEqual("Lead Guitar ", ProgramFields.NormalizeName("Lead Guitar"));
        Assert.AreEqual("A very long ", ProgramFields.NormalizeName("A very long name"));
        Assert.AreEqual("Caf  Hall   ", ProgramFields.NormalizeName("Café\tHall"));
    }

    [TestMethod]
    [Description("PRO-018: sort flags are listed effects first, then sources")]
    public void DescribeCategories_ListsSetFlags()
    {
        Assert.AreEqual("", ProgramFields.DescribeCategories(default));
        Assert.AreEqual("Hall, Guitar", ProgramFields.DescribeCategories(new ProgramCategories(EffectCategories.Hall, SourceCategories.Guitar)));
        Assert.AreEqual("Pitch, Dual, LivePA, SoundFX",
            ProgramFields.DescribeCategories(new ProgramCategories(EffectCategories.Pitch | EffectCategories.Dual, SourceCategories.LivePA | SourceCategories.SoundFX)));
    }
}
