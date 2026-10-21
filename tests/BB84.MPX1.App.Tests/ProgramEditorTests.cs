using System.Runtime.CompilerServices;

using BB84.MPX1.App.Services;
using BB84.MPX1.App.ViewModels;
using BB84.MPX1.App.ViewModels.Editing;
using BB84.MPX1.Core.Files;
using BB84.MPX1.Core.Programs;
using BB84.MPX1.Core.Protocol;
using BB84.MPX1.Midi.Learning;
using BB84.MPX1.Simulator;

namespace BB84.MPX1.App.Tests;

[TestClass]
public sealed class ProgramEditorTests
{
    private readonly StrongBox<Mpx1Simulator?> _created = new();
    private readonly InMemoryParameterDatabaseStore _store = new();
    private string _settingsPath = "";

    public TestContext TestContext { get; set; } = null!;

    private Mpx1Simulator Simulator => _created.Value!;

    private FakeDialogs Dialogs { get; } = new();

    [TestInitialize]
    public void Initialize() => _settingsPath = Path.Combine(Path.GetTempPath(), $"bb84-settings-{Guid.NewGuid():N}.json");

    [TestCleanup]
    public void Cleanup() => File.Delete(_settingsPath);

    private MainWindowViewModel CreateViewModel()
    {
        MidiPortCatalog catalog = new(createHardware: null,
            createSimulator: () => _created.Value = new Mpx1Simulator(options: new SimulatorOptions { BusyDuration = TimeSpan.FromMilliseconds(1) }));
        return new MainWindowViewModel(catalog, new SettingsStore(_settingsPath), _store, new ImmediateDispatcher(), Dialogs, TimeProvider.System);
    }

    private async Task<MainWindowViewModel> OpenSlotAsync(ushort number)
    {
        MainWindowViewModel vm = await WorkbenchTests.ConnectAndLearnAsync(CreateViewModel());
        await vm.ProgramEditor.OpenSlotAsync(number);
        Assert.AreEqual($"Slot {number:D3} \"{Simulator.GetProgram(number).Name.TrimEnd()}\"", vm.ProgramEditor.Source, vm.ProgramEditor.Status);
        return vm;
    }

