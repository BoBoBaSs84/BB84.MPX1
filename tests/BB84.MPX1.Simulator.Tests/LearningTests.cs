using System.Collections.Concurrent;

using BB84.MPX1.Core.Database;
using BB84.MPX1.Core.Programs;
using BB84.MPX1.Core.Protocol;
using BB84.MPX1.Midi;
using BB84.MPX1.Midi.Editing;
using BB84.MPX1.Midi.Learning;
using BB84.MPX1.Simulator.Fixtures;
using BB84.MPX1.Simulator.Recording;

using Types = BB84.MPX1.Simulator.Fixtures.DefaultFixture.Types;

namespace BB84.MPX1.Simulator.Tests;

[TestClass]
public sealed class LearningTests
{
    private static readonly DeviceId Device = new(0);

    private static readonly Mpx1SessionOptions FastOptions = new()
    {
        RequestTimeout = TimeSpan.FromMilliseconds(200),
        Retries = 1,
    };

    public TestContext TestContext { get; set; } = null!;

    private static async Task<(Mpx1Session Session, Mpx1Simulator Simulator, ConcurrentQueue<Mpx1Message> Sent)> CreateAsync()
    {
        Mpx1Simulator simulator = new();
        await simulator.OpenAsync(Mpx1Simulator.Port.Id, Mpx1Simulator.Port.Id);
        Mpx1Session session = new(simulator, Device, FastOptions);
        ConcurrentQueue<Mpx1Message> sent = new();
        session.FrameTraffic += (_, e) =>
        {
            if (e.Direction == FrameDirection.Sent && e.Message is not null)
                sent.Enqueue(e.Message);
        };
        session.Start();
        return (session, simulator, sent);
    }

    [TestMethod]
    [Description("AC-013, LRN-001, LRN-002: learning yields exactly the fixture tree, within 5 s")]
    public async Task Learn_MatchesFixture()
    {
        var (session, simulator, sent) = await CreateAsync();
        await using (session)
        await using (simulator)
        {
            List<LearningProgress> reports = [];
            long start = TimeProvider.System.GetTimestamp();

            ParameterDatabase db = await new ParameterLearner(session).LoadOrLearnAsync(new SyncProgress<LearningProgress>(reports.Add), cancellationToken: TestContext.CancellationToken);

            Assert.IsLessThan(TimeSpan.FromSeconds(5), TimeProvider.System.GetElapsedTime(start));
            SimulatorFixture fixture = DefaultFixture.CreateV110();
            CollectionAssert.AreEquivalent(
                fixture.Nodes.Select(n => $"{ControlAddress.From(n.Address)}={n.Type:X4}").ToArray(),
                db.Root.DescendantsAndSelf().Select(n => $"{n.Address}={n.Type:X4}").ToArray());
            Assert.HasCount(0x01C0, db.Descriptions);
            Assert.IsEmpty(db.Gaps);
            Assert.AreEqual(LearningPhase.Done, reports[^1].Phase);
            Assert.AreEqual(0x01C0, sent.Count(m => m is Request { RequestedClass: MessageClass.ParameterDescription }));
        }
    }

    [TestMethod]
    [Description("PRG-003, LRN-004: algorithm and controller parameter lists are learned, fit their blocks and survive the cache")]
    public async Task Learn_AlgorithmParameterLists()
    {
        var (session, simulator, sent) = await CreateAsync();
        await using (session)
        await using (simulator)
        {
            ParameterDatabase db = await new ParameterLearner(session).LoadOrLearnAsync(cancellationToken: TestContext.CancellationToken);

            AlgorithmParameters detune = db.FindAlgorithm(EffectType.Pitch, 1)!;
            Assert.AreEqual("Detune (M)", detune.Name);
            CollectionAssert.AreEqual(new[] { Types.Mix, Types.Level, Types.Tune, Types.PreDelay }, detune.Types.ToArray());
            CollectionAssert.AreEqual(new[] { 0, 1, 2, 4 }, db.Layout(EffectType.Pitch, 1)!.Parameters.Select(p => p.Offset).ToArray());
            Assert.AreEqual(2, db.AlgorithmCount(EffectType.Pitch));
            Assert.AreEqual(0, db.AlgorithmCount(EffectType.Reverb), "The fixture tree has no reverb algorithms.");

            foreach (ModulatorBlock block in ModulatorBlocks.All)
                Assert.AreEqual(block.Size, db.Layout(block.Type, 0)!.UsedBytes, block.Name);
            Assert.IsNotNull(db.FindAlgorithm(EffectType.Knob, 0));
            Assert.IsNull(db.Layout(EffectType.Knob, 0), "The knob has a fixed layout in the dump, not a modulator block.");

            // Effect blocks: 2 + 1 + 2 algorithms from the tree; controllers: types 6-16.
            Assert.AreEqual(5 + 11, sent.Count(m => m is Request { RequestedClass: MessageClass.EffectParameters }));

            ParameterDatabase reloaded = ParameterDatabaseSerializer.Deserialize(ParameterDatabaseSerializer.Serialize(db));
            CollectionAssert.AreEqual(
                db.Algorithms.Select(a => $"{a.Effect}/{a.Algorithm}/{a.Name}/{string.Join(',', a.Types)}").ToArray(),
                reloaded.Algorithms.Select(a => $"{a.Effect}/{a.Algorithm}/{a.Name}/{string.Join(',', a.Types)}").ToArray());
        }
    }

