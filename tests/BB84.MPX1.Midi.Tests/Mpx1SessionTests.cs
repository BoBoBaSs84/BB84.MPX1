using System.Collections.Immutable;
using System.Collections.Concurrent;

using BB84.MPX1.Core.Programs;
using BB84.MPX1.Core.Protocol;

namespace BB84.MPX1.Midi.Tests;

[TestClass]
public sealed class Mpx1SessionTests
{
    private static readonly DeviceId Device = new(0);
    private static readonly IEnumerable<(TimeSpan, Mpx1Message)> Nothing = [];

    private static readonly Mpx1SessionOptions FastOptions = new()
    {
        RequestTimeout = TimeSpan.FromMilliseconds(150),
        Retries = 2,
        ReadyTimeout = TimeSpan.FromMilliseconds(1000),
        BusyGracePeriod = TimeSpan.FromMilliseconds(100),
    };

    public TestContext TestContext { get; set; } = null!;

    private static async Task<(Mpx1Session Session, FakeDevice Device, LoopbackMidiTransport Wire)> CreateAsync(
        Func<Mpx1Message, IEnumerable<(TimeSpan, Mpx1Message)>> responder, Mpx1SessionOptions? options = null)
    {
        (LoopbackMidiTransport left, LoopbackMidiTransport right) = LoopbackMidiTransport.CreatePair();
        await left.OpenAsync(LoopbackMidiTransport.Port.Id, LoopbackMidiTransport.Port.Id);
        FakeDevice device = new(right, responder);
        Mpx1Session session = new(left, Device, options ?? FastOptions);
        session.Start();
        return (session, device, left);
    }

    private static IEnumerable<(TimeSpan, Mpx1Message)> Reply(Mpx1Message message) => [(TimeSpan.Zero, message)];

    [TestMethod]
    [Description("SES-002: response matched by class")]
    public async Task RequestAsync_ParameterType_ReturnsResponse()
    {
        var (session, device, _) = await CreateAsync(m => m is Request { RequestedClass: MessageClass.ParameterType }
            ? Reply(new ParameterType(Device, 0x014D))
            : Nothing);
        await using (session)
        await using (device)
        {
            ParameterType type = await session.RequestAsync<ParameterType>(Request.ParameterType(Device, new ControlAddress(0, 0)), TestContext.CancellationToken);
            Assert.AreEqual((ushort)0x014D, type.Type);
        }
    }

    [TestMethod]
    [Description("SES-002, SES-009: a response for another address is not accepted and is published as unsolicited")]
    public async Task RequestAsync_IgnoresResponseForOtherAddress()
    {
        ControlAddress wanted = new(0, 2, 1, 2);
        ControlAddress other = new(0, 2, 1, 3);
        var (session, device, _) = await CreateAsync(m => m is Request
            ? [(TimeSpan.Zero, new ParameterData(Device, new byte[] { 9 }, other)), (TimeSpan.Zero, new ParameterData(Device, new byte[] { 5 }, wanted))]
            : Nothing);
        ConcurrentBag<Mpx1Message> unsolicited = [];
        session.UnsolicitedMessage += (_, e) => unsolicited.Add(e.Message);
        await using (session)
        await using (device)
        {
            ParameterData data = await session.RequestAsync<ParameterData>(Request.ParameterData(Device, wanted), TestContext.CancellationToken);

            Assert.AreEqual((byte)5, data.Data.Span[0]);
            Assert.Contains(m => m is ParameterData p && p.Address == other, unsolicited);
        }
    }

    [TestMethod]
    [Description("AC-012, SES-003: two dropped responses, third attempt succeeds, exactly three requests on the wire")]
    public async Task RequestAsync_RetriesAfterTimeout()
    {
        int requests = 0;
        var (session, device, _) = await CreateAsync(m =>
            m is Request && Interlocked.Increment(ref requests) == 3
                ? Reply(new SystemConfiguration(Device, 1, 10, "", "", 0x01C0, 0x0164, 5))
                : Nothing);
        await using (session)
        await using (device)
        {
            SystemConfiguration config = await session.RequestAsync<SystemConfiguration>(Request.SystemConfiguration(Device), TestContext.CancellationToken);

            Assert.AreEqual((byte)10, config.MinorVersion);
            Assert.AreEqual(3, Volatile.Read(ref requests));
        }
    }

    [TestMethod]
    [Description("SES-003: typed timeout error including the request bytes")]
    public async Task RequestAsync_NoResponse_ThrowsTimeout()
    {
        var (session, device, _) = await CreateAsync(_ => Nothing);
        await using (session)
        await using (device)
        {
            Request request = Request.SystemConfiguration(Device);
            Mpx1TimeoutException ex = await Assert.ThrowsExactlyAsync<Mpx1TimeoutException>(
                () => session.RequestAsync<SystemConfiguration>(request, TestContext.CancellationToken));

            Assert.AreEqual(3, ex.Attempts);
            CollectionAssert.AreEqual(request.ToBytes(), ex.Frame.ToArray());
        }
    }

