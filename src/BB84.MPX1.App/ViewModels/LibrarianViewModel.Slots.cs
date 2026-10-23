using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.Input;

using BB84.MPX1.Core.Programs;
using BB84.MPX1.Core.Protocol;
using BB84.MPX1.Midi.Library;

namespace BB84.MPX1.App.ViewModels;

/// <summary>Slot list, load to active, edit and rename.</summary>
public sealed partial class LibrarianViewModel
{
    /// <summary>Raised when the user wants to open a slot in the program editor.</summary>
    public event EventHandler<ushort>? EditRequested;

    /// <summary>Gets the slots.</summary>
    public ObservableCollection<ProgramSlotViewModel> Slots { get; } = [];

    /// <summary>Formats algorithm numbers as names from the learned lists.</summary>
    /// <param name="algorithms">Algorithm numbers, Pitch through Delay.</param>
    public string AlgorithmNames(IReadOnlyList<byte> algorithms)
    {
        ArgumentNullException.ThrowIfNull(algorithms);
        if (algorithms.Count > 0 && algorithms[0] == ProgramData.ClearedAlgorithm)
            return "(cleared)";
        List<string> names = [];
        for (int i = 0; i < Math.Min(algorithms.Count, ProgramData.EffectBlockCount); i++)
        {
            if (algorithms[i] == 0)
                continue;
            string? name = _database?.FindAlgorithm((EffectType)i, algorithms[i])?.Name;
            names.Add(name ?? $"{(EffectType)i} #{algorithms[i]}");
        }
        return names.Count == 0 ? "—" : string.Join(", ", names);
    }

    /// <summary>Reads the slot list (PRG-007).</summary>
    [RelayCommand(CanExecute = nameof(CanRun))]
    public Task RefreshAsync() => RunAsync("Reading the program list…", async (librarian, progress, ct) =>
    {
        ReplaceSlots(await librarian.ListAsync(progress, ct));
        return $"{Slots.Count} programs.";
    });

    /// <summary>Copies the selected slot into the active program (PRG-001).</summary>
    [RelayCommand(CanExecute = nameof(CanRunOnSelection))]
    public Task LoadToActiveAsync() => RunOnSelectionAsync("Loading…", async (librarian, slot, _, ct) =>
    {
        await librarian.LoadToActiveAsync(slot.Number, ct);
        return $"{slot.NumberText} {slot.Name} is now the active program.";
    });

    /// <summary>Opens the selected slot in the program editor.</summary>
    [RelayCommand(CanExecute = nameof(CanRunOnSelection))]
    public void Edit()
    {
        if (SelectedSlot is { } slot)
            EditRequested?.Invoke(this, slot.Number);
    }

    /// <summary>Updates a slot after the program editor stored a program there.</summary>
    /// <param name="number">The slot.</param>
    /// <param name="program">The stored program.</param>
    public void OnProgramStored(ushort number, ProgramData program)
    {
        ArgumentNullException.ThrowIfNull(program);
        if (Slots.FirstOrDefault(s => s.Number == number) is { } slot)
        {
            slot.Name = program.Name.TrimEnd();
            slot.Algorithms = AlgorithmNames(program.Algorithms);
        }
    }

    /// <summary>Renames the selected slot to <see cref="RenameText"/>; presets need confirmation (PRG-004).</summary>
    [RelayCommand(CanExecute = nameof(CanRunOnSelection))]
    public Task RenameAsync() => RunOnSelectionAsync("Renaming…", async (librarian, slot, _, ct) =>
    {
        string name = ProgramFields.NormalizeName(RenameText);
        if (!await ConfirmPresetAsync(slot, $"Rename preset {slot.NumberText} \"{slot.Name}\" to \"{name.TrimEnd()}\"?"))
            return "Rename cancelled.";
        ProgramData program = await librarian.ReceiveAsync(slot.Number, ct);
        await librarian.StoreAsync(slot.Number, program with { Name = name }, presetConfirmed: true, ct);
        slot.Name = name.TrimEnd();
        return $"Renamed {slot.NumberText} to \"{slot.Name}\".";
    });

    /// <summary>
    /// Replaces the slot list with what the unit reported, drops unwritten sort flag edits and selects the same
    /// program number again, so the selection and the flag checkboxes show the new contents.
    /// </summary>
    private void ReplaceSlots(IReadOnlyList<ProgramSummary> summaries)
    {
        int? selected = SelectedSlot?.Number;
        Slots.Clear();
        foreach (ProgramSummary summary in summaries)
            Slots.Add(new ProgramSlotViewModel(summary, AlgorithmNames));
        IsLoaded = true;
        HasUnsavedFlags = false;
        SelectedSlot = selected is int n ? Slots.FirstOrDefault(s => s.Number == n) : null;
    }

    private async Task<bool> ConfirmPresetAsync(ProgramSlotViewModel slot, string message)
        => !slot.IsPreset || await _dialogs.ConfirmAsync("Overwrite factory preset", $"{message} Factory presets can only be restored from a backup.", "Overwrite");
}
