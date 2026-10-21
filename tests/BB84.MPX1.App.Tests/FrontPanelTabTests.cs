using System.Runtime.CompilerServices;

using Microsoft.Extensions.Time.Testing;

using BB84.MPX1.App.Services;
using BB84.MPX1.App.ViewModels;
using BB84.MPX1.Core.Dumps;
using BB84.MPX1.Core.Protocol;
using BB84.MPX1.Midi.Learning;
using BB84.MPX1.Simulator;

namespace BB84.MPX1.App.Tests;

[TestClass]
public sealed class FrontPanelTabTests
{
    private readonly StrongBox<Mpx1Simulator?> _created = new();
    private readonly FakeTimeProvider _time = new();
    private string _settingsPath = "";

    public TestContext TestContext { get; set; } = null!;

    private Mpx1Simulator Simulator => _created.Value!;

    [TestInitialize]
    public void Initialize() => _settingsPath = Path.Combine(Path.GetTempPath(), $"bb84-settings-{Guid.NewGuid():N}.json");

    [TestCleanup]
    public void Cleanup() => File.Delete(_settingsPath);

    private async Task<MainWindowViewModel> OpenPanelAsync()
    {
        MidiPortCatalog catalog = new(createHardware: null,
            createSimulator: () => _created.Value = new Mpx1Simulator(options: new SimulatorOptions { BusyDuration = TimeSpan.FromMilliseconds(1) }));
        // The fake clock only drives the mirror's polling timer; the session uses real time.
        MainWindowViewModel vm = new(catalog, new SettingsStore(_settingsPath), new InMemoryParameterDatabaseStore(), new ImmediateDispatcher(), new FakeDialogs(), _time);
        await WorkbenchTests.ConnectAndLearnAsync(vm);
        vm.SelectedTab = MainWindowViewModel.FrontPanelTab;
        await WaitAsync(() => vm.FrontPanel.Digits == "001");
        return vm;
    }

    private async Task WaitAsync(Func<bool> condition)
    {
        for (int i = 0; i < 300 && !condition(); i++)
            await Task.Delay(10, TestContext.CancellationToken);
        Assert.IsTrue(condition());
    }

    private static string Line(IEnumerable<DisplayCellViewModel> cells) => string.Concat(cells.Select(c => c.IsCustom ? "#" : c.Text));

    [TestMethod]
    [Description("UI-007, DMP-001, DMP-002: showing the tab mirrors display, LEDs and digits")]
    public async Task ShowTab_MirrorsPanel()
    {
        await using MainWindowViewModel vm = await OpenPanelAsync();
        FrontPanelViewModel panel = vm.FrontPanel;

        Assert.IsTrue(panel.IsActive);
        Assert.AreEqual("  MPX 1  V1.10  ", Line(panel.TopLine));
        Assert.IsTrue(panel.Leds.Single(l => l.Led == PanelLed.Program).IsOn);
        Assert.IsFalse(panel.Leds.Single(l => l.Led == PanelLed.Bypass).IsOn);
        Assert.IsTrue(panel.HasV110Codes);
        Assert.HasCount(22, panel.Buttons);
    }

    [TestMethod]
    [Description("UI-007, PAR-007: a click sends press and release and refreshes; hold sends HOLD then RELEASE")]
    public async Task Buttons_SendCodesAndRefresh()
    {
        await using MainWindowViewModel vm = await OpenPanelAsync();
        FrontPanelViewModel panel = vm.FrontPanel;

        await panel.Buttons.Single(b => b.Name == "Bypass").ClickCommand.ExecuteAsync(null);
        Assert.AreEqual("Bypass", Line(panel.BottomLine).Trim());
        Assert.IsTrue(panel.Leds.Single(l => l.Led == PanelLed.Bypass).IsOn);

        await panel.Buttons.Single(b => b.Name == "Edit").HoldCommand.ExecuteAsync(null);
        Assert.AreEqual(PanelButtons.Find("Edit").Release, Simulator.LastButtonCode);

        await panel.TurnClockwiseAsync();
        Assert.AreEqual(PanelButtons.EncoderClockwise, Simulator.LastButtonCode);
        Assert.AreEqual("Encoder +", Line(panel.BottomLine).Trim());
    }

    [TestMethod]
    [Description("UI-007, PRO-007, DMP-003: custom characters are drawn from their bitmaps")]
    public async Task CustomCharacters_AreDrawn()
    {
        await using MainWindowViewModel vm = await OpenPanelAsync();
        FrontPanelViewModel panel = vm.FrontPanel;
        panel.MessageTop = "\u0007 Level";
        panel.MessageBottom = "custom chars";

        await panel.ShowTextAsync();

        DisplayCellViewModel bar = panel.TopLine[0];
        Assert.IsTrue(bar.IsCustom);
        Assert.HasCount(40, bar.Pixels);
        Assert.IsTrue(bar.Pixels[0], "Fixture character 7 is a full bar.");
        Assert.IsFalse(bar.Pixels[39], "The underline row stays dark.");
        Assert.AreEqual("# Level         ", Line(panel.TopLine));
    }

    [TestMethod]
    [Description("UI-007: the mirror polls while visible and stops when another tab is shown")]
    public async Task Polling_OnlyWhileVisible()
    {
        await using MainWindowViewModel vm = await OpenPanelAsync();
        FrontPanelViewModel panel = vm.FrontPanel;

        // Something changes on the unit; the next poll shows it.
        await vm.Connection!.FrontPanel!.WriteDisplayAsync("Changed", "on the unit", TestContext.CancellationToken);
        _time.Advance(FrontPanelViewModel.PollInterval);
        await WaitAsync(() => Line(panel.TopLine).StartsWith("Changed", StringComparison.Ordinal));

        vm.SelectedTab = MainWindowViewModel.ParametersTab;
        Assert.IsFalse(panel.IsActive);
        await vm.Connection.FrontPanel.WriteDisplayAsync("Hidden", "", TestContext.CancellationToken);
        vm.Monitor.Flush();
        int frames = vm.Monitor.Entries.Count;
        _time.Advance(FrontPanelViewModel.PollInterval * 4);
        await Task.Delay(100, TestContext.CancellationToken);
        vm.Monitor.Flush();

        Assert.StartsWith("Changed", Line(panel.TopLine));
        Assert.AreEqual(frames, vm.Monitor.Entries.Count, "No requests while the tab is hidden.");
    }

    [TestMethod]
    [Description("UI-007: disconnecting stops the panel")]
    public async Task Disconnect_DetachesPanel()
    {
        await using MainWindowViewModel vm = await OpenPanelAsync();
        await vm.DisconnectAsync();
        Assert.IsFalse(vm.FrontPanel.IsAttached);
        Assert.IsFalse(vm.FrontPanel.Buttons[0].ClickCommand.CanExecute(null));
    }
}
