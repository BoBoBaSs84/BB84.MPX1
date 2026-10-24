using System.Collections.Immutable;

using BB84.MPX1.Core.Database;
using BB84.MPX1.Core.Files;
using BB84.MPX1.Core.Programs;
using BB84.MPX1.Core.Protocol;
using BB84.MPX1.Midi.Dumps;

namespace BB84.MPX1.Midi.Library;

/// <summary>One librarian slot: name and algorithms from Program Information, sort flags from the Database Dump.</summary>
/// <param name="Number">Program number 0–249.</param>
/// <param name="Name">The program name, trimmed.</param>
/// <param name="Algorithms">Algorithm numbers, Pitch through Delay.</param>
/// <param name="Categories">Sort flags.</param>
public sealed record ProgramSummary(ushort Number, string Name, ImmutableArray<byte> Algorithms, ProgramCategories Categories)
{
    /// <summary>Gets a value indicating whether the slot is a factory preset (PRG-004).</summary>
    public bool IsPreset => ProgramSlots.IsPreset(Number);
}

/// <summary>Progress of a librarian operation.</summary>
/// <param name="Step">What is being transferred.</param>
/// <param name="Done">Items done.</param>
/// <param name="Total">Items in total.</param>
public sealed record LibraryProgress(string Step, int Done, int Total);

/// <summary>Thrown instead of overwriting a factory preset without confirmation (PRG-004, CON-007).</summary>
public sealed class PresetOverwriteException : InvalidOperationException
{
    /// <summary>Initializes a new instance of the <see cref="PresetOverwriteException"/> class.</summary>
    public PresetOverwriteException()
    { }

    /// <summary>Initializes a new instance of the <see cref="PresetOverwriteException"/> class.</summary>
    /// <param name="message">The error message.</param>
    public PresetOverwriteException(string message)
        : base(message)
    { }

    /// <summary>Initializes a new instance of the <see cref="PresetOverwriteException"/> class.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception.</param>
    public PresetOverwriteException(string message, Exception innerException)
        : base(message, innerException)
    { }
}

/// <summary>What a backup file holds and what restoring it will write (CON-007 confirmation text).</summary>
/// <param name="Programs">Program dumps for slots 0–249.</param>
/// <param name="Database">The Database Dump, or <see langword="null"/>.</param>
/// <param name="Dumps">System dumps whose address is a dump in the learned database and whose values are legal (DMP-011).</param>
/// <param name="Warnings">Skipped frames and messages.</param>
public sealed record RestorePlan(ImmutableArray<ProgramDump> Programs, DatabaseDump? Database, ImmutableArray<ParameterData> Dumps, ImmutableArray<string> Warnings)
{
    /// <summary>Gets the number of factory presets the restore overwrites.</summary>
    public int PresetCount => Programs.Count(p => ProgramSlots.IsPreset(p.ProgramNumber));

    /// <summary>Gets a value indicating whether the plan writes anything.</summary>
    public bool IsEmpty => Programs.IsEmpty && Database is null && Dumps.IsEmpty;
}

/// <summary>
/// Moves programs, the sort-flag database and system dumps in and out of the device (PRG-001, PRG-004,
/// PRG-005, PRG-007). Bulk writes go through <see cref="Mpx1Session.SendAsync"/>, which waits for READY (SES-005).
/// </summary>
/// <param name="session">A started session.</param>
/// <param name="database">The learned database, used to find system dumps.</param>
public sealed class ProgramLibrarian(Mpx1Session session, ParameterDatabase database)
{
    private readonly Mpx1Session _session = session ?? throw new ArgumentNullException(nameof(session));
    private readonly ParameterDatabase _database = database ?? throw new ArgumentNullException(nameof(database));

