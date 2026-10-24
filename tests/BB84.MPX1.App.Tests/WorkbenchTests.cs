using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

using BB84.MPX1.App.Services;
using BB84.MPX1.App.ViewModels;
using BB84.MPX1.App.ViewModels.Parameters;
using BB84.MPX1.Core.Database;
using BB84.MPX1.Core.Protocol;
using BB84.MPX1.Midi;
using BB84.MPX1.Midi.Learning;
using BB84.MPX1.Simulator;

namespace BB84.MPX1.App.Tests;

/// <summary>
/// The M5 exit scenario against the simulator: connect, learn, browse, edit, and see the change in the simulator
/// and the monitor.
/// </summary>
[TestClass]
public sealed class WorkbenchTests
{
    private string _settingsPath = "";
    private InMemoryParameterDatabaseStore _store = null!;
    // The connection owns and disposes the simulator; the test only inspects it.
    private readonly StrongBox<Mpx1Simulator?> _created = new();

    private Mpx1Simulator Simulator => _created.Value!;

    private FakeDialogs Dialogs { get; } = new();

    public TestContext TestContext { get; set; } = null!;

    [TestInitialize]
    public void Initialize()
    {
        _settingsPath = Path.Combine(Path.GetTempPath(), $"bb84-settings-{Guid.NewGuid():N}.json");
        _store = new InMemoryParameterDatabaseStore();
    }

    [TestCleanup]
    public void Cleanup() => File.Delete(_settingsPath);

    private MainWindowViewModel CreateViewModel()
    {
        MidiPortCatalog catalog = new(createHardware: null, createSimulator: () => _created.Value = new Mpx1Simulator(options: new SimulatorOptions { BusyDuration = TimeSpan.FromMilliseconds(1) }));
        return new MainWindowViewModel(catalog, new SettingsStore(_settingsPath), _store, new ImmediateDispatcher(), Dialogs, TimeProvider.System);
    }

    internal static async Task<MainWindowViewModel> ConnectAndLearnAsync(MainWindowViewModel vm)
    {
        await vm.InitializeAsync();
        vm.Setup.SelectedInput = vm.Setup.Inputs.Single(p => p.IsSimulator);
        Assert.AreEqual(vm.Setup.SelectedInput, vm.Setup.SelectedOutput, "Choosing the simulator as input also selects it as output.");

        await vm.ConnectAsync();
        Assert.IsTrue(vm.IsConnected, vm.StatusMessage);
        Assert.AreEqual("V1.10 (Mar 30 1998 17:51:03)", vm.Firmware);
        Assert.IsTrue(vm.Learning.NeedsLearning);

        await vm.LearnAsync();
        Assert.IsTrue(vm.Learning.IsLearned, vm.StatusMessage);
        return vm;
    }

    private static ControlNodeViewModel FindNode(MainWindowViewModel vm, params string[] path)
    {
        IEnumerable<ControlNodeViewModel> level = vm.Tree.Roots;
        ControlNodeViewModel? node = null;
        foreach (string name in path)
            level = (node = level.Single(n => n.Name == name)).Children;
        return node!;
    }

    private static async Task<ParameterPageViewModel> OpenAsync(MainWindowViewModel vm, params string[] path)
    {
        vm.Tree.SelectedNode = FindNode(vm, path);
        ParameterPageViewModel page = vm.Tree.Page!;
        await page.Loading;
        return page;
    }

    [TestMethod]
    [Description("UI-001, UI-002, UI-003, UI-004, UI-009: connect, learn, browse, edit live; the simulator and the monitor see it")]
    public async Task EditParameter_ReachesSimulatorAndMonitor()
    {
        await using MainWindowViewModel vm = await ConnectAndLearnAsync(CreateViewModel());
        CollectionAssert.AreEqual(new[] { "Program", "System" }, vm.Tree.Roots.Select(r => r.Name).ToArray());

        // Pitch "no effect" is the algorithm loaded in the active program of the default fixture.
        ParameterPageViewModel page = await OpenAsync(vm, "Program", "Pitch", "no effect");
        Assert.AreEqual("Program › Pitch › no effect", page.Title);
        NumericParameterViewModel mix = (NumericParameterViewModel)page.Parameters.Single(p => p.Name == "Mix");
        Assert.IsTrue(mix.IsLoaded, mix.Error);
        Assert.IsTrue(mix.IsEditable);
        Assert.AreEqual(0, mix.Minimum);
        Assert.AreEqual(100, mix.Maximum);

        for (int v = 10; v <= 60; v += 10)
            mix.Value = v;
        await mix.Writer.Completion.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken);

