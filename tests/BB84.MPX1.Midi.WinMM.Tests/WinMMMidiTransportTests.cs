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
}
