using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using BB84.MPX1.App.Services;
using BB84.MPX1.App.ViewModels.Parameters;
using BB84.MPX1.Core.Database;
using BB84.MPX1.Core.Files;
using BB84.MPX1.Core.Programs;
using BB84.MPX1.Core.Protocol;
using BB84.MPX1.Midi;
using BB84.MPX1.Midi.Library;

namespace BB84.MPX1.App.ViewModels.Editing;

/// <summary>
/// The Program tab (UI-006, UI-010): edits a program document loaded from a slot, the active program or a
/// <c>.syx</c> file. While connected, every change is auditioned as a throttled Program Dump to the active program;
/// "Store" writes a slot (presets need confirmation, PRG-004). Offline, files are edited with the last learned
/// database.
/// </summary>
public sealed partial class ProgramEditorViewModel : ObservableObject, IProgramDocument, IDisposable
{
    /// <summary>Minimum time between audition dumps; a Program Dump takes about 270 ms on a MIDI cable.</summary>
    public static readonly TimeSpan AuditionInterval = TimeSpan.FromMilliseconds(300);

    private readonly IUserDialogs _dialogs;
    private readonly TimeProvider _time;
    private ProgramLibrarian? _librarian;
    private DeviceId _device;
    private int _maxMixingBlocks = 2;
    private ProgramData? _original;
    private ushort? _sourceNumber;
    private CoalescingWriter<ProgramData>? _audition;
    private bool _applying;

    /// <summary>Initializes a new instance of the <see cref="ProgramEditorViewModel"/> class.</summary>
    /// <param name="dialogs">Confirmation and file dialogs.</param>
    /// <param name="time">Time source for audition throttling.</param>
    public ProgramEditorViewModel(IUserDialogs dialogs, TimeProvider time)
    {
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        Program = ProgramFields.NewProgram();
    }

    /// <summary>Raised after a program was stored in a slot, so the librarian can update its list.</summary>
    public event EventHandler<(ushort Number, ProgramData Program)>? Stored;

    /// <inheritdoc/>
    public ProgramData Program { get; private set; }

    /// <inheritdoc/>
    public ParameterDatabase? Database { get; private set; }

    /// <summary>Gets the effect blocks.</summary>
    public ObservableCollection<EffectBlockViewModel> Blocks { get; } = [];

    /// <summary>Gets the routing table rows.</summary>
    public ObservableCollection<RoutingRowViewModel> RoutingRows { get; } = [];

    /// <summary>Gets the soft-row entries.</summary>
    public ObservableCollection<SoftRowEntryViewModel> SoftRow { get; } = [];

    /// <summary>Gets the patches.</summary>
    public ObservableCollection<PatchViewModel> Patches { get; } = [];

    /// <summary>Gets the modulator blocks.</summary>
    public ObservableCollection<ModulatorBlockViewModel> Modulators { get; } = [];

    /// <summary>Gets the tap sources, "None" first.</summary>
    public static IReadOnlyList<ChoiceItem> TapSources { get; } =
        [new(ControllerSources.Unassigned, "None"), .. ControllerSources.TapSources.Select(s => new ChoiceItem(s, ControllerSources.Name(s)))];

    /// <summary>Gets the tempo sources.</summary>
    public static IReadOnlyList<ChoiceItem> TempoSources { get; } = [.. ProgramFields.TempoSources.Select((n, i) => new ChoiceItem(i, n))];

    /// <summary>Gets the beat values.</summary>
    public static IReadOnlyList<ChoiceItem> BeatValues { get; } = [.. ProgramFields.BeatValues.Select((n, i) => new ChoiceItem(i, n))];

    /// <summary>Gets the meter sources.</summary>
    public static IReadOnlyList<ChoiceItem> Meters { get; } = [.. ProgramFields.Meters.Select((n, i) => new ChoiceItem(i, n))];

    /// <summary>Gets or sets where the program came from.</summary>
    [ObservableProperty]
    public partial string Source { get; set; } = "New program";

    /// <summary>Gets or sets a value indicating whether the program differs from what was loaded.</summary>
    [ObservableProperty]
    public partial bool IsDirty { get; set; }

    /// <summary>Gets or sets a value indicating whether a device is connected.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAudition))]
    [NotifyCanExecuteChangedFor(nameof(ReceiveActiveCommand), nameof(StoreCommand), nameof(SendToActiveCommand))]
    public partial bool IsConnected { get; set; }