        CollectionAssert.AreEqual(new byte[] { 60 }, Simulator.GetValue(new ControlAddress(0, 0, 0, 0)));
        Assert.AreEqual("60", mix.DisplayText, "The display text is read back from the device after the burst.");

        vm.Monitor.Flush();
        Assert.IsTrue(vm.Monitor.Entries.Any(e => e.Direction == "→" && e.Summary.StartsWith("Parameter Data A:0 B:0 C:0 D:0: 3C", StringComparison.Ordinal)));
        Assert.IsTrue(vm.Monitor.Entries.Any(e => e.Direction == "←" && e.Summary.StartsWith("Parameter Display A:0 B:0 C:0 D:0", StringComparison.Ordinal)));
    }

    [TestMethod]
    [Description("AC-015, LRN-008: parameters of an algorithm that is not loaded are read-only, explained, and never sent")]
    public async Task UnloadedAlgorithm_IsReadOnlyAndNeverSent()
    {
        await using MainWindowViewModel vm = await ConnectAndLearnAsync(CreateViewModel());
        ConcurrentQueue<Mpx1Message> sent = new();
        vm.Connection!.Session.FrameTraffic += (_, e) =>
        {
            if (e.Direction == FrameDirection.Sent && e.Message is not null)
                sent.Enqueue(e.Message);
        };

        ParameterPageViewModel page = await OpenAsync(vm, "Program", "Pitch", "Detune (M)");
        Assert.IsTrue(page.HasNotice);
        StringAssert.Contains(page.Notice, "Detune (M) is not loaded");
        NumericParameterViewModel mix = (NumericParameterViewModel)page.Parameters.Single(p => p.Name == "Mix");
        Assert.IsFalse(mix.IsEditable);
        Assert.IsTrue(mix.IsLoaded, "Reading is safe; only writes can crash the unit.");

        mix.Value = 42;
        await Task.Delay(300, TestContext.CancellationToken);

        Assert.IsFalse(sent.OfType<ParameterData>().Any(), "No Parameter Data frame may be sent.");
        Assert.IsFalse(Simulator.IsCrashed);
    }

    [TestMethod]
    [Description("UI-004: editors are chosen from the description and display units")]
    public async Task EditorKinds_FollowDescriptions()
    {
        await using MainWindowViewModel vm = await ConnectAndLearnAsync(CreateViewModel());

        ParameterPageViewModel midi = await OpenAsync(vm, "System", "MIDI");
        ChoiceParameterViewModel speed = (ChoiceParameterViewModel)midi.Parameters.Single(p => p.Name == "MIDI Speed");
        CollectionAssert.AreEqual(new[] { 0x01, 0x02, 0x04, 0x40 }, speed.Choices.Select(c => c.Value).ToArray());
        Assert.AreEqual(0x01, speed.Selected?.Value);
        Assert.IsTrue(midi.Parameters.Any(p => p.Name == "MIDI Dumps") is false, "Control levels are tree nodes, not editors.");

        ParameterPageViewModel panel = await OpenAsync(vm, "System", "Panel");
        Assert.IsInstanceOfType<DumpParameterViewModel>(panel.Parameters.Single(p => p.Name == "Display"));
        StringAssert.StartsWith(((DumpParameterViewModel)panel.Parameters.Single(p => p.Name == "Display")).Preview, "32 bytes:");
        Assert.IsFalse(panel.Parameters.Single(p => p.Name == "Button").IsEditable, "Panel buttons belong to the front panel.");

        ParameterPageViewModel eq = await OpenAsync(vm, "Program", "Pitch", "no effect");
        Assert.IsInstanceOfType<NumericParameterViewModel>(eq.Parameters.Single(p => p.Name == "Level"));
        Assert.AreEqual(-96, ((NumericParameterViewModel)eq.Parameters.Single(p => p.Name == "Level")).Minimum);
    }

    [TestMethod]
    [Description("LRN-004, spec 4.7: settings persist; the next start reconnects and loads the cached database without learning")]
    public async Task Startup_ReconnectsAndUsesCache()
    {
        await using (MainWindowViewModel first = await ConnectAndLearnAsync(CreateViewModel()))
        {
            first.Setup.DeviceId = 0;
        }

        await using MainWindowViewModel second = CreateViewModel();
        await second.InitializeAsync();

        Assert.IsTrue(second.IsConnected, second.StatusMessage);
        Assert.IsTrue(second.Learning.IsLearned);
        StringAssert.StartsWith(second.Learning.Status, "Loaded from cache");
        Assert.IsFalse(second.Learning.NeedsLearning);
        Assert.HasCount(2, second.Tree.Roots);
    }

    [TestMethod]
    [Description("UI-001, SES-003: an unanswered connect reports the problem and leaves the app disconnected")]
    public async Task Connect_WrongDeviceId_StaysDisconnected()
    {
        await using MainWindowViewModel vm = CreateViewModel();
        await vm.InitializeAsync();
        vm.Setup.SelectedInput = vm.Setup.Inputs.Single(p => p.IsSimulator);
        vm.Setup.DeviceId = 5;
        vm.Setup.RequestTimeoutMs = 100;
        vm.Setup.Retries = 0;

        await vm.ConnectAsync();

        Assert.IsFalse(vm.IsConnected);
        StringAssert.Contains(vm.StatusMessage, "No answer from device ID 5");
    }

    [TestMethod]
    [Description("UI-002, LRN-003: learning can be cancelled and the app asks to learn again")]
    public async Task Learn_Cancel_LeavesLearnPrompt()
    {
        await using MainWindowViewModel vm = CreateViewModel();
        await vm.InitializeAsync();
        vm.Setup.SelectedInput = vm.Setup.Inputs.Single(p => p.IsSimulator);
        await vm.ConnectAsync();

        Task learning = vm.LearnAsync();
        vm.CancelLearning();
        await learning;

        Assert.IsFalse(vm.Learning.IsLearning);
        Assert.IsFalse(vm.Learning.IsLearned);
        Assert.IsTrue(vm.Learning.NeedsLearning);
        Assert.AreEqual("Learning cancelled", vm.Learning.Status);
    }

    [TestMethod]
    [Description("SES-006: disconnecting during a connection test ends the test quietly")]
    public async Task Disconnect_DuringTestConnection_EndsQuietly()
    {
        await using MainWindowViewModel vm = await ConnectAndLearnAsync(CreateViewModel());
        Simulator.Faults.DropResponses = 100;

        Task ping = vm.TestConnectionAsync();
        await vm.DisconnectAsync();
        await ping;

        Assert.IsFalse(vm.IsConnected);
        Assert.AreEqual("Disconnected.", vm.StatusMessage);
    }

    [TestMethod]
    [Description("UI-002, LRN-003: disconnecting during a relearn leaves nothing attached to the closed connection")]
    public async Task Disconnect_DuringRelearn_LeavesTabsDetached()
    {
        await using MainWindowViewModel vm = await ConnectAndLearnAsync(CreateViewModel());
        Simulator.Faults.DropResponses = 1000;

        Task relearn = vm.RelearnAsync();
        Assert.IsTrue(vm.Learning.IsLearning);
        await vm.DisconnectAsync();
        await relearn;

        Assert.IsFalse(vm.IsConnected);
        Assert.IsFalse(vm.Learning.IsLearned);
        Assert.IsFalse(vm.Librarian.IsAttached);
        Assert.IsEmpty(vm.Tree.Roots);
        Assert.AreEqual("Not connected", vm.Learning.Status);
    }

    [TestMethod]
    [Description("LRN-004: a cache that cannot be written does not lose the learned database")]
    public async Task Learn_CacheNotWritable_StillLearns()
    {
        MidiPortCatalog catalog = new(createHardware: null, createSimulator: () => _created.Value = new Mpx1Simulator(options: new SimulatorOptions { BusyDuration = TimeSpan.FromMilliseconds(1) }));
        await using MainWindowViewModel vm = new(catalog, new SettingsStore(_settingsPath), new UnwritableStore(), new ImmediateDispatcher(), Dialogs, TimeProvider.System);

        await ConnectAndLearnAsync(vm);

        Assert.IsTrue(vm.Librarian.IsAttached);
        StringAssert.Contains(vm.StatusMessage, "cache");
        StringAssert.Contains(vm.StatusMessage, "Disk full.");
    }

    private sealed class UnwritableStore : IParameterDatabaseStore
    {
        public Task<ParameterDatabase?> TryLoadAsync(DatabaseKey key, CancellationToken cancellationToken = default)
            => Task.FromResult<ParameterDatabase?>(null);

        public Task SaveAsync(ParameterDatabase database, CancellationToken cancellationToken = default)
            => throw new IOException("Disk full.");
    }
}
