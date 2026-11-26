using BB84.MPX1.Core.Protocol;

namespace BB84.MPX1.Midi.WinMM.Tests.Hardware;

/// <summary>
/// Smoke tests against a real MPX 1 (roadmap M8). Excluded from normal runs with
/// <c>--filter "TestCategory!=Hardware"</c>; see <see cref="HardwareRig"/> for the configuration.
/// </summary>
[TestClass]
[OSCondition(OperatingSystems.Windows)]
[TestCategory("Hardware")]
[DoNotParallelize]
public sealed class HardwareSmokeTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [Description("PRO-024, TRN-002: a broadcast Device Inquiry is answered with the MPX 1 identity")]
    public async Task DeviceInquiry_Broadcast_IsAnswered()
    {
        await using HardwareRig rig = await HardwareRig.OpenAsync(TestContext);
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        Task<List<byte[]>> received = CollectSysExAsync(rig.Transport, timeout.Token);

        await rig.Transport.SendSysExAsync(new DeviceInquiry(DeviceId.All).ToBytes(), TestContext.CancellationToken);

        List<byte[]> frames = await received;
        foreach (byte[] frame in frames)
            rig.Log($"Received: {Convert.ToHexString(frame)}");
        IdentityReply? identity = frames
            .Select(f => Mpx1Codec.TryDecode(f, out Mpx1Message? m, out _) ? m : null)
            .OfType<IdentityReply>()
            .FirstOrDefault();
        Assert.IsNotNull(identity, $"No Identity Reply within 3 s ({frames.Count} frames received).");
        rig.Log($"Identity: device ID {identity.Device.Value}, V{identity.MajorVersion}.{identity.MinorVersion:D2}, phase {identity.DevelopmentPhase}.");
        Assert.AreEqual(rig.Device, identity.Device, $"The MPX 1 answers as device ID {identity.Device.Value}; set {HardwareRig.DeviceVariable}.");
    }

    [TestMethod]
    [Description("SES-007, PRO-010: the session detects the firmware from Device Inquiry and System Configuration")]
    public async Task DetectVersion_ReportsFirmware()
    {
        await using HardwareRig rig = await HardwareRig.OpenAsync(TestContext);
        await using Mpx1Session session = rig.StartSession();

        DeviceVersion version = await session.DetectVersionAsync(TestContext.CancellationToken);

        SystemConfiguration config = version.Configuration;
        rig.Log($"Firmware {version} built {config.BuildDate} {config.BuildTime}; identity reply: {version.Identity is not null}.");
        rig.Log($"Parameter types: {config.ParameterTypeCount}, bottom parameter: {config.BottomParameter}, control levels: {config.MaxControlLevels}.");
        Assert.AreEqual(1, version.MajorVersion);
        Assert.IsTrue(version.MinorVersion is 0 or 10, $"Unexpected firmware {version}.");
        Assert.IsGreaterThan(0, config.ParameterTypeCount);
    }

    [TestMethod]
    [Description("SES-006: ARE_YOU_THERE is answered with IM_ALIVE")]
    public async Task Ping_IsAnswered()
    {
        await using HardwareRig rig = await HardwareRig.OpenAsync(TestContext);
        await using Mpx1Session session = rig.StartSession();

        Assert.IsTrue(await session.PingAsync(TestContext.CancellationToken), "No IM_ALIVE from the device.");
    }

    private static async Task<List<byte[]>> CollectSysExAsync(IMidiTransport transport, CancellationToken cancellationToken)
    {
        List<byte[]> frames = [];
        try
        {
            await foreach (MidiInput input in transport.ReadAllAsync(cancellationToken))
            {
                if (input is SysExInput sysEx)
                    frames.Add(sysEx.Frame.ToArray());
            }
        }
        catch (OperationCanceledException)
        {
            // The collection window ended.
        }
        return frames;
    }
}