    /// <summary>Gets or sets a value indicating whether changes are sent to the active program as you edit.</summary>
    [ObservableProperty]
    public partial bool IsAuditionEnabled { get; set; } = true;

    /// <summary>Gets a value indicating whether auditioning is possible.</summary>
    public bool CanAudition => IsConnected;

    /// <summary>Gets or sets a value indicating whether a transfer runs.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ReceiveActiveCommand), nameof(StoreCommand), nameof(SendToActiveCommand))]
    public partial bool IsBusy { get; set; }

    /// <summary>Gets or sets the status line.</summary>
    [ObservableProperty]
    public partial string Status { get; set; } = "";

    /// <summary>Gets or sets a warning about the routing of a loaded program, or <see langword="null"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRoutingWarning))]
    public partial string? RoutingWarning { get; set; }

    /// <summary>Gets a value indicating whether <see cref="RoutingWarning"/> is set.</summary>
    public bool HasRoutingWarning => RoutingWarning is not null;

    /// <summary>Gets or sets the slot used by <see cref="StoreCommand"/>.</summary>
    [ObservableProperty]
    public partial int StoreTarget { get; set; } = ProgramSlots.FirstUser;

    /// <summary>Gets or sets the name (12 characters).</summary>
    [ObservableProperty]
    public partial string Name { get; set; } = "";

    /// <summary>Gets or sets the tempo in BPM.</summary>
    [ObservableProperty]
    public partial int Tempo { get; set; }

    /// <summary>Gets or sets the tempo source.</summary>
    [ObservableProperty]
    public partial ChoiceItem? SelectedTempoSource { get; set; }

    /// <summary>Gets or sets the beat value.</summary>
    [ObservableProperty]
    public partial ChoiceItem? SelectedBeatValue { get; set; }

    /// <summary>Gets or sets the tap source.</summary>
    [ObservableProperty]
    public partial ChoiceItem? SelectedTapSource { get; set; }

    /// <summary>Gets or sets the tap average (1–8 beats).</summary>
    [ObservableProperty]
    public partial int TapAverage { get; set; }

    /// <summary>Gets or sets the tap source level (0–127).</summary>
    [ObservableProperty]
    public partial int TapSourceLevel { get; set; }

    /// <summary>Gets or sets the meter source.</summary>
    [ObservableProperty]
    public partial ChoiceItem? SelectedMeter { get; set; }

    /// <summary>Gets or sets the master level in dB (−96 = Off … 0).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MasterLevelText))]
    public partial int MasterLevel { get; set; }

    /// <summary>Gets the master level as text.</summary>
    public string MasterLevelText => ProgramFields.FormatMasterLevel(ProgramFields.MasterLevelFromDb(MasterLevel));

    /// <summary>Gets or sets the master mix in percent.</summary>
    [ObservableProperty]
    public partial int MasterMix { get; set; }

    /// <summary>Gets or sets the knob value.</summary>
    [ObservableProperty]
    public partial int KnobValue { get; set; }

    /// <summary>Gets or sets the knob minimum.</summary>
    [ObservableProperty]
    public partial int KnobMinimum { get; set; }

    /// <summary>Gets or sets the knob maximum.</summary>
    [ObservableProperty]
    public partial int KnobMaximum { get; set; }

    /// <summary>Gets or sets the knob name (9 characters).</summary>
    [ObservableProperty]
    public partial string KnobName { get; set; } = "";

    /// <summary>Gets the routing map: input, six effects, output on upper/lower lanes.</summary>
    public IReadOnlyList<RoutingSlot> RoutingMap => AudioRouting.Layout(Program.Routing);

    /// <summary>Sets the learned database used for block layouts; it stays after disconnecting (UI-010).</summary>
    /// <param name="database">The database, or <see langword="null"/>.</param>
    public void SetDatabase(ParameterDatabase? database)
    {
        Database = database;
        if (database is not null)
            _maxMixingBlocks = AudioRouting.MaxMixingBlocks(database.Key.MajorVersion, database.Key.MinorVersion);
        Rebuild();
    }

    /// <summary>Connects or disconnects the device used for audition, receive and store.</summary>
    /// <param name="librarian">The librarian, or <see langword="null"/> when offline.</param>
    /// <param name="device">The device ID.</param>
    public void SetConnection(ProgramLibrarian? librarian, DeviceId device)
    {
        _audition?.Dispose();
        _audition = null;
        _librarian = librarian;
        _device = device;
        IsConnected = librarian is not null;
        if (librarian is not null)
        {
            _audition = new CoalescingWriter<ProgramData>(
                (program, ct) => librarian.SendToActiveAsync(program, ct),
                _ => Task.CompletedTask,
                ex => Status = ex is Mpx1TimeoutException ? "The device did not answer the audition dump." : ex.Message,
                _time, AuditionInterval, TimeSpan.Zero);
        }
    }

