using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using BB84.MPX1.App.Services;
using BB84.MPX1.App.ViewModels.Editing;
using BB84.MPX1.Core.Database;
using BB84.MPX1.Core.Protocol;
using BB84.MPX1.Midi;
using BB84.MPX1.Midi.Learning;

namespace BB84.MPX1.App.ViewModels;

/// <summary>
/// The workbench: connection toolbar, control tree, parameter page and MIDI monitor. Owns the live
/// <see cref="DeviceConnection"/>.
/// </summary>
public sealed partial class MainWindowViewModel : ObservableObject, IAsyncDisposable
{
    private readonly IMidiPortCatalog _catalog;
    private readonly SettingsStore _settingsStore;
    private readonly IParameterDatabaseStore _databaseStore;
    private readonly IUiDispatcher _dispatcher;
    private readonly TimeProvider _time;
    private AppSettings _settings = new();
    private DeviceConnection? _connection;
    private CancellationTokenSource? _learnCancel;

    /// <summary>Initializes a new instance of the <see cref="MainWindowViewModel"/> class.</summary>
    /// <param name="catalog">The port catalog.</param>
    /// <param name="settingsStore">The settings file.</param>
    /// <param name="databaseStore">The learned-database cache.</param>
    /// <param name="dispatcher">UI dispatcher.</param>
    /// <param name="dialogs">Confirmation and file dialogs.</param>
    /// <param name="time">Time source.</param>
    public MainWindowViewModel(IMidiPortCatalog catalog, SettingsStore settingsStore, IParameterDatabaseStore databaseStore, IUiDispatcher dispatcher, IUserDialogs dialogs, TimeProvider time)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        _databaseStore = databaseStore ?? throw new ArgumentNullException(nameof(databaseStore));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        Setup = new MidiSetupViewModel(catalog);
        Monitor = new MonitorViewModel(dispatcher, time);
        Librarian = new LibrarianViewModel(dialogs, dispatcher);
        ProgramEditor = new ProgramEditorViewModel(dialogs, time);
        System = new SystemViewModel(dialogs);
        FrontPanel = new FrontPanelViewModel(dispatcher, time);
        Librarian.EditRequested += async (_, number) =>
        {
            SelectedTab = ProgramTab;
            await ProgramEditor.OpenSlotAsync(number);
        };
        ProgramEditor.Stored += (_, stored) => Librarian.OnProgramStored(stored.Number, stored.Program);
    }

    /// <summary>Index of the Program tab.</summary>
    public const int ProgramTab = 2;

    /// <summary>Index of the System tab.</summary>
    public const int SystemTab = 3;

    /// <summary>Index of the Front panel tab.</summary>
    public const int FrontPanelTab = 4;

    /// <summary>Gets the virtual front panel (UI-007).</summary>
    public FrontPanelViewModel FrontPanel { get; }

    /// <summary>Gets the system dump editors (UI-008).</summary>
    public SystemViewModel System { get; }

    /// <summary>Gets the program editor (UI-006).</summary>
    public ProgramEditorViewModel ProgramEditor { get; }

    /// <summary>Index of the Parameters tab.</summary>
    public const int ParametersTab = 0;

    /// <summary>Index of the Librarian tab.</summary>
    public const int LibrarianTab = 1;

    /// <summary>Gets the librarian (UI-005).</summary>
    public LibrarianViewModel Librarian { get; }

    /// <summary>Gets or sets the selected center tab.</summary>
    [ObservableProperty]
    public partial int SelectedTab { get; set; }

    /// <summary>Gets the application version, for example <c>1.0.0</c> (the build metadata after '+' is dropped).</summary>
    public static string Version { get; } = ReadVersion();

    /// <summary>Gets the window title.</summary>
    public static string Title => $"BB84.MPX1 {Version} — SysEx editor for the Lexicon MPX 1";

    /// <summary>Gets the MIDI setup form.</summary>
    public MidiSetupViewModel Setup { get; }

    /// <summary>Gets the learning status.</summary>
    public LearningViewModel Learning { get; } = new();

    /// <summary>Gets the control tree and parameter page.</summary>
    public ControlTreeViewModel Tree { get; } = new();

    /// <summary>Gets the MIDI monitor.</summary>
    public MonitorViewModel Monitor { get; }

    /// <summary>Gets the live connection, or <see langword="null"/>.</summary>
    public DeviceConnection? Connection => _connection;

    /// <summary>Gets or sets a value indicating whether a device is connected.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDisconnected))]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand), nameof(DisconnectCommand), nameof(TestConnectionCommand), nameof(LearnCommand), nameof(RelearnCommand))]
    public partial bool IsConnected { get; set; }

    /// <summary>Gets a value indicating whether no device is connected.</summary>
    public bool IsDisconnected => !IsConnected;

    /// <summary>Gets or sets a value indicating whether a connect, test or learn operation runs.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand), nameof(DisconnectCommand), nameof(TestConnectionCommand), nameof(LearnCommand), nameof(RelearnCommand))]
    public partial bool IsBusy { get; set; }

    /// <summary>Gets or sets the firmware text, for example <c>V1.10 (Mar 30 1998 17:51:03)</c>.</summary>
    [ObservableProperty]
    public partial string Firmware { get; set; } = "";

    /// <summary>Gets or sets the status-bar message.</summary>
    [ObservableProperty]
    public partial string StatusMessage { get; set; } = "Not connected.";

    /// <summary>Gets or sets a value indicating whether the monitor panel is expanded.</summary>
    [ObservableProperty]
    public partial bool IsMonitorVisible { get; set; } = true;

    /// <summary>
    /// Loads the settings and, when the last ports are still present, reconnects to them.
    /// </summary>
    public async Task InitializeAsync()
    {
        _settings = _settingsStore.Load();
        Setup.Apply(_settings);
        IsMonitorVisible = _settings.MonitorVisible;
        if (_settings.InputPort is not null && _settings.OutputPort is not null && Setup.IsComplete)
        {
            StatusMessage = $"Reconnecting to {Setup.Summary}…";
            await ConnectAsync();
        }
        else
        {
            StatusMessage = "Choose MIDI ports in the setup and connect. The simulator works without hardware.";
        }

        // Offline editing of program files uses the last learned database (UI-010).
        if (ProgramEditor.Database is null && _settings.LastDatabase is { } key
            && await _databaseStore.TryLoadAsync(key) is { } database)
        {
            ProgramEditor.SetDatabase(database);
        }
    }

    /// <summary>Re-reads the port lists.</summary>
    [RelayCommand]
    public void RefreshPorts() => Setup.RefreshPorts();

    /// <summary>Connects with the setup form's ports and device ID, then loads the cached database.</summary>
    [RelayCommand(CanExecute = nameof(CanConnect))]
    public async Task ConnectAsync()
    {
        if (Setup.SelectedInput is not MidiPortChoice input || Setup.SelectedOutput is not MidiPortChoice output)
            return;

        IsBusy = true;
        try
        {
            await CloseConnectionAsync();
            IMidiTransport transport = _catalog.CreateTransport(input, output);
            DeviceId device = new((byte)Math.Clamp(Setup.DeviceId, 0, 127));
            _settings = Setup.ToSettings(_settings);
            _connection = await DeviceConnection.ConnectAsync(transport, input.Port.Id, output.Port.Id, device,
                _settings.ToSessionOptions(), _databaseStore, Monitor.Record);
            _connection.Session.UnsolicitedMessage += OnUnsolicitedMessage;

            DatabaseKey key = _connection.DatabaseKey;
            Firmware = $"{_connection.Version} ({key.BuildDate} {key.BuildTime})";
            IsConnected = true;
            SaveSettings();

            if (await _connection.TryLoadCachedAsync())
            {
                ShowDatabase(_connection, $"Loaded from cache ({_connection.Database!.NodeCount} nodes)");
                StatusMessage = $"Connected to {Setup.Summary}.";
            }
            else
            {
                Learning.NeedsLearning = true;
                Learning.Status = "Not learned yet";
                StatusMessage = $"Connected to {Setup.Summary}. This firmware has not been learned yet: choose Learn.";
            }
        }
        catch (Exception ex) when (ex is Mpx1TimeoutException or InvalidOperationException or ArgumentException or IOException)
        {
            await CloseConnectionAsync();
            StatusMessage = ex is Mpx1TimeoutException
                ? $"No answer from device ID {Setup.DeviceId} on {Setup.Summary}. Check cables, ports and the device ID."
                : $"Could not connect: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Closes the ports.</summary>
    [RelayCommand(CanExecute = nameof(CanDisconnect))]
    public async Task DisconnectAsync()
    {
        _learnCancel?.Cancel();
        await CloseConnectionAsync();
        StatusMessage = "Disconnected.";
    }

    /// <summary>Checks the connection with ARE_YOU_THERE (SES-006).</summary>
    [RelayCommand(CanExecute = nameof(CanUseConnection))]
    public async Task TestConnectionAsync()
    {
        if (_connection is null)
            return;
        IsBusy = true;
        try
        {
            StatusMessage = await _connection.Session.PingAsync()
                ? $"The MPX 1 answered (IM_ALIVE) on {Setup.Summary}."
                : "The MPX 1 did not answer ARE_YOU_THERE.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Learns the connected firmware, or loads it from the cache (UI-002).</summary>
    [RelayCommand(CanExecute = nameof(CanUseConnection))]
    public Task LearnAsync() => RunLearningAsync(force: false);

    /// <summary>Learns the connected firmware again, ignoring the cache (UI-002).</summary>
    [RelayCommand(CanExecute = nameof(CanUseConnection))]
    public Task RelearnAsync() => RunLearningAsync(force: true);

    /// <summary>Cancels a running learn (LRN-003).</summary>
    [RelayCommand]
    public void CancelLearning() => _learnCancel?.Cancel();

    /// <summary>Saves the settings and closes the connection.</summary>
    public async ValueTask DisposeAsync()
    {
        SaveSettings();
        _learnCancel?.Cancel();
        await CloseConnectionAsync();
        Monitor.Dispose();
        ProgramEditor.Dispose();
        FrontPanel.Dispose();
    }

    partial void OnSelectedTabChanged(int value)
    {
        // The mirror polls the unit only while it is visible.
        FrontPanel.IsActive = value == FrontPanelTab;
        if (value == LibrarianTab)
            _ = Librarian.EnsureLoadedAsync();
        else if (value == SystemTab)
            _ = System.EnsureLoadedAsync();
    }

    partial void OnIsMonitorVisibleChanged(bool value) => _settings = _settings with { MonitorVisible = value };

    private bool CanConnect() => !IsBusy;

    private bool CanDisconnect() => IsConnected;

    private bool CanUseConnection() => IsConnected && !IsBusy;

    private async Task RunLearningAsync(bool force)
    {
        if (_connection is not { } connection)
            return;

        IsBusy = true;
        Learning.IsLearning = true;
        Learning.NeedsLearning = false;
        using CancellationTokenSource cancel = new();
        _learnCancel = cancel;
        try
        {
            Tree.Clear();
            ParameterDatabase database = await connection.LearnAsync(new DispatchedProgress(_dispatcher, Learning.Report), force, cancel.Token);
            string gaps = database.Gaps.IsEmpty ? "" : $", {database.Gaps.Length} unanswered addresses";
            ShowDatabase(connection, $"Learned {database.NodeCount} nodes{gaps}");
            StatusMessage = $"Learned {connection.Version}: {database.Descriptions.Count} parameter types, {database.NodeCount} nodes.";
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            Learning.Status = "Learning cancelled";
            Learning.NeedsLearning = connection.Database is null;
            if (connection.Database is not null)
                ShowDatabase(connection, Learning.Status);
            StatusMessage = "Learning cancelled.";
        }
        catch (Mpx1TimeoutException)
        {
            Learning.Status = "Learning failed";
            Learning.NeedsLearning = true;
            StatusMessage = "The device stopped answering while learning.";
        }
        finally
        {
            _learnCancel = null;
            Learning.IsLearning = false;
            IsBusy = false;
        }
    }

    private void ShowDatabase(DeviceConnection connection, string status)
    {
        Tree.Load(connection.Database!, connection.Session, connection.Editor!, _time);
        Librarian.Attach(connection.Librarian!, connection.Database!, connection.Session.Device);
        ProgramEditor.SetDatabase(connection.Database);
        ProgramEditor.SetConnection(connection.Librarian, connection.Session.Device);
        System.Attach(connection.Dumps!, connection.Session.Device);
        FrontPanel.Attach(connection.FrontPanel);
        if (SelectedTab == SystemTab)
            _ = System.EnsureLoadedAsync();
        _settings = _settings with { LastDatabase = connection.Database!.Key };
        SaveSettings();
        if (SelectedTab == LibrarianTab)
            _ = Librarian.EnsureLoadedAsync();
        Learning.IsLearned = true;
        Learning.NeedsLearning = false;
        Learning.Status = status;
    }

    private async Task CloseConnectionAsync()
    {
        Tree.Clear();
        Librarian.Detach();
        ProgramEditor.SetConnection(null, default);
        System.Detach();
        FrontPanel.Detach();
        if (_connection is { } connection)
        {
            _connection = null;
            connection.Session.UnsolicitedMessage -= OnUnsolicitedMessage;
            await connection.DisposeAsync();
        }
        IsConnected = false;
        Firmware = "";
        Learning.IsLearned = false;
        Learning.NeedsLearning = false;
        Learning.Status = "Not connected";
    }

    private void OnUnsolicitedMessage(object? sender, MessageEventArgs e)
    {
        // The unit reports front-panel and automation changes unprompted (SES-009); refresh what is on screen.
        if (e.Message is ParameterData data)
            _dispatcher.Post(() => _ = Tree.OnDeviceChangedAsync(data.Address));
    }

    private void SaveSettings()
    {
        try
        {
            _settingsStore.Save(Setup.ToSettings(_settings));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusMessage = $"Settings could not be saved: {ex.Message}";
        }
    }

    private static string ReadVersion()
    {
        string? informational = typeof(MainWindowViewModel).Assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
            .FirstOrDefault()?.InformationalVersion;
        return informational is null ? "dev" : informational.Split('+')[0];
    }

    /// <summary>Reports progress on the UI thread, in order.</summary>
    private sealed class DispatchedProgress(IUiDispatcher dispatcher, Action<LearningProgress> report) : IProgress<LearningProgress>
    {
        public void Report(LearningProgress value) => dispatcher.Post(() => report(value));
    }
}
