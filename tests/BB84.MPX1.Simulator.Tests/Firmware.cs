using BB84.MPX1.Core.Database;
using BB84.MPX1.Core.Protocol;
using BB84.MPX1.Midi;
using BB84.MPX1.Midi.Learning;
using BB84.MPX1.Simulator.Fixtures;
using BB84.MPX1.Simulator.Recording;

namespace BB84.MPX1.Simulator.Tests;

/// <summary>
/// The simulator fixtures the version-neutral tests run against: the hand-built V1.10 fixture and the full-tree V1.00
/// fixture built from the M8 learn recording (SIM-002). Both, and the databases learned from them, are built once.
/// </summary>
internal static class Firmware
{
    public const string V110 = "V1.10";
    public const string V100 = "V1.00";

    // The learn recording was made with one retry; replies are released at once, so a short timeout only shortens the
    // two unanswered attempts (Pitch algorithm 10 has no Report Effect Parameters answer on V1.00).
    public static readonly Mpx1SessionOptions RecordedLearnOptions = new() { RequestTimeout = TimeSpan.FromMilliseconds(100), Retries = 1 };

    private static readonly DeviceId RecordedDevice = new(3);
    private static readonly InMemoryParameterDatabaseStore Store = new();
    private static ParameterDatabase? _recordedDatabase;
    private static SimulatorFixture? _v100;

    /// <summary>Gets the firmware versions for <see cref="DynamicDataAttribute"/>.</summary>
    public static IEnumerable<object[]> All => [[V110], [V100]];

    public static string RecordingPath(string name) => Path.Combine(AppContext.BaseDirectory, "data", "hardware", "v100", name);

    /// <summary>Returns the fixture of a firmware version.</summary>
    public static async Task<SimulatorFixture> FixtureAsync(string firmware, CancellationToken cancellationToken) => firmware switch
    {
        V110 => DefaultFixture.CreateV110(),
        V100 => _v100 ??= FixtureBuilder.FromDatabase(await RecordedDatabaseAsync(cancellationToken), RecordedConfiguration()),
        _ => throw new ArgumentOutOfRangeException(nameof(firmware), firmware, "Unknown firmware."),
    };

    /// <summary>Creates an opened simulator for a firmware version.</summary>
    public static async Task<Mpx1Simulator> SimulatorAsync(string firmware, SimulatorOptions? options, CancellationToken cancellationToken)
    {
        Mpx1Simulator simulator = new(await FixtureAsync(firmware, cancellationToken), options);
        await simulator.OpenAsync(Mpx1Simulator.Port.Id, Mpx1Simulator.Port.Id, cancellationToken);
        return simulator;
    }

    /// <summary>Learns the simulated unit once per firmware; later calls only send System Configuration.</summary>
    public static Task<ParameterDatabase> LoadOrLearnAsync(Mpx1Session session, CancellationToken cancellationToken)
        => new ParameterLearner(session, Store).LoadOrLearnAsync(cancellationToken: cancellationToken);

    /// <summary>Returns the V1.00 database learned from <c>learn.jsonl</c> by strict replay.</summary>
    public static async Task<ParameterDatabase> RecordedDatabaseAsync(CancellationToken cancellationToken)
    {
        if (_recordedDatabase is not null)
            return _recordedDatabase;
        await using ReplayMidiTransport replay = new(ReadRecording("learn.jsonl"));
        await replay.OpenAsync(ReplayMidiTransport.Port.Id, ReplayMidiTransport.Port.Id, cancellationToken);
        await using Mpx1Session session = new(replay, RecordedDevice, RecordedLearnOptions);
        session.Start();
        return _recordedDatabase = await new ParameterLearner(session).LoadOrLearnAsync(forceRelearn: true, cancellationToken: cancellationToken);
    }

    public static IReadOnlyList<RecordedFrame> ReadRecording(string name)
    {
        using StreamReader reader = new(RecordingPath(name));
        return RecordedFrame.ReadAll(reader);
    }

    // The learn recording starts with the unit's System Configuration reply.
    private static SystemConfiguration RecordedConfiguration()
        => ReadRecording("learn.jsonl")
            .Where(f => f.Dir == RecordedFrame.In)
            .Select(f => Mpx1Codec.TryDecode(f.ToBytes(), out Mpx1Message? message, out _) ? message : null)
            .OfType<SystemConfiguration>()
            .First();
}
