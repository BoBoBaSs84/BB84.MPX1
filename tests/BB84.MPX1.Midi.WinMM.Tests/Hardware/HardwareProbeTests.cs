using System.Globalization;

using BB84.MPX1.Core.Files;
using BB84.MPX1.Core.Programs;
using BB84.MPX1.Core.Protocol;

namespace BB84.MPX1.Midi.WinMM.Tests.Hardware;

/// <summary>
/// Diagnostics against a real MPX 1: sends one raw request and logs every frame and short message that comes back.
/// </summary>
/// <remarks>
/// <c>MPX1_PROBE_PROGRAM</c> selects the protocol program number (default 203).
/// </remarks>
[TestClass]
[OSCondition(OperatingSystems.Windows)]
[TestCategory("Hardware")]
[DoNotParallelize]
public sealed class HardwareProbeTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [Description("PRG-007, PRG-001: raw Program Information and Program Dump answers for one program")]
    public async Task Probe_Program()
    {
        ushort number = ushort.Parse(Environment.GetEnvironmentVariable("MPX1_PROBE_PROGRAM") ?? "203", CultureInfo.InvariantCulture);
        await using HardwareRig rig = await HardwareRig.OpenAsync(TestContext);

        int info = await ProbeAsync(rig, Request.ProgramInformation(rig.Device, number), TimeSpan.FromSeconds(3));
        int dump = await ProbeAsync(rig, Request.ProgramDump(rig.Device, number), TimeSpan.FromSeconds(5));

        Assert.IsGreaterThan(0, info + dump, "No answer at all.");
    }

    [TestMethod]
    [Description("PRG-001, PRG-004, PRO-018: Program Dumps written without a Database Dump set the sort flags of their programs")]
    public async Task Probe_WriteProgramsFromBackup()
    {
        string? path = Environment.GetEnvironmentVariable("MPX1_PROBE_BACKUP");
        if (string.IsNullOrWhiteSpace(path))
            Assert.Inconclusive("Set MPX1_PROBE_BACKUP to a backup .syx and MPX1_PROBE_FIRST/MPX1_PROBE_LAST to protocol program numbers. The probe overwrites those programs.");
        ushort first = ushort.Parse(Environment.GetEnvironmentVariable("MPX1_PROBE_FIRST") ?? "0", CultureInfo.InvariantCulture);
        ushort last = ushort.Parse(Environment.GetEnvironmentVariable("MPX1_PROBE_LAST") ?? "0", CultureInfo.InvariantCulture);
        SyxContent backup = SyxFile.Read(File.ReadAllBytes(path));
        Assert.IsNotNull(backup.Database, "The backup has no Database Dump.");
        await using HardwareRig rig = await HardwareRig.OpenAsync(TestContext);
        await using Mpx1Session session = rig.StartSession();

        DatabaseDump before = await session.RequestAsync<DatabaseDump>(Request.DatabaseDump(rig.Device), TestContext.CancellationToken);
        foreach (ProgramDump dump in backup.Programs.Where(p => p.ProgramNumber >= first && p.ProgramNumber <= last))
        {
            await session.SendAsync(dump with { Device = rig.Device }, TestContext.CancellationToken);
            rig.Log($"Wrote {dump.ProgramNumber + 1:D3} \"{dump.Program.Name.Trim()}\", sort flags {dump.Program.Categories}.");
        }
        DatabaseDump after = await session.RequestAsync<DatabaseDump>(Request.DatabaseDump(rig.Device), TestContext.CancellationToken);

        int[] wrong = [.. Enumerable.Range(0, after.Entries.Length).Where(i => after.Entries[i] != backup.Database.Entries[i])];
        foreach (int i in wrong)
            rig.Log($"Program {i + 1:D3}: backup {backup.Database.Entries[i]}, before {before.Entries[i]}, after {after.Entries[i]}.");
        Assert.IsEmpty(wrong, $"{wrong.Length} sort flag entries differ from the backup.");
    }

    [TestMethod]
    [Description("PRG-001, PRO-018: a Program Dump written to a user program sets that program's sort flags")]
    public async Task Probe_UserProgramSortFlags()
    {
        ushort number = ushort.Parse(Environment.GetEnvironmentVariable("MPX1_WRITE_SLOT") ?? "214", CultureInfo.InvariantCulture);
        if (Environment.GetEnvironmentVariable("MPX1_PROBE_USER_FLAGS") != "1")
            Assert.Inconclusive($"Set MPX1_PROBE_USER_FLAGS=1 to change and restore the sort flags of program {number} (MPX1_WRITE_SLOT).");
        await using HardwareRig rig = await HardwareRig.OpenAsync(TestContext);
        await using Mpx1Session session = rig.StartSession();

        ProgramDump original = await session.RequestAsync<ProgramDump>(Request.ProgramDump(rig.Device, number), TestContext.CancellationToken);
        ProgramCategories flags = original.Program.Categories;
        ProgramCategories changed = flags with { Sources = flags.Sources ^ SourceCategories.SoundFX };
        ProgramCategories? stored;
        ProgramCategories? restored;
        try
        {
            await session.SendAsync(original with { Program = original.Program with { Categories = changed } }, TestContext.CancellationToken);
            stored = (await session.RequestAsync<DatabaseDump>(Request.DatabaseDump(rig.Device), TestContext.CancellationToken)).Entries[number];
        }
        finally
        {
            await session.SendAsync(original, CancellationToken.None);
            restored = (await session.RequestAsync<DatabaseDump>(Request.DatabaseDump(rig.Device), CancellationToken.None)).Entries[number];
        }
        rig.Log($"Program {number + 1:D3}: original {flags}, written {changed}, database after write {stored}, after restore {restored}.");
        Assert.AreEqual(changed, stored);
        Assert.AreEqual(flags, restored);
    }

    [TestMethod]
    [Description("PRO-018: a Database Dump written on its own, followed by a wait, reads back unchanged")]
    public async Task Probe_WriteSortFlagsFromBackup()
    {
        string? path = Environment.GetEnvironmentVariable("MPX1_PROBE_BACKUP");
        if (string.IsNullOrWhiteSpace(path))
            Assert.Inconclusive("Set MPX1_PROBE_BACKUP to a backup .syx and optionally MPX1_PROBE_WAIT (ms). The probe overwrites the sort flags of all programs.");
        int wait = int.Parse(Environment.GetEnvironmentVariable("MPX1_PROBE_WAIT") ?? "15000", CultureInfo.InvariantCulture);
        DatabaseDump? sortFlags = SyxFile.Read(File.ReadAllBytes(path)).Database;
        Assert.IsNotNull(sortFlags, "The backup has no Database Dump.");
        await using HardwareRig rig = await HardwareRig.OpenAsync(TestContext);
        await using Mpx1Session session = rig.StartSession();
        session.FrameTraffic += (_, e) => rig.Log($"{e.Direction}: {e.Message?.GetType().Name ?? "undecodable"} {(e.Message is Handshake h ? h.Command : "")} ({e.Frame.Length} bytes)");

        // MPX1_PROBE_SHIFT=n sends the first 100 bytes n bytes later, to test a receive offset in the first block.
        int shift = int.Parse(Environment.GetEnvironmentVariable("MPX1_PROBE_SHIFT") ?? "0", CultureInfo.InvariantCulture);
        byte[] frame = (sortFlags with { Device = rig.Device }).ToBytes();
        if (shift > 0)
        {
            byte[] original = [.. frame];
            Array.Copy(original, 5, frame, 5 + (2 * shift), 200 - (2 * shift));
            rig.Log($"Sending bytes 0-{99 - shift} at {shift}-99.");
        }

        DatabaseDump before = await session.RequestAsync<DatabaseDump>(Request.DatabaseDump(rig.Device), TestContext.CancellationToken);
        await session.SendAsync(Mpx1Codec.Decode(frame), TestContext.CancellationToken);
        rig.Log($"Sent the Database Dump; waiting {wait} ms.");
        await Task.Delay(wait, TestContext.CancellationToken);
        DatabaseDump after = await session.RequestAsync<DatabaseDump>(Request.DatabaseDump(rig.Device), TestContext.CancellationToken);

        byte[] expected = sortFlags.ToBytes();
        byte[] actual = after.ToBytes();
        int[] offsets = [.. Enumerable.Range(0, expected.Length).Where(i => expected[i] != actual[i])];
        rig.Log($"Frame offsets that differ from the backup: {(offsets.Length == 0 ? "none" : $"{offsets.Length}, {offsets[0]}-{offsets[^1]}")}.");
        rig.Log($"Before: {Convert.ToHexString(before.ToBytes().AsSpan(5, 220))}");
        rig.Log($"Sent:   {Convert.ToHexString(expected.AsSpan(5, 220))}");
        rig.Log($"After:  {Convert.ToHexString(actual.AsSpan(5, 220))}");
        Assert.IsEmpty(offsets, "The sort flags read back differ from the ones sent.");
    }

    private static string Describe(Mpx1Message message) => message switch
    {
        ProgramInformation info => $"ProgramInformation \"{info.Name}\" algorithms {string.Join(' ', info.Algorithms)}",
        ProgramDump dump => $"ProgramDump \"{dump.Program.Name}\"",
        _ => message.GetType().Name,
    };

    private async Task<int> ProbeAsync(HardwareRig rig, Request request, TimeSpan window)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
        timeout.CancelAfter(window);
        int count = 0;
        Task listen = Task.Run(async () =>
        {
            try
            {
                await foreach (MidiInput input in rig.Transport.ReadAllAsync(timeout.Token))
                {
                    count++;
                    if (input is SysExInput sysEx)
                    {
                        byte[] frame = sysEx.Frame.ToArray();
                        string decoded = Mpx1Codec.TryDecode(frame, out Mpx1Message? message, out Mpx1FormatError? error) ? Describe(message!) : $"undecodable: {error}";
                        rig.Log($"  in: {frame.Length} bytes, {decoded}, starts {Convert.ToHexString(frame.AsSpan(0, Math.Min(frame.Length, 24)))}, ends {Convert.ToHexString(frame.AsSpan(Math.Max(0, frame.Length - 8)))}");
                    }
                    else
                    {
                        rig.Log($"  in: {input}");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // The window ended.
            }
        }, TestContext.CancellationToken);

        rig.Log($"out: {request.RequestedClass} {Convert.ToHexString(request.ToBytes())}");
        await rig.Transport.SendSysExAsync(request.ToBytes(), TestContext.CancellationToken);
        await listen;
        rig.Log($"  {count} input(s) in {window.TotalSeconds} s.");
        return count;
    }
}
