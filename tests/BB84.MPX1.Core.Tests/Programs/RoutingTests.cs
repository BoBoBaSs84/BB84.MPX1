using System.Collections.Immutable;

using BB84.MPX1.Core.Programs;

namespace BB84.MPX1.Core.Tests.Programs;

[TestClass]
public sealed class RoutingTests
{
    private const int V110 = 2;

    private static RoutingBlock B(RoutingBlockType type, RoutingMode mode, byte upper = 0, byte lower = 0, byte path = 0)
        => new((byte)type, upper, lower, (byte)mode, path);

    // The document's routing example: Input split, EQ upper, Mod lower, Pitch upper, Chorus merge, Delay, Reverb, Output.
    private static readonly ImmutableArray<RoutingBlock> Example =
    [
        B(RoutingBlockType.Input, RoutingMode.Split, path: 1),
        B(RoutingBlockType.EQ, RoutingMode.Upper, upper: 3, path: 1),
        B(RoutingBlockType.Mod, RoutingMode.Lower, lower: 4, path: 1),
        B(RoutingBlockType.Pitch, RoutingMode.Upper, path: 1),
        B(RoutingBlockType.Chorus, RoutingMode.Merge, path: 1),
        B(RoutingBlockType.Delay, RoutingMode.Upper),
        B(RoutingBlockType.Reverb, RoutingMode.Upper),
        B(RoutingBlockType.Output, RoutingMode.Upper),
    ];

    [TestMethod]
    [Description("PRG-002: the document's routing example follows every rule")]
    public void Example_IsValid()
    {
        Assert.IsTrue(AudioRouting.IsValid(Example, V110));
        Assert.IsTrue(AudioRouting.IsValid(AudioRouting.Default, V110));
    }

    [TestMethod]
    [Description("PRG-002: allowed routings depend on the path and the preceding block (rules tables)")]
    public void AllowedModes_FollowRuleTables()
    {
        CollectionAssert.AreEqual(new[] { RoutingMode.Upper, RoutingMode.Split }, AudioRouting.AllowedModes(AudioRouting.Default, 0, V110).ToArray());
        CollectionAssert.AreEqual(new[] { RoutingMode.Upper, RoutingMode.Split }, AudioRouting.AllowedModes(AudioRouting.Default, 3, V110).ToArray());

        // Double path after an upper/lower block: upper, lower, parallel, merge.
        CollectionAssert.AreEqual(new[] { RoutingMode.Upper, RoutingMode.Lower, RoutingMode.Parallel, RoutingMode.Merge },
            AudioRouting.AllowedModes(Example, 2, V110).ToArray());
        // Directly after the split: upper or lower only.
        CollectionAssert.AreEqual(new[] { RoutingMode.Upper, RoutingMode.Lower }, AudioRouting.AllowedModes(Example, 1, V110).ToArray());
        // After the merge the path is single again.
        CollectionAssert.AreEqual(new[] { RoutingMode.Upper, RoutingMode.Split }, AudioRouting.AllowedModes(Example, 5, V110).ToArray());
    }

    [TestMethod]
    [Description("PRG-002: V1.10 allows two merge/parallel blocks, V1.00 three")]
    public void AllowedModes_LimitMixingBlocks()
    {
        Assert.AreEqual(2, AudioRouting.MaxMixingBlocks(1, 10));
        Assert.AreEqual(3, AudioRouting.MaxMixingBlocks(1, 0));
        // Split at 1, parallel at 3 and 6: position 5 follows an upper block but two mixing blocks exist already.
        ImmutableArray<RoutingBlock> twoMixing = AudioRouting.SetMode(AudioRouting.Default, 1, RoutingMode.Split, 3);
        twoMixing = AudioRouting.SetMode(twoMixing, 3, RoutingMode.Parallel, 3);
        twoMixing = AudioRouting.SetMode(twoMixing, 6, RoutingMode.Parallel, 3);

        Assert.DoesNotContain(RoutingMode.Merge, AudioRouting.AllowedModes(twoMixing, 5, 2).ToList());
        Assert.Contains(RoutingMode.Merge, AudioRouting.AllowedModes(twoMixing, 5, 3).ToList());
    }

    [TestMethod]
    [Description("PRG-002 note 3: a split without a merge makes the output block merge")]
    public void SetMode_SplitWithoutMerge_OutputMerges()
    {
        ImmutableArray<RoutingBlock> split = AudioRouting.SetMode(AudioRouting.Default, 3, RoutingMode.Split, V110);

        Assert.AreEqual((byte)RoutingMode.Merge, split[^1].Routing);
        CollectionAssert.AreEqual(new byte[] { 0, 0, 0, 1, 1, 1, 1, 1 }, split.Select(b => b.PatchType).ToArray());
        Assert.IsTrue(AudioRouting.IsValid(split, V110));
    }

