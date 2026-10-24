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

    private async Task<MainWindowViewModel> OpenPageAsync(params string[] path)
    {
        MidiPortCatalog catalog = new(createHardware: null,
            createSimulator: () => _created.Value = new Mpx1Simulator(options: new SimulatorOptions { BusyDuration = TimeSpan.FromMilliseconds(1) }));
        MainWindowViewModel vm = new(catalog, new SettingsStore(_settingsPath), new InMemoryParameterDatabaseStore(), new ImmediateDispatcher(), new FakeDialogs(), TimeProvider.System);
        await WorkbenchTests.ConnectAndLearnAsync(vm);

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
}
