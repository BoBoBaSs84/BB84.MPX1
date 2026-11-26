using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using static BB84.MPX1.Midi.Alsa.NativeMethods;

namespace BB84.MPX1.Midi.Alsa.Tests;

/// <summary>Tests of the ALSA adapter that make no native calls, so they run on every OS.</summary>
[TestClass]
public sealed class AlsaMidiTransportTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [Description("TRN-008: the event struct matches snd_seq_event_t on Linux (28 bytes, packed ext data at offset 16)")]
    public void SndSeqEvent_MatchesNativeLayout()
    {
        Assert.AreEqual(28, Unsafe.SizeOf<SndSeqEvent>());
        Assert.AreEqual(4, (int)Marshal.OffsetOf<SndSeqEvent>(nameof(SndSeqEvent.Time)));
        Assert.AreEqual(12, (int)Marshal.OffsetOf<SndSeqEvent>(nameof(SndSeqEvent.SourceClient)));
        Assert.AreEqual(14, (int)Marshal.OffsetOf<SndSeqEvent>(nameof(SndSeqEvent.DestClient)));
        Assert.AreEqual(16, (int)Marshal.OffsetOf<SndSeqEvent>(nameof(SndSeqEvent.ExtLength)));
        Assert.AreEqual(20, (int)Marshal.OffsetOf<SndSeqEvent>(nameof(SndSeqEvent.ExtPointer)));
    }

    [TestMethod]
    [Description("TRN-008: client and port names form stable, unique port IDs (TRN-006)")]
    public void ToPorts_JoinsClientAndPortNamesAndMakesThemUnique()
    {
        IReadOnlyList<MidiPortInfo> ports = AlsaMidiTransport.ToPorts([("USB MIDI", "Port 1"), ("Midi Through", "Midi Through Port-0"), ("USB MIDI", "Port 1")]);

        Assert.AreSequenceEqual(
            new[] { "USB MIDI:Port 1", "Midi Through:Midi Through Port-0", "USB MIDI:Port 1 (2)" }, ports.Select(p => p.Id.Value).ToArray());
    }

    [TestMethod]
    [DataRow(8192, 32)]
    [DataRow(AlsaMidiTransport.SysExChunkSize, 1)]
    [DataRow(AlsaMidiTransport.SysExChunkSize + 1, 2)]
    [DataRow(6, 1)]
    [Description("TRN-008: outgoing SysEx is split into sequencer-sized chunks that rejoin to the frame")]
    public void Chunk_SplitsFrameIntoPiecesThatRejoin(int length, int expectedChunks)
    {
        byte[] frame = Frame(length);

        ReadOnlyMemory<byte>[] chunks = [.. AlsaMidiTransport.Chunk(frame, AlsaMidiTransport.SysExChunkSize)];

        Assert.HasCount(expectedChunks, chunks);
        Assert.IsTrue(chunks.All(c => c.Length is > 0 and <= AlsaMidiTransport.SysExChunkSize));
        Assert.AreSequenceEqual(frame, chunks.SelectMany(c => c.ToArray()).ToArray());
    }

    [TestMethod]
    [Description("TRN-004, TRN-008: SysEx arriving in several sequencer events is reassembled; clock and active sensing are dropped")]
    public async Task Input_ReassemblesChunksAndDropsRealTimeMessages()
    {
        await using AlsaMidiTransport transport = new();
        byte[] frame = Frame(600);

        transport.OnSysEx(frame.AsSpan(0, 256));
        transport.OnShort([0xF8]);
        transport.OnSysEx(frame.AsSpan(256, 256));
        transport.OnShort([0xFE]);
        transport.OnSysEx(frame.AsSpan(512));
        transport.OnShort([0x90, 0x40, 0x7F]);
        transport.OnShort([0xC0, 0x05]);
        await transport.CloseAsync(TestContext.CancellationToken);

        List<MidiInput> received = [];
        await foreach (MidiInput input in transport.ReadAllAsync(TestContext.CancellationToken))
            received.Add(input);

        Assert.HasCount(3, received);
        Assert.AreSequenceEqual(frame, ((SysExInput)received[0]).Frame.ToArray());
        Assert.AreEqual(0x7F4090, ((ShortInput)received[1]).Message);
        Assert.AreEqual(0x05C0, ((ShortInput)received[2]).Message);
    }

    [TestMethod]
    [Description("TRN-006, TRN-008: sending before open fails clearly; close and dispose are idempotent")]
    public async Task UnopenedTransport_RefusesSendsAndClosesCleanly()
    {
        AlsaMidiTransport transport = new();

        Assert.IsFalse(transport.IsOpen);
        InvalidOperationException error = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            async () => await transport.SendSysExAsync(Frame(6), TestContext.CancellationToken));
        Assert.AreEqual("The transport is not open.", error.Message);
        await Assert.ThrowsExactlyAsync<ArgumentException>(
            async () => await transport.SendSysExAsync(new byte[] { 0x01, 0x02 }, TestContext.CancellationToken));

        await transport.CloseAsync(TestContext.CancellationToken);
        await transport.CloseAsync(TestContext.CancellationToken);
        await transport.DisposeAsync();
        await transport.DisposeAsync();
        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(
            async () => await transport.OpenAsync(new MidiPortId("a"), new MidiPortId("b"), TestContext.CancellationToken));
    }

    /// <summary>Builds a SysEx frame of the given length with a counting payload.</summary>
    internal static byte[] Frame(int length)
    {
        byte[] frame = new byte[length];
        frame[0] = 0xF0;
        for (int i = 1; i < length - 1; i++)
            frame[i] = (byte)(i & 0x7F);
        frame[^1] = 0xF7;
        return frame;
    }
}