    /// <summary>Gets the audition writer, for tests.</summary>
    internal CoalescingWriter<ProgramData>? Audition => _audition;

    /// <summary>Loads a program into the editor.</summary>
    /// <param name="program">The program.</param>
    /// <param name="source">Where it came from, for the header.</param>
    /// <param name="number">Its slot, or <see langword="null"/>.</param>
    public void Load(ProgramData program, string source, ushort? number)
    {
        ArgumentNullException.ThrowIfNull(program);
        _audition?.Cancel();
        Program = program;
        _original = program;
        _sourceNumber = number;
        Source = source;
        IsDirty = false;
        if (number is ushort slot && ProgramSlots.IsValid(slot) && slot != Request.ActiveProgram)
            StoreTarget = slot;
        RoutingWarning = AudioRouting.IsValid(program.Routing, _maxMixingBlocks)
            ? null
            : "This program's routing breaks the routing rules; changing any routing block repairs it.";
        Rebuild();
    }

    /// <summary>Reads a slot from the device and opens it.</summary>
    /// <param name="number">Program number 0–249.</param>
    public Task OpenSlotAsync(ushort number) => RunAsync($"Reading program {number:D3}…", async (librarian, ct) =>
    {
        ProgramData program = await librarian.ReceiveAsync(number, ct);
        Load(program, $"Slot {number:D3} \"{program.Name.TrimEnd()}\"", number);
        return $"Opened {number:D3}.";
    });

    /// <inheritdoc/>
    public void Update(Func<ProgramData, ProgramData> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        Program = change(Program);
        IsDirty = true;
        OnPropertyChanged(nameof(RoutingMap));
        if (IsAuditionEnabled && _audition is not null)
            _audition.Submit(Program);
    }

    /// <summary>Starts a blank program.</summary>
    [RelayCommand]
    public void New()
    {
        Load(ProgramFields.NewProgram(), "New program", null);
        Status = "New program.";
    }

    /// <summary>Opens the active program of the device.</summary>
    [RelayCommand(CanExecute = nameof(CanTransfer))]
    public Task ReceiveActiveAsync() => RunAsync("Reading the active program…", async (librarian, ct) =>
    {
        ProgramData program = await librarian.ReceiveAsync(Request.ActiveProgram, ct);
        Load(program, $"Active program \"{program.Name.TrimEnd()}\"", Request.ActiveProgram);
        return "Opened the active program.";
    });

    /// <summary>Opens the first program of a <c>.syx</c> file (UI-010).</summary>
    [RelayCommand]
    public async Task OpenFileAsync()
    {
        try
        {
            PickedFile? file = await _dialogs.OpenSyxAsync("Open a program");
            if (file is null)
                return;
            SyxContent content = SyxFile.Read(file.Contents);
            if (content.Programs.FirstOrDefault() is not { } dump)
            {
                Status = $"{file.Name} contains no program.";
                return;
            }
            ushort? number = dump.ProgramNumber == Request.ActiveProgram ? null : dump.ProgramNumber;
            Load(dump.Program, $"File {file.Name}", number);
            int others = content.Programs.Count() - 1;
            Status = others > 0 ? $"Opened the first of {others + 1} programs in {file.Name}." : $"Opened {file.Name}.";
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            Status = ex.Message;
        }
    }

