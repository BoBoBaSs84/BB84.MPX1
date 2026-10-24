using Microsoft.Extensions.Time.Testing;

using BB84.MPX1.App.Services;
using BB84.MPX1.App.ViewModels;
using BB84.MPX1.Core.Database;
using BB84.MPX1.Core.Protocol;
using BB84.MPX1.Midi;
using BB84.MPX1.Midi.Learning;

namespace BB84.MPX1.App.Tests;

internal sealed class ImmediateDispatcher : IUiDispatcher
{
    public void Post(Action action) => action();
}

[TestClass]
public sealed class ServiceTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [Description("UI-009: monitor summaries decode requests, responses and errors")]
    public void MessageSummary_DescribesMessages()
    {
        DeviceId device = new(0);
        ControlAddress address = new(0, 2, 1, 2);

        Assert.AreEqual("Request ParameterData A:0 B:2 C:1 D:2", MessageSummary.Describe(Request.ParameterData(device, address), null));
        Assert.AreEqual("Request ProgramDump active", MessageSummary.Describe(Request.ProgramDump(device, Request.ActiveProgram), null));
        Assert.AreEqual("Handshake AreYouThere", MessageSummary.Describe(new Handshake(device, HandshakeCommand.AreYouThere), null));
        Assert.AreEqual("Parameter Data A:0 B:2 C:1 D:2: 64", MessageSummary.Describe(new ParameterData(device, new byte[] { 0x64 }, address), null));
        StringAssert.StartsWith(MessageSummary.Describe(null, new Mpx1FormatError(7, "Nibble above 0x0F")), "Invalid at byte 7");
    }

    [TestMethod]
    [Description("UI-009: monitor shows sent/received frames as hex with summaries, batched and capped")]
    public void Monitor_FlushesBatchesAndCaps()
    {
        FakeTimeProvider time = new();
        using MonitorViewModel monitor = new(new ImmediateDispatcher(), time);
        byte[] frame = Request.SystemConfiguration(new DeviceId(0)).ToBytes();

        monitor.Record(null, new FrameEventArgs(FrameDirection.Sent, time.GetUtcNow(), frame, Mpx1Codec.Decode(frame), null));
        Assert.IsEmpty(monitor.Entries);

        time.Advance(TimeSpan.FromMilliseconds(100));
        Assert.HasCount(1, monitor.Entries);
        MonitorEntry entry = monitor.Entries[0];
        Assert.AreEqual("→", entry.Direction);
        Assert.AreEqual("F0 06 09 00 06 00 00 00 00 00 00 00 00 F7", entry.Hex);
        Assert.AreEqual("Request SystemConfiguration", entry.Summary);

        monitor.IsPaused = true;
        monitor.Record(null, new FrameEventArgs(FrameDirection.Received, time.GetUtcNow(), frame, null, new Mpx1FormatError(0, "x")));
        monitor.Flush();
        Assert.HasCount(1, monitor.Entries);

        monitor.IsPaused = false;
        for (int i = 0; i < MonitorViewModel.Capacity + 10; i++)
            monitor.Record(null, new FrameEventArgs(FrameDirection.Received, time.GetUtcNow(), frame, null, new Mpx1FormatError(0, "x")));
        monitor.Flush();
        Assert.HasCount(MonitorViewModel.Capacity, monitor.Entries);
        Assert.IsTrue(monitor.Entries[^1].IsError);
    }

    [TestMethod]
    [Description("UI-005, UI-008: a transfer's status is its result, a cancel, a described failure, or kept when the session closed")]
    public async Task OperationStatus_RunAsync_MapsOutcomes()
    {
        Assert.AreEqual("Done.", await OperationStatus.RunAsync(_ => Task.FromResult("Done.")));
        Assert.AreEqual(OperationStatus.DeviceDidNotAnswer, await OperationStatus.RunAsync(_ => throw new Mpx1TimeoutException("x")));
        Assert.AreEqual("Locked.", await OperationStatus.RunAsync(_ => throw new UnauthorizedAccessException("Locked.")));
        Assert.AreEqual("Disk full.", await OperationStatus.RunAsync(_ => throw new IOException("Disk full.")));

        using CancellationTokenSource cancel = new();
        await cancel.CancelAsync();
        Assert.AreEqual(OperationStatus.Cancelled, await OperationStatus.RunAsync(ct => Task.FromCanceled<string>(ct), cancel.Token));

        // A disposed session cancels its pending request without the caller's token, and refuses new calls.
        Assert.IsNull(await OperationStatus.RunAsync(_ => throw new TaskCanceledException()));
        Assert.IsNull(await OperationStatus.RunAsync(_ => throw new ObjectDisposedException("session")));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => OperationStatus.RunAsync(_ => throw new InvalidOperationException()));
    }

    [TestMethod]
    [Description("LRN-004: cache read and write failures become a warning instead of an error")]
    public async Task TolerantDatabaseStore_TurnsFailuresIntoWarnings()
    {
        ParameterDatabaseBuilder builder = new(new DatabaseKey(1, 10, "Mar 30 1998", "17:51:03"));
        builder.AddDescription(new ParameterDescription(new DeviceId(0), 0, "Root", 1, ParameterAttributes.ControlLevel, null, [new UnitLimit(0, 0, 0)]));
        builder.AddNode(ControlAddress.Root, 0);
        ParameterDatabase database = builder.Build();

        TolerantDatabaseStore broken = new(new FailingStore(new UnauthorizedAccessException("Access denied.")));
        Assert.IsNull(await broken.TryLoadAsync(database.Key, TestContext.CancellationToken));
        StringAssert.Contains(broken.LastError, "could not be read: Access denied.");
        await broken.SaveAsync(database, TestContext.CancellationToken);
        StringAssert.Contains(broken.LastError, "could not be saved: Access denied.");

        TolerantDatabaseStore working = new(new InMemoryParameterDatabaseStore());
        await working.SaveAsync(database, TestContext.CancellationToken);
        Assert.AreSame(database, await working.TryLoadAsync(database.Key, TestContext.CancellationToken));
        Assert.IsNull(working.LastError);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => new TolerantDatabaseStore(new FailingStore(new InvalidOperationException())).SaveAsync(database, TestContext.CancellationToken));
    }

    private sealed class FailingStore(Exception failure) : IParameterDatabaseStore
    {
        public Task<ParameterDatabase?> TryLoadAsync(DatabaseKey key, CancellationToken cancellationToken = default) => throw failure;

        public Task SaveAsync(ParameterDatabase database, CancellationToken cancellationToken = default) => throw failure;
    }

    [TestMethod]
    [Description("UI-004: live edits are coalesced to the latest value and read back once")]
    public async Task CoalescingWriter_SendsLatestValueThenSettles()
    {
        FakeTimeProvider time = new();
        List<int> written = [];
        int settled = 0;
        TaskCompletionSource firstWrite = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using CoalescingWriter<int> writer = new(
            (v, _) =>
            {
                lock (written)
                    written.Add(v);
                firstWrite.TrySetResult();
                return Task.CompletedTask;
            },
            _ =>
            {
                Interlocked.Increment(ref settled);
                return Task.CompletedTask;
            },
            ex => Assert.Fail(ex.ToString()),
            time,
            TimeSpan.FromMilliseconds(30),
            TimeSpan.FromMilliseconds(150));

        writer.Submit(1);
        await firstWrite.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken);
        for (int v = 2; v <= 10; v++)
            writer.Submit(v);

        // Drive the clock until the burst completes.
        for (int i = 0; i < 50 && !writer.Completion.IsCompleted; i++)
        {
            time.Advance(TimeSpan.FromMilliseconds(30));
            await Task.Delay(5, TestContext.CancellationToken);
        }
        await writer.Completion.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken);

        CollectionAssert.AreEqual(new[] { 1, 10 }, written);
        Assert.AreEqual(1, settled);
    }

    [TestMethod]
    [Description("UI-001: the simulator pseudo-port pairs input and output")]
    public void Catalog_ListsSimulatorAndRejectsMixedPairs()
    {
        MidiPortCatalog catalog = new(createHardware: null);
        Assert.AreEqual(MidiPortCatalog.Simulator, catalog.GetInputs().Single());
        Assert.AreEqual(MidiPortCatalog.Simulator, catalog.GetOutputs().Single());

        MidiPortChoice hardware = new("midi:x", "x", new MidiPortInfo(new MidiPortId("x"), "x"), IsSimulator: false);
        Assert.ThrowsExactly<ArgumentException>(() => catalog.CreateTransport(MidiPortCatalog.Simulator, hardware));
    }

    [TestMethod]
    [Description("Spec 4.7: settings persist as JSON; a corrupt file falls back to defaults (SEC-002)")]
    public void SettingsStore_RoundTripsAndToleratesCorruptFiles()
    {
        string path = Path.Combine(Path.GetTempPath(), $"bb84-settings-{Guid.NewGuid():N}.json");
        try
        {
            SettingsStore store = new(path);
            AppSettings saved = new() { InputPort = "sim:simulator", OutputPort = "sim:simulator", DeviceId = 5, Retries = 4, MonitorVisible = false };
            store.Save(saved);
            Assert.AreEqual(saved, store.Load());

            File.WriteAllText(path, "{ not json");
            Assert.AreEqual(new AppSettings(), store.Load());
        }
        finally
        {
            File.Delete(path);
        }
    }
}
