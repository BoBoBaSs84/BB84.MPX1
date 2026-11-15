using BB84.MPX1.Core.Database;
using BB84.MPX1.Core.Dumps;
using BB84.MPX1.Core.Files;
using BB84.MPX1.Core.Programs;
using BB84.MPX1.Core.Protocol;
using BB84.MPX1.Midi;
using BB84.MPX1.Midi.Dumps;
using BB84.MPX1.Midi.Learning;
using BB84.MPX1.Midi.Library;
using BB84.MPX1.Simulator.Recording;

namespace BB84.MPX1.Simulator.Tests;

/// <summary>
/// Replays the recordings of a real MPX 1 V1.00 in <c>tests/data/hardware/v100</c> (roadmap M8). Each test repeats the
/// call sequence of the hardware test that made the recording; strict replay fails on any difference.
/// </summary>
[TestClass]
public sealed class HardwareReplayTests
{
    private static readonly DeviceId Device = new(3);
    private static ParameterDatabase? _database;

    public TestContext TestContext { get; set; } = null!;

    private static string DataPath(string name) => Path.Combine(AppContext.BaseDirectory, "data", "hardware", "v100", name);

    // The learn recording was made with one retry; replies are released at once, so a short timeout only shortens the
    // two unanswered attempts (Pitch algorithm 10 has no Report Effect Parameters answer on V1.00).
    private static readonly Mpx1SessionOptions LearnOptions = new() { RequestTimeout = TimeSpan.FromMilliseconds(100), Retries = 1 };

    private static async Task<(ReplayMidiTransport Replay, Mpx1Session Session)> OpenAsync(string recording, CancellationToken cancellationToken, Mpx1SessionOptions? options = null)
    {
        using StreamReader reader = new(DataPath(recording));
        ReplayMidiTransport replay = new(RecordedFrame.ReadAll(reader));
        await replay.OpenAsync(ReplayMidiTransport.Port.Id, ReplayMidiTransport.Port.Id, cancellationToken);
        Mpx1Session session = new(replay, Device, options);
        session.Start();
        return (replay, session);
    }

    // The V1.00 database, learned once from the recording.
    private async Task<ParameterDatabase> DatabaseAsync()
    {
        if (_database is not null)
            return _database;
        var (replay, session) = await OpenAsync("learn.jsonl", TestContext.CancellationToken, LearnOptions);
        await using (replay)
        await using (session)
            return _database = await new ParameterLearner(session).LoadOrLearnAsync(forceRelearn: true, cancellationToken: TestContext.CancellationToken);
    }

    // The hardware tests load the cached database first, which sends one System Configuration request.
    private async Task<ParameterDatabase> LoadCachedAsync(Mpx1Session session)
    {
        InMemoryParameterDatabaseStore store = new();
        await store.SaveAsync(await DatabaseAsync(), TestContext.CancellationToken);
        return await new ParameterLearner(session, store).LoadOrLearnAsync(cancellationToken: TestContext.CancellationToken);
    }

    [TestMethod]
    [Description("SES-007, PRO-024: the V1.00 unit identifies as device 3, firmware V1.00")]
    public async Task Replay_DetectVersion()
    {
        var (replay, session) = await OpenAsync("detect-version.jsonl", TestContext.CancellationToken);
        await using (replay)
        await using (session)
        {
            DeviceVersion version = await session.DetectVersionAsync(TestContext.CancellationToken);

            Assert.AreEqual("V1.00", version.ToString());
            Assert.AreEqual(Device, version.Identity!.Device);
            Assert.AreEqual("Nov 21 1996", version.Configuration.BuildDate);
            Assert.AreEqual((ushort)448, version.Configuration.ParameterTypeCount);
            Assert.IsTrue(replay.IsComplete);
        }
    }

    [TestMethod]
    [Description("LRN-001, LRN-002, LRN-007: the recorded V1.00 learn yields the full tree, with the dead end at A:1 B:2 C:3")]
    public async Task Replay_Learn()
    {
        ParameterDatabase db = await DatabaseAsync();

        Assert.HasCount(448, db.Descriptions);
        Assert.AreEqual(2653, db.NodeCount);
        Assert.IsEmpty(db.Gaps);
        ControlTreeNode remap = db.Find(new ControlAddress(1, 2, 3))!;
        Assert.AreEqual("Cont Remap", remap.Name.TrimEnd('\0'));
        Assert.IsTrue(remap.IsDeadEnd);
        Assert.AreEqual("no effect", db.FindAlgorithm(EffectType.Pitch, 0)!.Name);
    }

    [TestMethod]
    [Description("DMP-004..DMP-007, LRN-005: the V1.00 tree has all setups, Global Patches, Bypass and Remap dumps under their V1.00 names")]
    public async Task Replay_Learn_FindsSystemDumps()
    {
        ParameterDatabase db = await DatabaseAsync();

        ControlTreeNode allSetups = SystemDumps.Find(db, SystemDumpKind.AllSetups).Single();
        Assert.AreEqual(new ControlAddress(1, 9, 1), allSetups.Address);
        Assert.AreEqual(258, db.ValueSize(allSetups.Type));
        Assert.HasCount(5, SystemDumps.Find(db, SystemDumpKind.Setup));
        // The unit pads 9-character names with a NUL byte, which is kept as-is (PRO-007).
        Assert.AreEqual("GPatcDump", SystemDumps.Find(db, SystemDumpKind.GlobalPatches).Single().Name.TrimEnd('\0'));
        Assert.AreEqual("BypCtDump", SystemDumps.Find(db, SystemDumpKind.BypassControllers).Single().Name.TrimEnd('\0'));
        Assert.AreEqual("RemapDump", SystemDumps.Find(db, SystemDumpKind.RemapControllers).Single().Name.TrimEnd('\0'));
    }

