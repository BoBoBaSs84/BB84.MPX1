using System.Runtime.CompilerServices;

using BB84.MPX1.App.Services;
using BB84.MPX1.App.ViewModels;
using BB84.MPX1.Core.Database;
using BB84.MPX1.Core.Dumps;
using BB84.MPX1.Core.Protocol;
using BB84.MPX1.Midi.Learning;
using BB84.MPX1.Simulator;

namespace BB84.MPX1.App.Tests;

[TestClass]
public sealed class SystemTabTests
{
    private readonly StrongBox<Mpx1Simulator?> _created = new();
    private string _settingsPath = "";

    public TestContext TestContext { get; set; } = null!;

    private Mpx1Simulator Simulator => _created.Value!;

    private FakeDialogs Dialogs { get; } = new();

    [TestInitialize]
    public void Initialize() => _settingsPath = Path.Combine(Path.GetTempPath(), $"bb84-settings-{Guid.NewGuid():N}.json");

    [TestCleanup]
    public void Cleanup() => File.Delete(_settingsPath);

    private async Task<MainWindowViewModel> OpenSystemAsync()
    {
        MidiPortCatalog catalog = new(createHardware: null,
            createSimulator: () => _created.Value = new Mpx1Simulator(options: new SimulatorOptions { BusyDuration = TimeSpan.FromMilliseconds(1) }));
        MainWindowViewModel vm = new(catalog, new SettingsStore(_settingsPath), new InMemoryParameterDatabaseStore(), new ImmediateDispatcher(), Dialogs, TimeProvider.System);
        await WorkbenchTests.ConnectAndLearnAsync(vm);
        vm.SelectedTab = MainWindowViewModel.SystemTab;
        await WaitAsync(() => vm.System.SelectedSection is { IsLoaded: true });
        return vm;
    }

    private async Task WaitAsync(Func<bool> condition)
    {
        for (int i = 0; i < 300 && !condition(); i++)
            await Task.Delay(10, TestContext.CancellationToken);
        Assert.IsTrue(condition());
    }

    private static DumpFieldViewModel Field(DumpSectionViewModel section, string name) => section.Fields.Single(f => f.Name == name);

    [TestMethod]
    [Description("UI-008, DMP-004..DMP-008: the System tab offers the firmware's dumps; setups list five stored and the active one")]
    public async Task OpenTab_ShowsSections()
    {
        await using MainWindowViewModel vm = await OpenSystemAsync();
        SystemViewModel system = vm.System;

        CollectionAssert.AreEqual(new[] { "Setups", "Global Patches", "Bypass Controllers", "Remap Controllers", "Current Choices" },
            system.Sections.Select(s => s.Title).ToArray());
        DumpSectionViewModel setups = system.Sections[0];
        Assert.HasCount(6, setups.Instances);
        Assert.AreEqual("Active setup", setups.Instances[^1].Label);
        Assert.AreEqual("Setup 1", Field(setups, "Setup name").Text);
        Assert.AreEqual("Fast", Field(setups, "MIDI transmit speed").Selected!.Label);
        Assert.IsTrue(Field(setups, "Pedal max (calibration)").IsReadOnly);
        Assert.IsFalse(system.Sections[^1].IsWritable, "Current Choices is read-only.");
        Assert.HasCount(163, system.Sections[3].Fields);
    }

