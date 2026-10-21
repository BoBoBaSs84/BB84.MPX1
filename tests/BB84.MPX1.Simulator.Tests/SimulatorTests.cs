using BB84.MPX1.Core.Programs;
using BB84.MPX1.Core.Protocol;
using BB84.MPX1.Midi;
using BB84.MPX1.Simulator.Fixtures;

using Types = BB84.MPX1.Simulator.Fixtures.DefaultFixture.Types;

namespace BB84.MPX1.Simulator.Tests;

[TestClass]
public sealed class SimulatorTests
{
    private static readonly DeviceId Device = new(0);

    private static readonly Mpx1SessionOptions FastOptions = new()
    {
        RequestTimeout = TimeSpan.FromMilliseconds(150),
        Retries = 2,
        ReadyTimeout = TimeSpan.FromMilliseconds(1000),
        BusyGracePeriod = TimeSpan.FromMilliseconds(100),
    };

    public TestContext TestContext { get; set; } = null!;

    private static async Task<(Mpx1Session Session, Mpx1Simulator Simulator)> CreateAsync(SimulatorOptions? options = null, Mpx1SessionOptions? sessionOptions = null)
    {
        Mpx1Simulator simulator = new(options: options);
        await simulator.OpenAsync(Mpx1Simulator.Port.Id, Mpx1Simulator.Port.Id);
        Mpx1Session session = new(simulator, Device, sessionOptions ?? FastOptions);
        session.Start();
        return (session, simulator);
    }

    [TestMethod]
    [Description("SIM-001, PRO-010: System Configuration from the fixture")]
    public async Task SystemConfiguration_FromFixture()
    {
        var (session, simulator) = await CreateAsync();
        await using (session)
        await using (simulator)
        {
            DeviceVersion version = await session.DetectVersionAsync(TestContext.CancellationToken);

            Assert.AreEqual("V1.10", version.ToString());
            Assert.AreEqual((ushort)0x01C0, version.Configuration.ParameterTypeCount);
            Assert.IsNotNull(version.Identity);
        }
    }

    [TestMethod]
    [Description("SIM-001: tree, descriptions, labels, values and display text")]
    public async Task ParameterQueries_AnswerFromTree()
    {
        var (session, simulator) = await CreateAsync();
        await using (session)
        await using (simulator)
        {
            ControlAddress tune = new(0, 0, 1, 2);
            CancellationToken ct = TestContext.CancellationToken;

            ParameterType type = await session.RequestAsync<ParameterType>(Request.ParameterType(Device, tune), ct);
            ParameterDescription description = await session.RequestAsync<ParameterDescription>(Request.ParameterDescription(Device, type.Type), ct);
            ParameterLabel label = await session.RequestAsync<ParameterLabel>(Request.ParameterLabel(Device, tune), ct);
            ParameterData data = await session.RequestAsync<ParameterData>(Request.ParameterData(Device, tune), ct);
            ParameterDisplay display = await session.RequestAsync<ParameterDisplay>(Request.ParameterDisplay(Device, tune), ct);

            Assert.AreEqual(Types.Tune, type.Type);
            Assert.AreEqual(Types.TuneOption, description.OptionType);
            Assert.AreEqual("Tune ", label.Label);
            CollectionAssert.AreEqual(new byte[] { 0x0A, 0x01 }, data.Data.ToArray(), "Tune value followed by its option byte");
            Assert.AreEqual("10", display.Text);
        }
    }

    [TestMethod]
    [Description("SIM-001: every type below the reported count has a description")]
    public async Task ParameterDescription_GenericForUndefinedTypes()
    {
        var (session, simulator) = await CreateAsync();
        await using (session)
        await using (simulator)
        {
            ParameterDescription description = await session.RequestAsync<ParameterDescription>(Request.ParameterDescription(Device, 0x0001), TestContext.CancellationToken);
            Assert.AreEqual((ushort)0x0001, description.Type);
        }
    }

    [TestMethod]
    [Description("AC-012, SIM-004: two dropped responses, third attempt succeeds")]
    public async Task DroppedResponses_AreRetried()
    {
        var (session, simulator) = await CreateAsync();
        await using (session)
        await using (simulator)
        {
            simulator.Faults.DropResponses = 2;

            await session.RequestAsync<SystemConfiguration>(Request.SystemConfiguration(Device), TestContext.CancellationToken);

            Assert.AreEqual(3, simulator.ReceivedFrames);
        }
    }