    [TestMethod]
    [Description("PRG-002 notes 5 and 6: a merge closes the path; later lower blocks become upper; inputs reset to stereo")]
    public void SetMode_Merge_RepairsFollowingBlocks()
    {
        ImmutableArray<RoutingBlock> merged = AudioRouting.SetMode(Example, 2, RoutingMode.Merge, V110);

        Assert.AreEqual((byte)RoutingMode.Merge, merged[2].Routing);
        Assert.AreEqual((byte)0, merged[2].LowerInput, "Changing a block's routing resets its inputs.");
        Assert.AreEqual((byte)RoutingMode.Upper, merged[4].Routing, "The old merge after the new one has no double path left.");
        Assert.AreEqual((byte)RoutingMode.Upper, merged[^1].Routing);
        Assert.IsTrue(AudioRouting.IsValid(merged, V110));
        Assert.ThrowsExactly<ArgumentException>(() => AudioRouting.SetMode(AudioRouting.Default, 2, RoutingMode.Lower, V110));
    }

    [TestMethod]
    [Description("UI-006: effects swap positions; routings stay with the position")]
    public void PlaceEffect_Swaps()
    {
        ImmutableArray<RoutingBlock> moved = AudioRouting.PlaceEffect(Example, 1, RoutingBlockType.Reverb);

        Assert.AreEqual((byte)RoutingBlockType.Reverb, moved[1].EffectType);
        Assert.AreEqual((byte)RoutingBlockType.EQ, moved[6].EffectType);
        Assert.AreEqual(Example[1].Routing, moved[1].Routing);
        Assert.ThrowsExactly<ArgumentException>(() => AudioRouting.PlaceEffect(Example, 1, RoutingBlockType.Input));
    }

    [TestMethod]
    [Description("UI-006: the map layout puts blocks on the upper and lower lanes")]
    public void Layout_AssignsLanes()
    {
        IReadOnlyList<RoutingSlot> slots = AudioRouting.Layout(Example);

        Assert.IsTrue(slots[2].OnLower && !slots[2].OnUpper, "Mod is on the lower lane.");
        Assert.IsTrue(slots[1].OnUpper && !slots[1].OnLower);
        Assert.IsTrue(slots[4].OnUpper && slots[4].OnLower, "The merge takes both lanes.");
        Assert.IsFalse(slots[5].IsDoublePath);
    }

    [TestMethod]
    [Description("DMP-011, GUD-004: Appendix A names and source ranges are data tables")]
    public void ControllerSources_NamesAndRanges()
    {
        Assert.AreEqual("Off", ControllerSources.Name(0x00));
        Assert.AreEqual("Sine1", ControllerSources.Name(0x05));
        Assert.AreEqual("Pedal", ControllerSources.Name(0x18));
        Assert.AreEqual("Sw3", ControllerSources.Name(0x1E));
        Assert.AreEqual("CC1", ControllerSources.Name(0x20));
        Assert.AreEqual("CC119", ControllerSources.Name(0x96));
        Assert.AreEqual("Bend", ControllerSources.Name(0x97));
        Assert.AreEqual("TSw", ControllerSources.Name(0xA2));
        Assert.AreEqual("None", ControllerSources.Name(0xFF));

        Assert.IsFalse(ControllerSources.IsSelectable(0x1F), "Bank select (CC0) is not selectable.");
        Assert.IsFalse(ControllerSources.IsSelectable(0x3F), "Bank select (CC32) is not selectable.");
        Assert.AreEqual((byte)0x18, ControllerSources.GlobalPatchSources[0]);
        Assert.AreEqual((byte)0xA2, ControllerSources.GlobalPatchSources[^1]);
        Assert.AreEqual((byte)0x19, ControllerSources.BypassSources[0]);
        Assert.AreEqual((byte)0x96, ControllerSources.BypassSources[^1]);
        Assert.IsTrue(ControllerSources.IsValid(0xFF, ControllerSources.BypassSources));
        Assert.IsFalse(ControllerSources.IsValid(0x97, ControllerSources.BypassSources));
        Assert.HasCount(163 - 2, ControllerSources.PatchSources);
    }

    [TestMethod]
    [Description("DMP-012: master level A0 = Off, A1-FF = -95..-1 dB, 00 = 0 dB")]
    public void ProgramFields_MasterLevel()
    {
        Assert.AreEqual("Off", ProgramFields.FormatMasterLevel(0xA0));
        Assert.AreEqual("-95dB", ProgramFields.FormatMasterLevel(0xA1));
        Assert.AreEqual("-1dB", ProgramFields.FormatMasterLevel(0xFF));
        Assert.AreEqual("0dB", ProgramFields.FormatMasterLevel(0x00));
        Assert.AreEqual((byte)0xF4, ProgramFields.MasterLevelFromDb(-12));
        Assert.AreEqual((byte)0xA0, ProgramFields.MasterLevelFromDb(-200));
        Assert.AreEqual("LFO 1", ProgramFields.DestinationName(7));
        Assert.AreEqual("System", ProgramFields.DestinationName(17));
    }
}