    /// <summary>Saves the program as a <c>.syx</c> file (PRG-006).</summary>
    [RelayCommand]
    public async Task SaveFileAsync()
    {
        ushort number = _sourceNumber is ushort n && n != Request.ActiveProgram ? n : (ushort)StoreTarget;
        byte[] file = SyxFile.Write([new ProgramDump(_device, number, Program)]);
        try
        {
            string? saved = await _dialogs.SaveSyxAsync("Save program", $"{Program.Name.Trim()}.syx", file);
            if (saved is not null)
            {
                Status = $"Saved {saved}.";
                _original = Program;
                IsDirty = false;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status = ex.Message;
        }
    }

    /// <summary>Stores the program in <see cref="StoreTarget"/>; presets need confirmation (PRG-004).</summary>
    [RelayCommand(CanExecute = nameof(CanTransfer))]
    public Task StoreAsync() => RunAsync("Storing…", async (librarian, ct) =>
    {
        ushort target = (ushort)Math.Clamp(StoreTarget, 0, ProgramSlots.Count - 1);
        if (ProgramSlots.IsPreset(target)
            && !await _dialogs.ConfirmAsync("Overwrite factory preset", $"Store \"{Program.Name.TrimEnd()}\" in preset {target:D3}? Factory presets can only be restored from a backup.", "Overwrite"))
        {
            return "Store cancelled.";
        }
        await librarian.StoreAsync(target, Program, presetConfirmed: true, ct);
        _original = Program;
        _sourceNumber = target;
        Source = $"Slot {target:D3} \"{Program.Name.TrimEnd()}\"";
        IsDirty = false;
        Stored?.Invoke(this, (target, Program));
        return $"Stored in {target:D3}.";
    });

    /// <summary>Sends the program to the active program without storing it.</summary>
    [RelayCommand(CanExecute = nameof(CanTransfer))]
    public Task SendToActiveAsync() => RunAsync("Sending…", async (librarian, ct) =>
    {
        await librarian.SendToActiveAsync(Program, ct);
        return "Sent to the active program.";
    });

    /// <summary>Discards changes since loading.</summary>
    [RelayCommand]
    public void Revert()
    {
        if (_original is { } original)
        {
            Load(original, Source, _sourceNumber);
            if (IsAuditionEnabled)
                _audition?.Submit(original);
            Status = "Changes discarded.";
        }
    }

    /// <inheritdoc/>
    public void Dispose() => _audition?.Dispose();

    partial void OnNameChanged(string value)
        => Edit(p => p with { Name = LibrarianViewModel.NormalizeName(value) });

    partial void OnTempoChanged(int value)
        => Edit(p => p with { Tempo = (ushort)Math.Clamp(value, ProgramFields.MinTempo, ProgramFields.MaxTempo) });

    partial void OnSelectedTempoSourceChanged(ChoiceItem? value) => Edit(p => p with { TempoSource = (byte)(value?.Value ?? 0) });

    partial void OnSelectedBeatValueChanged(ChoiceItem? value) => Edit(p => p with { BeatValue = (byte)(value?.Value ?? 0) });

    partial void OnSelectedTapSourceChanged(ChoiceItem? value) => Edit(p => p with { TapSource = (byte)(value?.Value ?? ControllerSources.Unassigned) });

    partial void OnTapAverageChanged(int value) => Edit(p => p with { TapAverage = (byte)Math.Clamp(value, 1, 8) });

    partial void OnTapSourceLevelChanged(int value) => Edit(p => p with { TapSourceLevel = (byte)Math.Clamp(value, 0, 127) });

    partial void OnSelectedMeterChanged(ChoiceItem? value) => Edit(p => p with { Meter = (byte)(value?.Value ?? 0) });

    partial void OnMasterLevelChanged(int value) => Edit(p => p with { MasterLevel = ProgramFields.MasterLevelFromDb(value) });

    partial void OnMasterMixChanged(int value) => Edit(p => p with { MasterMix = (byte)Math.Clamp(value, 0, ProgramFields.MaxMasterMix) });

    partial void OnKnobValueChanged(int value) => EditKnob();

    partial void OnKnobMinimumChanged(int value) => EditKnob();

    partial void OnKnobMaximumChanged(int value) => EditKnob();

    partial void OnKnobNameChanged(string value) => EditKnob();

    partial void OnIsAuditionEnabledChanged(bool value)
    {
        if (value && IsDirty)
            _audition?.Submit(Program);
    }

    private void EditKnob()
        => Edit(p => p with
        {
            Knob = new Knob((byte)Math.Clamp(KnobValue, 0, 127), (byte)Math.Clamp(KnobMinimum, 0, 127), (byte)Math.Clamp(KnobMaximum, 0, 127),
                (KnobName ?? "").PadRight(Knob.NameLength)[..Knob.NameLength]),
        });

    private void Edit(Func<ProgramData, ProgramData> change)
    {
        if (!_applying)
            Update(change);
    }

    private bool CanTransfer() => IsConnected && !IsBusy;

    private void Rebuild()
    {
        _applying = true;
        try
        {
            ProgramData p = Program;
            Name = p.Name.TrimEnd();
            Tempo = p.Tempo;
            SelectedTempoSource = TempoSources.FirstOrDefault(t => t.Value == p.TempoSource);
            SelectedBeatValue = BeatValues.FirstOrDefault(b => b.Value == p.BeatValue);
            SelectedTapSource = TapSources.FirstOrDefault(t => t.Value == p.TapSource) ?? TapSources[0];
            TapAverage = p.TapAverage;
            TapSourceLevel = p.TapSourceLevel;
            SelectedMeter = Meters.FirstOrDefault(m => m.Value == p.Meter);
            MasterLevel = ProgramFields.MasterLevelToDb(p.MasterLevel);
            MasterMix = p.MasterMix;
            KnobValue = p.Knob.Value;
            KnobMinimum = p.Knob.Minimum;
            KnobMaximum = p.Knob.Maximum;
            KnobName = p.Knob.Name.TrimEnd();
        }
        finally
        {
            _applying = false;
        }

        foreach (EffectBlockViewModel block in Blocks)
            block.AlgorithmChanged -= OnAlgorithmChanged;
        Blocks.Clear();
        for (int i = 0; i < ProgramData.EffectBlockCount; i++)
        {
            EffectBlockViewModel block = new(this, i);
            block.AlgorithmChanged += OnAlgorithmChanged;
            Blocks.Add(block);
        }

        SoftRow.Clear();
        for (int i = 0; i < ProgramData.SoftValueCount; i++)
            SoftRow.Add(new SoftRowEntryViewModel(this, i));
        Patches.Clear();
        for (int i = 0; i < ProgramData.PatchCount; i++)
            Patches.Add(new PatchViewModel(this, i));
        Modulators.Clear();
        foreach (ModulatorBlock block in ModulatorBlocks.All)
            Modulators.Add(new ModulatorBlockViewModel(this, block));
        RebuildRouting();
    }

    private void OnAlgorithmChanged(object? sender, EventArgs e)
    {
        foreach (SoftRowEntryViewModel entry in SoftRow)
            entry.Destination.RefreshParameters();
        foreach (PatchViewModel patch in Patches)
            patch.RefreshDestination();
    }

    private void RebuildRouting()
    {
        RoutingRows.Clear();
        IReadOnlyList<RoutingSlot> slots = AudioRouting.Layout(Program.Routing);
        foreach (RoutingSlot slot in slots)
        {
            IReadOnlyList<RoutingMode> modes = slot.Position < AudioRouting.BlockCount - 1
                ? AudioRouting.AllowedModes(Program.Routing, slot.Position, _maxMixingBlocks)
                : [(RoutingMode)slot.Block.Routing];
            RoutingRows.Add(new RoutingRowViewModel(slot, modes, OnRoutingRowChanged));
        }
        OnPropertyChanged(nameof(RoutingMap));
    }

    private void OnRoutingRowChanged(RoutingRowViewModel row)
    {
        switch (row.LastChange)
        {
            case nameof(RoutingRowViewModel.SelectedEffect) when row.SelectedEffect is { } effect:
                Update(p => p with { Routing = AudioRouting.PlaceEffect(p.Routing, row.Position, (RoutingBlockType)effect.Value) });
                break;
            case nameof(RoutingRowViewModel.SelectedMode) when row.SelectedMode is { } mode:
                if (!AudioRouting.AllowedModes(Program.Routing, row.Position, _maxMixingBlocks).Contains((RoutingMode)mode.Value))
                    return;
                Update(p => p with { Routing = AudioRouting.SetMode(p.Routing, row.Position, (RoutingMode)mode.Value, _maxMixingBlocks) });
                RoutingWarning = null;
                break;
            case nameof(RoutingRowViewModel.SelectedUpperInput) or nameof(RoutingRowViewModel.SelectedLowerInput):
                Update(p => p with
                {
                    Routing = AudioRouting.SetInputs(p.Routing, row.Position,
                        (InputConnection)(row.SelectedUpperInput?.Value ?? 0), (InputConnection)(row.SelectedLowerInput?.Value ?? 0)),
                });
                return; // Lanes do not change.
            default:
                return;
        }
        RebuildRouting();
    }

    private async Task RunAsync(string busy, Func<ProgramLibrarian, CancellationToken, Task<string>> work)
    {
        if (_librarian is not { } librarian)
            return;
        IsBusy = true;
        Status = busy;
        try
        {
            Status = await work(librarian, CancellationToken.None);
        }
        catch (Exception ex) when (ex is Mpx1TimeoutException or PresetOverwriteException or ArgumentException)
        {
            Status = ex is Mpx1TimeoutException ? "The device did not answer." : ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
