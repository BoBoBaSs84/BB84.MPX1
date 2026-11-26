using BB84.MPX1.Core.Protocol;

namespace BB84.MPX1.Midi.Alsa.Tests;

/// <summary>
/// Round trips through the kernel's <c>Midi Through</c> port (snd-seq-dummy), which echoes everything it receives.
/// Without an ALSA sequencer or that port the tests report as inconclusive instead of failing.
/// </summary>
[TestClass]
[OSCondition(OperatingSystems.Linux)]
public sealed class AlsaLoopbackTests
{
    private const string ThroughPrefix = "Midi Through:";
    private static readonly TimeSpan ReceiveTimeout = TimeSpan.FromSeconds(10);

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [TestCategory("Alsa")]
    [Description("TRN-006, TRN-008: enumeration lists the Midi Through port as input and output")]
    public async Task GetPorts_ListsMidiThrough()
    {
        await using AlsaMidiTransport transport = new();
        MidiPortId through = FindThrough(transport);

        TestContext.WriteLine($"Inputs: {string.Join(", ", transport.GetInputPorts().Select(p => p.Name))}");
        TestContext.WriteLine($"Outputs: {string.Join(", ", transport.GetOutputPorts().Select(p => p.Name))}");
        Assert.Contains(through, transport.GetOutputPorts().Select(p => p.Id).ToList());
    }

    [TestMethod]
    [TestCategory("Alsa")]
    [DataRow(6)]
    [DataRow(844)]
    [DataRow(1506)]
    [DataRow(4096)]
    [DataRow(16384)]
    [Description("TRN-004, TRN-008: SysEx frames up to beyond the largest dump size round-trip byte for byte")]
    public async Task Loopback_RoundTripsSysExFrames(int length)
    {
        await using AlsaMidiTransport transport = await OpenThroughAsync();
        await using IAsyncEnumerator<MidiInput> inputs = transport.ReadAllAsync(TestContext.CancellationToken).GetAsyncEnumerator(TestContext.CancellationToken);
        byte[] frame = AlsaMidiTransportTests.Frame(length);

        await transport.SendSysExAsync(frame, TestContext.CancellationToken);

        Assert.AreSequenceEqual(frame, (await NextAsync<SysExInput>(inputs)).Frame.ToArray());
    }

    [TestMethod]
    [TestCategory("Alsa")]
    [Description("TRN-005, TRN-008: short messages round-trip separately from SysEx")]
    public async Task Loopback_RoundTripsShortMessages()
    {
        await using AlsaMidiTransport transport = await OpenThroughAsync();
        await using IAsyncEnumerator<MidiInput> inputs = transport.ReadAllAsync(TestContext.CancellationToken).GetAsyncEnumerator(TestContext.CancellationToken);

        await transport.SendShortAsync(0x7F4090, TestContext.CancellationToken);
        await transport.SendShortAsync(0x05C0, TestContext.CancellationToken);

        Assert.AreEqual(0x7F4090, (await NextAsync<ShortInput>(inputs)).Message);
        Assert.AreEqual(0x05C0, (await NextAsync<ShortInput>(inputs)).Message);
    }

    [TestMethod]
    [TestCategory("Alsa")]
    [Description("TRN-006, TRN-008: open is idempotent and reopening works every time")]
    public async Task Loopback_ReopenCycles_WorkEveryTime()
    {
        await using AlsaMidiTransport transport = new();
        MidiPortId through = FindThrough(transport);
        byte[] frame = Mpx1Codec.Encode(new Handshake(DeviceId.All, HandshakeCommand.ImAlive));

        for (int cycle = 0; cycle < 5; cycle++)
        {
            await transport.OpenAsync(through, through, TestContext.CancellationToken);
            await transport.OpenAsync(through, through, TestContext.CancellationToken);
            await using IAsyncEnumerator<MidiInput> inputs = transport.ReadAllAsync(TestContext.CancellationToken).GetAsyncEnumerator(TestContext.CancellationToken);

            await transport.SendSysExAsync(frame, TestContext.CancellationToken);

            Assert.AreSequenceEqual(frame, (await NextAsync<SysExInput>(inputs)).Frame.ToArray(), $"cycle {cycle}");
            await transport.CloseAsync(TestContext.CancellationToken);
            Assert.IsFalse(transport.IsOpen);
        }
    }

    [TestMethod]
    [TestCategory("Alsa")]
    [Description("TRN-006, TRN-008: disposing ends a pending read")]
    public async Task Dispose_EndsPendingRead()
    {
        AlsaMidiTransport transport = await OpenThroughAsync();
        Task<int> reading = CountAsync(transport.ReadAllAsync(TestContext.CancellationToken));

        await transport.DisposeAsync();

        Assert.AreEqual(0, await reading.WaitAsync(ReceiveTimeout, TestContext.CancellationToken));
    }

    private async Task<AlsaMidiTransport> OpenThroughAsync()
    {
        AlsaMidiTransport transport = new();
        try
        {
            MidiPortId through = FindThrough(transport);
            await transport.OpenAsync(through, through, TestContext.CancellationToken);
            return transport;
        }
        catch
        {
            await transport.DisposeAsync();
            throw;
        }
    }

    private static MidiPortId FindThrough(AlsaMidiTransport transport)
    {
        IReadOnlyList<MidiPortInfo> inputs = [];
        try
        {
            inputs = transport.GetInputPorts();
        }
        catch (Exception ex) when (ex is AlsaException or DllNotFoundException)
        {
            Assert.Inconclusive($"No ALSA sequencer: {ex.Message}");
        }
        MidiPortInfo? through = inputs.FirstOrDefault(p => p.Id.Value.StartsWith(ThroughPrefix, StringComparison.Ordinal));
        if (through is null)
            Assert.Inconclusive("No 'Midi Through' port; load the snd-seq-dummy kernel module.");
        return through.Id;
    }

    private static async Task<T> NextAsync<T>(IAsyncEnumerator<MidiInput> inputs) where T : MidiInput
    {
        while (await inputs.MoveNextAsync().AsTask().WaitAsync(ReceiveTimeout))
        {
            if (inputs.Current is T match)
                return match;
        }
        throw new AssertFailedException($"The input ended before a {typeof(T).Name} arrived.");
    }

    private static async Task<int> CountAsync(IAsyncEnumerable<MidiInput> inputs)
    {
        int count = 0;
        await foreach (MidiInput _ in inputs)
            count++;
        return count;
    }
}
