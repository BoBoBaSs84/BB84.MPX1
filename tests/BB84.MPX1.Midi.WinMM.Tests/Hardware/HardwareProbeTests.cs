using System.Globalization;

using BB84.MPX1.Core.Protocol;

namespace BB84.MPX1.Midi.WinMM.Tests.Hardware;

/// <summary>
/// Diagnostics against a real MPX 1: sends one raw request and logs every frame and short message that comes back.
/// </summary>
/// <remarks>
/// <c>MPX1_PROBE_PROGRAM</c> selects the protocol program number (default 203).
/// </remarks>
[TestClass]
[TestCategory("Hardware")]
[DoNotParallelize]
public sealed class HardwareProbeTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [Description("PRG-007, PRG-001: raw Program Information and Program Dump answers for one program")]
    public async Task Probe_Program()
    {
        ushort number = ushort.Parse(Environment.GetEnvironmentVariable("MPX1_PROBE_PROGRAM") ?? "203", CultureInfo.InvariantCulture);
        await using HardwareRig rig = await HardwareRig.OpenAsync(TestContext);

        int info = await ProbeAsync(rig, Request.ProgramInformation(rig.Device, number), TimeSpan.FromSeconds(3));
        int dump = await ProbeAsync(rig, Request.ProgramDump(rig.Device, number), TimeSpan.FromSeconds(5));

        Assert.IsGreaterThan(0, info + dump, "No answer at all.");
    }

    private static string Describe(Mpx1Message message) => message switch
    {
        ProgramInformation info => $"ProgramInformation \"{info.Name}\" algorithms {string.Join(' ', info.Algorithms)}",
        ProgramDump dump => $"ProgramDump \"{dump.Program.Name}\"",
        _ => message.GetType().Name,
    };

    private async Task<int> ProbeAsync(HardwareRig rig, Request request, TimeSpan window)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
        timeout.CancelAfter(window);
        int count = 0;
        Task listen = Task.Run(async () =>
        {
            try
            {
                await foreach (MidiInput input in rig.Transport.ReadAllAsync(timeout.Token))
                {
                    count++;
                    if (input is SysExInput sysEx)
                    {
                        byte[] frame = sysEx.Frame.ToArray();
                        string decoded = Mpx1Codec.TryDecode(frame, out Mpx1Message? message, out Mpx1FormatError? error) ? Describe(message!) : $"undecodable: {error}";
                        rig.Log($"  in: {frame.Length} bytes, {decoded}, starts {Convert.ToHexString(frame.AsSpan(0, Math.Min(frame.Length, 24)))}, ends {Convert.ToHexString(frame.AsSpan(Math.Max(0, frame.Length - 8)))}");
                    }
                    else
                    {
                        rig.Log($"  in: {input}");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // The window ended.
            }
        }, TestContext.CancellationToken);

        rig.Log($"out: {request.RequestedClass} {Convert.ToHexString(request.ToBytes())}");
        await rig.Transport.SendSysExAsync(request.ToBytes(), TestContext.CancellationToken);
        await listen;
        rig.Log($"  {count} input(s) in {window.TotalSeconds} s.");
        return count;
    }
}
