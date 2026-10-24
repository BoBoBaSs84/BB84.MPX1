using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using BB84.MPX1.App.Services;
using BB84.MPX1.Core.Database;
using BB84.MPX1.Core.Protocol;
using BB84.MPX1.Midi.Library;

namespace BB84.MPX1.App.ViewModels;

/// <summary>
/// The Librarian tab (UI-005): 250-slot list, load to active, rename, sort flags, single-program <c>.syx</c>
/// import/export and full backup/restore (PRG-001 to PRG-007).
/// </summary>
public sealed partial class LibrarianViewModel : ObservableObject, IDeviceTab
{
    private readonly IUserDialogs _dialogs;
    private readonly IUiDispatcher _dispatcher;
    private ProgramLibrarian? _librarian;
    private ParameterDatabase? _database;
    private DeviceId _device;
    private CancellationTokenSource? _cancel;

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

    /// <summary>Gets or sets the progress in percent.</summary>
    [ObservableProperty]
    public partial double Progress { get; set; }

    /// <summary>Gets or sets the status line.</summary>
    [ObservableProperty]
    public partial string Status { get; set; } = "Connect and learn a device to use the librarian.";

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

    void IDeviceTab.Attach(DeviceConnection connection, TimeProvider time)
        => Attach(connection.Librarian!, connection.Database!, connection.Session.Device);

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

    /// <summary>Cancels the running transfer.</summary>
    [RelayCommand]
    public void Cancel() => _cancel?.Cancel();

    private bool CanRun() => IsAttached && !IsBusy;

    private bool CanRunOnSelection() => CanRun() && SelectedSlot is not null;

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
            DispatchedProgress<LibraryProgress> progress = new(_dispatcher, OnProgress);
            if (await OperationStatus.RunAsync(ct => work(librarian, progress, ct), cancel.Token) is { } status && IsAttached)
                Status = status;
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
}
