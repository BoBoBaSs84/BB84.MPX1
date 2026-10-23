using System.Collections.ObjectModel;
using System.Globalization;

using CommunityToolkit.Mvvm.Input;

using BB84.MPX1.App.Services;
using BB84.MPX1.Core.Files;
using BB84.MPX1.Core.Programs;
using BB84.MPX1.Core.Protocol;
using BB84.MPX1.Midi.Library;

namespace BB84.MPX1.App.ViewModels;

/// <summary>Single-program <c>.syx</c> import/export and full backup/restore.</summary>
public sealed partial class LibrarianViewModel
{
    /// <summary>Gets the system dumps the connected firmware offers for backups.</summary>
    public ObservableCollection<DumpOptionViewModel> DumpOptions { get; } = [];

    /// <summary>Saves the selected slot as a <c>.syx</c> file (PRG-006).</summary>
    [RelayCommand(CanExecute = nameof(CanRunOnSelection))]
    public Task ExportAsync() => RunOnSelectionAsync("Exporting…", async (librarian, slot, _, ct) =>
    {
        ProgramData program = await librarian.ReceiveAsync(slot.Number, ct);
        byte[] file = SyxFile.Write([new ProgramDump(_device, slot.Number, program)]);
        string? saved = await _dialogs.SaveSyxAsync("Export program", $"{slot.NumberText} {SafeFileName(slot.Name)}.syx", file);
        return saved is null ? "Export cancelled." : $"Saved {slot.NumberText} {slot.Name} to {saved}.";
    });

    /// <summary>Stores the first program of a <c>.syx</c> file in the selected slot (PRG-006, PRG-004).</summary>
    [RelayCommand(CanExecute = nameof(CanRunOnSelection))]
    public Task ImportAsync() => RunOnSelectionAsync("Importing…", async (librarian, slot, _, ct) =>
    {
        PickedFile? file = await _dialogs.OpenSyxAsync($"Import a program into {slot.NumberText}");
        if (file is null)
            return "Import cancelled.";
        SyxContent content = SyxFile.Read(file.Contents);
        if (content.Programs.FirstOrDefault() is not { } dump)
            return $"{file.Name} contains no program. {string.Join(" ", content.Warnings)}";
        string name = dump.Program.Name.TrimEnd();
        if (!await ConfirmPresetAsync(slot, $"Overwrite preset {slot.NumberText} \"{slot.Name}\" with \"{name}\" from {file.Name}?"))
            return "Import cancelled.";
        await librarian.StoreAsync(slot.Number, dump.Program, presetConfirmed: true, ct);
        slot.Name = name;
        slot.Algorithms = AlgorithmNames(dump.Program.Algorithms);
        string skipped = content.Warnings.IsEmpty ? "" : $" {content.Warnings.Length} frame(s) skipped.";
        return $"Stored \"{name}\" in {slot.NumberText}.{skipped}";
    });

    /// <summary>Backs up programs, sort flags and the chosen system dumps into a <c>.syx</c> file (PRG-005).</summary>
    [RelayCommand(CanExecute = nameof(CanRun))]
    public Task BackupAsync() => RunAsync("Backing up…", async (librarian, progress, ct) =>
    {
        IReadOnlyList<Mpx1Message> messages = await librarian.BackupAsync([.. DumpOptions.Where(d => d.IsChecked).Select(d => d.Kind)], progress, ct);
        string name = $"MPX1 backup {DateTime.Now.ToString("yyyy-MM-dd HHmm", CultureInfo.InvariantCulture)}.syx";
        string? saved = await _dialogs.SaveSyxAsync("Save backup", name, SyxFile.Write(messages));
        return saved is null ? "Backup not saved." : $"Backed up {messages.Count} messages to {saved}.";
    });

    /// <summary>Restores a backup file after confirmation (PRG-005, CON-007).</summary>
    [RelayCommand(CanExecute = nameof(CanRun))]
    public Task RestoreAsync() => RunAsync("Restoring…", async (librarian, progress, ct) =>
    {
        PickedFile? file = await _dialogs.OpenSyxAsync("Restore a backup");
        if (file is null)
            return "Restore cancelled.";
        RestorePlan plan = librarian.Plan(SyxFile.Read(file.Contents));
        if (plan.IsEmpty)
            return $"{file.Name} contains nothing to restore. {string.Join(" ", plan.Warnings)}";

        List<string> parts = [$"{plan.Programs.Length} program(s)"];
        if (plan.PresetCount > 0)
            parts[0] += $", including {plan.PresetCount} factory preset(s)";
        if (plan.Database is not null)
            parts.Add("the sort flags");
        if (!plan.Dumps.IsEmpty)
            parts.Add($"{plan.Dumps.Length} system dump(s)");
        string skipped = plan.Warnings.IsEmpty ? "" : $"\n\n{plan.Warnings.Length} item(s) in the file will be skipped.";
        if (!await _dialogs.ConfirmAsync("Restore backup", $"Restoring {file.Name} overwrites {string.Join(", ", parts)} on the MPX 1. This cannot be undone.{skipped}", "Restore"))
            return "Restore cancelled.";

        await librarian.RestoreAsync(plan, presetsConfirmed: true, progress, ct);
        ReplaceSlots(await librarian.ListAsync(null, ct));
        return $"Restored {file.Name}.";
    });

    private static string SafeFileName(string name)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        return new string([.. name.Trim().Select(c => invalid.Contains(c) ? '_' : c)]);
    }
}
