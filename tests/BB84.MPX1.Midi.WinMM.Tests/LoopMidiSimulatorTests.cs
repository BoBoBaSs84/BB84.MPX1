using BB84.MPX1.Core.Database;
using BB84.MPX1.Core.Programs;
using BB84.MPX1.Core.Protocol;
using BB84.MPX1.Midi.Learning;
using BB84.MPX1.Midi.Library;
using BB84.MPX1.Simulator.Fixtures;

namespace BB84.MPX1.Midi.WinMM.Tests;

// The editor stack against the simulator, with every frame crossing the real WinMM transport through the two
// loopMIDI ports "loopMIDI Port Editor" and "loopMIDI Port Simulator". Inconclusive when the ports are missing.
[TestClass]
public sealed class LoopMidiSimulatorTests
{
    private static readonly DeviceId Device = new(0);

    private static readonly Mpx1SessionOptions Options = new()
    {
        RequestTimeout = TimeSpan.FromSeconds(2),
        Retries = 1,
    };

    public TestContext TestContext { get; set; } = null!;

    private Task<LoopMidiRig> OpenRigAsync() => LoopMidiRig.OpenAsync(options: null, TestContext.CancellationToken);

    [TestMethod]
    [TestCategory("LoopMidi")]
    [Description("SES-006, TRN-002: ARE_YOU_THERE reaches the simulator and IM_ALIVE comes back over the ports")]
    public async Task Ping_AnswersOverLoopMidi()
    {
        await using LoopMidiRig rig = await OpenRigAsync();
        await using Mpx1Session session = new(rig.Editor, Device, Options);
        session.Start();

        Assert.IsTrue(await session.PingAsync(TestContext.CancellationToken));
    }

    [TestMethod]
    [TestCategory("LoopMidi")]
    [Description("LRN-001, LRN-002: learning over the ports yields exactly the fixture tree")]
    public async Task Learn_OverLoopMidi_MatchesFixture()
    {
        await using LoopMidiRig rig = await OpenRigAsync();
        await using Mpx1Session session = new(rig.Editor, Device, Options);
        session.Start();

        ParameterDatabase db = await new ParameterLearner(session).LoadOrLearnAsync(cancellationToken: TestContext.CancellationToken);

        SimulatorFixture fixture = DefaultFixture.CreateV110();
        CollectionAssert.AreEquivalent(
            fixture.Nodes.Select(n => $"{ControlAddress.From(n.Address)}={n.Type:X4}").ToArray(),
            db.Root.DescendantsAndSelf().Select(n => $"{n.Address}={n.Type:X4}").ToArray());
        Assert.IsEmpty(db.Gaps);
    }

    [TestMethod]
    [TestCategory("LoopMidi")]
    [Description("PRG-001, PRG-007, SES-004: listing and storing programs works over the ports, including BUSY/READY flow control")]
    public async Task Librarian_ListsAndStoresProgram_OverLoopMidi()
    {
        await using LoopMidiRig rig = await OpenRigAsync();
        await using Mpx1Session session = new(rig.Editor, Device, Options);
        session.Start();
        ParameterDatabase db = await new ParameterLearner(session).LoadOrLearnAsync(cancellationToken: TestContext.CancellationToken);
        ProgramLibrarian librarian = new(session, db);

        IReadOnlyList<ProgramSummary> slots = await librarian.ListAsync(cancellationToken: TestContext.CancellationToken);
        Assert.HasCount(250, slots);
        Assert.AreEqual("User 01", slots[200].Name);

        ProgramData program = await librarian.ReceiveAsync(7, TestContext.CancellationToken) with { Name = "Stored      " };
        await librarian.StoreAsync(210, program, presetConfirmed: false, TestContext.CancellationToken);

        Assert.AreEqual("Stored      ", rig.Simulator.GetProgram(210).Name);
    }
}