    [TestMethod]
    [Description("LRN-004: a cache written by an older schema is not loaded, so the device is learned again")]
    public void Deserialize_OldSchema_IsRejected()
    {
        const string json = """{"schemaVersion":1,"key":{"majorVersion":1,"minorVersion":10,"buildDate":"Mar 30 1998","buildTime":"17:51:03"},"descriptions":[],"nodes":[],"gaps":[]}""";
        Assert.ThrowsExactly<System.Text.Json.JsonException>(() => ParameterDatabaseSerializer.Deserialize(json));
    }

    [TestMethod]
    [Description("AC-013, LRN-004: a cached database is used for the same firmware; no 04 requests are sent")]
    public async Task LoadOrLearn_UsesCache()
    {
        InMemoryParameterDatabaseStore store = new();
        var (session, simulator, sent) = await CreateAsync();
        await using (session)
        await using (simulator)
        {
            ParameterDatabase first = await new ParameterLearner(session, store).LoadOrLearnAsync(cancellationToken: TestContext.CancellationToken);
            sent.Clear();

            ParameterDatabase second = await new ParameterLearner(session, store).LoadOrLearnAsync(cancellationToken: TestContext.CancellationToken);

            Assert.AreSame(first, second);
            Assert.IsFalse(sent.Any(m => m is Request { RequestedClass: MessageClass.ParameterDescription or MessageClass.ParameterType }));
        }
    }