    [TestMethod]
    [Description("PRG-005, PRG-006, DMP-011: the recorded full backup holds every preset, the user programs and valid system dumps")]
    public async Task Replay_FullBackup()
    {
        var (replay, session) = await OpenAsync("backup.jsonl", TestContext.CancellationToken);
        await using (replay)
        await using (session)
        {
            ProgramLibrarian librarian = new(session, await LoadCachedAsync(session));
            IReadOnlyList<SystemDumpKind> kinds = librarian.AvailableDumps();

            IReadOnlyList<Mpx1Message> backup = await librarian.BackupAsync(kinds, cancellationToken: TestContext.CancellationToken);

            Assert.IsTrue(replay.IsComplete);
            CollectionAssert.AreEqual(new[] { SystemDumpKind.AllSetups, SystemDumpKind.GlobalPatches, SystemDumpKind.BypassControllers, SystemDumpKind.RemapControllers }, kinds.ToArray());
            ProgramDump[] programs = [.. backup.OfType<ProgramDump>()];
            Assert.IsTrue(Enumerable.Range(0, ProgramSlots.FirstUser).All(n => programs.Any(p => p.ProgramNumber == n)), "Every preset.");
            Assert.AreEqual("no exec on", programs.Single(p => p.ProgramNumber == 210).Program.Name.Trim());
            Assert.HasCount(9, programs.Where(p => ProgramSlots.IsUser(p.ProgramNumber)));

            RestorePlan plan = librarian.Plan(SyxFile.Read(SyxFile.Write(backup)));
            Assert.IsEmpty(plan.Warnings);
            Assert.HasCount(4, plan.Dumps);
            IReadOnlyList<ReadOnlyMemory<byte>> setups = SystemDumps.SplitSetups(plan.Dumps.Single(d => d.Data.Length == 258).Data);
            DumpField name = DumpLayouts.SetupV100.Fields.Single(f => f.Name == "Setup name");
            CollectionAssert.AreEqual(new[] { "Setup 1", "Setup 1", "Setup 2", "Setup 3", "Setup 4", "Setup 5" },
                setups.Select(s => name.ReadText(s.Span).TrimEnd()).ToArray(), "Active setup first, then stored setups 1-5.");
        }
    }

    [TestMethod]
    [Description("PRG-001, PRG-004, SES-005: the recorded write of program 214 reads back identically and is restored")]
    public async Task Replay_WriteSlot()
    {
        var (replay, session) = await OpenAsync("write-slot-215.jsonl", TestContext.CancellationToken);
        await using (replay)
        await using (session)
        {
            ProgramLibrarian librarian = new(session, await LoadCachedAsync(session));
            ProgramData original = await librarian.ReceiveAsync(214, TestContext.CancellationToken);
            ProgramData changed = original with { Name = ProgramFields.NormalizeName("BB84 TEST") };

            await librarian.StoreAsync(214, changed, presetConfirmed: false, TestContext.CancellationToken);
            ProgramData readBack = await librarian.ReceiveAsync(214, TestContext.CancellationToken);
            await librarian.StoreAsync(214, original, presetConfirmed: false, TestContext.CancellationToken);
            ProgramData restored = await librarian.ReceiveAsync(214, TestContext.CancellationToken);

            Assert.IsTrue(replay.IsComplete);
            Assert.AreEqual("Clean DlyRvb", original.Name.Trim());
            CollectionAssert.AreEqual(changed.ToBytes(), readBack.ToBytes());
            CollectionAssert.AreEqual(original.ToBytes(), restored.ToBytes());
        }
    }

    [TestMethod]
    [Description("DMP-004..DMP-007, PRG-005: the recorded write-back of the V1.00 system dumps reads back identically")]
    public async Task Replay_SystemDumpsWriteBack()
    {
        var (replay, session) = await OpenAsync("system-dumps-write-back.jsonl", TestContext.CancellationToken);
        await using (replay)
        await using (session)
        {
            DumpService dumps = new(session, await LoadCachedAsync(session));
            SystemDumpKind[] kinds = [SystemDumpKind.AllSetups, SystemDumpKind.GlobalPatches, SystemDumpKind.BypassControllers, SystemDumpKind.RemapControllers];
            List<(SystemDumpKind Kind, ControlTreeNode Node, byte[] Data)> before = [];
            foreach (SystemDumpKind kind in kinds)
            {
                foreach (ControlTreeNode node in dumps.Nodes(kind))
                    before.Add((kind, node, await dumps.ReadAsync(node, TestContext.CancellationToken)));
            }

            foreach ((SystemDumpKind kind, ControlTreeNode node, byte[] data) in before)
            {
                await dumps.WriteAsync(kind, node, data, TestContext.CancellationToken);
                CollectionAssert.AreEqual(data, await dumps.ReadAsync(node, TestContext.CancellationToken), SystemDumps.DisplayName(kind));
            }
            Assert.IsTrue(replay.IsComplete);
        }
    }

    [TestMethod]
    [Description("PRG-001: the recorded empty user program 249 is recognized from its Program Information")]
    public async Task Replay_ReadEmptySlot()
    {
        var (replay, session) = await OpenAsync("read-empty-slot-250.jsonl", TestContext.CancellationToken);
        await using (replay)
        await using (session)
        {
            ProgramLibrarian librarian = new(session, await LoadCachedAsync(session));

            Assert.IsTrue(await librarian.IsClearedAsync(249, TestContext.CancellationToken));
            await Assert.ThrowsExactlyAsync<EmptyProgramException>(() => librarian.ReceiveAsync(249, TestContext.CancellationToken));
            Assert.IsTrue(replay.IsComplete);
        }
    }
}