    [TestMethod]
    [Description("UI-008, CON-007: writing a setup asks first; a refusal sends nothing")]
    public async Task WriteSetup_AsksFirst()
    {
        await using MainWindowViewModel vm = await OpenSystemAsync();
        DumpSectionViewModel setups = vm.System.Sections[0];
        setups.SelectedInstance = setups.Instances[2];
        await WaitAsync(() => setups.IsLoaded);
        ControlAddress address = setups.Node!.Address;

        Field(setups, "LCD contrast").Number = 13;
        Field(setups, "Setup name").Text = "Studio";
        Assert.IsTrue(setups.IsDirty);

        Dialogs.Answers.Enqueue(false);
        await setups.WriteAsync();
        Assert.AreEqual("Write cancelled.", vm.System.Status);
        Assert.AreEqual("Setup 3", DumpLayouts.Setup.Fields.Single(f => f.Name == "Setup name").ReadText(Simulator.GetValue(address)!).TrimEnd());

        Dialogs.Answers.Enqueue(true);
        await setups.WriteAsync();
        byte[] stored = Simulator.GetValue(address)!;
        Assert.AreEqual(13, DumpLayouts.Setup.Fields.Single(f => f.Name == "LCD contrast").Read(stored));
        Assert.AreEqual("Studio", DumpLayouts.Setup.Fields.Single(f => f.Name == "Setup name").ReadText(stored).TrimEnd());
        Assert.IsFalse(setups.IsDirty);
    }

    [TestMethod]
    [Description("UI-008, DMP-005, DMP-011: global patch sources offer Pedal..TSw only and are written without asking")]
    public async Task GlobalPatches_UseSourceRange()
    {
        await using MainWindowViewModel vm = await OpenSystemAsync();
        DumpSectionViewModel global = vm.System.Sections[1];
        vm.System.SelectedSection = global;
        await WaitAsync(() => global.IsLoaded);

        DumpFieldViewModel source = Field(global, "Patch 1 source");
        Assert.AreEqual("None", source.Options[0].Label);
        Assert.AreEqual("Pedal", source.Options[1].Label);
        Assert.AreEqual("TSw", source.Options[^1].Label);

        source.Selected = source.Options.Single(o => o.Label == "Pedal");
        Field(global, "Patch 1 destination").Selected = Field(global, "Patch 1 destination").Options.Single(o => o.Label == "Reverb mix");
        await global.WriteAsync();

        byte[] stored = Simulator.GetValue(global.Node!.Address)!;
        Assert.AreEqual(0x18, stored[0]);
        Assert.AreEqual(7, stored[10]);
        Assert.IsEmpty(Dialogs.Confirmations);
    }

    [TestMethod]
    [Description("UI-008, DMP-011: illegal stored values are flagged; export and import round trip")]
    public async Task InvalidValues_AndSyxRoundTrip()
    {
        await using MainWindowViewModel vm = await OpenSystemAsync();
        DumpSectionViewModel bypass = vm.System.Sections[2];
        vm.System.SelectedSection = bypass;
        await WaitAsync(() => bypass.IsLoaded);

        // Put an out-of-range source on the unit behind the editor's back.
        byte[] raw = [0x97, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x19];
        await vm.Connection!.Session.SendAsync(new ParameterData(new DeviceId(0), raw, bypass.Node!.Address), TestContext.CancellationToken);
        await bypass.ReadAsync();
        Assert.IsTrue(Field(bypass, "Master bypass").IsInvalid);
        Assert.AreEqual("Tog1", Field(bypass, "Delay bypass").Selected!.Label);

        await bypass.ExportAsync();
        byte[] file = Dialogs.Saved.Single().Contents;

        Field(bypass, "Master bypass").Selected = Field(bypass, "Master bypass").Options.Single(o => o.Label == "Tog2");
        await bypass.WriteAsync();
        Assert.AreEqual(0x1A, Simulator.GetValue(bypass.Node.Address)![0]);

        Dialogs.FileToOpen = new PickedFile("bypass.syx", file);
        await bypass.ImportAsync();
        Assert.IsTrue(bypass.IsDirty);
        Assert.IsTrue(Field(bypass, "Master bypass").IsInvalid, "The imported file holds the illegal value again.");
        await bypass.WriteAsync();
        StringAssert.Contains(vm.System.Status, "illegal value");
    }

    [TestMethod]
    [Description("UI-008: disconnecting clears the System tab")]
    public async Task Disconnect_ClearsSystem()
    {
        await using MainWindowViewModel vm = await OpenSystemAsync();
        await vm.DisconnectAsync();
        Assert.IsEmpty(vm.System.Sections);
        Assert.IsFalse(vm.System.IsAttached);
    }
}