    [TestMethod]
    [Description("SIM-004: a corrupted response is reported and the request retried")]
    public async Task CorruptedResponse_IsRetried()
    {
        var (session, simulator) = await CreateAsync();
        int rejected = 0;
        session.FrameTraffic += (_, e) =>
        {
            if (e.Error is not null)
                Interlocked.Increment(ref rejected);
        };
        await using (session)
        await using (simulator)
        {
            simulator.Faults.CorruptResponses = 1;

            await session.RequestAsync<SystemConfiguration>(Request.SystemConfiguration(Device), TestContext.CancellationToken);

            Assert.AreEqual(1, Volatile.Read(ref rejected));
            Assert.AreEqual(2, simulator.ReceivedFrames);
        }
    }

    [TestMethod]
    [Description("AC-011, SIM-003: dump writes wait for READY")]
    public async Task ProgramDumpWrites_WaitForReady()
    {
        TimeSpan busy = TimeSpan.FromMilliseconds(250);
        var (session, simulator) = await CreateAsync(new SimulatorOptions { BusyDuration = busy });
        await using (session)
        await using (simulator)
        {
            ProgramData program = DefaultFixture.CreateProgram(5) with { Name = "Written     " };
            long start = TimeProvider.System.GetTimestamp();

            await session.SendAsync(new ProgramDump(Device, 200, program), TestContext.CancellationToken);
            await session.SendAsync(new ProgramDump(Device, 201, program), TestContext.CancellationToken);

            Assert.IsGreaterThanOrEqualTo(busy * 2 - TimeSpan.FromMilliseconds(30), TimeProvider.System.GetElapsedTime(start));
            Assert.AreEqual("Written     ", simulator.GetProgram(201).Name);
        }
    }

    [TestMethod]
    [Description("SES-005, SIM-004: suppressed READY times out")]
    public async Task SuppressedReady_TimesOut()
    {
        var (session, simulator) = await CreateAsync(sessionOptions: FastOptions with { ReadyTimeout = TimeSpan.FromMilliseconds(200) });
        await using (session)
        await using (simulator)
        {
            simulator.Faults.SuppressReady = true;
            await Assert.ThrowsExactlyAsync<Mpx1TimeoutException>(
                () => session.SendAsync(new ProgramDump(Device, 200, DefaultFixture.CreateProgram(0)), TestContext.CancellationToken));
        }
    }

    [TestMethod]
    [Description("PRG-001: read a program and the active program")]
    public async Task ProgramDump_Read()
    {
        var (session, simulator) = await CreateAsync();
        await using (session)
        await using (simulator)
        {
            ProgramDump preset = await session.RequestAsync<ProgramDump>(Request.ProgramDump(Device, 7), TestContext.CancellationToken);
            ProgramDump active = await session.RequestAsync<ProgramDump>(Request.ProgramDump(Device, Request.ActiveProgram), TestContext.CancellationToken);
            ProgramInformation info = await session.RequestAsync<ProgramInformation>(Request.ProgramInformation(Device, 210), TestContext.CancellationToken);

            Assert.AreEqual("Preset 008", preset.Program.Name.Trim());
            Assert.AreEqual(Request.ActiveProgram, active.ProgramNumber);
            Assert.AreEqual("User 11", info.Name.Trim());
        }
    }

    [TestMethod]
    [Description("PRO-019, PRO-020: effect parameter lists come from the tree")]
    public async Task EffectParameters_FromTree()
    {
        var (session, simulator) = await CreateAsync();
        await using (session)
        await using (simulator)
        {
            EffectParameters detune = await session.RequestAsync<EffectParameters>(Request.EffectParameters(Device, EffectType.Pitch, 1), TestContext.CancellationToken);
            AllEffectParameters all = await session.RequestAsync<AllEffectParameters>(Request.AllEffectParameters(Device, 1), TestContext.CancellationToken);

            Assert.AreEqual("Detune (M)", detune.Parameters.Name.Trim());
            CollectionAssert.AreEqual(new[] { Types.Mix, Types.Level, Types.Tune, Types.PreDelay }, detune.Parameters.ParameterTypes.ToArray());
            Assert.AreEqual("Detune (M)", all.Effects[0].Name.Trim(), "Program 1 runs pitch algorithm 1");
        }
    }

    [TestMethod]
    [Description("SIM-005, Appendix D bug 1: type 0x014A reports size 0 but holds 542 bytes")]
    public async Task Bug1_BrokenSizeDump()
    {
        var (session, simulator) = await CreateAsync();
        await using (session)
        await using (simulator)
        {
            ParameterDescription description = await session.RequestAsync<ParameterDescription>(Request.ParameterDescription(Device, Types.BrokenSizeDump), TestContext.CancellationToken);
            ParameterData data = await session.RequestAsync<ParameterData>(Request.ParameterData(Device, new ControlAddress(1, 11, 0, 0)), TestContext.CancellationToken);

            Assert.AreEqual((ushort)0, description.Size);
            Assert.AreEqual(DefaultFixture.BrokenSizeDumpLength, data.Data.Length);
        }
    }

