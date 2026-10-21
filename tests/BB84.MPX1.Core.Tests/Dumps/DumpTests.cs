using BB84.MPX1.Core.Database;
using BB84.MPX1.Core.Dumps;
using BB84.MPX1.Core.Programs;

namespace BB84.MPX1.Core.Tests.Dumps;

[TestClass]
public sealed class DumpTests
{
    [TestMethod]
    [Description("DMP-004..DMP-010: documented dump layouts have the documented sizes")]
    public void Layouts_HaveDocumentedSizes()
    {
        Assert.AreEqual(44, DumpLayouts.Setup.Size, "Setup: frame bytes 1-88.");
        Assert.AreEqual(20, DumpLayouts.GlobalPatches.Size);
        Assert.AreEqual(7, DumpLayouts.BypassControllers.Size);
        Assert.AreEqual(163, DumpLayouts.RemapControllers.Size);
        Assert.AreEqual(72, DumpLayouts.CurrentChoices.Size, "Current Choices: frame bytes 1-144.");
        Assert.AreEqual(60, DumpLayouts.Patches.Size);
        Assert.AreEqual(20, DumpLayouts.SoftRow.Size);
        Assert.AreEqual(10, SystemDumps.Size(SystemDumpKind.Leds));
        Assert.AreEqual(32, SystemDumps.Size(SystemDumpKind.Display));
        Assert.AreEqual(64, SystemDumps.Size(SystemDumpKind.CustomCharacters));
        Assert.AreEqual(34, DumpLayouts.Setup.Fields.Single(f => f.Name == "Setup name").Offset, "setup_name: frame bytes 69-86.");
        Assert.AreEqual(43, DumpLayouts.Setup.Fields[^1].Offset, "Global bypass level: frame bytes 87-88.");
    }

    [TestMethod]
    [Description("DMP-004, PAR-006: setup fields accept only legal values; MIDI speed only 1, 2, 4, 0x40")]
    public void Setup_ValidatesFields()
    {
        DumpLayout setup = DumpLayouts.Setup;
        DumpField speed = setup.Fields.Single(f => f.Name == "MIDI transmit speed");
        byte[] data = new byte[setup.Size];

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => speed.Write(data, 3));
        data = speed.Write(data, 0x40);
        Assert.AreEqual(0x40, speed.Read(data));
        Assert.AreEqual("Slow", speed.Format(data));

