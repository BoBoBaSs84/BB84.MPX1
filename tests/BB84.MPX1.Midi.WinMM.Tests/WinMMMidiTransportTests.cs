using BB84.MPX1.Core.Protocol;

namespace BB84.MPX1.Midi.WinMM.Tests;

[TestClass]
public sealed class WinMMMidiTransportTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [Description("TRN-006: duplicate device names get stable, unique IDs")]
    public void ToPorts_MakesDuplicateNamesUnique()
    {
        IReadOnlyList<MidiPortInfo> ports = WinMMMidiTransport.ToPorts(["USB MIDI", "Synth", "USB MIDI", "USB MIDI"]);

        CollectionAssert.AreEqual(
            new[] { "USB MIDI", "Synth", "USB MIDI (2)", "USB MIDI (3)" },
            ports.Select(p => p.Id.Value).ToArray());
    }

    [TestMethod]
    [Description("TRN-002, TRN-006: enumeration works with any number of devices")]
    public async Task GetPorts_DoesNotThrow()
    {
        await using WinMMMidiTransport transport = new();

        IReadOnlyList<MidiPortInfo> inputs = transport.GetInputPorts();
        IReadOnlyList<MidiPortInfo> outputs = transport.GetOutputPorts();

        TestContext.WriteLine($"Inputs: {string.Join(", ", inputs.Select(p => p.Name))}");
        TestContext.WriteLine($"Outputs: {string.Join(", ", outputs.Select(p => p.Name))}");
        Assert.IsNotNull(inputs);
        Assert.IsNotNull(outputs);
    }

    [TestMethod]
    [Description("TRN-002: open a port pair and send a SysEx frame (needs at least one input and one output)")]
    public async Task OpenAndSendSysEx_WhenPortsExist()
    {
        await using WinMMMidiTransport transport = new();
        IReadOnlyList<MidiPortInfo> inputs = transport.GetInputPorts();
        IReadOnlyList<MidiPortInfo> outputs = transport.GetOutputPorts();
        if (inputs.Count == 0 || outputs.Count == 0)
            Assert.Inconclusive("No MIDI input/output ports on this machine.");

        await transport.OpenAsync(inputs[0].Id, outputs[0].Id, TestContext.CancellationToken);
        Assert.IsTrue(transport.IsOpen);

        // Handshake NOP to device 0x7F: harmless for any receiver.
        await transport.SendSysExAsync(new byte[] { 0xF0, 0x06, 0x09, 0x7F, 0x12, 0x00, 0x00, 0xF7 }, TestContext.CancellationToken);

        await transport.CloseAsync(TestContext.CancellationToken);
        Assert.IsFalse(transport.IsOpen);
    }

    // The tests below need a virtual loopback port (loopMIDI, port name "loopMIDI Port"): whatever is sent to
    // its output comes back on its input.
    private const string LoopbackPort = "loopMIDI Port";

    private static async Task<WinMMMidiTransport> OpenLoopbackAsync(CancellationToken cancellationToken)
    {
        WinMMMidiTransport transport = new();
        MidiPortInfo? input = transport.GetInputPorts().FirstOrDefault(p => p.Name == LoopbackPort);
        MidiPortInfo? output = transport.GetOutputPorts().FirstOrDefault(p => p.Name == LoopbackPort);
        if (input is null || output is null)
        {
            string found = string.Join(", ", transport.GetInputPorts().Select(p => $"'{p.Name}'"));
            await transport.DisposeAsync();
            Assert.Inconclusive($"No '{LoopbackPort}' (loopMIDI) port on this machine. Input ports: {found}.");
        }

        await transport.OpenAsync(input!.Id, output!.Id, cancellationToken);
        return transport;
    }

    private async Task<byte[]> ReceiveSysExAsync(WinMMMidiTransport transport)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        await foreach (MidiInput input in transport.ReadAllAsync(timeout.Token))
        {
            if (input is SysExInput sysEx)
                return sysEx.Frame.ToArray();
        }

        throw new InvalidOperationException("The transport closed before a frame arrived.");
    }

    private static byte[] Frame(int payloadLength)
    {
        byte[] frame = new byte[payloadLength + 2];
        frame[0] = 0xF0;
        for (int i = 1; i <= payloadLength; i++)
            frame[i] = (byte)(i % 0x80);
        frame[^1] = 0xF7;
        return frame;
    }

    [TestMethod]
    [TestCategory("LoopMidi")]
    [Description("TRN-002, TRN-003: a SysEx frame sent to the loopback port is received unchanged")]
    public async Task Loopback_RoundTripsFrame()
    {
        await using WinMMMidiTransport transport = await OpenLoopbackAsync(TestContext.CancellationToken);
        byte[] frame = Frame(40);

        await transport.SendSysExAsync(frame, TestContext.CancellationToken);

        CollectionAssert.AreEqual(frame, await ReceiveSysExAsync(transport));
    }

    [TestMethod]
    [TestCategory("LoopMidi")]
    [Description("TRN-002, PRO-017: an encoded protocol message survives the wire and decodes to the same bytes")]
    public async Task Loopback_ProtocolMessage_DecodesToSameBytes()
    {
        await using WinMMMidiTransport transport = await OpenLoopbackAsync(TestContext.CancellationToken);
        byte[] frame = Mpx1Codec.Encode(new Handshake(DeviceId.All, HandshakeCommand.ImAlive));

        await transport.SendSysExAsync(frame, TestContext.CancellationToken);

        byte[] received = await ReceiveSysExAsync(transport);
        CollectionAssert.AreEqual(frame, Mpx1Codec.Encode(Mpx1Codec.Decode(received)));
    }

    [TestMethod]
    [TestCategory("LoopMidi")]
    [Description("TRN-002: a short message sent to the loopback port is received unchanged")]
    public async Task Loopback_RoundTripsShortMessage()
    {
        await using WinMMMidiTransport transport = await OpenLoopbackAsync(TestContext.CancellationToken);
        const int noteOn = 0x90 | (0x3C << 8) | (0x7F << 16);

        await transport.SendShortAsync(noteOn, TestContext.CancellationToken);

        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        await foreach (MidiInput input in transport.ReadAllAsync(timeout.Token))
        {
            if (input is ShortInput received)
            {
                Assert.AreEqual(noteOn, received.Message);
                return;
            }
        }
        Assert.Fail("The transport closed before the message arrived.");
    }

    [TestMethod]
    [TestCategory("LoopMidi")]
    [Description("TRN-003: a frame larger than one input buffer is reassembled")]
    public async Task Loopback_ReassemblesFrameLargerThanInputBuffer()
    {
        await using WinMMMidiTransport transport = await OpenLoopbackAsync(TestContext.CancellationToken);
        byte[] frame = Frame(10_000);

        await transport.SendSysExAsync(frame, TestContext.CancellationToken);

        CollectionAssert.AreEqual(frame, await ReceiveSysExAsync(transport));
    }

    [TestMethod]
    [TestCategory("LoopMidi")]
    [Description("TRN-003: many frames in a row all arrive in order")]
    public async Task Loopback_ManyFrames_ArriveInOrder()
    {
        await using WinMMMidiTransport transport = await OpenLoopbackAsync(TestContext.CancellationToken);

        for (int i = 0; i < 50; i++)
        {
            byte[] frame = Frame(100 + i);
            await transport.SendSysExAsync(frame, TestContext.CancellationToken);
            CollectionAssert.AreEqual(frame, await ReceiveSysExAsync(transport));
        }
    }

    [TestMethod]
    [TestCategory("LoopMidi")]
    [Description("TRN-006: opening and closing repeatedly leaves no stale state")]
    public async Task Loopback_ReopenCycles_WorkEveryTime()
    {
        await using WinMMMidiTransport transport = await OpenLoopbackAsync(TestContext.CancellationToken);
        MidiPortInfo input = transport.GetInputPorts().First(p => p.Name == LoopbackPort);
        MidiPortInfo output = transport.GetOutputPorts().First(p => p.Name == LoopbackPort);

        for (int i = 0; i < 10; i++)
        {
            byte[] frame = Frame(20 + i);
            await transport.SendSysExAsync(frame, TestContext.CancellationToken);
            CollectionAssert.AreEqual(frame, await ReceiveSysExAsync(transport));

            await transport.CloseAsync(TestContext.CancellationToken);
            Assert.IsFalse(transport.IsOpen);
            await transport.CloseAsync(TestContext.CancellationToken);

            await transport.OpenAsync(input.Id, output.Id, TestContext.CancellationToken);
            Assert.IsTrue(transport.IsOpen);
        }
    }

    [TestMethod]
    [TestCategory("LoopMidi")]
    [Description("TRN-006: disposing while open closes the ports, and disposing twice is harmless")]
    public async Task Loopback_Dispose_ClosesAndIsIdempotent()
    {
        WinMMMidiTransport transport = await OpenLoopbackAsync(TestContext.CancellationToken);

        await transport.DisposeAsync();
        await transport.DisposeAsync();
        transport.Dispose();

        Assert.IsFalse(transport.IsOpen);

        // The ports are free again.
        await using WinMMMidiTransport again = await OpenLoopbackAsync(TestContext.CancellationToken);
        Assert.IsTrue(again.IsOpen);
    }
}
