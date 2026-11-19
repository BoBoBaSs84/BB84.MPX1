using System.Diagnostics;
using System.Globalization;

using BB84.MPX1.Core.Database;
using BB84.MPX1.Core.Dumps;
using BB84.MPX1.Core.Files;
using BB84.MPX1.Core.Programs;
using BB84.MPX1.Core.Protocol;
using BB84.MPX1.Midi.Dumps;
using BB84.MPX1.Midi.Library;

namespace BB84.MPX1.Midi.WinMM.Tests.Hardware;

/// <summary>
/// Learning, program and backup tests against a real MPX 1 (roadmap M8). Front-panel program numbers are 1–250;
/// protocol numbers are 0–249.
/// </summary>
/// <remarks>
/// Optional environment variables:
/// <list type="bullet">
/// <item><c>MPX1_READ_SLOT</c>/<c>MPX1_READ_NAME</c>: a protocol program number and its expected name (default 210, "no exec on").</item>
/// <item><c>MPX1_WRITE_SLOT</c>: the user program the write test may change and restore (default 214, front panel 215). It must not be cleared ("available"): the unit sends no dump for cleared programs, so there is nothing to restore. Its contents are
/// saved to <c>artifacts/hardware/backups</c> before anything is written.</item>
/// </list>
/// </remarks>
[TestClass]
[TestCategory("Hardware")]
[DoNotParallelize]
public sealed class HardwareProgramTests
{
    private static readonly Mpx1SessionOptions LearnOptions = new() { RequestTimeout = TimeSpan.FromSeconds(1), Retries = 1 };

    public TestContext TestContext { get; set; } = null!;

