using System.Collections.ObjectModel;
using System.Globalization;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using BB84.MPX1.App.Services;
using BB84.MPX1.Core.Database;
using BB84.MPX1.Core.Files;
using BB84.MPX1.Core.Programs;
using BB84.MPX1.Core.Protocol;
using BB84.MPX1.Midi;
using BB84.MPX1.Midi.Library;

namespace BB84.MPX1.App.ViewModels;

/// <summary>One sort flag checkbox.</summary>
public sealed partial class CategoryFlagViewModel(string name, int bit, bool isSource) : ObservableObject
{
    /// <summary>Gets the flag name.</summary>
    public string Name { get; } = name;

    /// <summary>Gets the bit value.</summary>
    public int Bit { get; } = bit;

    /// <summary>Gets a value indicating whether this is a source (input) flag rather than an effect flag.</summary>
    public bool IsSource { get; } = isSource;

    /// <summary>Gets or sets a value indicating whether the flag is set.</summary>
    [ObservableProperty]
    public partial bool IsChecked { get; set; }
}

/// <summary>A system dump the user can include in a backup (PRG-005).</summary>
public sealed partial class DumpOptionViewModel(SystemDumpKind kind) : ObservableObject
{
    /// <summary>Gets the dump kind.</summary>
    public SystemDumpKind Kind { get; } = kind;

    /// <summary>Gets the display name.</summary>
    public string Name { get; } = SystemDumps.DisplayName(kind);

    /// <summary>Gets or sets a value indicating whether the dump is included.</summary>
    [ObservableProperty]
    public partial bool IsChecked { get; set; } = true;
}

/// <summary>One of the 250 librarian slots (UI-005).</summary>
public sealed partial class ProgramSlotViewModel : ObservableObject
{
    /// <summary>Initializes a new instance of the <see cref="ProgramSlotViewModel"/> class.</summary>
    /// <param name="summary">The slot contents.</param>
    /// <param name="algorithmNames">Formats algorithm numbers.</param>
    public ProgramSlotViewModel(ProgramSummary summary, Func<IReadOnlyList<byte>, string> algorithmNames)
    {
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentNullException.ThrowIfNull(algorithmNames);
        Number = summary.Number;
        Name = summary.Name;
        Algorithms = algorithmNames(summary.Algorithms);
        Categories = summary.Categories;
    }

    /// <summary>Gets the program number.</summary>
    public ushort Number { get; }

    /// <summary>Gets the number as shown in the list.</summary>
    public string NumberText => Number.ToString("D3", CultureInfo.InvariantCulture);

    /// <summary>Gets a value indicating whether the slot is a factory preset.</summary>
    public bool IsPreset => ProgramSlots.IsPreset(Number);

    /// <summary>Gets "Preset" or "User".</summary>
    public string Kind => IsPreset ? "Preset" : "User";

    /// <summary>Gets or sets the program name.</summary>
    [ObservableProperty]
    public partial string Name { get; set; }

    /// <summary>Gets or sets the loaded algorithms, as names.</summary>
    [ObservableProperty]
    public partial string Algorithms { get; set; }

    /// <summary>Gets or sets the sort flags.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CategoriesText))]
    public partial ProgramCategories Categories { get; set; }

    /// <summary>Gets the sort flags as text.</summary>
    public string CategoriesText => LibrarianViewModel.Describe(Categories);
}

/// <summary>
/// The Librarian tab (UI-005): 250-slot list, load to active, rename, sort flags, single-program <c>.syx</c>
/// import/export and full backup/restore (PRG-001 to PRG-007).
/// </summary>
public sealed partial class LibrarianViewModel : ObservableObject
{
    private readonly IUserDialogs _dialogs;
    private readonly IUiDispatcher _dispatcher;
    private ProgramLibrarian? _librarian;
    private ParameterDatabase? _database;
    private DeviceId _device;
    private CancellationTokenSource? _cancel;
    private bool _loadingFlags;

