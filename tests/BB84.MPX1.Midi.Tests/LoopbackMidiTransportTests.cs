namespace BB84.MPX1.Midi.Tests;

[TestClass]
public sealed class LoopbackMidiTransportTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [Description("TRN-005, TRN-006, TRN-007: open/close idempotent, SysEx frames and short messages delivered separately to the peer")]
    public async Task Pair_DeliversFramesAndShortMessages()
    {
        (LoopbackMidiTransport left, LoopbackMidiTransport right) = LoopbackMidiTransport.CreatePair();
        await using (left)
        await using (right)
        {
            await left.OpenAsync(LoopbackMidiTransport.Port.Id, LoopbackMidiTransport.Port.Id, TestContext.CancellationToken);
            await left.OpenAsync(LoopbackMidiTransport.Port.Id, LoopbackMidiTransport.Port.Id, TestContext.CancellationToken);
            await left.SendSysExAsync(new byte[] { 0xF0, 0x01, 0xF7 }, TestContext.CancellationToken);
            await left.SendShortAsync(0x00403C90, TestContext.CancellationToken);

            await using IAsyncEnumerator<MidiInput> reader = right.ReadAllAsync(TestContext.CancellationToken).GetAsyncEnumerator(TestContext.CancellationToken);
            Assert.IsTrue(await reader.MoveNextAsync());
            Assert.IsInstanceOfType<SysExInput>(reader.Current);
            Assert.IsTrue(await reader.MoveNextAsync());
            Assert.AreEqual(0x00403C90, ((ShortInput)reader.Current).Message);

            await left.CloseAsync(TestContext.CancellationToken);
            await left.CloseAsync(TestContext.CancellationToken);
            Assert.IsFalse(left.IsOpen);
        }
    }

    [TestMethod]
    public async Task Send_WhenClosed_Throws()
    {
        (LoopbackMidiTransport left, LoopbackMidiTransport right) = LoopbackMidiTransport.CreatePair();
        await using (left)
        await using (right)
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () => await left.SendSysExAsync(new byte[] { 0xF0, 0xF7 }, TestContext.CancellationToken));
    }
}