    /// <summary>Lists all slots without transferring full dumps (PRG-007).</summary>
    /// <param name="progress">Progress receiver.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<IReadOnlyList<ProgramSummary>> ListAsync(IProgress<LibraryProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        DatabaseDump sortFlags = await ReadDatabaseAsync(cancellationToken).ConfigureAwait(false);
        List<ProgramSummary> slots = new(ProgramSlots.Count);
        for (ushort number = 0; number < ProgramSlots.Count; number++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ProgramInformation info = await _session.RequestAsync<ProgramInformation>(Request.ProgramInformation(_session.Device, number), cancellationToken).ConfigureAwait(false);
            slots.Add(new ProgramSummary(number, info.Name.Trim(), info.Algorithms, sortFlags.Entries[number]));
            Report(progress, "Program list", number + 1, ProgramSlots.Count);
        }
        return slots;
    }

    /// <summary>Reads a stored program or the active program (PRG-001).</summary>
    /// <param name="number">Program number 0–249 or <see cref="Request.ActiveProgram"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<ProgramData> ReceiveAsync(ushort number, CancellationToken cancellationToken = default)
    {
        RequireValid(number);
        ProgramDump dump = await _session.RequestAsync<ProgramDump>(Request.ProgramDump(_session.Device, number), cancellationToken).ConfigureAwait(false);
        return dump.Program;
    }

    /// <summary>Replaces the active program without storing it (edge case in spec 9).</summary>
    /// <param name="program">The program.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task SendToActiveAsync(ProgramData program, CancellationToken cancellationToken = default)
        => SendDumpAsync(Request.ActiveProgram, program, cancellationToken);

    /// <summary>Stores a program in a slot. Presets need <paramref name="presetConfirmed"/> (PRG-004).</summary>
    /// <param name="number">Program number 0–249.</param>
    /// <param name="program">The program.</param>
    /// <param name="presetConfirmed">The user confirmed overwriting a factory preset.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="PresetOverwriteException">The slot is a preset and the overwrite was not confirmed.</exception>
    public Task StoreAsync(ushort number, ProgramData program, bool presetConfirmed, CancellationToken cancellationToken = default)
    {
        if (number == Request.ActiveProgram || !ProgramSlots.IsValid(number))
            throw new ArgumentOutOfRangeException(nameof(number), number, "Programs are stored in slots 0-249.");
        RequirePresetConfirmation(ProgramSlots.IsPreset(number), presetConfirmed, $"Program {number} is a factory preset.");
        return SendDumpAsync(number, program, cancellationToken);
    }

    /// <summary>Copies a stored program into the active program.</summary>
    /// <param name="number">Program number 0–249.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<ProgramData> LoadToActiveAsync(ushort number, CancellationToken cancellationToken = default)
    {
        ProgramData program = await ReceiveAsync(number, cancellationToken).ConfigureAwait(false);
        await SendToActiveAsync(program, cancellationToken).ConfigureAwait(false);
        return program;
    }

    /// <summary>Reads the sort flags of all programs (PRO-018).</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<DatabaseDump> ReadDatabaseAsync(CancellationToken cancellationToken = default)
        => _session.RequestAsync<DatabaseDump>(Request.DatabaseDump(_session.Device), cancellationToken);

    /// <summary>Writes the sort flags of all programs. Changing flags of presets needs confirmation.</summary>
    /// <param name="entries">250 entries.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task WriteDatabaseAsync(IReadOnlyList<ProgramCategories> entries, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (entries.Count != DatabaseDump.ProgramCount)
            throw new ArgumentException($"The database has {DatabaseDump.ProgramCount} entries.", nameof(entries));
        return _session.SendAsync(new DatabaseDump(_session.Device, [.. entries]), cancellationToken);
    }

    /// <summary>Returns the system dump kinds the learned database contains.</summary>
    public IReadOnlyList<SystemDumpKind> AvailableDumps()
        => [.. SystemDumps.BackupKinds.Where(k => SystemDumps.Find(_database, k).Count > 0)];

    /// <summary>
    /// Reads the Database Dump, all 250 programs and the chosen system dumps, in that order (PRG-005). The result
    /// can be written with <see cref="SyxFile.Write"/>.
    /// </summary>
    /// <param name="dumps">System dumps to include.</param>
    /// <param name="progress">Progress receiver.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<IReadOnlyList<Mpx1Message>> BackupAsync(IEnumerable<SystemDumpKind> dumps, IProgress<LibraryProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dumps);
        List<ControlTreeNode> dumpNodes = [.. dumps.Distinct().SelectMany(k => SystemDumps.Find(_database, k))];
        int total = 1 + ProgramSlots.Count + dumpNodes.Count;
        List<Mpx1Message> messages = new(total);

        messages.Add(await ReadDatabaseAsync(cancellationToken).ConfigureAwait(false));
        Report(progress, "Database", 1, total);

        for (ushort number = 0; number < ProgramSlots.Count; number++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            messages.Add(await _session.RequestAsync<ProgramDump>(Request.ProgramDump(_session.Device, number), cancellationToken).ConfigureAwait(false));
            Report(progress, "Programs", messages.Count, total);
        }

        foreach (ControlTreeNode node in dumpNodes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            messages.Add(await _session.RequestAsync<ParameterData>(Request.ParameterData(_session.Device, node.Address), cancellationToken).ConfigureAwait(false));
            Report(progress, node.Name, messages.Count, total);
        }
        return messages;
    }

    /// <summary>
    /// Works out what a file would write: programs for valid slots, the Database Dump, and Parameter Data whose
    /// address is a dump in the learned database with a matching size and, for documented dumps, legal values
    /// (DMP-011). Everything else becomes a warning.
    /// </summary>
    /// <param name="content">The parsed file.</param>
    public RestorePlan Plan(SyxContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var warnings = content.Warnings.ToBuilder();
        var programs = ImmutableArray.CreateBuilder<ProgramDump>();
        var dumps = ImmutableArray.CreateBuilder<ParameterData>();
        DatabaseDump? sortFlags = null;

        foreach (Mpx1Message message in content.Messages)
        {
            switch (message)
            {
                case ProgramDump { ProgramNumber: < ProgramSlots.Count } dump:
                    programs.Add(dump);
                    break;
                case ProgramDump dump:
                    warnings.Add($"Program dump for {(dump.ProgramNumber == Request.ActiveProgram ? "the active program" : $"slot {dump.ProgramNumber}")} is not restored.");
                    break;
                case DatabaseDump database:
                    sortFlags ??= database;
                    break;
                case ParameterData data when _database.Find(data.Address) is { } node && SystemDumps.IsDump(node) && data.Data.Length == _database.ValueSize(node.Type):
                    if (InvalidDumpReason(node, data) is { } reason)
                        warnings.Add(reason);
                    else
                        dumps.Add(data);
                    break;
                case ParameterData data:
                    warnings.Add($"Parameter Data at {data.Address} does not match a dump of the connected firmware; skipped.");
                    break;
                default:
                    warnings.Add($"{message.GetType().Name} is not restored.");
                    break;
            }
        }
        return new RestorePlan(programs.ToImmutable(), sortFlags, dumps.ToImmutable(), warnings.ToImmutable());
    }

    /// <summary>Checks a dump of a known kind against its documented layout (DMP-011); dumps of unknown kind pass.</summary>
    private string? InvalidDumpReason(ControlTreeNode node, ParameterData data)
    {
        if (SystemDumps.KindOf(_database, node) is not { } kind)
            return null;
        try
        {
            DumpService.Validate(kind, data.Data.Span);
            return null;
        }
        catch (ArgumentException ex)
        {
            return $"{SystemDumps.DisplayName(kind)} at {data.Address}: {ex.Message} Skipped.";
        }
    }

    /// <summary>
    /// Writes a restore plan with handshake flow control (PRG-005, SES-005): Database Dump, programs, then system
    /// dumps. Overwriting presets needs <paramref name="presetsConfirmed"/> (PRG-004, CON-007).
    /// </summary>
    /// <param name="plan">The plan from <see cref="Plan"/>.</param>
    /// <param name="presetsConfirmed">The user confirmed overwriting presets.</param>
    /// <param name="progress">Progress receiver.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="PresetOverwriteException">The plan writes presets and they were not confirmed.</exception>
    public async Task RestoreAsync(RestorePlan plan, bool presetsConfirmed, IProgress<LibraryProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        RequirePresetConfirmation(plan.PresetCount > 0, presetsConfirmed, $"The restore overwrites {plan.PresetCount} factory preset(s).");

        int total = (plan.Database is null ? 0 : 1) + plan.Programs.Length + plan.Dumps.Length;
        int done = 0;
        if (plan.Database is { } sortFlags)
        {
            await _session.SendAsync(sortFlags with { Device = _session.Device }, cancellationToken).ConfigureAwait(false);
            Report(progress, "Database", ++done, total);
        }
        foreach (ProgramDump dump in plan.Programs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _session.SendAsync(dump with { Device = _session.Device }, cancellationToken).ConfigureAwait(false);
            Report(progress, "Programs", ++done, total);
        }
        foreach (ParameterData data in plan.Dumps)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _session.SendAsync(data with { Device = _session.Device }, cancellationToken).ConfigureAwait(false);
            Report(progress, "System dumps", ++done, total);
        }
    }

    private Task SendDumpAsync(ushort number, ProgramData program, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(program);
        return _session.SendAsync(new ProgramDump(_session.Device, number, program), cancellationToken);
    }

    private static void RequireValid(ushort number)
    {
        if (!ProgramSlots.IsValid(number))
            throw new ArgumentOutOfRangeException(nameof(number), number, "Programs are 0-249 or the active program (0xFFFF).");
    }

    private static void RequirePresetConfirmation(bool writesPreset, bool confirmed, string what)
    {
        if (writesPreset && !confirmed)
            throw new PresetOverwriteException($"{what} Overwriting presets needs confirmation.");
    }

    private static void Report(IProgress<LibraryProgress>? progress, string step, int done, int total)
    {
        if (progress is not null && (done == total || done % 10 == 0))
            progress.Report(new LibraryProgress(step, done, total));
    }
}
