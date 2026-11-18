using BB84.MPX1.Core.Database;
using BB84.MPX1.Core.Protocol;
using BB84.MPX1.Midi;
using BB84.MPX1.Simulator.Fixtures;

namespace BB84.MPX1.Simulator.Tests;

/// <summary>The full-tree V1.00 fixture built from the M8 learn recording (SIM-002).</summary>
[TestClass]
public sealed class FixtureBuilderTests
{
    private static readonly DeviceId Device = new(0);

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [Description("SIM-002, LRN-005: the V1.00 fixture holds the recorded tree and finds its special cases by name, not by V1.10 type numbers")]
    public async Task V100Fixture_HasRecordedTreeAndSpecials()
    {
        SimulatorFixture fixture = await Firmware.FixtureAsync(Firmware.V100, TestContext.CancellationToken);

        Assert.AreEqual((byte)1, fixture.MajorVersion);
        Assert.AreEqual((byte)0, fixture.MinorVersion);
        Assert.AreEqual("Nov 21 1996", fixture.BuildDate);
        Assert.HasCount(448, fixture.Descriptions);
        Assert.HasCount(2653, fixture.Nodes);
        Assert.HasCount(11, fixture.Controllers);

        // Type numbers observed on the V1.00 unit in M8.
        FixtureSpecials specials = fixture.Specials!;
        Assert.AreEqual((ushort)0x011D, specials.PanelButton);
        Assert.AreEqual((ushort)0x011E, specials.Display);
        Assert.AreEqual((ushort)0x012F, specials.Leds);
        Assert.AreEqual((ushort)0x0129, specials.Patches);
        Assert.AreEqual((ushort)0x012A, specials.SoftRow);
        Assert.AreEqual((ushort)0x0128, specials.AllSetups);
        Assert.IsNull(specials.Setups!.Active);
        Assert.HasCount(5, specials.Setups.Stored);
        Assert.IsTrue(specials.Setups.StoredReadAsFirst);
    }

    [TestMethod]
    [Description("SIM-002: the V1.00 fixture survives a JSON round trip")]
    public async Task V100Fixture_JsonRoundTrip()
    {
        SimulatorFixture fixture = await Firmware.FixtureAsync(Firmware.V100, TestContext.CancellationToken);

        string json = SimulatorFixtureSerializer.Serialize(fixture);

        Assert.AreEqual(json, SimulatorFixtureSerializer.Serialize(SimulatorFixtureSerializer.Deserialize(json)));
    }

    [TestMethod]
    [Description("SIM-002, LRN-001, LRN-007: learning the simulated V1.00 unit yields the tree learned from the real one, dead end included")]
    public async Task V100Fixture_LearnsLikeTheRecordedUnit()
    {
        ParameterDatabase recorded = await Firmware.RecordedDatabaseAsync(TestContext.CancellationToken);
        await using Mpx1Simulator simulator = await Firmware.SimulatorAsync(Firmware.V100, null, TestContext.CancellationToken);
        await using Mpx1Session session = new(simulator, Device);
        session.Start();

        ParameterDatabase learned = await Firmware.LoadOrLearnAsync(session, TestContext.CancellationToken);

        Assert.AreEqual(recorded.Key, learned.Key);
        Assert.HasCount(recorded.Descriptions.Count, learned.Descriptions);
        Assert.AreSequenceEqual(
            recorded.Root.DescendantsAndSelf().Select(n => $"{n.Address}={n.Type:X4}").ToArray(),
            learned.Root.DescendantsAndSelf().Select(n => $"{n.Address}={n.Type:X4}").ToArray());
        Assert.IsTrue(learned.Find(new ControlAddress(1, 2, 3))!.IsDeadEnd);
        Assert.IsEmpty(learned.Gaps);
    }
}