    [TestMethod]
    [Description("AC-011, SES-005: second dump is not sent until READY")]
    public async Task SendAsync_ProgramDump_WaitsForReady()
    {
        TimeSpan busyFor = TimeSpan.FromMilliseconds(300);
        ConcurrentQueue<DateTimeOffset> readySent = new();
        var (session, device, _) = await CreateAsync(m => m is ProgramDump
            ? [(TimeSpan.Zero, new Handshake(Device, HandshakeCommand.Busy)), (busyFor, new Handshake(Device, HandshakeCommand.Ready))]
            : Nothing);
        session.FrameTraffic += (_, e) =>
        {
            if (e.Message is Handshake { Command: HandshakeCommand.Ready })
                readySent.Enqueue(e.Timestamp);
        };
        await using (session)
        await using (device)
        {
            ProgramDump dump = new(Device, 200, TestProgram());

            await Task.WhenAll(session.SendAsync(dump, TestContext.CancellationToken), session.SendAsync(dump with { ProgramNumber = 201 }, TestContext.CancellationToken));

            (DateTimeOffset Time, Mpx1Message Message)[] received = [.. device.Received];
            Assert.HasCount(2, received);
            Assert.IsTrue(readySent.TryPeek(out DateTimeOffset firstReady));
            Assert.IsGreaterThanOrEqualTo(firstReady - TimeSpan.FromMilliseconds(20), received[1].Time,
                "The second dump arrived before the device sent READY.");
        }
    }

    [TestMethod]
    [Description("SES-005: missing READY raises a timeout and unblocks later sends")]
    public async Task SendAsync_MissingReady_Throws()
    {
        var (session, device, _) = await CreateAsync(m => m is ProgramDump ? Reply(new Handshake(Device, HandshakeCommand.Busy)) : Nothing,
            FastOptions with { ReadyTimeout = TimeSpan.FromMilliseconds(200) });
        await using (session)
        await using (device)
        {
            await Assert.ThrowsExactlyAsync<Mpx1TimeoutException>(() => session.SendAsync(new ProgramDump(Device, 200, TestProgram()), TestContext.CancellationToken));
            Assert.IsFalse(session.IsDeviceBusy);
        }
    }

    [TestMethod]
    [Description("SES-006")]
    public async Task PingAsync_ReturnsTrueWhenDeviceAnswers()
    {
        var (session, device, _) = await CreateAsync(m => m is Handshake { Command: HandshakeCommand.AreYouThere }
            ? Reply(new Handshake(Device, HandshakeCommand.ImAlive))
            : Nothing);
        await using (session)
        await using (device)
            Assert.IsTrue(await session.PingAsync(TestContext.CancellationToken));
    }

    [TestMethod]
    [Description("SES-006")]
    public async Task PingAsync_ReturnsFalseWithoutDevice()
    {
        var (session, device, _) = await CreateAsync(_ => Nothing, FastOptions with { Retries = 0 });
        await using (session)
        await using (device)
            Assert.IsFalse(await session.PingAsync(TestContext.CancellationToken));
    }

    [TestMethod]
    [Description("SES-007: Device Inquiry first, then System Configuration")]
    public async Task DetectVersionAsync_UsesInquiryAndConfiguration()
    {
        var (session, device, _) = await CreateAsync(m => m switch
        {
            DeviceInquiry => Reply(new IdentityReply(Device, 1, 10, 0)),
            Request { RequestedClass: MessageClass.SystemConfiguration } => Reply(new SystemConfiguration(Device, 1, 10, "17:51:03", "May 10 1996", 0x01C0, 0x0164, 5)),
            _ => Nothing,
        });
        await using (session)
        await using (device)
        {
            DeviceVersion version = await session.DetectVersionAsync(TestContext.CancellationToken);

            Assert.AreEqual("V1.10", version.ToString());
            Assert.IsNotNull(version.Identity);
            Assert.IsInstanceOfType<DeviceInquiry>(device.Received.First().Message);
        }
    }

