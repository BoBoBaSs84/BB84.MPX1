using BB84.MPX1.Core.Files;
using BB84.MPX1.Core.Programs;
using BB84.MPX1.Core.Protocol;
using BB84.MPX1.Core.Values;

namespace BB84.MPX1.Core.Tests.Programs;

[TestClass]
public sealed class ProgramLayoutTests
{
    private static readonly DeviceId Device = new(0);

    // Detune (M) from the document's "Parameter Data" example: Mix, Level, Tune (+ 1-byte option), P Dly.
    private static readonly Dictionary<ushort, ParameterDescription> Descriptions = new()
    {
        [0x002D] = new(Device, 0x002D, "Mix  ", 1, ParameterAttributes.Patchable, null, [new UnitLimit(0, 100, 0x00)]),
        [0x002E] = new(Device, 0x002E, "Level", 1, ParameterAttributes.Patchable, null, [new UnitLimit(-96, 0, 0x01)]),
        [0x003B] = new(Device, 0x003B, "Tune ", 1, ParameterAttributes.Patchable, 0x003C, [new UnitLimit(-100, 100, 0x81)]),
        [0x003C] = new(Device, 0x003C, "Optim", 1, ParameterAttributes.None, null, [new UnitLimit(0, 1, 0x00)]),
        [0x003D] = new(Device, 0x003D, "P Dly", 1, ParameterAttributes.Patchable, null, [new UnitLimit(0, 200, 0x3B)]),
    };

    private static readonly ushort[] DetuneTypes = [0x002D, 0x002E, 0x003B, 0x003D];

    private static ParameterDescription? Describe(ushort type) => Descriptions.GetValueOrDefault(type);

    [TestMethod]
    [Description("PRG-003: offsets sum each preceding parameter's size including option bytes (document example)")]
    public void Layout_WalksSizesIncludingOptions()
    {
        ParameterBlockLayout layout = ParameterBlockLayout.Create(DetuneTypes, Describe, ProgramData.EffectDataSize);

        CollectionAssert.AreEqual(new[] { 0, 1, 2, 4 }, layout.Parameters.Select(p => p.Offset).ToArray());
        CollectionAssert.AreEqual(new[] { 1, 1, 2, 1 }, layout.Parameters.Select(p => p.Size).ToArray());
        Assert.AreEqual(5, layout.UsedBytes);

        byte[] block = new byte[ProgramData.EffectDataSize];
        new byte[] { 0x64, 0x00, 0x0A, 0x01, 0x00 }.CopyTo(block, 0);
        ParameterValue tune = layout.Read(block, 2);
        Assert.AreEqual(10, tune.Value);
        Assert.AreEqual(1, tune.Option);
        Assert.AreEqual(100, layout.Read(block, 0).Value);
    }

    [TestMethod]
    [Description("PRG-003: writing one parameter changes only its bytes and keeps the option")]
    public void Layout_WriteKeepsOtherBytesAndOption()
    {
        ParameterBlockLayout layout = ParameterBlockLayout.Create(DetuneTypes, Describe, ProgramData.EffectDataSize);
        byte[] block = new byte[ProgramData.EffectDataSize];
        new byte[] { 0x64, 0x00, 0x0A, 0x01, 0x00 }.CopyTo(block, 0);
        block[31] = 0x5A;

        byte[] changed = layout.Write(block, 2, -5);

        CollectionAssert.AreEqual(new byte[] { 0x64, 0x00, 0xFB, 0x01, 0x00 }, changed[..5]);
        Assert.AreEqual(0x5A, changed[31], "Bytes that belong to no parameter are left alone.");
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => layout.Write(block, 0, 101));
    }

    [TestMethod]
    [Description("PRG-003, SEC-002: unknown types and oversized lists are rejected")]
    public void Layout_RejectsUnknownAndOversized()
    {
        Assert.ThrowsExactly<ArgumentException>(() => ParameterBlockLayout.Create([0x0999], Describe, 32));
        Assert.ThrowsExactly<ArgumentException>(() => ParameterBlockLayout.Create(DetuneTypes, Describe, 4));
    }

    [TestMethod]
    [Description("Spec 4.5: modulator blocks cover the 61 bytes LFO 1 through Envelope 2 without gaps")]
    public void ModulatorBlocks_CoverModulatorData()
    {
        int offset = 0;
        foreach (ModulatorBlock block in ModulatorBlocks.All)
        {
            Assert.AreEqual(offset, block.Offset, block.Name);
            offset += block.Size;
        }
        Assert.AreEqual(ProgramData.ModulatorDataSize, offset);
        Assert.AreEqual(EffectType.Lfo1, ModulatorBlocks.All[0].Type);
        Assert.AreEqual(EffectType.EnvelopeGenerator2, ModulatorBlocks.All[^1].Type);
    }

    [TestMethod]
    [Description("PRG-002: effect and modulator blocks can be replaced in a program copy")]
    public void ProgramData_ReplacesBlocks()
    {
        ProgramData program = TestData.Program();
        byte[] block = new byte[ProgramData.EffectDataSize];
        block[0] = 0x42;
        ModulatorBlock adsr = ModulatorBlocks.Find(EffectType.Adsr1)!;

        ProgramData changed = program.WithEffectData(3, block).WithModulatorBlock(adsr, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 });

        Assert.AreEqual(0x42, changed.EffectData[3][0]);
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 }, changed.GetModulatorBlock(adsr).ToArray());
        CollectionAssert.AreEqual(program.EffectData[2].ToArray(), changed.EffectData[2].ToArray());
        Assert.IsFalse(program.IsCleared);
        Assert.IsTrue((program with { Algorithms = [0xFF, 0, 0, 0, 0, 0] }).IsCleared);
    }

    [TestMethod]
    [Description("PRG-004: presets 0-199, user registers 200-249, active 0xFFFF")]
    public void ProgramSlots_Classify()
    {
        Assert.IsTrue(ProgramSlots.IsPreset(0));
        Assert.IsTrue(ProgramSlots.IsPreset(199));
        Assert.IsFalse(ProgramSlots.IsPreset(200));
        Assert.IsTrue(ProgramSlots.IsUser(249));
        Assert.IsFalse(ProgramSlots.IsValid(250));
        Assert.IsTrue(ProgramSlots.IsValid(Request.ActiveProgram));
    }

    [TestMethod]
    [Description("PRG-006: .syx files round trip; other devices' frames and invalid frames are skipped with warnings")]
    public void SyxFile_ReadsMpx1FramesAndWarns()
    {
        ProgramDump dump = new(Device, 201, TestData.Program());
        byte[] file = SyxFile.Write([dump]);
        byte[] foreign = [0xF0, 0x41, 0x10, 0x42, 0x12, 0x00, 0xF7];
        byte[] corrupt = [0xF0, 0x06, 0x09, 0x00, 0x1B, 0x1F, 0xF7];

        SyxContent content = SyxFile.Read([.. foreign, .. file, .. corrupt]);

        Assert.HasCount(1, content.Messages);
        CollectionAssert.AreEqual(file, content.Programs.Single().ToBytes());
        Assert.HasCount(2, content.Warnings);
        StringAssert.Contains(content.Warnings[0], "not an MPX 1 frame");
        StringAssert.StartsWith(content.Warnings[1], "Frame 3:");
    }

    [TestMethod]
    [Description("PRG-006: an empty or non-SysEx file yields a warning, not an exception")]
    public void SyxFile_EmptyFile_Warns()
    {
        SyxContent content = SyxFile.Read("hello"u8);
        Assert.IsEmpty(content.Messages);
        Assert.HasCount(1, content.Warnings);
    }
}
