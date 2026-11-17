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

    private sealed record Rig(Mpx1Simulator Simulator, Mpx1Session Session, ProgramLibrarian Librarian, ConcurrentQueue<Mpx1Message> Sent) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await Session.DisposeAsync();
            await Simulator.DisposeAsync();
        }
    }

    private async Task<Rig> CreateAsync()
    {
        Mpx1Simulator simulator = new(options: new SimulatorOptions { BusyDuration = TimeSpan.FromMilliseconds(1) });
        await simulator.OpenAsync(Mpx1Simulator.Port.Id, Mpx1Simulator.Port.Id, TestContext.CancellationToken);
        Mpx1Session session = new(simulator, Device, FastOptions);
        ConcurrentQueue<Mpx1Message> sent = new();
        session.FrameTraffic += (_, e) =>
        {
            if (e.Direction == FrameDirection.Sent && e.Message is not null)
                sent.Enqueue(e.Message);
        };
        session.Start();
        ParameterDatabase database = await new ParameterLearner(session).LoadOrLearnAsync(cancellationToken: TestContext.CancellationToken);
        return new Rig(simulator, session, new ProgramLibrarian(session, database), sent);
    }

    private static string Snapshot(Mpx1Simulator simulator)
        => SimulatorFixtureSerializer.Serialize(simulator.ToFixture()) + string.Join(",", simulator.Database);

    [TestMethod]
    [Description("PRG-007: the slot list comes from Program Information and the Database Dump")]
    public async Task List_ReturnsAllSlots()
    {
        await using Rig rig = await CreateAsync();

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
    [Description("PRG-001, PRG-004: user registers are stored freely; presets only after confirmation")]
    public async Task Store_RequiresConfirmationForPresets()
    {
        await using Rig rig = await CreateAsync();
        ProgramData program = await rig.Librarian.ReceiveAsync(7, TestContext.CancellationToken) with { Name = "Stored      " };
        rig.Sent.Clear();

        await Assert.ThrowsExactlyAsync<PresetOverwriteException>(() => rig.Librarian.StoreAsync(3, program, presetConfirmed: false, TestContext.CancellationToken));
        Assert.IsEmpty(rig.Sent.OfType<ProgramDump>());

        await rig.Librarian.StoreAsync(210, program, presetConfirmed: false, TestContext.CancellationToken);
        await rig.Librarian.StoreAsync(3, program, presetConfirmed: true, TestContext.CancellationToken);

        Assert.AreEqual("Stored      ", rig.Simulator.GetProgram(210).Name);
        Assert.AreEqual("Stored      ", rig.Simulator.GetProgram(3).Name);
    }

    [TestMethod]
    [Description("PRG-001: a stored program can be loaded into the active program")]
    public async Task LoadToActive_CopiesProgram()
    {
        await using Rig rig = await CreateAsync();

        await rig.Librarian.LoadToActiveAsync(42, TestContext.CancellationToken);

        Assert.AreSequenceEqual(rig.Simulator.GetProgram(42).ToBytes(), rig.Simulator.ActiveProgram.ToBytes());
    }

    [TestMethod]
    [Description("AC-017, PRG-005, SES-005: backup, change, restore leaves the simulator byte-identical")]
    public async Task BackupRestore_RoundTrip()
    {
        await using Rig rig = await CreateAsync();
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
        await rig.Session.SendAsync(new ParameterData(Device, new byte[20], new ControlAddress(1, 12, 0)), TestContext.CancellationToken);
        Assert.AreNotEqual(before, Snapshot(rig.Simulator));

        RestorePlan plan = rig.Librarian.Plan(SyxFile.Read(file));
        Assert.AreEqual(200, plan.PresetCount);
        Assert.HasCount(4, plan.Dumps);
        Assert.IsEmpty(plan.Warnings);
        await Assert.ThrowsExactlyAsync<PresetOverwriteException>(() => rig.Librarian.RestoreAsync(plan, presetsConfirmed: false, cancellationToken: TestContext.CancellationToken));

        await rig.Librarian.RestoreAsync(plan, presetsConfirmed: true, cancellationToken: TestContext.CancellationToken);

        Assert.AreEqual(before, Snapshot(rig.Simulator));
        Assert.IsFalse(rig.Simulator.IsCrashed);
    }

    [TestMethod]
    [Description("PRG-005: a backup leaves out cleared user registers, which the unit does not dump")]
    public async Task Backup_SkipsClearedUserPrograms()
    {
        await using Rig rig = await CreateAsync();
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
    [Description("PRG-001: reading an empty user register fails at once with a clear error instead of timing out")]
    public async Task Receive_EmptySlot_ThrowsWithoutDumpRequest()
    {
        await using Rig rig = await CreateAsync();
        ProgramData program = rig.Simulator.GetProgram(230);
        await rig.Librarian.StoreAsync(230, program with { Algorithms = [ProgramData.ClearedAlgorithm, .. program.Algorithms[1..]] }, presetConfirmed: false, TestContext.CancellationToken);
        rig.Sent.Clear();
        long start = TimeProvider.System.GetTimestamp();

        EmptyProgramException error = await Assert.ThrowsExactlyAsync<EmptyProgramException>(() => rig.Librarian.ReceiveAsync(230, TestContext.CancellationToken));

        Assert.Contains("230", error.Message);
        Assert.IsLessThan(FastOptions.RequestTimeout, TimeProvider.System.GetElapsedTime(start));
        Assert.DoesNotContain(r => r.RequestedClass == MessageClass.ProgramDump, rig.Sent.OfType<Request>());
        Assert.AreEqual(rig.Simulator.GetProgram(231).Name, (await rig.Librarian.ReceiveAsync(231, TestContext.CancellationToken)).Name, "Filled user registers still read.");
    }

    [TestMethod]
    [Description("DMP-004, PRG-005: the backup holds the all-setups dump (active setup first), restores the active setup, and skips single setups")]
    public async Task Backup_AllSetups_RestoresActiveSetup()
    {
        await using Rig rig = await CreateAsync();
        ControlAddress active = new(1, 13, 5);
        byte[] original = rig.Simulator.GetValue(active)!;

        IReadOnlyList<Mpx1Message> backup = await rig.Librarian.BackupAsync(SystemDumps.BackupKinds, cancellationToken: TestContext.CancellationToken);

        ParameterData all = backup.OfType<ParameterData>().Single(d => d.Data.Length == 6 * DumpLayouts.Setup.Size);
        Assert.AreSequenceEqual(original, SystemDumps.SplitSetups(all.Data)[0].ToArray(), "The active setup comes first.");
        Assert.AreSequenceEqual(rig.Simulator.GetValue(new ControlAddress(1, 13, 0)), SystemDumps.SplitSetups(all.Data)[1].ToArray());
        Assert.DoesNotContain(d => d.Data.Length == DumpLayouts.Setup.Size, backup.OfType<ParameterData>(), "No single setups.");

        // Change the active setup on the unit, then restore a file that also carries a stale single setup.
        DumpField name = DumpLayouts.Setup.Fields.Single(f => f.Name == "Setup name");
        await rig.Session.SendAsync(new ParameterData(Device, name.WriteText(original, "Changed"), active), TestContext.CancellationToken);
        ParameterData stale = new(Device, name.WriteText(original, "Stale"), new ControlAddress(1, 13, 1));
        RestorePlan plan = rig.Librarian.Plan(SyxFile.Read(SyxFile.Write([.. backup, stale])));
        Assert.IsTrue(plan.Warnings.Any(w => w.Contains("Single setup", StringComparison.Ordinal)), string.Join("; ", plan.Warnings));
        Assert.DoesNotContain(stale, plan.Dumps);

        await rig.Librarian.RestoreAsync(plan, presetsConfirmed: true, cancellationToken: TestContext.CancellationToken);

        Assert.AreSequenceEqual(original, rig.Simulator.GetValue(active));
        Assert.AreNotEqual("Stale", name.ReadText(rig.Simulator.GetValue(new ControlAddress(1, 13, 1))!).TrimEnd());
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
    [Description("PRG-006, SEC-002: restore plans skip what does not belong to the connected firmware")]
    public async Task Plan_SkipsForeignAndMismatchedData()
    {
        await using Rig rig = await CreateAsync();
        ProgramData program = rig.Simulator.GetProgram(1);
        byte[] file = SyxFile.Write(
        [
            new ProgramDump(Device, Request.ActiveProgram, program),
            new ProgramDump(Device, 220, program),
            new ParameterData(Device, new byte[5], new ControlAddress(1, 12, 0)),
            new ParameterData(Device, new byte[20], new ControlAddress(9, 9)),
        ]);

        RestorePlan plan = rig.Librarian.Plan(SyxFile.Read([0xF0, 0x43, 0x10, 0xF7, .. file]));

        Assert.HasCount(1, plan.Programs);
        Assert.AreEqual(0, plan.PresetCount);
        Assert.IsEmpty(plan.Dumps);
        Assert.HasCount(4, plan.Warnings);
    }

    [TestMethod]
    [Description("PRG-005, DMP-011: restore plans skip system dumps that hold illegal values")]
    public async Task Plan_SkipsInvalidDumps()
    {
        await using Rig rig = await CreateAsync();
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