    [TestMethod]
    [Description("SES-004: outgoing requests are serialized; the second waits until the first is answered")]
    public async Task RequestAsync_ConcurrentRequests_AreSerialized()
    {
        TaskCompletionSource firstSeen = new(TaskCreationOptions.RunContinuationsAsynchronously);
        (Mpx1Session session, FakeDevice device, _) = await CreateAsync(m =>
        {
            if (m is not Request { RequestedClass: MessageClass.ParameterType } request)
                return Nothing;
            ControlAddress address = request.GetControlAddress();
            if (address == new ControlAddress(0))
                firstSeen.TrySetResult();
            // Answer the first request late, the second at once.
            return [(address == new ControlAddress(0) ? TimeSpan.FromMilliseconds(80) : TimeSpan.Zero, new ParameterType(Device, (ushort)(address[0] + 1)))];
        });
        await using (session)
        await using (device)
        {
            Task<ParameterType> first = session.RequestAsync<ParameterType>(Request.ParameterType(Device, new ControlAddress(0)), TestContext.CancellationToken);
            await firstSeen.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.CancellationToken);
            Task<ParameterType> second = session.RequestAsync<ParameterType>(Request.ParameterType(Device, new ControlAddress(1)), TestContext.CancellationToken);

            Assert.AreEqual((ushort)1, (await first).Type);
            Assert.AreEqual((ushort)2, (await second).Type);
            DateTimeOffset[] times = [.. device.Received.Where(r => r.Message is Request).Select(r => r.Time)];
            Assert.HasCount(2, times);
            Assert.IsGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(60), times[1] - times[0], "The second request is sent only after the first answer.");
        }
    }

    [TestMethod]
    [Description("SES-008: minimum gap between outgoing frames")]
    public async Task SendAsync_HonoursInterFrameGap()
    {
        var (session, device, _) = await CreateAsync(_ => Nothing, FastOptions with { InterFrameGap = TimeSpan.FromMilliseconds(100) });
        await using (session)
        await using (device)
        {
            await session.SendAsync(new Handshake(Device, HandshakeCommand.Nop), TestContext.CancellationToken);
            await session.SendAsync(new Handshake(Device, HandshakeCommand.Nop), TestContext.CancellationToken);
            await Task.Delay(50, TestContext.CancellationToken);

            (DateTimeOffset Time, Mpx1Message _)[] received = [.. device.Received];
            Assert.HasCount(2, received);
            Assert.IsGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(90), received[1].Time - received[0].Time);
        }
    }

    [TestMethod]
    [Description("PRO-026, UI-009: undecodable frames are reported, not thrown")]
    public async Task ReceiveLoop_ReportsMalformedFrames()
    {
        var (session, device, _) = await CreateAsync(_ => Nothing);
        TaskCompletionSource<FrameEventArgs> rejected = new(TaskCreationOptions.RunContinuationsAsynchronously);
        session.FrameTraffic += (_, e) =>
        {
            if (e.Error is not null)
                rejected.TrySetResult(e);
        };
        await using (session)
        await using (device)
        {
            await device.SendAsync(new RawLexiconMessage(Device, (byte)MessageClass.ParameterType, new byte[] { 0x10 }));

            FrameEventArgs args = await rejected.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.CancellationToken);
            Assert.AreEqual(FrameDirection.Received, args.Direction);
            Assert.IsNull(args.Message);
        }
    }

    [TestMethod]
    [Description("SES-001, SES-002: matcher keys; responses from another device ID are not accepted")]
    public void ResponseMatcher_Keys()
    {
        Func<Mpx1Message, bool> description = ResponseMatcher.For(Request.ParameterDescription(Device, 0x0125));
        Assert.IsTrue(description(new ParameterDescription(Device, 0x0125, "x", 1, ParameterAttributes.None, null, [])));
        Assert.IsFalse(description(new ParameterDescription(Device, 0x0126, "x", 1, ParameterAttributes.None, null, [])));
        Assert.IsFalse(description(new ParameterDescription(new DeviceId(1), 0x0125, "x", 1, ParameterAttributes.None, null, [])));

        Func<Mpx1Message, bool> program = ResponseMatcher.For(Request.ProgramDump(DeviceId.All, 7));
        Assert.IsTrue(program(new ProgramDump(new DeviceId(5), 7, TestProgram())));
        Assert.IsFalse(program(new ProgramDump(new DeviceId(5), 8, TestProgram())));

        Func<Mpx1Message, bool> lfo = ResponseMatcher.For(Request.EffectParameters(Device, EffectType.Lfo1, 99));
        Assert.IsTrue(lfo(new EffectParameters(Device, EffectType.Lfo1, 0, new EffectParameterList("LFO", 0, [.. new ushort[30]]))));
    }

    private static ProgramData TestProgram() => new()
    {
        EffectData = [.. Enumerable.Repeat(new byte[ProgramData.EffectDataSize].ToImmutableArray(), ProgramData.EffectBlockCount)],
        Categories = default,
        Routing = [.. new RoutingBlock[ProgramData.RoutingBlockCount]],
        Algorithms = [0, 0, 0, 0, 0, 0],
        Name = "Test",
        EffectStatus = EffectStatus.None,
        SoftValues = [.. new SoftValue[ProgramData.SoftValueCount]],
        Tempo = 120,
        TempoSource = 0,
        BeatValue = 2,
        TapSource = 0,
        TapAverage = 2,
        TapSourceLevel = 64,
        Meter = 0,
        MasterLevel = 0,
        MasterMix = 100,
        Patches = [.. new Patch[ProgramData.PatchCount]],
        Knob = new Knob(0, 0, 127, "Knob"),
        ModulatorData = [.. new byte[ProgramData.ModulatorDataSize]],
    };
}