        DumpField level = setup.Fields.Single(f => f.Name == "Digital input level");
        data = level.Write(data, -90);
        Assert.AreEqual(-90, level.Read(data));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => level.Write(data, 7));

        data = setup.Fields.Single(f => f.Name == "Setup name").WriteText(data, "Liveé Rig 2026");
        Assert.AreEqual("Live  Rig", setup.Fields.Single(f => f.Name == "Setup name").ReadText(data), "Nine characters, printable ASCII only.");
        Assert.IsTrue(setup.IsValid(data));
        data[setup.Fields.Single(f => f.Name == "Program sort mode").Offset] = 9;
        Assert.IsFalse(setup.IsValid(data));
        Assert.AreEqual("Program sort mode", setup.InvalidFields(data).Single().Name);
    }

    [TestMethod]
    [Description("DMP-005, DMP-006, DMP-007, DMP-011: controller sources are validated per dump; remap bytes follow Appendix A")]
    public void ControllerFields_UseTheirRanges()
    {
        DumpField globalSource = DumpLayouts.GlobalPatches.Fields[0];
        DumpField bypassSource = DumpLayouts.BypassControllers.Fields[0];
        byte[] global = new byte[20];
        byte[] bypass = new byte[7];

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => globalSource.Write(global, 0x17));
        Assert.AreEqual(0x18, globalSource.Read(globalSource.Write(global, 0x18)));
        Assert.AreEqual(0xFF, globalSource.Read(globalSource.Write(global, 0xFF)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => bypassSource.Write(bypass, 0x97));
        Assert.AreEqual("Tog1", bypassSource.Format(bypassSource.Write(bypass, 0x19)));
        Assert.AreEqual("Reverb mix", DumpLayouts.GlobalPatches.Fields[10].Choices[7].Label);
        Assert.AreEqual("Delay bypass", DumpLayouts.BypassControllers.Fields[6].Name);
        Assert.AreEqual("Sine1", DumpLayouts.RemapControllers.Fields[5].Name, "Remap byte n belongs to Appendix A source n.");
    }

    [TestMethod]
    [Description("DMP-001: LEDs decode headroom bars, active-low columns, 7-segment digits and keep scan bits")]
    public void Leds_DecodeAndKeepScanBits()
    {
        byte[] data = [.. LedState.AllOff().Data];
        data[0] = 0x07; // left headroom -12 dB
        data[2] = 0x3F; // right headroom clip
        data[4] = 0x3F;
        data[6] = 0x5B;
        data[8] = 0x5E;
        LedState leds = LedState.FromBytes(data).With(PanelLed.Bypass, true).With(PanelLed.MidiActivity, true);

        Assert.AreEqual(3, leds.LeftHeadroom);
        Assert.AreEqual(6, leds.RightHeadroom);
        Assert.AreEqual("02P", leds.Digits3);
        Assert.IsTrue(leds.IsOn(PanelLed.Bypass));
        Assert.IsFalse(leds.IsOn(PanelLed.Program));
        Assert.IsTrue(leds.IsOn(PanelLed.MidiActivity));
        Assert.AreEqual(0, leds.Data[5] & (1 << 5), "Column LEDs are active low.");
        Assert.IsTrue(leds.HasValidScanBits());
        data[3] = 0x00;
        Assert.IsFalse(LedState.FromBytes(data).HasValidScanBits());
    }

    [TestMethod]
    [Description("DMP-002, DMP-003, PRO-007: display lines keep custom characters; bitmaps are 5x8 rows")]
    public void DisplayAndCustomCharacters()
    {
        byte[] display = DisplayText.Encode("\u0000 Edited", "Hallÿ");
        (string top, string bottom) = DisplayText.Lines(display);

        Assert.AreEqual('\0', top[0]);
        Assert.IsTrue(DisplayText.IsCustom(display[0]));
        Assert.AreEqual("Hall            ", bottom);

        byte[] bitmap = new byte[CustomCharacters.Size];
        new byte[] { 0x1F, 0x04, 0x04, 0x04, 0x04, 0x04, 0x04, 0x00 }.CopyTo(bitmap, 8); // the document's 'T'
        Assert.IsTrue(CustomCharacters.IsSet(bitmap, 1, 0, 0));
        Assert.IsTrue(CustomCharacters.IsSet(bitmap, 1, 3, 2));
        Assert.IsFalse(CustomCharacters.IsSet(bitmap, 1, 3, 1));
    }

    [TestMethod]
    [Description("PAR-007: panel button codes match the document; Left + Right has dedicated codes")]
    public void PanelButtons_MatchDocument()
    {
        PanelButton program = PanelButtons.Find("Program");
        Assert.AreEqual((byte)0x03, program.Press);
        Assert.AreEqual((byte)0x19, program.Hold);
        Assert.AreEqual((byte)0x2F, program.Release);
        Assert.AreEqual((byte)0x15, PanelButtons.LeftAndRight.Press);
        Assert.AreEqual((byte)0x2B, PanelButtons.LeftAndRight.Hold);
        Assert.AreEqual((byte)0x41, PanelButtons.LeftAndRight.Release);
        Assert.AreEqual((byte)0x13, PanelButtons.Find("Foot switch tip").Press);
        Assert.HasCount(22, PanelButtons.All);
    }

    [TestMethod]
    [Description("DMP-009, DMP-010: Patches and Soft Row dumps are the program's patch and soft value bytes")]
    public void ProgramData_PatchAndSoftRowDumps()
    {
        ProgramData program = ProgramFields.NewProgram();
        byte[] patches = program.GetPatchesDump();
        byte[] soft = program.GetSoftRowDump();
        Assert.HasCount(60, patches);
        Assert.HasCount(20, soft);
        Assert.AreEqual(0xFF, patches[0], "An unassigned patch source.");

        patches[0] = 0x18;
        soft[0] = 2;
        soft[1] = 1;
        ProgramData changed = program.WithPatchesDump(patches).WithSoftRowDump(soft);

        Assert.AreEqual((byte)0x18, changed.Patches[0].SourceController);
        Assert.AreEqual(new SoftValue(2, 1), changed.SoftValues[0]);
        Assert.AreEqual(program.Name, changed.Name);
    }
}
