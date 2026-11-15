using System.Globalization;

using BB84.MPX1.Core.Database;
using BB84.MPX1.Core.Protocol;
using BB84.MPX1.Midi.Learning;
using BB84.MPX1.Simulator.Recording;

namespace BB84.MPX1.Midi.WinMM.Tests.Hardware;

/// <summary>
/// Connection to a real MPX 1 for the <c>Hardware</c> test category (roadmap M8).
/// </summary>
/// <remarks>
/// Configured with environment variables:
/// <list type="bullet">
/// <item><c>MPX1_MIDI_IN</c>, <c>MPX1_MIDI_OUT</c>: WinMM port names (required).</item>
/// <item><c>MPX1_DEVICE_ID</c>: device ID 0–127 (default 0).</item>
/// </list>
/// Every session is recorded as JSON lines to <c>artifacts/hardware/&lt;test&gt;.jsonl</c> for replay fixtures, and
/// its messages are written to <c>artifacts/hardware/&lt;test&gt;.log</c> (the test runner hides the output of passing tests).
/// </remarks>
internal sealed class HardwareRig : IAsyncDisposable
{
    public const string InputVariable = "MPX1_MIDI_IN";
    public const string OutputVariable = "MPX1_MIDI_OUT";
    public const string DeviceVariable = "MPX1_DEVICE_ID";

    private readonly TestContext _context;
    private readonly StreamWriter _recording;
    private readonly StreamWriter _log;

    private HardwareRig(TestContext context, IMidiTransport transport, StreamWriter recording, StreamWriter log, DeviceId device)
    {
        _context = context;
        Transport = transport;
        _recording = recording;
        _log = log;
        Device = device;
    }

    /// <summary>Gets the open, recording transport.</summary>
    public IMidiTransport Transport { get; }

    /// <summary>Gets the configured device ID.</summary>
    public DeviceId Device { get; }

    /// <summary>Opens the configured ports, or marks the test inconclusive when they are not configured or missing.</summary>
    public static async Task<HardwareRig> OpenAsync(TestContext context)
    {
        string? input = Environment.GetEnvironmentVariable(InputVariable);
        string? output = Environment.GetEnvironmentVariable(OutputVariable);
        WinMMMidiTransport winmm = new();
        string ports = $"Input ports: {Names(winmm.GetInputPorts())}. Output ports: {Names(winmm.GetOutputPorts())}.";
        context.WriteLine(ports);

        if (string.IsNullOrWhiteSpace(input) || string.IsNullOrWhiteSpace(output))
        {
            await winmm.DisposeAsync();
            Assert.Inconclusive($"Set {InputVariable} and {OutputVariable} to the MPX 1 ports. {ports}");
        }
        if (!winmm.GetInputPorts().Any(p => p.Id.Value == input) || !winmm.GetOutputPorts().Any(p => p.Id.Value == output))
        {
            await winmm.DisposeAsync();
            Assert.Inconclusive($"Port '{input}' or '{output}' not found. {ports}");
        }

        DeviceId device = new(byte.Parse(Environment.GetEnvironmentVariable(DeviceVariable) ?? "0", CultureInfo.InvariantCulture));
        string path = ArtifactPath($"{context.TestName}.jsonl");
        StreamWriter recording = new(path, append: false);
        StreamWriter log = new(Path.ChangeExtension(path, ".log"), append: false) { AutoFlush = true };
        RecordingMidiTransport transport = new(winmm, recording);
        try
        {
            await transport.OpenAsync(new MidiPortId(input!), new MidiPortId(output!), context.CancellationToken);
        }
        catch
        {
            await transport.DisposeAsync();
            await recording.DisposeAsync();
            await log.DisposeAsync();
            throw;
        }
        HardwareRig rig = new(context, transport, recording, log, device);
        rig.Log($"Opened '{input}' / '{output}', device ID {device.Value}. Recording to {path}.");
        return rig;
    }

    /// <summary>Writes a line to the test output and the log file.</summary>
    /// <param name="message">The line.</param>
    public void Log(string message)
    {
        _context.WriteLine(message);
        lock (_log)
            _log.WriteLine($"{DateTime.Now:HH:mm:ss.fff} {message}");
    }

    /// <summary>Creates a started session with timing suited to a hardware MIDI interface.</summary>
    public Mpx1Session StartSession(Mpx1SessionOptions? options = null)
    {
        Mpx1Session session = new(Transport, Device, options ?? new Mpx1SessionOptions { RequestTimeout = TimeSpan.FromSeconds(2) });
        session.Start();
        return session;
    }

    /// <summary>Loads the learned database from <c>artifacts/hardware/database</c>, learning the device when it is missing.</summary>
    /// <param name="session">A started session.</param>
    /// <param name="forceRelearn">Learn even when a cached database exists.</param>
    public Task<ParameterDatabase> LoadDatabaseAsync(Mpx1Session session, bool forceRelearn = false)
    {
        FileParameterDatabaseStore store = new(Path.Combine(RepositoryRoot(), "artifacts", "hardware", "database"));
        Progress<LearningProgress> progress = new(p => Log($"{p.Phase}: {p.DescriptionsDone}/{p.DescriptionsTotal} descriptions, {p.NodesVisited} nodes, {p.Gaps} gaps"));
        return new ParameterLearner(session, store).LoadOrLearnAsync(progress, forceRelearn, _context.CancellationToken);
    }

    /// <summary>Returns a path under <c>artifacts/hardware</c>, creating its directory.</summary>
    /// <param name="parts">Path segments below <c>artifacts/hardware</c>.</param>
    public static string ArtifactPath(params string[] parts)
    {
        string path = Path.Combine([RepositoryRoot(), "artifacts", "hardware", .. parts]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return path;
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await Transport.DisposeAsync();
        await _recording.DisposeAsync();
        await _log.DisposeAsync();
    }

    private static string Names(IReadOnlyList<MidiPortInfo> ports)
        => ports.Count == 0 ? "(none)" : string.Join(", ", ports.Select(p => $"'{p.Id.Value}'"));

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "BB84.MPX1.slnx")))
                return dir.FullName;
        }
        throw new InvalidOperationException("Repository root (BB84.MPX1.slnx) not found.");
    }
}