    [TestMethod]
    [Description("SIM-005, Appendix D bug 2: A:1 B:2 C:0 is a control level without children")]
    public async Task Bug2_DeadEndControlLevel()
    {
        var (session, simulator) = await CreateAsync(sessionOptions: FastOptions with { Retries = 0 });
        await using (session)
        await using (simulator)
        {
            ControlAddress deadEnd = new(1, 2, 0);
            ParameterType type = await session.RequestAsync<ParameterType>(Request.ParameterType(Device, deadEnd), TestContext.CancellationToken);
            ParameterDescription description = await session.RequestAsync<ParameterDescription>(Request.ParameterDescription(Device, type.Type), TestContext.CancellationToken);

            Assert.IsTrue(description.IsControlLevel);
            await Assert.ThrowsExactlyAsync<Mpx1TimeoutException>(
                () => session.RequestAsync<ParameterType>(Request.ParameterType(Device, deadEnd.Child(0)), TestContext.CancellationToken));
        }
    }

    [TestMethod]
    [Description("SIM-005, Appendix D bug 3: writing an unloaded algorithm's parameter crashes the unit")]
    public async Task Bug3_UnloadedAlgorithmWriteCrashes()
    {
        var (session, simulator) = await CreateAsync(sessionOptions: FastOptions with { Retries = 0 });
        await using (session)
        await using (simulator)
        {
            // Program 0 runs pitch algorithm 0 ("no effect"); A:0 B:0 C:1 is Detune (M).
            await session.SendAsync(new ParameterData(Device, new byte[] { 50 }, new ControlAddress(0, 0, 1, 0)), TestContext.CancellationToken);

            Assert.IsTrue(simulator.IsCrashed);
            Assert.IsFalse(await session.PingAsync(TestContext.CancellationToken));
        }
    }

    [TestMethod]
    [Description("PAR-001: writing a loaded algorithm's parameter is stored")]
    public async Task ParameterWrite_IsStored()
    {
        var (session, simulator) = await CreateAsync();
        await using (session)
        await using (simulator)
        {
            ControlAddress mix = new(0, 0, 0, 0);
            await session.SendAsync(new ParameterData(Device, new byte[] { 42 }, mix), TestContext.CancellationToken);
            ParameterData data = await session.RequestAsync<ParameterData>(Request.ParameterData(Device, mix), TestContext.CancellationToken);

            Assert.AreEqual((byte)42, data.Data.Span[0]);
            Assert.IsFalse(simulator.IsCrashed);
        }
    }

    [TestMethod]
    [Description("SIM-001: frames for other device IDs are ignored; 127 is broadcast")]
    public async Task DeviceIdFiltering()
    {
        var (session, simulator) = await CreateAsync();
        await using (session)
        await using (simulator)
        {
            Mpx1Message reply = await session.ExchangeAsync(Request.SystemConfiguration(DeviceId.All), m => m is SystemConfiguration, TestContext.CancellationToken);
            Assert.AreEqual(Device, reply.Device);

            int before = simulator.ReceivedFrames;
            await Assert.ThrowsExactlyAsync<Mpx1TimeoutException>(
                () => session.ExchangeAsync(Request.SystemConfiguration(new DeviceId(5)), m => m is SystemConfiguration, TestContext.CancellationToken));
            Assert.AreEqual(before + 3, simulator.ReceivedFrames, "Frames for device 5 reach the unit but are not answered");
        }
    }

    [TestMethod]
    [Description("SIM-002: fixtures survive a JSON round trip")]
    public void Fixture_JsonRoundTrip()
    {
        SimulatorFixture fixture = DefaultFixture.CreateV110();

        string json = SimulatorFixtureSerializer.Serialize(fixture);
        SimulatorFixture copy = SimulatorFixtureSerializer.Deserialize(json);

        Assert.AreEqual(fixture.Nodes.Count, copy.Nodes.Count);
        Assert.AreEqual(fixture.Descriptions.Count, copy.Descriptions.Count);
        Assert.AreEqual(fixture.Programs[42].Hex, copy.Programs[42].Hex);
        Assert.AreEqual(json, SimulatorFixtureSerializer.Serialize(copy));
    }

    [TestMethod]
    [Description("SIM-002: state exported from a simulator loads into a new one")]
    public async Task ToFixture_PreservesWrites()
    {
        var (session, simulator) = await CreateAsync();
        await using (session)
        await using (simulator)
        {
            await session.SendAsync(new ProgramDump(Device, 220, DefaultFixture.CreateProgram(1) with { Name = "Saved       " }), TestContext.CancellationToken);

            await using Mpx1Simulator copy = new(SimulatorFixtureSerializer.Deserialize(SimulatorFixtureSerializer.Serialize(simulator.ToFixture())));
            Assert.AreEqual("Saved       ", copy.GetProgram(220).Name);
        }
    }
}
