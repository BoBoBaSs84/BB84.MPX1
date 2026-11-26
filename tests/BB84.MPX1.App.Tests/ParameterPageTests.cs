using System.Runtime.CompilerServices;

using BB84.MPX1.App.Services;
using BB84.MPX1.App.ViewModels;
using BB84.MPX1.Core.Protocol;
using BB84.MPX1.Midi.Learning;
using BB84.MPX1.Simulator;

namespace BB84.MPX1.App.Tests;

[TestClass]
public sealed class ParameterPageTests
{
    private readonly StrongBox<Mpx1Simulator?> _created = new();
    private string _settingsPath = "";

    public TestContext TestContext { get; set; } = null!;

    private Mpx1Simulator Simulator => _created.Value!;

    [TestInitialize]
    public void Initialize() => _settingsPath = Path.Combine(Path.GetTempPath(), $"bb84-settings-{Guid.NewGuid():N}.json");

    [TestCleanup]
    public void Cleanup() => File.Delete(_settingsPath);

    private Task<MainWindowViewModel> OpenPageAsync(params string[] path) => OpenPageAsync(closeTransport: false, path);

    private async Task<MainWindowViewModel> OpenPageAsync(bool closeTransport, params string[] path)
    {
        MidiPortCatalog catalog = new(createHardware: null,
            createSimulator: () => _created.Value = new Mpx1Simulator(options: new SimulatorOptions { BusyDuration = TimeSpan.FromMilliseconds(1) }));
        MainWindowViewModel vm = new(catalog, new SettingsStore(_settingsPath), new InMemoryParameterDatabaseStore(), new ImmediateDispatcher(), new FakeDialogs(), TimeProvider.System);
        await WorkbenchTests.ConnectAndLearnAsync(vm);
        if (closeTransport)
            await Simulator.CloseAsync();

        IEnumerable<ControlNodeViewModel> level = vm.Tree.Roots;
        ControlNodeViewModel? node = null;
        foreach (string name in path)
            level = (node = level.Single(n => n.Name == name)).Children;
        vm.Tree.SelectedNode = node;
        await vm.Tree.Page!.Loading;
        return vm;
    }

    [TestMethod]
    [Description("SES-009: disconnecting while a device-reported change is being re-read ends the read quietly")]
    public async Task Disconnect_DuringDeviceChangeReload_EndsQuietly()
    {
        await using MainWindowViewModel vm = await OpenPageAsync("Program", "Pitch", "no effect");
        Simulator.Faults.DropResponses = 100;

        Task reload = vm.Tree.OnDeviceChangedAsync(new ControlAddress(0, 0, 0, 0));
        await vm.DisconnectAsync();
        await reload;

        Assert.IsNull(vm.Tree.Page);
    }

    [TestMethod]
    [Description("PAR-001: a MIDI error while reading parameter values shows on every parameter instead of stopping the page")]
    public async Task TransportError_WhileReadingValues_ShowsErrorOnEachParameter()
    {
        await using MainWindowViewModel vm = await OpenPageAsync(closeTransport: true, "System", "MIDI");

        ParameterPageViewModel page = vm.Tree.Page!;
        Assert.IsNotEmpty(page.Parameters);
        Assert.IsTrue(page.Parameters.All(p => p.Error == "InvalidOperationException: The transport is not open."));
        Assert.IsFalse(page.IsLoading);
    }

    [TestMethod]
    [Description("PAR-001: a MIDI error while reading the active algorithms shows a notice on the page")]
    public async Task TransportError_WhileReadingActiveAlgorithms_ShowsNotice()
    {
        await using MainWindowViewModel vm = await OpenPageAsync(closeTransport: true, "Program", "Pitch", "no effect");

        Assert.AreEqual("Could not read this page: The transport is not open.", vm.Tree.Page!.Notice);
    }
}