    /// <summary>Initializes a new instance of the <see cref="LibrarianViewModel"/> class.</summary>
    /// <param name="dialogs">Confirmation and file dialogs.</param>
    /// <param name="dispatcher">UI dispatcher.</param>
    public LibrarianViewModel(IUserDialogs dialogs, IUiDispatcher dispatcher)
    {
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        foreach (EffectCategories flag in Enum.GetValues<EffectCategories>().Where(f => f != EffectCategories.None))
            Flags.Add(new CategoryFlagViewModel(flag.ToString(), (int)flag, isSource: false));
        foreach (SourceCategories flag in Enum.GetValues<SourceCategories>().Where(f => f != SourceCategories.None))
            Flags.Add(new CategoryFlagViewModel(flag.ToString(), (int)flag, isSource: true));
        foreach (CategoryFlagViewModel flag in Flags)
            flag.PropertyChanged += (_, _) => OnFlagChanged();
    }

    /// <summary>Raised when the user wants to open a slot in the program editor.</summary>
    public event EventHandler<ushort>? EditRequested;

    /// <summary>Gets the slots.</summary>
    public ObservableCollection<ProgramSlotViewModel> Slots { get; } = [];

    /// <summary>Gets the sort flag checkboxes for the selected slot.</summary>
    public ObservableCollection<CategoryFlagViewModel> Flags { get; } = [];

    /// <summary>Gets the effect sort flags.</summary>
    public IEnumerable<CategoryFlagViewModel> EffectFlags => Flags.Where(f => !f.IsSource);

    /// <summary>Gets the source sort flags.</summary>
    public IEnumerable<CategoryFlagViewModel> SourceFlags => Flags.Where(f => f.IsSource);

    /// <summary>Gets the system dumps the connected firmware offers for backups.</summary>
    public ObservableCollection<DumpOptionViewModel> DumpOptions { get; } = [];

    /// <summary>Gets or sets the selected slot.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection), nameof(IsIdleWithSelection))]
    [NotifyCanExecuteChangedFor(nameof(LoadToActiveCommand), nameof(RenameCommand), nameof(ExportCommand), nameof(ImportCommand), nameof(EditCommand))]
    public partial ProgramSlotViewModel? SelectedSlot { get; set; }

    /// <summary>Gets a value indicating whether a slot is selected.</summary>
    public bool HasSelection => SelectedSlot is not null;

    /// <summary>Gets or sets the new name for <see cref="RenameCommand"/>.</summary>
    [ObservableProperty]
    public partial string RenameText { get; set; } = "";

    /// <summary>Gets a value indicating whether commands can run.</summary>
    public bool IsIdle => CanRun();

    /// <summary>Gets a value indicating whether commands on the selected slot can run.</summary>
    public bool IsIdleWithSelection => CanRunOnSelection();