    private async Task WaitForAuditionAsync(ProgramEditorViewModel editor)
        => await editor.Audition!.Completion.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken);

    [TestMethod]
    [Description("UI-006, PRG-003: a slot opens with algorithm names and block parameters decoded from the learned layout")]
    public async Task OpenSlot_DecodesBlocks()
    {
        await using MainWindowViewModel vm = await OpenSlotAsync(1);
        ProgramEditorViewModel editor = vm.ProgramEditor;

        Assert.AreEqual("Preset 002", editor.Name);
        EffectBlockViewModel pitch = editor.Blocks[0];
        Assert.AreEqual("Detune (M)", pitch.SelectedAlgorithm!.Label);
        CollectionAssert.AreEqual(new[] { "Mix", "Level", "Tune", "P Dly" }, pitch.Parameters.Select(p => p.Name).ToArray());
        Assert.AreEqual(100, pitch.Parameters[0].Value);
        Assert.AreEqual("100%", pitch.Parameters[0].DisplayText);
        Assert.IsTrue(pitch.Parameters[2].HasOptions, "Tune has a unit option.");
        Assert.HasCount(10, editor.Modulators);
        Assert.HasCount(7, editor.Modulators[0].Parameters, "LFO 1 of the fixture has seven parameters.");
        Assert.IsFalse(editor.IsDirty);
        Assert.IsFalse(editor.HasRoutingWarning, "Fixture programs use a valid series routing.");
    }

    [TestMethod]
    [Description("UI-006: edits are auditioned as Program Dumps to the active program; nothing is stored")]
    public async Task Edit_IsAuditioned()
    {
        await using MainWindowViewModel vm = await OpenSlotAsync(1);
        ProgramEditorViewModel editor = vm.ProgramEditor;
        byte[] stored = Simulator.GetProgram(1).ToBytes();

        editor.Blocks[0].Parameters[0].Value = 42;
        editor.MasterMix = 77;
        await WaitForAuditionAsync(editor);

        Assert.IsTrue(editor.IsDirty);
        Assert.AreEqual(42, Simulator.ActiveProgram.EffectData[0][0]);
        Assert.AreEqual(77, Simulator.ActiveProgram.MasterMix);
        CollectionAssert.AreEqual(stored, Simulator.GetProgram(1).ToBytes(), "Auditioning never stores.");

        vm.Monitor.Flush();
        Assert.IsTrue(vm.Monitor.Entries.Any(e => e.Summary.StartsWith("Program Dump active", StringComparison.Ordinal)));
    }

    [TestMethod]
    [Description("UI-006: without audition, changes stay local until sent")]
    public async Task AuditionOff_SendsNothing()
    {
        await using MainWindowViewModel vm = await OpenSlotAsync(1);
        ProgramEditorViewModel editor = vm.ProgramEditor;
        editor.IsAuditionEnabled = false;
        byte[] active = Simulator.ActiveProgram.ToBytes();

        editor.Tempo = 99;
        await Task.Delay(400, TestContext.CancellationToken);
        CollectionAssert.AreEqual(active, Simulator.ActiveProgram.ToBytes());

        await editor.SendToActiveAsync();
        Assert.AreEqual((ushort)99, Simulator.ActiveProgram.Tempo);
    }

    [TestMethod]
    [Description("UI-006: changing the algorithm rebuilds the parameters and the destination lists")]
    public async Task AlgorithmChange_RebuildsParameters()
    {
        await using MainWindowViewModel vm = await OpenSlotAsync(1);
        ProgramEditorViewModel editor = vm.ProgramEditor;
        SoftRowEntryViewModel soft = editor.SoftRow[0];
        soft.Destination.SelectedType = soft.Destination.Types.Single(t => t.Label == "Pitch");
        Assert.HasCount(4, soft.Destination.Parameters);

        editor.Blocks[0].SelectedAlgorithm = editor.Blocks[0].Algorithms.Single(a => a.Label == "no effect");

        Assert.AreEqual(0, editor.Program.Algorithms[0]);
        CollectionAssert.AreEqual(new[] { "Mix", "Level" }, editor.Blocks[0].Parameters.Select(p => p.Name).ToArray());
        Assert.HasCount(2, soft.Destination.Parameters);
    }

    [TestMethod]
    [Description("UI-006, PRG-002: the routing table offers allowed routings only and repairs the chain")]
    public async Task Routing_SplitMergesAtOutput()
    {
        await using MainWindowViewModel vm = await OpenSlotAsync(1);
        ProgramEditorViewModel editor = vm.ProgramEditor;
        RoutingRowViewModel third = editor.RoutingRows[3];
        CollectionAssert.AreEqual(new[] { "Upper", "Split" }, third.Modes.Select(m => m.Label).ToArray());

        third.SelectedMode = third.Modes.Single(m => m.Label == "Split");

        Assert.AreEqual((byte)RoutingMode.Merge, editor.Program.Routing[^1].Routing);
        Assert.IsTrue(editor.RoutingMap[5].IsDoublePath);
        CollectionAssert.Contains(editor.RoutingRows[4].Modes.Select(m => m.Label).ToList(), "Lower");

        editor.RoutingRows[1].SelectedEffect = RoutingRowViewModel.Effects.Single(e => e.Label == "Reverb");
        Assert.AreEqual((byte)RoutingBlockType.Reverb, editor.Program.Routing[1].EffectType);
    }

    [TestMethod]
    [Description("UI-006: soft row and patch destinations are chosen by block and parameter name; patch limits follow the parameter")]
    public async Task SoftRowAndPatch_UseTwoStepPicker()
    {
        await using MainWindowViewModel vm = await OpenSlotAsync(1);
        ProgramEditorViewModel editor = vm.ProgramEditor;

        DestinationPickerViewModel soft = editor.SoftRow[2].Destination;
        soft.SelectedType = soft.Types.Single(t => t.Label == "Pitch");
        soft.SelectedParameter = soft.Parameters.Single(p => p.Label == "Tune");
        Assert.AreEqual(new SoftValue(0, 2), editor.Program.SoftValues[2]);

        PatchViewModel patch = editor.Patches[0];
        patch.SelectedSource = PatchViewModel.Sources.Single(s => s.Label == "Pedal");
        patch.Destination.SelectedType = patch.Destination.Types.Single(t => t.Label == "EQ");
        patch.Destination.SelectedParameter = patch.Destination.Parameters.Single(p => p.Label == "Gain");

        Assert.AreEqual(-72, patch.DestinationLimitMinimum);
        Assert.AreEqual(24, patch.DestinationLimitMaximum);
        Patch stored = editor.Program.Patches[0];
        Assert.AreEqual((byte)0x18, stored.SourceController);
        Assert.AreEqual((byte)2, stored.DestinationEffectType);
        Assert.AreEqual((byte)2, stored.DestinationIndex);
        Assert.AreEqual(-72, (short)stored.DestinationMinimum, "Signed destination values are stored as words.");
        Assert.IsFalse(patch.Destination.Types.Any(t => t.Label == "Arpeggiator"), "Arpeggiator parameters cannot be patched.");

        patch.Destination.SelectedType = patch.Destination.Types.Single(t => t.Label == "System");
        Assert.AreEqual("Master Mix", patch.Destination.SelectedParameter!.Label);
        Assert.AreEqual(100, patch.DestinationMaximum);
    }

    [TestMethod]
    [Description("UI-006: modulator parameters edit their block of the modulator data")]
    public async Task Modulator_EditsItsBlock()
    {
        await using MainWindowViewModel vm = await OpenSlotAsync(1);
        ProgramEditorViewModel editor = vm.ProgramEditor;
        ModulatorBlockViewModel adsr = editor.Modulators.Single(m => m.Name == "ADSR 1");

        adsr.Parameters[3].Value = 99;

        Assert.AreEqual(99, editor.Program.GetModulatorBlock(ModulatorBlocks.Find(EffectType.Adsr1)!)[3]);
        Assert.AreEqual(0, editor.Program.GetModulatorBlock(ModulatorBlocks.Find(EffectType.Adsr2)!)[3]);
    }

    [TestMethod]
    [Description("PRG-004, CON-007: storing asks before overwriting a preset; the librarian list follows")]
    public async Task Store_AsksForPresetsAndUpdatesLibrarian()
    {
        await using MainWindowViewModel vm = await OpenSlotAsync(1);
        ProgramEditorViewModel editor = vm.ProgramEditor;
        editor.Name = "Edited";

        Assert.AreEqual(1, editor.StoreTarget, "The store target starts at the source slot.");
        Dialogs.Answers.Enqueue(false);
        await editor.StoreAsync();
        Assert.AreEqual("Store cancelled.", editor.Status);
        Assert.AreEqual("Preset 002  ", Simulator.GetProgram(1).Name);

        vm.SelectedTab = MainWindowViewModel.LibrarianTab;
        for (int i = 0; i < 300 && !vm.Librarian.IsLoaded; i++)
            await Task.Delay(10, TestContext.CancellationToken);

        editor.StoreTarget = 222;
        await editor.StoreAsync();
        Assert.AreEqual("Edited      ", Simulator.GetProgram(222).Name);
        Assert.AreEqual("Edited", vm.Librarian.Slots[222].Name);
        Assert.IsFalse(editor.IsDirty);
        Assert.HasCount(1, Dialogs.Confirmations);
    }

    [TestMethod]
    [Description("UI-005: Edit in the librarian opens the slot on the Program tab")]
    public async Task LibrarianEdit_OpensProgramTab()
    {
        await using MainWindowViewModel vm = await WorkbenchTests.ConnectAndLearnAsync(CreateViewModel());
        vm.SelectedTab = MainWindowViewModel.LibrarianTab;
        for (int i = 0; i < 300 && !vm.Librarian.IsLoaded; i++)
            await Task.Delay(10, TestContext.CancellationToken);

        vm.Librarian.SelectedSlot = vm.Librarian.Slots[33];
        vm.Librarian.EditCommand.Execute(null);
        for (int i = 0; i < 300 && vm.ProgramEditor.IsBusy; i++)
            await Task.Delay(10, TestContext.CancellationToken);

        Assert.AreEqual(MainWindowViewModel.ProgramTab, vm.SelectedTab);
        Assert.AreEqual("Preset 034", vm.ProgramEditor.Name);
    }

    [TestMethod]
    [Description("UI-010, LRN-004: offline, files are edited with the last learned database; save writes a .syx")]
    public async Task Offline_EditsFiles()
    {
        byte[] file;
        await using (MainWindowViewModel online = await OpenSlotAsync(1))
        {
            await online.ProgramEditor.SaveFileAsync();
            file = Dialogs.Saved.Single().Contents;
        }

        // A new start without ports: the editor still has the cached database.
        File.WriteAllText(_settingsPath, File.ReadAllText(_settingsPath).Replace("\"sim:simulator\"", "null", StringComparison.Ordinal));
        await using MainWindowViewModel offline = CreateViewModel();
        await offline.InitializeAsync();
        Assert.IsFalse(offline.IsConnected);
        Assert.IsNotNull(offline.ProgramEditor.Database);
        Assert.IsFalse(offline.ProgramEditor.CanAudition);

        Dialogs.FileToOpen = new PickedFile("p.syx", file);
        await offline.ProgramEditor.OpenFileAsync();
        Assert.AreEqual("File p.syx", offline.ProgramEditor.Source);
        Assert.AreEqual("Detune (M)", offline.ProgramEditor.Blocks[0].SelectedAlgorithm!.Label);

        offline.ProgramEditor.Blocks[0].Parameters[0].Value = 12;
        await offline.ProgramEditor.SaveFileAsync();
        ProgramDump saved = SyxFile.Read(Dialogs.Saved[^1].Contents).Programs.Single();
        Assert.AreEqual(12, saved.Program.EffectData[0][0]);
        Assert.AreEqual((ushort)1, saved.ProgramNumber);
        Assert.IsFalse(offline.ProgramEditor.StoreCommand.CanExecute(null));
    }

    [TestMethod]
    [Description("UI-006: a new program has a valid series routing and no effects")]
    public void New_IsValid()
    {
        ProgramEditorViewModel editor = new(Dialogs, TimeProvider.System);
        editor.New();
        Assert.IsTrue(AudioRouting.IsValid(editor.Program.Routing, 2));
        Assert.AreEqual("New Program", editor.Name);
        Assert.IsTrue(editor.Blocks.All(b => b.HasNotice), "Without a database the blocks explain what is missing.");
        editor.Dispose();
    }
}
