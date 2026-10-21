using System.Runtime.CompilerServices;

using BB84.MPX1.App.Services;
using BB84.MPX1.App.ViewModels;
using BB84.MPX1.Core.Files;
using BB84.MPX1.Core.Programs;
using BB84.MPX1.Core.Protocol;
using BB84.MPX1.Midi.Learning;
using BB84.MPX1.Simulator;

namespace BB84.MPX1.App.Tests;

[TestClass]
public sealed class LibrarianTabTests
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

    private async Task<MainWindowViewModel> OpenLibrarianAsync()
    {
        MidiPortCatalog catalog = new(createHardware: null,
            createSimulator: () => _created.Value = new Mpx1Simulator(options: new SimulatorOptions { BusyDuration = TimeSpan.FromMilliseconds(1) }));
        MainWindowViewModel vm = new(catalog, new SettingsStore(_settingsPath), new InMemoryParameterDatabaseStore(), new ImmediateDispatcher(), Dialogs, TimeProvider.System);
        await WorkbenchTests.ConnectAndLearnAsync(vm);

        vm.SelectedTab = MainWindowViewModel.LibrarianTab;
        await WaitIdleAsync(vm.Librarian);
        Assert.IsTrue(vm.Librarian.IsLoaded, vm.Librarian.Status);
        return vm;
    }

    private async Task WaitIdleAsync(LibrarianViewModel librarian)
    {
        for (int i = 0; i < 500 && librarian.IsBusy; i++)
            await Task.Delay(10, TestContext.CancellationToken);
        Assert.IsFalse(librarian.IsBusy);
    }

    private static ProgramSlotViewModel Select(LibrarianViewModel librarian, int number)
        => librarian.SelectedSlot = librarian.Slots.Single(s => s.Number == number);

    [TestMethod]
    [Description("UI-005, PRG-007: opening the tab lists 250 slots with names, algorithm names and sort flags")]
    public async Task OpenTab_ListsSlots()
    {
        await using MainWindowViewModel vm = await OpenLibrarianAsync();
        LibrarianViewModel librarian = vm.Librarian;

        Assert.HasCount(250, librarian.Slots);
        ProgramSlotViewModel first = librarian.Slots[1];
        Assert.AreEqual("001", first.NumberText);
        Assert.AreEqual("Preset 002", first.Name);
        Assert.AreEqual("Detune (M), 1-Band (M)", first.Algorithms, "Odd fixture programs load Detune (M) and 1-Band (M).");
        Assert.AreEqual("—", librarian.Slots[0].Algorithms);
        Assert.AreEqual("User", librarian.Slots[249].Kind);
        CollectionAssert.AreEquivalent(new[] { "Setups", "Global Patches", "Bypass Controllers", "Remap Controllers" }, librarian.DumpOptions.Select(d => d.Name).ToArray());
    }

    [TestMethod]
    [Description("UI-005, PRG-004, CON-007: renaming a preset asks first; a refusal changes nothing")]
    public async Task Rename_Preset_AsksFirst()
    {
        await using MainWindowViewModel vm = await OpenLibrarianAsync();
        LibrarianViewModel librarian = vm.Librarian;
        Select(librarian, 5);
        librarian.RenameText = "Lead Guitar";

        Dialogs.Answers.Enqueue(false);
        await librarian.RenameAsync();
        Assert.AreEqual("Preset 006    "[..12], Simulator.GetProgram(5).Name);

        Dialogs.Answers.Enqueue(true);
        await librarian.RenameAsync();
        Assert.AreEqual("Lead Guitar ", Simulator.GetProgram(5).Name);
        Assert.AreEqual("Lead Guitar", librarian.SelectedSlot!.Name);
        Assert.HasCount(2, Dialogs.Confirmations);

        // User registers are renamed without asking.
        Select(librarian, 230);
        librarian.RenameText = "Mine";
        await librarian.RenameAsync();
        Assert.AreEqual("Mine        ", Simulator.GetProgram(230).Name);
        Assert.HasCount(2, Dialogs.Confirmations);
    }

    [TestMethod]
    [Description("UI-005, PRG-006: export writes one Program Dump; import stores a file's program in the selected slot")]
    public async Task ExportImport_RoundTrip()
    {
        await using MainWindowViewModel vm = await OpenLibrarianAsync();
        LibrarianViewModel librarian = vm.Librarian;

        Select(librarian, 7);
        await librarian.ExportAsync();
        (string name, byte[] contents) = Dialogs.Saved.Single();
        Assert.AreEqual("007 Preset 008.syx", name);
        ProgramDump exported = SyxFile.Read(contents).Programs.Single();
        Assert.AreEqual((ushort)7, exported.ProgramNumber);

        Dialogs.FileToOpen = new PickedFile(name, contents);
        Select(librarian, 240);
        await librarian.ImportAsync();

        CollectionAssert.AreEqual(Simulator.GetProgram(7).ToBytes(), Simulator.GetProgram(240).ToBytes());
        Assert.AreEqual("Preset 008", librarian.SelectedSlot!.Name);
        Assert.IsEmpty(Dialogs.Confirmations, "Importing into a user register does not ask.");
    }

    [TestMethod]
    [Description("UI-005, PRO-018: edited sort flags are written with the Database Dump")]
    public async Task SortFlags_AreWritten()
    {
        await using MainWindowViewModel vm = await OpenLibrarianAsync();
        LibrarianViewModel librarian = vm.Librarian;
        Select(librarian, 12);

        foreach (CategoryFlagViewModel flag in librarian.Flags)
            flag.IsChecked = flag.Name is "Hall" or "Guitar";
        Assert.IsTrue(librarian.HasUnsavedFlags);
        await librarian.WriteFlagsAsync();

        Assert.IsFalse(librarian.HasUnsavedFlags);
        Assert.AreEqual(new ProgramCategories(EffectCategories.Hall, SourceCategories.Guitar), Simulator.Database[12]);
        Assert.AreEqual("Hall, Guitar", librarian.SelectedSlot!.CategoriesText);
    }

    [TestMethod]
    [Description("UI-005, PRG-005, CON-007: backup includes only the chosen dumps; restore asks and writes the file back")]
    public async Task BackupRestore_WithChosenDumps()
    {
        await using MainWindowViewModel vm = await OpenLibrarianAsync();
        LibrarianViewModel librarian = vm.Librarian;
        librarian.DumpOptions.Single(d => d.Name == "Remap Controllers").IsChecked = false;

        await librarian.BackupAsync();
        byte[] backup = Dialogs.Saved.Single().Contents;
        SyxContent content = SyxFile.Read(backup);
        Assert.HasCount(250, content.Programs);
        Assert.IsNotNull(content.Database);
        Assert.HasCount(6 + 1 + 1, content.Dumps, "Six setups, global patches and bypass; remap was not chosen.");

        Select(librarian, 230);
        librarian.RenameText = "Scratch";
        await librarian.RenameAsync();

        Dialogs.FileToOpen = new PickedFile("backup.syx", backup);
        Dialogs.Answers.Enqueue(false);
        await librarian.RestoreAsync();
        Assert.AreEqual("Restore cancelled.", librarian.Status);
        Assert.AreEqual("Scratch     ", Simulator.GetProgram(230).Name);
        StringAssert.Contains(Dialogs.Confirmations[^1], "including 200 factory preset(s)");

        Dialogs.Answers.Enqueue(true);
        await librarian.RestoreAsync();
        Assert.AreEqual("Restored backup.syx.", librarian.Status);
        Assert.AreEqual("User 31     ", Simulator.GetProgram(230).Name);
        Assert.AreEqual("User 31", librarian.Slots[230].Name);
    }

    [TestMethod]
    [Description("Spec 10: a full learn, edit, backup and restore cycle against the simulator runs without errors")]
    public async Task ValidationCycle_LearnEditBackupRestore()
    {
        await using MainWindowViewModel vm = await OpenLibrarianAsync();

        // Edit a live parameter on the parameter page.
        vm.SelectedTab = MainWindowViewModel.ParametersTab;
        vm.Tree.SelectedNode = vm.Tree.Roots.Single(r => r.Name == "Program").Children.Single(c => c.Name == "Pitch").Children.Single(c => c.Name == "no effect");
        await vm.Tree.Page!.Loading;
        var mix = (BB84.MPX1.App.ViewModels.Parameters.NumericParameterViewModel)vm.Tree.Page.Parameters.Single(p => p.Name == "Mix");
        mix.Value = 33;
        await mix.Writer.Completion.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken);

        // Edit and store a program, back up, change it again, restore.
        await vm.ProgramEditor.OpenSlotAsync(210);
        vm.ProgramEditor.Name = "Cycle";
        await vm.ProgramEditor.StoreAsync();
        await vm.Librarian.BackupAsync();
        byte[] backup = Dialogs.Saved[^1].Contents;
        string snapshot = Convert.ToHexString(Simulator.GetProgram(210).ToBytes());

        vm.ProgramEditor.Name = "Changed";
        await vm.ProgramEditor.StoreAsync();
        Dialogs.FileToOpen = new PickedFile("cycle.syx", backup);
        Dialogs.Answers.Enqueue(true);
        await vm.Librarian.RestoreAsync();

        Assert.AreEqual(snapshot, Convert.ToHexString(Simulator.GetProgram(210).ToBytes()));
        Assert.AreEqual("Restored cycle.syx.", vm.Librarian.Status);
        Assert.IsFalse(Simulator.IsCrashed);
        CollectionAssert.AreEqual(new byte[] { 33 }, Simulator.GetValue(new ControlAddress(0, 0, 0, 0)));
    }

    [TestMethod]
    [Description("UI-005: disconnecting clears the librarian")]
    public async Task Disconnect_ClearsLibrarian()
    {
        await using MainWindowViewModel vm = await OpenLibrarianAsync();

        await vm.DisconnectAsync();

        Assert.IsFalse(vm.Librarian.IsAttached);
        Assert.IsEmpty(vm.Librarian.Slots);
        Assert.IsFalse(vm.Librarian.RefreshCommand.CanExecute(null));
    }
}
