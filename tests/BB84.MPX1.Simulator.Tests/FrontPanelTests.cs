using System.Collections.Concurrent;

using BB84.MPX1.Core.Database;
using BB84.MPX1.Core.Dumps;
using BB84.MPX1.Core.Protocol;
using BB84.MPX1.Midi;
using BB84.MPX1.Midi.Dumps;
using BB84.MPX1.Midi.Learning;
using BB84.MPX1.Simulator.Fixtures;

namespace BB84.MPX1.Simulator.Tests;

[TestClass]
public sealed class FrontPanelTests
{
    private static readonly DeviceId Device = new(0);

    public TestContext TestContext { get; set; } = null!;

    private sealed record Rig(Mpx1Simulator Simulator, Mpx1Session Session, DumpService Dumps, FrontPanelController Panel, ConcurrentQueue<FrameEventArgs> Sent) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await Session.DisposeAsync();
            await Simulator.DisposeAsync();
        }
    }

    private async Task<Rig> CreateAsync(string firmware)
    {
        Mpx1Simulator simulator = await Firmware.SimulatorAsync(firmware, new SimulatorOptions { BusyDuration = TimeSpan.FromMilliseconds(1) }, TestContext.CancellationToken);
        Mpx1Session session = new(simulator, Device, new Mpx1SessionOptions { RequestTimeout = TimeSpan.FromMilliseconds(500), BusyGracePeriod = TimeSpan.FromMilliseconds(50) });
        ConcurrentQueue<FrameEventArgs> sent = new();
        session.FrameTraffic += (_, e) =>
        {
            if (e.Direction == FrameDirection.Sent)
                sent.Enqueue(e);
        };
        session.Start();
        ParameterDatabase database = await Firmware.LoadOrLearnAsync(session, TestContext.CancellationToken);
        DumpService dumps = new(session, database);
        return new Rig(simulator, session, dumps, new FrontPanelController(session, dumps), sent);
    }

    private static byte[] ButtonCodes(Rig rig)
        => [.. rig.Sent.Select(e => e.Message).OfType<ParameterData>().Select(d => d.Data.Span[0])];

    [TestMethod]
    [DynamicData(nameof(Firmware.All), typeof(Firmware))]
    [Description("PAR-007: a click sends PRESS then RELEASE; the RELEASE frame matches the document's example")]
    public async Task Click_SendsPressThenRelease(string firmware)
    {
        await using Rig rig = await CreateAsync(firmware);
        rig.Sent.Clear();

        await rig.Panel.ClickAsync(PanelButtons.Find("Program"), TestContext.CancellationToken);

        Assert.AreSequenceEqual(new byte[] { 0x03, 0x2F }, ButtonCodes(rig));
        // Document example: F0 06 09 00 01  01 00 00 00  0F 02  03 00 00 00  01 00 00 00 08 00 00 00  00 00 00 00 F7
        Assert.AreSequenceEqual(
            Convert.FromHexString("F006090001" + "01000000" + "0F02" + "03000000" + "01000000" + "08000000" + "00000000" + "F7"), rig.Sent.Last().Frame.ToArray());
        Assert.AreEqual((byte)0x2F, rig.Simulator.LastButtonCode);
    }

    [TestMethod]
    [DynamicData(nameof(Firmware.All), typeof(Firmware))]
    [Description("PAR-007: hold is HOLD then RELEASE; Left + Right uses its own codes, never two PRESS messages")]
    public async Task HoldAndCombination_UseDedicatedCodes(string firmware)
    {
        await using Rig rig = await CreateAsync(firmware);
        rig.Sent.Clear();

        await rig.Panel.HoldAsync(PanelButtons.Find("Edit"), TestContext.CancellationToken);
        await rig.Panel.ClickAsync(PanelButtons.LeftAndRight, TestContext.CancellationToken);
        await rig.Panel.TurnAsync(-2, TestContext.CancellationToken);

        Assert.AreSequenceEqual(new byte[] { 0x1D, 0x33, 0x15, 0x41, 0x43, 0x43 }, ButtonCodes(rig));
        Assert.AreEqual(firmware == Firmware.V110, rig.Panel.HasV110Codes);
    }

    [TestMethod]
    [DynamicData(nameof(Firmware.All), typeof(Firmware))]
    [Description("UI-007, DMP-001, DMP-002: the mirror reads display and LEDs; a press changes them on the unit")]
    public async Task Snapshot_ReflectsButtonPresses(string firmware)
    {
        await using Rig rig = await CreateAsync(firmware);

        PanelSnapshot before = await rig.Panel.ReadAsync(TestContext.CancellationToken);
        Assert.AreEqual($"  MPX 1  {firmware}  ", DisplayText.Lines(before.Display).Top);
        Assert.IsNotNull(before.Leds);
        Assert.IsTrue(before.Leds.IsOn(PanelLed.Program));
        Assert.AreEqual("001", before.Leds.Digits3);
        Assert.IsFalse(before.Leds.IsOn(PanelLed.Bypass));

        await rig.Panel.ClickAsync(PanelButtons.Find("Bypass"), TestContext.CancellationToken);
        PanelSnapshot after = await rig.Panel.ReadAsync(TestContext.CancellationToken);

        Assert.AreEqual("Bypass", DisplayText.Lines(after.Display).Bottom.Trim());
        Assert.IsTrue(after.Leds!.IsOn(PanelLed.Bypass));

        byte[]? characters = await rig.Panel.ReadCustomCharactersAsync(TestContext.CancellationToken);
        Assert.IsTrue(CustomCharacters.IsSet(characters!, 7, 0, 0), "Fixture character 7 is a full bar.");

        await rig.Panel.WriteDisplayAsync("Hello", "from the editor", TestContext.CancellationToken);
        ControlAddress display = rig.Dumps.Nodes(SystemDumpKind.Display).Single().Address;
        Assert.AreEqual("from the editor ", DisplayText.Lines(rig.Simulator.GetValue(display)!).Bottom);
    }

    [TestMethod]
    [DynamicData(nameof(Firmware.All), typeof(Firmware))]
    [Description("DMP-004, LRN-005: setups are found by name and size; the 542-byte bug-1 dump is not a setup; V1.00 reads every stored setup as setup 1 (E13)")]
    public async Task Setups_AreFoundAndWritable(string firmware)
    {
        await using Rig rig = await CreateAsync(firmware);

        IReadOnlyList<ControlTreeNode> setups = rig.Dumps.Nodes(SystemDumpKind.Setup);
        Assert.HasCount(firmware == Firmware.V110 ? 6 : 5, setups, "V1.00 has no single active-setup address.");
        Assert.IsTrue(setups.All(n => n.Type == setups[0].Type));

        byte[] data = await rig.Dumps.ReadAsync(setups[1], TestContext.CancellationToken);
        DumpLayout layout = SystemDumps.Layout(SystemDumpKind.Setup, rig.Dumps.Database.ValueSize(setups[0].Type))!;
        Assert.AreEqual(firmware == Firmware.V110 ? "Setup 2" : "Setup 1", layout.Fields.Single(f => f.Name == "Setup name").ReadText(data).TrimEnd());
        Assert.IsTrue(layout.IsValid(data));

        data = layout.Fields.Single(f => f.Name == "LCD contrast").Write(data, 12);
        await rig.Dumps.WriteAsync(SystemDumpKind.Setup, setups[1], data, TestContext.CancellationToken);
        Assert.AreSequenceEqual(data, rig.Simulator.GetValue(setups[1].Address));

        byte[] bad = [.. data];
        bad[layout.Fields.Single(f => f.Name == "MIDI transmit speed").Offset] = 0x03;
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => rig.Dumps.WriteAsync(SystemDumpKind.Setup, setups[1], bad, TestContext.CancellationToken));
    }

    [TestMethod]
    [DynamicData(nameof(Firmware.All), typeof(Firmware))]
    [Description("DMP-001: LED writes must keep the scan bits")]
    public async Task LedWrite_RequiresScanBits(string firmware)
    {
        await using Rig rig = await CreateAsync(firmware);
        ControlTreeNode leds = rig.Dumps.Nodes(SystemDumpKind.Leds).Single();

        await Assert.ThrowsExactlyAsync<ArgumentException>(() => rig.Dumps.WriteAsync(SystemDumpKind.Leds, leds, new byte[LedState.Size], TestContext.CancellationToken));
        await rig.Dumps.WriteAsync(SystemDumpKind.Leds, leds, LedState.AllOff().With(PanelLed.Store, true).Data.ToArray(), TestContext.CancellationToken);
        Assert.IsTrue(LedState.FromBytes(rig.Simulator.GetValue(leds.Address)).IsOn(PanelLed.Store));
    }

    [TestMethod]
    [DynamicData(nameof(Firmware.All), typeof(Firmware))]
    [Description("DMP-008, DMP-009, DMP-010: active-program dumps read and edit the running program")]
    public async Task ActiveProgramDumps_EditRunningProgram(string firmware)
    {
        await using Rig rig = await CreateAsync(firmware);
        ControlTreeNode patches = rig.Dumps.Nodes(SystemDumpKind.Patches).Single();
        ControlTreeNode soft = rig.Dumps.Nodes(SystemDumpKind.SoftRow).Single();
        Assert.HasCount(1, rig.Dumps.Nodes(SystemDumpKind.CurrentChoices));
        Assert.HasCount(1, rig.Dumps.Nodes(SystemDumpKind.GlobalPatches), "\"Global Ptch\" is not mistaken for the Patches dump.");

        byte[] data = await rig.Dumps.ReadAsync(patches, TestContext.CancellationToken);
        Assert.AreSequenceEqual(rig.Simulator.ActiveProgram.GetPatchesDump(), data);

        byte[] row = new byte[DumpLayouts.SoftRow.Size];
        row.AsSpan().Fill(0xFF);
        row[0] = 0;
        row[1] = 0;
        await rig.Dumps.WriteAsync(SystemDumpKind.SoftRow, soft, row, TestContext.CancellationToken);
        Assert.AreEqual((byte)0, rig.Simulator.ActiveProgram.SoftValues[0].EffectType);
        Assert.AreEqual((byte)0xFF, rig.Simulator.ActiveProgram.SoftValues[1].EffectType);
    }

    [TestMethod]
    [DynamicData(nameof(Firmware.All), typeof(Firmware))]
    [Description("LRN-005, DMP-011: a learned dump node maps back to its dump kind by name; other nodes map to none")]
    public async Task KindOf_MapsDumpNodes(string firmware)
    {
        await using Rig rig = await CreateAsync(firmware);
        ParameterDatabase database = rig.Dumps.Database;

        foreach (SystemDumpKind kind in Enum.GetValues<SystemDumpKind>())
        {
            foreach (ControlTreeNode node in rig.Dumps.Nodes(kind))
                Assert.AreEqual(kind, SystemDumps.KindOf(database, node), node.Name);
        }
        Assert.IsNull(SystemDumps.KindOf(database, database.FindByPath("Program", "Pitch", "no effect", "Mix")!));
    }
}
