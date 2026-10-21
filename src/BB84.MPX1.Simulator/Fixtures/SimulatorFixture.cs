using System.Text.Json;
using System.Text.Json.Serialization;

namespace BB84.MPX1.Simulator.Fixtures;

/// <summary>
/// Serializable description of a simulated MPX 1: firmware info, parameter descriptions, the control tree,
/// parameter values and programs (SIM-002).
/// </summary>
public sealed record SimulatorFixture
{
    /// <summary>Current fixture schema version.</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>Gets the fixture schema version.</summary>
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    /// <summary>Gets the major software version.</summary>
    public byte MajorVersion { get; init; }

    /// <summary>Gets the minor software version.</summary>
    public byte MinorVersion { get; init; }

    /// <summary>Gets the software development phase (Identity Reply).</summary>
    public byte DevelopmentPhase { get; init; }

    /// <summary>Gets the 8-character build time.</summary>
    public string BuildTime { get; init; } = "00:00:00";

    /// <summary>Gets the 11-character build date.</summary>
    public string BuildDate { get; init; } = "Jan 01 1998";

    /// <summary>Gets the number of parameter types; every type below it has a description.</summary>
    public ushort ParameterTypeCount { get; init; }

    /// <summary>Gets the "bottom" parameter number reported in System Configuration.</summary>
    public ushort BottomParameter { get; init; }

    /// <summary>Gets the maximum number of control levels.</summary>
    public ushort MaxControlLevels { get; init; }

    /// <summary>Gets the explicitly defined parameter descriptions. Missing types get a generic description.</summary>
    public IReadOnlyList<FixtureDescription> Descriptions { get; init; } = [];

    /// <summary>Gets the nodes of the control tree (address → parameter type).</summary>
    public IReadOnlyList<FixtureNode> Nodes { get; init; } = [];

    /// <summary>Gets initial parameter values (address → raw bytes, hex).</summary>
    public IReadOnlyList<FixtureValue> Values { get; init; } = [];

    /// <summary>Gets the stored programs (raw program data, hex).</summary>
    public IReadOnlyList<FixtureProgram> Programs { get; init; } = [];

    /// <summary>Gets the number of the program loaded at start-up.</summary>
    public ushort ActiveProgram { get; init; }

    /// <summary>
    /// Gets the parameter lists of the controllers (Knob through Envelope 2, effect types 6–16) reported by
    /// Report Effect Parameters. Effect block algorithms are derived from the tree instead.
    /// </summary>
    public IReadOnlyList<FixtureController> Controllers { get; init; } = [];
}

/// <summary>The parameter list of one controller.</summary>
/// <param name="EffectType">Controller type 6–16.</param>
/// <param name="Name">Name as reported by the device.</param>
/// <param name="Types">Parameter types in data order.</param>
public sealed record FixtureController(byte EffectType, string Name, IReadOnlyList<ushort> Types);

/// <summary>A parameter description in a fixture.</summary>
/// <param name="Type">Parameter type number.</param>
/// <param name="Name">Name as reported by the device.</param>
/// <param name="Size">Reported size in bytes (may be wrong on purpose, see Appendix D).</param>
/// <param name="Attributes">Control flags.</param>
/// <param name="OptionType">Option type, or <see langword="null"/>.</param>
/// <param name="Units">Unit/limit triples.</param>
public sealed record FixtureDescription(ushort Type, string Name, ushort Size, byte Attributes, ushort? OptionType, IReadOnlyList<FixtureUnit> Units);

/// <summary>One unit/limit triple.</summary>
/// <param name="Minimum">Minimum.</param>
/// <param name="Maximum">Maximum.</param>
/// <param name="DisplayUnits">Display units type.</param>
public sealed record FixtureUnit(short Minimum, short Maximum, ushort DisplayUnits);

/// <summary>A control tree node.</summary>
/// <param name="Address">Control level values, level A first.</param>
/// <param name="Type">Parameter type at this address.</param>
public sealed record FixtureNode(IReadOnlyList<ushort> Address, ushort Type);

/// <summary>A parameter value.</summary>
/// <param name="Address">Control level values, level A first.</param>
/// <param name="Hex">Raw bytes as hex (parameter followed by option bytes).</param>
public sealed record FixtureValue(IReadOnlyList<ushort> Address, string Hex);

/// <summary>A stored program.</summary>
/// <param name="Number">Program number 0–249.</param>
/// <param name="Hex">Raw program data as hex.</param>
public sealed record FixtureProgram(ushort Number, string Hex);

/// <summary>Source-generated JSON metadata for simulator types (CON-003).</summary>
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(SimulatorFixture))]
[JsonSerializable(typeof(Recording.RecordedFrame))]
public sealed partial class SimulatorJsonContext : JsonSerializerContext;

/// <summary>Loads and saves fixtures as JSON.</summary>
public static class SimulatorFixtureSerializer
{
    /// <summary>Serializes a fixture.</summary>
    /// <param name="fixture">The fixture.</param>
    public static string Serialize(SimulatorFixture fixture)
        => JsonSerializer.Serialize(fixture, SimulatorJsonContext.Default.SimulatorFixture);

    /// <summary>Deserializes a fixture.</summary>
    /// <param name="json">The JSON text.</param>
    /// <exception cref="JsonException">The JSON is invalid.</exception>
    public static SimulatorFixture Deserialize(string json)
        => JsonSerializer.Deserialize(json, SimulatorJsonContext.Default.SimulatorFixture)
            ?? throw new JsonException("The fixture is empty.");
}