    /// <summary>Gets or sets a value indicating whether a device is attached.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle), nameof(IsIdleWithSelection))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand), nameof(BackupCommand), nameof(RestoreCommand), nameof(WriteFlagsCommand),
        nameof(LoadToActiveCommand), nameof(RenameCommand), nameof(ExportCommand), nameof(ImportCommand), nameof(EditCommand))]
    public partial bool IsAttached { get; set; }

    /// <summary>Gets or sets a value indicating whether a transfer runs.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle), nameof(IsIdleWithSelection))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand), nameof(BackupCommand), nameof(RestoreCommand), nameof(WriteFlagsCommand),
        nameof(LoadToActiveCommand), nameof(RenameCommand), nameof(ExportCommand), nameof(ImportCommand), nameof(EditCommand))]
    public partial bool IsBusy { get; set; }

    /// <summary>Gets or sets a value indicating whether the slot list was read.</summary>
    [ObservableProperty]
    public partial bool IsLoaded { get; set; }

    /// <summary>Gets or sets a value indicating whether sort flags were changed but not written.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(WriteFlagsCommand))]
    public partial bool HasUnsavedFlags { get; set; }

    /// <summary>Gets or sets the progress in percent.</summary>
    [ObservableProperty]
    public partial double Progress { get; set; }

    /// <summary>Gets or sets the status line.</summary>
    [ObservableProperty]
    public partial string Status { get; set; } = "Connect and learn a device to use the librarian.";

    /// <summary>Formats sort flags.</summary>
    /// <param name="categories">The flags.</param>
    public static string Describe(ProgramCategories categories)
    {
        IEnumerable<string> effects = Enum.GetValues<EffectCategories>().Where(f => f != EffectCategories.None && categories.Effects.HasFlag(f)).Select(f => f.ToString());
        IEnumerable<string> sources = Enum.GetValues<SourceCategories>().Where(f => f != SourceCategories.None && categories.Sources.HasFlag(f)).Select(f => f.ToString());
        return string.Join(", ", effects.Concat(sources));
    }

    /// <summary>Connects the tab to a learned device.</summary>
    /// <param name="librarian">The librarian.</param>
    /// <param name="database">The learned database.</param>
    /// <param name="device">The device ID.</param>
    public void Attach(ProgramLibrarian librarian, ParameterDatabase database, DeviceId device)
    {
        _librarian = librarian ?? throw new ArgumentNullException(nameof(librarian));
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _device = device;
        DumpOptions.Clear();
        foreach (SystemDumpKind kind in librarian.AvailableDumps())
            DumpOptions.Add(new DumpOptionViewModel(kind));
        IsAttached = true;
        Status = "Open the slot list with Refresh.";
    }

    /// <summary>Disconnects the tab.</summary>
    public void Detach()
    {
        _cancel?.Cancel();
        _librarian = null;
        _database = null;
        IsAttached = false;
        IsLoaded = false;
        SelectedSlot = null;
        Slots.Clear();
        DumpOptions.Clear();
        Status = "Connect and learn a device to use the librarian.";
    }

    /// <summary>Reads the slot list once, the first time the tab is shown.</summary>
    public Task EnsureLoadedAsync() => IsAttached && !IsLoaded && !IsBusy ? RefreshAsync() : Task.CompletedTask;

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
        IReadOnlyList<ProgramSummary> summaries = await librarian.ListAsync(progress, ct);
        int? selected = SelectedSlot?.Number;
        Slots.Clear();
        foreach (ProgramSummary summary in summaries)
            Slots.Add(new ProgramSlotViewModel(summary, AlgorithmNames));
        IsLoaded = true;
        HasUnsavedFlags = false;
        SelectedSlot = selected is int n ? Slots.FirstOrDefault(s => s.Number == n) : null;
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
        string name = NormalizeName(RenameText);
        if (!await ConfirmPresetAsync(slot, $"Rename preset {slot.NumberText} \"{slot.Name}\" to \"{name.TrimEnd()}\"?"))
            return "Rename cancelled.";
        ProgramData program = await librarian.ReceiveAsync(slot.Number, ct);
        await librarian.StoreAsync(slot.Number, program with { Name = name }, presetConfirmed: true, ct);
        slot.Name = name.TrimEnd();
        return $"Renamed {slot.NumberText} to \"{slot.Name}\".";
    });

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

    /// <summary>Writes the edited sort flags of all slots (Database Dump).</summary>
    [RelayCommand(CanExecute = nameof(CanWriteFlags))]
    public Task WriteFlagsAsync() => RunAsync("Writing sort flags…", async (librarian, _, ct) =>
    {
        await librarian.WriteDatabaseAsync([.. Slots.Select(s => s.Categories)], ct);
        HasUnsavedFlags = false;
        return "Sort flags written.";
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
        IReadOnlyList<ProgramSummary> summaries = await librarian.ListAsync(null, ct);
        Slots.Clear();
        foreach (ProgramSummary summary in summaries)
            Slots.Add(new ProgramSlotViewModel(summary, AlgorithmNames));
        IsLoaded = true;
        return $"Restored {file.Name}.";
    });

    /// <summary>Cancels the running transfer.</summary>
    [RelayCommand]
    public void Cancel() => _cancel?.Cancel();

    /// <summary>Normalizes a program name: printable ASCII, 12 characters, space padded (PRO-007).</summary>
    /// <param name="name">The name.</param>
    public static string NormalizeName(string? name)
    {
        char[] chars = [.. (name ?? "").Select(c => c is >= ' ' and <= '~' ? c : ' ')];
        return new string(chars).PadRight(ProgramData.NameLength)[..ProgramData.NameLength];
    }

    partial void OnSelectedSlotChanged(ProgramSlotViewModel? value)
    {
        RenameText = value?.Name ?? "";
        _loadingFlags = true;
        try
        {
            foreach (CategoryFlagViewModel flag in Flags)
            {
                flag.IsChecked = value is not null && (flag.IsSource
                    ? ((int)value.Categories.Sources & flag.Bit) != 0
                    : ((int)value.Categories.Effects & flag.Bit) != 0);
            }
        }
        finally
        {
            _loadingFlags = false;
        }
    }

    private void OnFlagChanged()
    {
        if (_loadingFlags || SelectedSlot is not { } slot)
            return;
        int effects = Flags.Where(f => !f.IsSource && f.IsChecked).Sum(f => f.Bit);
        int sources = Flags.Where(f => f.IsSource && f.IsChecked).Sum(f => f.Bit);
        ProgramCategories categories = new((EffectCategories)effects, (SourceCategories)sources);
        if (categories != slot.Categories)
        {
            slot.Categories = categories;
            HasUnsavedFlags = true;
        }
    }

    private bool CanRun() => IsAttached && !IsBusy;

    private bool CanRunOnSelection() => CanRun() && SelectedSlot is not null;

    private bool CanWriteFlags() => CanRun() && IsLoaded && HasUnsavedFlags;

    private async Task<bool> ConfirmPresetAsync(ProgramSlotViewModel slot, string message)
        => !slot.IsPreset || await _dialogs.ConfirmAsync("Overwrite factory preset", $"{message} Factory presets can only be restored from a backup.", "Overwrite");

    private Task RunOnSelectionAsync(string busy, Func<ProgramLibrarian, ProgramSlotViewModel, IProgress<LibraryProgress>, CancellationToken, Task<string>> work)
        => SelectedSlot is { } slot ? RunAsync(busy, (l, p, ct) => work(l, slot, p, ct)) : Task.CompletedTask;

    private async Task RunAsync(string busy, Func<ProgramLibrarian, IProgress<LibraryProgress>, CancellationToken, Task<string>> work)
    {
        if (_librarian is not { } librarian)
            return;
        IsBusy = true;
        Progress = 0;
        Status = busy;
        using CancellationTokenSource cancel = new();
        _cancel = cancel;
        try
        {
            Status = await work(librarian, new DispatchedProgress(_dispatcher, OnProgress), cancel.Token);
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            Status = "Cancelled.";
        }
        catch (Exception ex) when (ex is Mpx1TimeoutException or PresetOverwriteException or IOException or ArgumentException or UnauthorizedAccessException)
        {
            Status = ex is Mpx1TimeoutException ? "The device did not answer." : ex.Message;
        }
        finally
        {
            _cancel = null;
            IsBusy = false;
        }
    }

    private void OnProgress(LibraryProgress progress)
    {
        Progress = progress.Total == 0 ? 0 : 100.0 * progress.Done / progress.Total;
        Status = $"{progress.Step}: {progress.Done}/{progress.Total}";
    }

    private static string SafeFileName(string name)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        return new string([.. name.Trim().Select(c => invalid.Contains(c) ? '_' : c)]);
    }

    private sealed class DispatchedProgress(IUiDispatcher dispatcher, Action<LibraryProgress> report) : IProgress<LibraryProgress>
    {
        public void Report(LibraryProgress value) => dispatcher.Post(() => report(value));
    }
}
