using System.Collections.Concurrent;

using BB84.MPX1.Core.Database;
using BB84.MPX1.Core.Files;
using BB84.MPX1.Core.Programs;
using BB84.MPX1.Core.Protocol;
using BB84.MPX1.Midi;
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
        Assert.IsFalse(rig.Sent.OfType<Request>().Any(r => r.RequestedClass == MessageClass.ProgramDump), "Listing must not transfer full dumps.");
    }

    [TestMethod]
    [Description("PRG-001, PRG-004: user registers are stored freely; presets only after confirmation")]
    public async Task Store_RequiresConfirmationForPresets()
    {
        await using Rig rig = await CreateAsync();
        ProgramData program = await rig.Librarian.ReceiveAsync(7, TestContext.CancellationToken) with { Name = "Stored      " };
        rig.Sent.Clear();

        await Assert.ThrowsExactlyAsync<PresetOverwriteException>(() => rig.Librarian.StoreAsync(3, program, presetConfirmed: false, TestContext.CancellationToken));
        Assert.IsFalse(rig.Sent.OfType<ProgramDump>().Any());

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

        CollectionAssert.AreEqual(rig.Simulator.GetProgram(42).ToBytes(), rig.Simulator.ActiveProgram.ToBytes());
    }

    [TestMethod]
    [Description("AC-017, PRG-005, SES-005: backup, change, restore leaves the simulator byte-identical")]
    public async Task BackupRestore_RoundTrip()
    {
        await using Rig rig = await CreateAsync();
        CollectionAssert.AreEquivalent(SystemDumps.BackupKinds, rig.Librarian.AvailableDumps().ToArray());
        string before = Snapshot(rig.Simulator);

        IReadOnlyList<Mpx1Message> backup = await rig.Librarian.BackupAsync(SystemDumps.BackupKinds, cancellationToken: TestContext.CancellationToken);
        byte[] file = SyxFile.Write(backup);
        Assert.AreEqual(1 + 250 + 6 + 3, backup.Count, "Database, 250 programs, 6 setups, global patches, bypass and remap.");

        // Change programs, sort flags and a dump.
        ProgramData changed = rig.Simulator.GetProgram(0) with { Name = "Changed     " };
        await rig.Librarian.StoreAsync(0, changed, presetConfirmed: true, TestContext.CancellationToken);
        await rig.Librarian.StoreAsync(249, changed, presetConfirmed: false, TestContext.CancellationToken);
        await rig.Librarian.WriteDatabaseAsync([.. Enumerable.Repeat(default(ProgramCategories), 250)], TestContext.CancellationToken);
        await rig.Session.SendAsync(new ParameterData(Device, new byte[20], new ControlAddress(1, 12, 0)), TestContext.CancellationToken);
        Assert.AreNotEqual(before, Snapshot(rig.Simulator));

        RestorePlan plan = rig.Librarian.Plan(SyxFile.Read(file));
        Assert.AreEqual(200, plan.PresetCount);
        Assert.HasCount(9, plan.Dumps);
        Assert.IsEmpty(plan.Warnings);
        await Assert.ThrowsExactlyAsync<PresetOverwriteException>(() => rig.Librarian.RestoreAsync(plan, presetsConfirmed: false, cancellationToken: TestContext.CancellationToken));

        await rig.Librarian.RestoreAsync(plan, presetsConfirmed: true, cancellationToken: TestContext.CancellationToken);

        Assert.AreEqual(before, Snapshot(rig.Simulator));
        Assert.IsFalse(rig.Simulator.IsCrashed);
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
        CollectionAssert.AreEqual(valid.Data.ToArray(), plan.Dumps[0].Data.ToArray());
        StringAssert.Contains(plan.Warnings.Single(), "illegal value");
    }
}
