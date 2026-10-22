using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using BB84.MPX1.App.Services;
using BB84.MPX1.Core.Database;
using BB84.MPX1.Core.Programs;
using BB84.MPX1.Core.Protocol;
using BB84.MPX1.Midi.Library;

namespace BB84.MPX1.App.ViewModels.Editing;

/// <summary>
/// The Program tab (UI-006, UI-010): edits a program document loaded from a slot, the active program or a
/// <c>.syx</c> file. While connected, every change is auditioned as a throttled Program Dump to the active program;
/// "Store" writes a slot (presets need confirmation, PRG-004). Offline, files are edited with the last learned
/// database.
/// </summary>
public sealed partial class ProgramEditorViewModel : ObservableObject, IProgramDocument, IDeviceTab, IDisposable
{
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

    /// <summary>Gets the soft-row entries.</summary>
    public ObservableCollection<SoftRowEntryViewModel> SoftRow { get; } = [];

    /// <summary>Gets the patches.</summary>
    public ObservableCollection<PatchViewModel> Patches { get; } = [];

    /// <summary>Gets the modulator blocks.</summary>
    public ObservableCollection<ModulatorBlockViewModel> Modulators { get; } = [];

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

    /// <summary>Sets the learned database used for block layouts; it stays after disconnecting (UI-010).</summary>
    /// <param name="database">The database, or <see langword="null"/>.</param>
    public void SetDatabase(ParameterDatabase? database)
    {
        Database = database;
        if (database is not null)
            _maxMixingBlocks = AudioRouting.MaxMixingBlocks(database.Key.MajorVersion, database.Key.MinorVersion);
        Rebuild();
    }

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

    private void Edit(Func<ProgramData, ProgramData> change)
    {
        if (!_applying)
            Update(change);
    }

    private void Rebuild()
    {
        ApplyFields();

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
}