    private static ushort SlotFromEnvironment(string variable, ushort fallback)
        => ushort.Parse(Environment.GetEnvironmentVariable(variable) ?? fallback.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

    private static string Stamp() => DateTime.Now.ToString("yyyy-MM-dd HHmmss", CultureInfo.InvariantCulture);

    [TestMethod]
    [Description("LRN-001, LRN-002, LRN-004: the device is learned completely and the database is cached")]
    public async Task Learn_WalksDescriptionsAndTree()
    {
        await using HardwareRig rig = await HardwareRig.OpenAsync(TestContext);
        await using Mpx1Session session = rig.StartSession(LearnOptions);
        Stopwatch watch = Stopwatch.StartNew();

        ParameterDatabase database = await rig.LoadDatabaseAsync(session, forceRelearn: true);

        rig.Log($"Learned {database.Key.FileStem} in {watch.Elapsed.TotalSeconds:F1} s: {database.Descriptions.Count} descriptions, {database.NodeCount} nodes, {database.Gaps.Length} gaps.");
        foreach (ControlAddress gap in database.Gaps)
            rig.Log($"Gap: {gap}");
        Assert.IsGreaterThan(0, database.Descriptions.Count);
        Assert.IsGreaterThan(1, database.NodeCount);
    }

    [TestMethod]
    [Description("PRG-007, PRG-001: a stored program is listed by name and read as a Program Dump")]
    public async Task ReadSlot_NameAndDump()
    {
        ushort number = SlotFromEnvironment("MPX1_READ_SLOT", 210);
        string expected = Environment.GetEnvironmentVariable("MPX1_READ_NAME") ?? "no exec on";
        await using HardwareRig rig = await HardwareRig.OpenAsync(TestContext);
        await using Mpx1Session session = rig.StartSession();

        ProgramInformation info = await session.RequestAsync<ProgramInformation>(Request.ProgramInformation(rig.Device, number), TestContext.CancellationToken);
        ProgramDump dump = await session.RequestAsync<ProgramDump>(Request.ProgramDump(rig.Device, number), TestContext.CancellationToken);

        rig.Log($"Program {number} (front panel {number + 1}): \"{info.Name}\", algorithms {string.Join(' ', info.Algorithms)}.");
        rig.Log($"Dump: \"{dump.Program.Name}\", tempo {dump.Program.Tempo}, algorithms {string.Join(' ', dump.Program.Algorithms)}, {dump.ToBytes().Length} bytes.");
        File.WriteAllBytes(HardwareRig.ArtifactPath("programs", $"{number:D3} {Stamp()}.syx"), SyxFile.Write([dump]));
        Assert.AreEqual(expected, info.Name.Trim(), StringComparer.OrdinalIgnoreCase);
        Assert.AreEqual(info.Name.Trim(), dump.Program.Name.Trim());
        Assert.AreSequenceEqual(info.Algorithms.ToArray(), dump.Program.Algorithms.ToArray());
        Assert.AreSequenceEqual(dump.ToBytes(), Mpx1Codec.Encode(Mpx1Codec.Decode(dump.ToBytes())));
    }

    [TestMethod]
    [Description("PRG-005, PRG-006: a full backup reads all programs, the sort flags and the system dumps into a .syx file that re-reads identically")]
    public async Task FullBackup_WritesSyxFile()
    {
        await using HardwareRig rig = await HardwareRig.OpenAsync(TestContext);
        await using Mpx1Session session = rig.StartSession();
        ParameterDatabase database = await rig.LoadDatabaseAsync(session);
        ProgramLibrarian librarian = new(session, database);
        IReadOnlyList<SystemDumpKind> dumps = librarian.AvailableDumps();
        rig.Log($"System dumps: {string.Join(", ", dumps)}.");
        Stopwatch watch = Stopwatch.StartNew();

        IReadOnlyList<Mpx1Message> messages = await librarian.BackupAsync(dumps, cancellationToken: TestContext.CancellationToken);

        byte[] file = SyxFile.Write(messages);
        string path = HardwareRig.ArtifactPath("backups", $"MPX1 backup {Stamp()}.syx");
        File.WriteAllBytes(path, file);
        rig.Log($"Backed up {messages.Count} messages ({file.Length} bytes) in {watch.Elapsed.TotalSeconds:F1} s to {path}.");

        SyxContent content = SyxFile.Read(file);
        Assert.IsEmpty(content.Warnings, string.Join(Environment.NewLine, content.Warnings));
        int[] skipped = [.. Enumerable.Range(0, ProgramSlots.Count).Except(content.Programs.Select(p => (int)p.ProgramNumber))];
        rig.Log($"Cleared user programs left out (front panel numbers): {(skipped.Length == 0 ? "none" : string.Join(", ", skipped.Select(n => n + 1)))}.");
        Assert.IsTrue(skipped.All(ProgramSlots.IsUser), "Every preset is in the backup.");
        Assert.IsNotNull(content.Database);
        Assert.AreSequenceEqual(file, SyxFile.Write(content.Messages));
        foreach (ProgramDump program in content.Programs.Where(p => ProgramSlots.IsUser(p.ProgramNumber)))
            rig.Log($"{program.ProgramNumber + 1:D3} {program.Program.Name}");

        // The restore plan validates every dump against its documented layout (DMP-011); nothing is written.
        RestorePlan plan = librarian.Plan(content);
        foreach (ParameterData dump in plan.Dumps)
            rig.Log($"Dump {dump.Address}: {dump.Data.Length} bytes.");
        foreach (string warning in plan.Warnings)
            rig.Log($"Plan warning: {warning}");
        CollectionAssert.IsSubsetOf(new[] { SystemDumpKind.AllSetups, SystemDumpKind.GlobalPatches, SystemDumpKind.BypassControllers, SystemDumpKind.RemapControllers }, dumps.ToArray());
        Assert.HasCount(content.Dumps.Count(), plan.Dumps);
        Assert.IsEmpty(plan.Warnings);
    }

    [TestMethod]
    [Description("DMP-004: the combined setup dump holds the five stored setups, then the active setup")]
    public async Task CombinedSetupDump_HoldsStoredThenActive()
    {
        await using HardwareRig rig = await HardwareRig.OpenAsync(TestContext);
        await using Mpx1Session session = rig.StartSession();
        ParameterDatabase database = await rig.LoadDatabaseAsync(session);
        IReadOnlyList<ControlTreeNode> stored = SystemDumps.Find(database, SystemDumpKind.Setup);
        ControlTreeNode? combined = database.Root.DescendantsAndSelf()
            .FirstOrDefault(n => SystemDumps.IsDump(n) && n.Name.Contains("Setup", StringComparison.OrdinalIgnoreCase) && database.ValueSize(n.Type) > 100);
        Assert.IsNotNull(combined, "No combined setup dump in the learned tree.");
        rig.Log($"Combined: {combined} ({database.ValueSize(combined.Type)} bytes); stored: {string.Join(", ", stored.Select(n => n.Address))}.");

        byte[] all = (await session.RequestAsync<ParameterData>(Request.ParameterData(rig.Device, combined.Address), TestContext.CancellationToken)).Data.ToArray();
        int size = database.ValueSize(stored[0].Type);
        DumpLayout layout = SystemDumps.Layout(SystemDumpKind.Setup, size)!;
        DumpField name = layout.Fields.Single(f => f.Name == "Setup name");
        for (int i = 0; i * size < all.Length; i++)
            rig.Log($"Chunk {i}: \"{name.ReadText(all.AsSpan(i * size, size)).TrimEnd()}\", valid {layout.IsValid(all.AsSpan(i * size, size))}, {Convert.ToHexString(all.AsSpan(i * size, size))}");
        for (int i = 0; i < stored.Count; i++)
        {
            byte[] one = (await session.RequestAsync<ParameterData>(Request.ParameterData(rig.Device, stored[i].Address), TestContext.CancellationToken)).Data.ToArray();
            rig.Log($"Stored {i}: \"{name.ReadText(one).TrimEnd()}\", same as chunk {i}: {one.AsSpan().SequenceEqual(all.AsSpan(i * size, size))}");
        }
        Assert.HasCount((stored.Count + 1) * size, all);
    }

    [TestMethod]
    [Description("DMP-004..DMP-007, PRG-005, SES-005: system dumps written back unchanged through the restore path read back identically")]
    public async Task SystemDumps_WriteBackUnchanged()
    {
        await using HardwareRig rig = await HardwareRig.OpenAsync(TestContext);
        await using Mpx1Session session = rig.StartSession();
        ParameterDatabase database = await rig.LoadDatabaseAsync(session);
        DumpService dumps = new(session, database);
        SystemDumpKind[] kinds = [SystemDumpKind.AllSetups, SystemDumpKind.GlobalPatches, SystemDumpKind.BypassControllers, SystemDumpKind.RemapControllers];
        List<(SystemDumpKind Kind, ControlTreeNode Node, byte[] Data)> before = [];
        foreach (SystemDumpKind kind in kinds)
        {
            foreach (ControlTreeNode node in dumps.Nodes(kind))
                before.Add((kind, node, await dumps.ReadAsync(node, TestContext.CancellationToken)));
        }
        string backup = HardwareRig.ArtifactPath("backups", $"system dumps before write-back {Stamp()}.syx");
        File.WriteAllBytes(backup, SyxFile.Write([.. before.Select(b => new ParameterData(rig.Device, b.Data, b.Node.Address))]));
        rig.Log($"Saved {before.Count} dumps to {backup}.");
        Assert.HasCount(kinds.Length, before, "One node per kind on this firmware.");

        foreach ((SystemDumpKind kind, ControlTreeNode node, byte[] data) in before)
        {
            Stopwatch watch = Stopwatch.StartNew();
            await dumps.WriteAsync(kind, node, data, TestContext.CancellationToken);
            long written = watch.ElapsedMilliseconds;
            byte[] after = await dumps.ReadAsync(node, TestContext.CancellationToken);
            rig.Log($"{SystemDumps.DisplayName(kind)} at {node.Address}: {data.Length} bytes written in {written} ms, read back {(after.AsSpan().SequenceEqual(data) ? "identical" : "DIFFERENT")}.");
            Assert.AreSequenceEqual(data, after, SystemDumps.DisplayName(kind));
        }
    }

    [TestMethod]
    [Description("PRG-001: reading an empty user register fails at once with a clear error instead of timing out")]
    public async Task ReadEmptySlot_ThrowsEmptyProgram()
    {
        ushort number = SlotFromEnvironment("MPX1_EMPTY_SLOT", 249);
        await using HardwareRig rig = await HardwareRig.OpenAsync(TestContext);
        await using Mpx1Session session = rig.StartSession();
        ParameterDatabase database = await rig.LoadDatabaseAsync(session);
        ProgramLibrarian librarian = new(session, database);
        if (!await librarian.IsClearedAsync(number, TestContext.CancellationToken))
            Assert.Inconclusive($"Program {number} (front panel {number + 1}) is not empty; set MPX1_EMPTY_SLOT to an empty user program.");
        Stopwatch watch = Stopwatch.StartNew();

        EmptyProgramException error = await Assert.ThrowsExactlyAsync<EmptyProgramException>(() => librarian.ReceiveAsync(number, TestContext.CancellationToken));

        rig.Log($"{error.Message} ({watch.ElapsedMilliseconds} ms)");
        Assert.IsLessThan(TimeSpan.FromSeconds(1), watch.Elapsed);
    }

    [TestMethod]
    [Description("PRG-001, PRG-004, SES-005: a user program is written with handshake flow control, read back identically, and restored")]
    public async Task WriteSlot_RoundTripsAndRestores()
    {
        ushort number = SlotFromEnvironment("MPX1_WRITE_SLOT", 214);
        Assert.IsTrue(ProgramSlots.IsUser(number), $"Program {number} is not a user program.");
        await using HardwareRig rig = await HardwareRig.OpenAsync(TestContext);
        await using Mpx1Session session = rig.StartSession();
        ParameterDatabase database = await rig.LoadDatabaseAsync(session);
        ProgramLibrarian librarian = new(session, database);

        ProgramData original = await librarian.ReceiveAsync(number, TestContext.CancellationToken);
        string backup = HardwareRig.ArtifactPath("backups", $"{number:D3} before write test {Stamp()}.syx");
        File.WriteAllBytes(backup, SyxFile.Write([new ProgramDump(rig.Device, number, original)]));
        rig.Log($"Program {number} (front panel {number + 1}) \"{original.Name}\" saved to {backup}.");

        string testName = ProgramFields.NormalizeName(original.Name.Trim() == "BB84 TEST" ? "BB84 TEST 2" : "BB84 TEST");
        ProgramData changed = original with { Name = testName };
        ProgramData? readBack = null;
        ProgramData restored;
        Stopwatch watch = Stopwatch.StartNew();
        try
        {
            await librarian.StoreAsync(number, changed, presetConfirmed: false, TestContext.CancellationToken);
            rig.Log($"Stored \"{testName}\" in {watch.ElapsedMilliseconds} ms.");
            readBack = await librarian.ReceiveAsync(number, TestContext.CancellationToken);
        }
        finally
        {
            await librarian.StoreAsync(number, original, presetConfirmed: false, CancellationToken.None);
            restored = await librarian.ReceiveAsync(number, CancellationToken.None);
        }

        AssertSameBytes(rig, original, restored, "restored");
        AssertSameBytes(rig, changed, readBack, "read back");
    }

    [TestMethod]
    [Description("PRG-005, PRG-004, CON-007, SES-005: a full backup restored with handshake flow control yields a second backup identical apart from unused effect-data bytes")]
    public async Task FullRestore_SecondBackupMatches()
    {
        await using HardwareRig rig = await HardwareRig.OpenAsync(TestContext);
        await using Mpx1Session session = rig.StartSession();
        ParameterDatabase database = await rig.LoadDatabaseAsync(session);
        ProgramLibrarian librarian = new(session, database);
        IReadOnlyList<SystemDumpKind> dumps = librarian.AvailableDumps();
        Stopwatch watch = Stopwatch.StartNew();

        IReadOnlyList<Mpx1Message> first = await librarian.BackupAsync(dumps, cancellationToken: TestContext.CancellationToken);
        byte[] file = SyxFile.Write(first);
        string backup = HardwareRig.ArtifactPath("backups", $"MPX1 backup before restore test {Stamp()}.syx");
        File.WriteAllBytes(backup, file);
        rig.Log($"Backed up {first.Count} messages in {watch.Elapsed.TotalSeconds:F1} s to {backup}.");

        RestorePlan plan = librarian.Plan(SyxFile.Read(file));
        foreach (string warning in plan.Warnings)
            rig.Log($"Plan warning: {warning}");
        Assert.IsEmpty(plan.Warnings);
        Assert.AreEqual(ProgramSlots.FirstUser, plan.PresetCount);
        rig.Log($"Plan: {plan.Programs.Length} programs ({plan.PresetCount} presets), sort flags {plan.Database is not null}, {plan.Dumps.Length} system dumps.");

        RestoreTiming timing = new();
        IReadOnlyList<ushort> unset;
        session.FrameTraffic += timing.OnFrame;
        try
        {
            await Assert.ThrowsExactlyAsync<PresetOverwriteException>(() => librarian.RestoreAsync(plan, presetsConfirmed: false, cancellationToken: TestContext.CancellationToken));
            Assert.AreEqual(0, timing.Sent, "Nothing is sent before the presets are confirmed.");

            watch.Restart();
            Progress<LibraryProgress> progress = new(p => rig.Log($"Restore: {p.Step} {p.Done}/{p.Total}"));
            unset = await librarian.RestoreAsync(plan, presetsConfirmed: true, progress, TestContext.CancellationToken);
            rig.Log($"Restored in {watch.Elapsed.TotalSeconds:F1} s. Sort flags not restored: {(unset.Count == 0 ? "none" : string.Join(", ", unset.Select(n => n + 1)))}.");
        }
        finally
        {
            session.FrameTraffic -= timing.OnFrame;
        }
        foreach (string line in timing.Summary())
            rig.Log(line);

        watch.Restart();
        IReadOnlyList<Mpx1Message> second = await librarian.BackupAsync(dumps, cancellationToken: TestContext.CancellationToken);
        rig.Log($"Second backup: {second.Count} messages in {watch.Elapsed.TotalSeconds:F1} s.");

        // Preset reads carry varying values in effect-data bytes no parameter uses (spec section 9, E19).
        int differing = 0;
        int paddingOnly = 0;
        foreach ((Mpx1Message before, Mpx1Message after) in first.Zip(second))
        {
            byte[] b = before.ToBytes();
            byte[] a = after.ToBytes();
            if (b.AsSpan().SequenceEqual(a))
                continue;
            if (Comparable(database, before).AsSpan().SequenceEqual(Comparable(database, after)))
            {
                paddingOnly++;
                continue;
            }
            differing++;
            int[] offsets = [.. Enumerable.Range(0, Math.Min(b.Length, a.Length)).Where(i => b[i] != a[i]).Take(10)];
            rig.Log($"{Name(before)} differs: {b.Length} vs {a.Length} bytes, first frame offsets {string.Join(", ", offsets)}.");
        }
        rig.Log($"Programs that differ only in unused effect-data bytes: {paddingOnly}.");
        Assert.IsEmpty(unset, "Sort flags not restored.");
        Assert.HasCount(first.Count, second, "Message count of the second backup.");
        Assert.AreEqual(0, differing, $"{differing} messages differ after the restore.");
    }

    private static byte[] Comparable(ParameterDatabase database, Mpx1Message message)
        => message is ProgramDump dump ? (dump with { Program = database.WithoutUnusedEffectBytes(dump.Program) }).ToBytes() : message.ToBytes();

    [TestMethod]
    [Description("PRO-018: a sort-flag change of preset 1, written as a compensated Database Dump on V1.00, reads back at once and is restored (spec 9, E18)")]
    public async Task SortFlags_PresetChange_RoundTrips()
    {
        await using HardwareRig rig = await HardwareRig.OpenAsync(TestContext);
        await using Mpx1Session session = rig.StartSession();
        ParameterDatabase database = await rig.LoadDatabaseAsync(session);
        ProgramLibrarian librarian = new(session, database);
        ProgramCategories[] original = [.. (await librarian.ReadDatabaseAsync(TestContext.CancellationToken)).Entries];
        File.WriteAllBytes(HardwareRig.ArtifactPath("backups", $"sort flags before write test {Stamp()}.syx"), SyxFile.Write([new DatabaseDump(rig.Device, [.. original])]));
        ProgramCategories[] changed = [.. original];
        changed[0] = original[0] with { Sources = original[0].Sources ^ SourceCategories.SoundFX };

        IReadOnlyList<ushort> unsetChanged;
        ProgramCategories[] afterChange;
        IReadOnlyList<ushort> unsetRestored;
        try
        {
            unsetChanged = await librarian.WriteDatabaseAsync(changed, TestContext.CancellationToken);
            afterChange = [.. (await librarian.ReadDatabaseAsync(TestContext.CancellationToken)).Entries];
        }
        finally
        {
            unsetRestored = await librarian.WriteDatabaseAsync(original, CancellationToken.None);
        }
        ProgramCategories[] restored = [.. (await librarian.ReadDatabaseAsync(TestContext.CancellationToken)).Entries];

        rig.Log($"Program 001: original {original[0]}, written {changed[0]}, read {afterChange[0]}. Not taken: {string.Join(", ", unsetChanged)} / {string.Join(", ", unsetRestored)}.");
        CollectionAssert.IsSubsetOf(unsetChanged.ToArray(), SortFlagTable.UnwritableOnV100.ToArray());
        CollectionAssert.IsSubsetOf(unsetRestored.ToArray(), SortFlagTable.UnwritableOnV100.ToArray());
        Assert.AreEqual(changed[0], afterChange[0]);
        int[] differing = [.. Enumerable.Range(0, restored.Length).Where(i => restored[i] != original[i])];
        Assert.AreSequenceEqual(unsetRestored.Select(n => (int)n).ToArray(), differing);
    }

    private static string Name(Mpx1Message message) => message switch
    {
        ProgramDump dump => $"Program {dump.ProgramNumber + 1:D3} \"{dump.Program.Name.Trim()}\"",
        ParameterData data => $"Dump {data.Address}",
        _ => message.GetType().Name,
    };

    /// <summary>Measures, for every bulk write, the time to BUSY and from BUSY to READY.</summary>
    private sealed class RestoreTiming
    {
        private readonly List<double> _toBusy = [];
        private readonly List<double> _toReady = [];
        private DateTimeOffset? _sent;
        private DateTimeOffset? _busy;
        private int _noBusy;

        public int Sent { get; private set; }

        public void OnFrame(object? sender, FrameEventArgs e)
        {
            lock (_toBusy)
            {
                switch (e.Direction, e.Message)
                {
                    case (FrameDirection.Sent, ProgramDump or DatabaseDump or ParameterData { Data.Length: > 2 }):
                        if (_sent is not null && _busy is null)
                            _noBusy++;
                        Sent++;
                        _sent = e.Timestamp;
                        _busy = null;
                        break;
                    case (FrameDirection.Sent, _):
                        Sent++;
                        break;
                    case (FrameDirection.Received, Handshake { Command: HandshakeCommand.Busy }) when _sent is { } sent:
                        _busy = e.Timestamp;
                        _toBusy.Add((e.Timestamp - sent).TotalMilliseconds);
                        break;
                    case (FrameDirection.Received, Handshake { Command: HandshakeCommand.Ready }) when _busy is { } busy:
                        _toReady.Add((e.Timestamp - busy).TotalMilliseconds);
                        _sent = null;
                        _busy = null;
                        break;
                }
            }
        }

        public string[] Summary()
        {
            lock (_toBusy)
            {
                int noBusy = _noBusy + (_sent is not null && _busy is null ? 1 : 0);
                return [
                    $"Frames sent: {Sent}. Bulk writes without BUSY: {noBusy}.",
                    $"Send to BUSY: {Stats(_toBusy)}.",
                    $"BUSY to READY: {Stats(_toReady)}.",
                ];
            }
        }

        private static string Stats(List<double> values)
            => values.Count == 0 ? "none" : $"{values.Count} times, min {values.Min():F0} ms, avg {values.Average():F0} ms, max {values.Max():F0} ms";
    }

    private static void AssertSameBytes(HardwareRig rig, ProgramData expected, ProgramData? actual, string what)
    {
        Assert.IsNotNull(actual, $"Nothing {what}.");
        byte[] e = expected.ToBytes();
        byte[] a = actual.ToBytes();
        int[] differences = [.. Enumerable.Range(0, Math.Min(e.Length, a.Length)).Where(i => e[i] != a[i])];
        foreach (int i in differences.Take(20))
            rig.Log($"{what}: offset {i}: expected {e[i]:X2}, actual {a[i]:X2}");
        Assert.HasCount(e.Length, a, $"Length {what}.");
        Assert.IsEmpty(differences, $"{differences.Length} bytes differ ({what}).");
    }
}
