using System.Collections.Concurrent;

using BB84.MPX1.Core.Database;
using BB84.MPX1.Core.Dumps;
using BB84.MPX1.Core.Files;
using BB84.MPX1.Core.Programs;
using BB84.MPX1.Core.Protocol;
using BB84.MPX1.Midi;
using BB84.MPX1.Midi.Dumps;
using BB84.MPX1.Midi.Learning;
using BB84.MPX1.Midi.Library;
using BB84.MPX1.Simulator.Fixtures;

namespace BB84.MPX1.Simulator.Tests;

[TestClass]
public sealed class LibrarianTests
{
    private static readonly DeviceId Device = new(0);

    private static readonly Mpx1SessionOptions FastOptions = new()
    {
        RequestTimeout = TimeSpan.FromMilliseconds(500),
        Retries = 1,
        BusyGracePeriod = TimeSpan.FromMilliseconds(50),
    };

    public TestContext TestContext { get; set; } = null!;

    private sealed record Rig(Mpx1Simulator Simulator, Mpx1Session Session, ParameterDatabase Database, ProgramLibrarian Librarian, ConcurrentQueue<Mpx1Message> Sent) : IAsyncDisposable
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
        Mpx1Session session = new(simulator, Device, FastOptions);
        ConcurrentQueue<Mpx1Message> sent = new();
        session.FrameTraffic += (_, e) =>
        {
            if (e.Direction == FrameDirection.Sent && e.Message is not null)
                sent.Enqueue(e.Message);
        };
        session.Start();
        ParameterDatabase database = await Firmware.LoadOrLearnAsync(session, TestContext.CancellationToken);
        return new Rig(simulator, session, database, new ProgramLibrarian(session, database), sent);
    }

    private static string Snapshot(Mpx1Simulator simulator)
        => SimulatorFixtureSerializer.Serialize(simulator.ToFixture()) + string.Join(",", simulator.Database);

    [TestMethod]
    [DynamicData(nameof(Firmware.All), typeof(Firmware))]
    [Description("PRG-007: the slot list comes from Program Information and the Database Dump")]
    public async Task List_ReturnsAllSlots(string firmware)
    {
        await using Rig rig = await CreateAsync(firmware);

        IReadOnlyList<ProgramSummary> slots = await rig.Librarian.ListAsync(cancellationToken: TestContext.CancellationToken);

        Assert.HasCount(250, slots);
        Assert.AreEqual("Preset 001", slots[0].Name);
        Assert.AreEqual("User 01", slots[200].Name);
        Assert.IsTrue(slots[199].IsPreset);
        Assert.IsFalse(slots[200].IsPreset);
        Assert.AreEqual(rig.Simulator.Database[17], slots[17].Categories);
        Assert.DoesNotContain(r => r.RequestedClass == MessageClass.ProgramDump, rig.Sent.OfType<Request>(), "Listing must not transfer full dumps.");
    }

    [TestMethod]
    [DynamicData(nameof(Firmware.All), typeof(Firmware))]
    [Description("PRG-001, PRG-004: user registers are stored freely; presets only after confirmation")]
    public async Task Store_RequiresConfirmationForPresets(string firmware)
    {
        await using Rig rig = await CreateAsync(firmware);
        ProgramData program = await rig.Librarian.ReceiveAsync(7, TestContext.CancellationToken) with { Name = "Stored      " };
        rig.Sent.Clear();

        PresetOverwriteException error = await Assert.ThrowsExactlyAsync<PresetOverwriteException>(() => rig.Librarian.StoreAsync(3, program, presetConfirmed: false, TestContext.CancellationToken));
        Assert.Contains("Program 004", error.Message, "Messages use front-panel numbers.");
        Assert.IsEmpty(rig.Sent.OfType<ProgramDump>());

        await rig.Librarian.StoreAsync(210, program, presetConfirmed: false, TestContext.CancellationToken);
        await rig.Librarian.StoreAsync(3, program, presetConfirmed: true, TestContext.CancellationToken);

        Assert.AreEqual("Stored      ", rig.Simulator.GetProgram(210).Name);
        Assert.AreEqual("Stored      ", rig.Simulator.GetProgram(3).Name);
    }

    [TestMethod]
    [DynamicData(nameof(Firmware.All), typeof(Firmware))]
    [Description("PRG-001: a stored program can be loaded into the active program")]
    public async Task LoadToActive_CopiesProgram(string firmware)
    {
        await using Rig rig = await CreateAsync(firmware);

        await rig.Librarian.LoadToActiveAsync(42, TestContext.CancellationToken);

        Assert.AreSequenceEqual(rig.Simulator.GetProgram(42).ToBytes(), rig.Simulator.ActiveProgram.ToBytes());
    }

    [TestMethod]
    [DynamicData(nameof(Firmware.All), typeof(Firmware))]
    [Description("PRO-018: writing sort flags the unit already holds sends nothing")]
    public async Task WriteDatabase_Unchanged_SendsNothing(string firmware)
    {
        await using Rig rig = await CreateAsync(firmware);
        IReadOnlyList<ProgramCategories> flags = rig.Simulator.Database;
        rig.Sent.Clear();

        IReadOnlyList<ushort> unset = await rig.Librarian.WriteDatabaseAsync(flags, TestContext.CancellationToken);

        Assert.IsEmpty(unset);
        Assert.IsFalse(rig.Sent.Any(m => m is DatabaseDump or ProgramDump));
    }

    [TestMethod]
    [Description("PRO-018, PRG-001: on V1.00, changed user-program flags are written as Program Dumps, leaving the table bytes V1.00 cannot store alone (spec 9, E18)")]
    public async Task WriteDatabase_V100_UserChange_UsesProgramDump()
    {
        await using Rig rig = await CreateAsync(Firmware.V100);
        ProgramCategories[] flags = [.. rig.Simulator.Database];
        ProgramCategories[] before = [.. flags];
        flags[210] = new ProgramCategories(EffectCategories.Hall, SourceCategories.SoundFX);
        rig.Sent.Clear();

        IReadOnlyList<ushort> unset = await rig.Librarian.WriteDatabaseAsync(flags, TestContext.CancellationToken);

        Assert.IsEmpty(unset);
        Assert.AreSequenceEqual(flags, rig.Simulator.Database.ToArray());
        Assert.AreEqual(flags[210], rig.Simulator.GetProgram(210).Categories);
        Assert.IsFalse(rig.Sent.Any(m => m is DatabaseDump));
        Assert.AreEqual(before[32], rig.Simulator.Database[32]);
    }

    [TestMethod]
    [Description("PRO-018: on V1.00, changed preset flags are sent as a compensated Database Dump; only programs 33 and 34 may differ (spec 9, E18)")]
    public async Task WriteDatabase_V100_PresetChange_Compensates()
    {
        await using Rig rig = await CreateAsync(Firmware.V100);
        ProgramCategories[] flags = [.. rig.Simulator.Database];
        flags[0] = new ProgramCategories(EffectCategories.Pitch | EffectCategories.Dual, SourceCategories.Drums);
        flags[200] = new ProgramCategories(EffectCategories.Plate, SourceCategories.Vocal);

        IReadOnlyList<ushort> unset = await rig.Librarian.WriteDatabaseAsync(flags, TestContext.CancellationToken);

        Assert.AreSequenceEqual(SortFlagTable.UnwritableOnV100.ToArray(), unset.ToArray());
        int[] differing = [.. Enumerable.Range(0, 250).Where(i => rig.Simulator.Database[i] != flags[i])];
        Assert.AreSequenceEqual(new[] { 32, 33 }, differing);
    }

    [TestMethod]
    [Description("PRO-018: on V1.10, changed sort flags are sent as one Database Dump and stored as sent")]
    public async Task WriteDatabase_V110_SendsDatabaseDump()
    {
        await using Rig rig = await CreateAsync(Firmware.V110);
        ProgramCategories[] flags = [.. Enumerable.Repeat(new ProgramCategories(EffectCategories.Delay, SourceCategories.Guitar), 250)];
        rig.Sent.Clear();

        IReadOnlyList<ushort> unset = await rig.Librarian.WriteDatabaseAsync(flags, TestContext.CancellationToken);

        Assert.IsEmpty(unset);
        Assert.AreSequenceEqual(flags, rig.Simulator.Database.ToArray());
        Assert.AreEqual(1, rig.Sent.Count(m => m is DatabaseDump));
    }

    [TestMethod]
    [DynamicData(nameof(Firmware.All), typeof(Firmware))]
    [Description("AC-017, PRG-005, SES-005: backup, change, restore leaves the simulator byte-identical, apart from the sort flags V1.00 cannot store")]
    public async Task BackupRestore_RoundTrip(string firmware)
    {
        await using Rig rig = await CreateAsync(firmware);
        Assert.AreSequenceEqual(SystemDumps.BackupKinds.Remove(SystemDumpKind.Setup), rig.Librarian.AvailableDumps().ToArray(), Microsoft.VisualStudio.TestTools.UnitTesting.SequenceOrder.InAnyOrder, "All setups replace the single setups.");
        string before = Snapshot(rig.Simulator);

        IReadOnlyList<Mpx1Message> backup = await rig.Librarian.BackupAsync(SystemDumps.BackupKinds, cancellationToken: TestContext.CancellationToken);
        byte[] file = SyxFile.Write(backup);
        Assert.HasCount(1 + 250 + 1 + 3, backup, "Database, 250 programs, all setups, global patches, bypass and remap.");

        // Change programs, sort flags and a dump.
        ProgramData changed = rig.Simulator.GetProgram(0) with { Name = "Changed     " };
        await rig.Librarian.StoreAsync(0, changed, presetConfirmed: true, TestContext.CancellationToken);
        await rig.Librarian.StoreAsync(249, changed, presetConfirmed: false, TestContext.CancellationToken);
        await rig.Librarian.WriteDatabaseAsync([.. Enumerable.Repeat(default(ProgramCategories), 250)], TestContext.CancellationToken);
        ControlAddress globalPatches = SystemDumps.Find(rig.Database, SystemDumpKind.GlobalPatches).Single().Address;
        await rig.Session.SendAsync(new ParameterData(Device, new byte[20], globalPatches), TestContext.CancellationToken);
        Assert.AreNotEqual(before, Snapshot(rig.Simulator));

        RestorePlan plan = rig.Librarian.Plan(SyxFile.Read(file));
        Assert.AreEqual(200, plan.PresetCount);
        Assert.HasCount(4, plan.Dumps);
        Assert.IsEmpty(plan.Warnings);
        await Assert.ThrowsExactlyAsync<PresetOverwriteException>(() => rig.Librarian.RestoreAsync(plan, presetsConfirmed: false, cancellationToken: TestContext.CancellationToken));

        IReadOnlyList<ushort> unset = await rig.Librarian.RestoreAsync(plan, presetsConfirmed: true, cancellationToken: TestContext.CancellationToken);

        // A V1.00 unit cannot take two table bytes from a Database Dump (spec section 9, E18).
        if (firmware == Firmware.V100)
            CollectionAssert.IsSubsetOf(unset.ToArray(), SortFlagTable.UnwritableOnV100.ToArray());
        else
            Assert.IsEmpty(unset);
        ProgramCategories[] flags = [.. rig.Simulator.Database];
        foreach (ushort number in unset)
            flags[number] = plan.Database!.Entries[number];
        Assert.AreEqual(before, SimulatorFixtureSerializer.Serialize(rig.Simulator.ToFixture()) + string.Join(",", flags));
        Assert.IsFalse(rig.Simulator.IsCrashed);
    }

    [TestMethod]
    [DynamicData(nameof(Firmware.All), typeof(Firmware))]
    [Description("PRG-005: a backup leaves out cleared user registers, which the unit does not dump")]
    public async Task Backup_SkipsClearedUserPrograms(string firmware)
    {
        await using Rig rig = await CreateAsync(firmware);
        ProgramData program = rig.Simulator.GetProgram(203);
        ProgramData cleared = program with { Algorithms = [ProgramData.ClearedAlgorithm, .. program.Algorithms[1..]] };
        await rig.Librarian.StoreAsync(203, cleared, presetConfirmed: false, TestContext.CancellationToken);
        Assert.IsTrue(await rig.Librarian.IsClearedAsync(203, TestContext.CancellationToken));
        Assert.IsFalse(await rig.Librarian.IsClearedAsync(204, TestContext.CancellationToken));
        rig.Sent.Clear();

        IReadOnlyList<Mpx1Message> backup = await rig.Librarian.BackupAsync([], cancellationToken: TestContext.CancellationToken);

        ProgramDump[] programs = [.. backup.OfType<ProgramDump>()];
        Assert.HasCount(249, programs);
        Assert.DoesNotContain(p => p.ProgramNumber == 203, programs);
        Assert.DoesNotContain(r => r.RequestedClass == MessageClass.ProgramDump && r.GetWord(0) == 203, rig.Sent.OfType<Request>());
        Assert.DoesNotContain(r => r.RequestedClass == MessageClass.ProgramInformation && r.GetWord(0) < ProgramSlots.FirstUser, rig.Sent.OfType<Request>(), "Presets are never cleared.");
    }

    [TestMethod]
    [DynamicData(nameof(Firmware.All), typeof(Firmware))]
    [Description("PRG-001: reading an empty user register fails at once with a clear error instead of timing out")]
    public async Task Receive_EmptySlot_ThrowsWithoutDumpRequest(string firmware)
    {
        await using Rig rig = await CreateAsync(firmware);
        ProgramData program = rig.Simulator.GetProgram(230);
        await rig.Librarian.StoreAsync(230, program with { Algorithms = [ProgramData.ClearedAlgorithm, .. program.Algorithms[1..]] }, presetConfirmed: false, TestContext.CancellationToken);
        rig.Sent.Clear();
        long start = TimeProvider.System.GetTimestamp();

        EmptyProgramException error = await Assert.ThrowsExactlyAsync<EmptyProgramException>(() => rig.Librarian.ReceiveAsync(230, TestContext.CancellationToken));

        Assert.Contains("Program 231", error.Message, "Messages use front-panel numbers.");
        Assert.IsLessThan(FastOptions.RequestTimeout, TimeProvider.System.GetElapsedTime(start));
        Assert.DoesNotContain(r => r.RequestedClass == MessageClass.ProgramDump, rig.Sent.OfType<Request>());
        Assert.AreEqual(rig.Simulator.GetProgram(231).Name, (await rig.Librarian.ReceiveAsync(231, TestContext.CancellationToken)).Name, "Filled user registers still read.");
    }

    [TestMethod]
    [DynamicData(nameof(Firmware.All), typeof(Firmware))]
    [Description("DMP-004, PRG-005: the backup holds the all-setups dump (active setup first), restores the active setup, and skips single setups")]
    public async Task Backup_AllSetups_RestoresActiveSetup(string firmware)
    {
        await using Rig rig = await CreateAsync(firmware);
        DumpService dumps = new(rig.Session, rig.Database);
        ControlTreeNode allSetups = SystemDumps.Find(rig.Database, SystemDumpKind.AllSetups).Single();
        IReadOnlyList<ControlTreeNode> stored = SystemDumps.Find(rig.Database, SystemDumpKind.Setup);
        int setupSize = rig.Database.ValueSize(allSetups.Type) / SystemDumps.SetupCount;
        DumpLayout layout = SystemDumps.Layout(SystemDumpKind.Setup, setupSize)!;

        IReadOnlyList<Mpx1Message> backup = await rig.Librarian.BackupAsync(SystemDumps.BackupKinds, cancellationToken: TestContext.CancellationToken);

        ParameterData all = backup.OfType<ParameterData>().Single(d => d.Data.Length == SystemDumps.SetupCount * setupSize);
        byte[] original = SystemDumps.SplitSetups(all.Data)[0].ToArray();
        Assert.AreEqual("Active", layout.Fields.Single(f => f.Name == "Setup name").ReadText(original).TrimEnd(), "The active setup comes first.");
        Assert.AreSequenceEqual(rig.Simulator.GetValue(stored[0].Address), SystemDumps.SplitSetups(all.Data)[1].ToArray());
        Assert.DoesNotContain(d => d.Data.Length == setupSize, backup.OfType<ParameterData>(), "No single setups.");

        // Change the active setup on the unit, then restore a file that also carries a stale single setup.
        DumpField name = layout.Fields.Single(f => f.Name == "Setup name");
        byte[] changed = [.. all.Data.ToArray()];
        name.WriteText(original, "Changed").CopyTo(changed, 0);
        await rig.Session.SendAsync(new ParameterData(Device, changed, allSetups.Address), TestContext.CancellationToken);
        Assert.AreEqual("Changed", name.ReadText(SystemDumps.SplitSetups(await dumps.ReadAsync(allSetups, TestContext.CancellationToken))[0].Span).TrimEnd());
        ParameterData stale = new(Device, name.WriteText(rig.Simulator.GetValue(stored[1].Address)!, "Stale"), stored[1].Address);
        RestorePlan plan = rig.Librarian.Plan(SyxFile.Read(SyxFile.Write([.. backup, stale])));
        Assert.IsTrue(plan.Warnings.Any(w => w.Contains("Single setup", StringComparison.Ordinal)), string.Join("; ", plan.Warnings));
        Assert.DoesNotContain(stale, plan.Dumps);

        await rig.Librarian.RestoreAsync(plan, presetsConfirmed: true, cancellationToken: TestContext.CancellationToken);

        Assert.AreSequenceEqual(original, SystemDumps.SplitSetups(await dumps.ReadAsync(allSetups, TestContext.CancellationToken))[0].ToArray());
        Assert.AreNotEqual("Stale", name.ReadText(rig.Simulator.GetValue(stored[1].Address)!).TrimEnd());
        Assert.ThrowsExactly<ArgumentException>(() => DumpService.Validate(SystemDumpKind.AllSetups, new byte[6 * DumpLayouts.Setup.Size - 1]));
    }

    [TestMethod]
    [Description("DMP-004, DMP-011: a 43-byte V1.00 setup validates against the V1.00 layout; other sizes are rejected")]
    public void Validate_AcceptsV100Setup()
    {
        byte[] v110 = DefaultFixture.InitialSetup(0);
        byte[] v100 = v110[..DumpLayouts.SetupV100.Size];

        DumpService.Validate(SystemDumpKind.Setup, v110);
        DumpService.Validate(SystemDumpKind.Setup, v100);
        Assert.ThrowsExactly<ArgumentException>(() => DumpService.Validate(SystemDumpKind.Setup, new byte[42]));
        v100[DumpLayouts.SetupV100.Fields.Single(f => f.Name == "Program sort mode").Offset] = 9;
        Assert.ThrowsExactly<ArgumentException>(() => DumpService.Validate(SystemDumpKind.Setup, v100));
    }

    [TestMethod]
    [DynamicData(nameof(Firmware.All), typeof(Firmware))]
    [Description("PRG-006, SEC-002: restore plans skip what does not belong to the connected firmware")]
    public async Task Plan_SkipsForeignAndMismatchedData(string firmware)
    {
        await using Rig rig = await CreateAsync(firmware);
        ProgramData program = rig.Simulator.GetProgram(1);
        byte[] file = SyxFile.Write(
        [
            new ProgramDump(Device, Request.ActiveProgram, program),
            new ProgramDump(Device, 220, program),
            new ParameterData(Device, new byte[5], SystemDumps.Find(rig.Database, SystemDumpKind.GlobalPatches).Single().Address),
            new ParameterData(Device, new byte[20], new ControlAddress(9, 9)),
        ]);

        RestorePlan plan = rig.Librarian.Plan(SyxFile.Read([0xF0, 0x43, 0x10, 0xF7, .. file]));

        Assert.HasCount(1, plan.Programs);
        Assert.AreEqual(0, plan.PresetCount);
        Assert.IsEmpty(plan.Dumps);
        Assert.HasCount(4, plan.Warnings);
    }

    [TestMethod]
    [DynamicData(nameof(Firmware.All), typeof(Firmware))]
    [Description("PRG-005, DMP-011: restore plans skip system dumps that hold illegal values")]
    public async Task Plan_SkipsInvalidDumps(string firmware)
    {
        await using Rig rig = await CreateAsync(firmware);
        ParameterData valid = (await rig.Librarian.BackupAsync([SystemDumpKind.BypassControllers], cancellationToken: TestContext.CancellationToken))
            .OfType<ParameterData>().Single();
        byte[] illegal = valid.Data.ToArray();
        illegal[0] = 0x97;
        byte[] file = SyxFile.Write([valid, valid with { Data = illegal }]);

        RestorePlan plan = rig.Librarian.Plan(SyxFile.Read(file));

        Assert.HasCount(1, plan.Dumps);
        Assert.AreSequenceEqual(valid.Data.ToArray(), plan.Dumps[0].Data.ToArray());
        Assert.Contains("illegal value", plan.Warnings.Single());
    }
}
