using BB84.MPX1.Core.Protocol;
using BB84.MPX1.Midi;
using BB84.MPX1.Simulator.Recording;

namespace BB84.MPX1.Simulator.Tests;

[TestClass]
public sealed class RecordReplayTests
{
    private static readonly DeviceId Device = new(0);

    public TestContext TestContext { get; set; } = null!;

    private static async Task<List<Mpx1Message>> RunScriptAsync(IMidiTransport transport, CancellationToken ct)
    {
        await transport.OpenAsync(new MidiPortId("x"), new MidiPortId("x"), ct);
        await using Mpx1Session session = new(transport, Device, new Mpx1SessionOptions { RequestTimeout = TimeSpan.FromMilliseconds(500), Retries = 0 });
        session.Start();

        return
        [
            await session.RequestAsync<SystemConfiguration>(Request.SystemConfiguration(Device), ct),
            await session.RequestAsync<ParameterType>(Request.ParameterType(Device, ControlAddress.Root), ct),
            await session.RequestAsync<ParameterDescription>(Request.ParameterDescription(Device, 0x0163), ct),
            await session.RequestAsync<ProgramDump>(Request.ProgramDump(Device, 3), ct),
        ];
    }

    [TestMethod]
    [Description("TRN-007, M3 exit: a recorded session replays deterministically")]
    public async Task RecordedSession_ReplaysIdentically()
    {
        CancellationToken ct = TestContext.CancellationToken;
        StringWriter log = new();
        List<Mpx1Message> live;
        await using (RecordingMidiTransport recorder = new(new Mpx1Simulator(), log))
            live = await RunScriptAsync(recorder, ct);

        IReadOnlyList<RecordedFrame> frames = RecordedFrame.ReadAll(new StringReader(log.ToString()));
        Assert.HasCount(8, frames);
        Assert.AreEqual(RecordedFrame.Out, frames[0].Dir);
        Assert.AreEqual(RecordedFrame.In, frames[1].Dir);

        await using ReplayMidiTransport replay = new(frames);
        List<Mpx1Message> replayed = await RunScriptAsync(replay, ct);

        Assert.IsTrue(replay.IsComplete);
        CollectionAssert.AreEqual(
            live.Select(m => Convert.ToHexString(m.ToBytes())).ToArray(),
            replayed.Select(m => Convert.ToHexString(m.ToBytes())).ToArray());
    }

    [TestMethod]
    [Description("TRN-007: strict replay rejects unexpected frames")]
    public async Task StrictReplay_RejectsMismatch()
    {
        RecordedFrame[] frames = [new(0, RecordedFrame.Out, "F0060900120100F7")];
        await using ReplayMidiTransport replay = new(frames);
        await replay.OpenAsync(ReplayMidiTransport.Port.Id, ReplayMidiTransport.Port.Id, TestContext.CancellationToken);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () => await replay.SendSysExAsync(new byte[] { 0xF0, 0x06, 0x09, 0x00, 0x12, 0x02, 0x00, 0xF7 }, TestContext.CancellationToken));
    }

    [TestMethod]
    public void RecordedFrame_JsonLineFormat()
    {
        RecordedFrame frame = new(12, RecordedFrame.In, "F0F7");
        Assert.AreEqual("{\"t\":12,\"dir\":\"in\",\"hex\":\"F0F7\"}", frame.ToJsonLine());
    }
}