    [TestMethod]
    [Description("LRN-004: file store survives a restart")]
    public async Task FileStore_RoundTrip()
    {
        string directory = Path.Combine(Path.GetTempPath(), "bb84-mpx1-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var (session, simulator, _) = await CreateAsync();
            await using (session)
            await using (simulator)
            {
                FileParameterDatabaseStore store = new(directory);
                ParameterDatabase learned = await new ParameterLearner(session, store).LoadOrLearnAsync(cancellationToken: TestContext.CancellationToken);

                ParameterDatabase? loaded = await new FileParameterDatabaseStore(directory).TryLoadAsync(learned.Key, TestContext.CancellationToken);

                Assert.IsNotNull(loaded);
                Assert.AreEqual(learned.NodeCount, loaded.NodeCount);
            }
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    [Description("AC-014, LRN-006, LRN-007: size override and no recursion below A:1 B:2 C:0")]
    public async Task Learn_AppliesKnownBugWorkarounds()
    {
        var (session, simulator, sent) = await CreateAsync();
        await using (session)
        await using (simulator)
        {
            ParameterDatabase db = await new ParameterLearner(session).LoadOrLearnAsync(cancellationToken: TestContext.CancellationToken);

            Assert.AreEqual((ushort)542, db.Describe(Types.BrokenSizeDump)!.Size);
            Assert.IsTrue(db.Find(KnownBugs.DeadEndAddress)!.IsDeadEnd);
            Assert.IsFalse(sent.Any(m => m is Request { RequestedClass: MessageClass.ParameterType } r
                && r.GetControlAddress().Depth == 4 && r.GetControlAddress().Parent() == KnownBugs.DeadEndAddress));
        }
    }

    [TestMethod]
    [Description("LRN-003: learning can be cancelled")]
    public async Task Learn_CanBeCancelled()
    {
        var (session, simulator, _) = await CreateAsync();
        await using (session)
        await using (simulator)
        {
            using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
            Progress<LearningProgress> progress = new(p =>
            {
                if (p.DescriptionsDone >= 32)
                    cts.Cancel();
            });

            await Assert.ThrowsAsync<OperationCanceledException>(() => new ParameterLearner(session).LoadOrLearnAsync(progress, cancellationToken: cts.Token));
        }
    }

    [TestMethod]
    [Description("LRN-009: learning from a recorded session gives the same database")]
    public async Task Learn_FromRecording()
    {
        StringWriter log = new();
        ParameterDatabase live;
        await using (RecordingMidiTransport recorder = new(new Mpx1Simulator(), log))
        {
            await recorder.OpenAsync(Mpx1Simulator.Port.Id, Mpx1Simulator.Port.Id, TestContext.CancellationToken);
            await using Mpx1Session session = new(recorder, Device, FastOptions);
            session.Start();
            live = await new ParameterLearner(session).LoadOrLearnAsync(cancellationToken: TestContext.CancellationToken);
        }

        IReadOnlyList<RecordedFrame> frames = RecordedFrame.ReadAll(new StringReader(log.ToString()));
        ParameterDatabase captured = CaptureLearner.Learn(frames.Select(f => Mpx1Codec.Decode(f.ToBytes())));

        Assert.AreEqual(ParameterDatabaseSerializer.Serialize(live), ParameterDatabaseSerializer.Serialize(captured));
    }

    [TestMethod]
    [Description("AC-015, LRN-008: no frame is sent for a parameter of an unloaded algorithm")]
    public async Task Editor_BlocksUnloadedAlgorithm()
    {
        var (session, simulator, sent) = await CreateAsync();
        await using (session)
        await using (simulator)
        {
            ParameterDatabase db = await new ParameterLearner(session).LoadOrLearnAsync(cancellationToken: TestContext.CancellationToken);
            ParameterEditor editor = new(session, db);
            sent.Clear();

            // Program 0 runs pitch algorithm 0 ("no effect"); A:0 B:0 C:1 D:2 is Detune (M) Tune.
            UnloadedAlgorithmException ex = await Assert.ThrowsExactlyAsync<UnloadedAlgorithmException>(
                () => editor.WriteValueAsync(new ControlAddress(0, 0, 1, 2), 10, 1, TestContext.CancellationToken));

            StringAssert.Contains(ex.Message, "Detune (M)");
            Assert.IsFalse(sent.Any(m => m is ParameterData));
            Assert.IsFalse(simulator.IsCrashed);
        }
    }

    [TestMethod]
    [Description("PAR-001, PAR-002, PAR-003: read value and display text, write value")]
    public async Task Editor_ReadAndWrite()
    {
        var (session, simulator, _) = await CreateAsync();
        await using (session)
        await using (simulator)
        {
            ParameterDatabase db = await new ParameterLearner(session).LoadOrLearnAsync(cancellationToken: TestContext.CancellationToken);
            ParameterEditor editor = new(session, db);
            ControlAddress mix = db.FindByPath("Program", "Pitch", "no effect", "Mix")!.Address;

            await editor.WriteValueAsync(mix, 75, cancellationToken: TestContext.CancellationToken);
            ParameterReading reading = await editor.ReadAsync(mix, TestContext.CancellationToken);

            Assert.AreEqual(75, reading.Value!.Value.Value);
            Assert.AreEqual("75", reading.DisplayText);
            await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() => editor.WriteValueAsync(mix, 101, cancellationToken: TestContext.CancellationToken));
        }
    }

    [TestMethod]
    [Description("PAR-006: MIDI Speed accepts only the documented values")]
    public async Task Editor_ValidatesMidiSpeed()
    {
        var (session, simulator, _) = await CreateAsync();
        await using (session)
        await using (simulator)
        {
            ParameterDatabase db = await new ParameterLearner(session).LoadOrLearnAsync(cancellationToken: TestContext.CancellationToken);
            ParameterEditor editor = new(session, db);
            ControlAddress speed = new(1, 2, 10);

            await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() => editor.WriteAsync(speed, new byte[] { 0x03 }, TestContext.CancellationToken));
            await editor.WriteAsync(speed, new byte[] { 0x40 }, TestContext.CancellationToken);

            CollectionAssert.AreEqual(new byte[] { 0x40 }, simulator.GetValue(speed));
        }
    }

    private sealed class SyncProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }
}
