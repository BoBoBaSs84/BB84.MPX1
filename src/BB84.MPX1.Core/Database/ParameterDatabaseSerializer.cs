using System.Text.Json;
using System.Text.Json.Serialization;

using BB84.MPX1.Core.Protocol;

namespace BB84.MPX1.Core.Database;

/// <summary>
/// Saves and loads a <see cref="ParameterDatabase"/> as JSON with a source-generated serializer (LRN-004, CON-003).
/// </summary>
public static class ParameterDatabaseSerializer
{
    /// <summary>Current document schema version.</summary>
    /// <remarks>Version 2 added the algorithm parameter lists; older caches are re-learned.</remarks>
    public const int SchemaVersion = 2;

    /// <summary>Serializes a database.</summary>
    /// <param name="database">The database.</param>
    public static string Serialize(ParameterDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        DatabaseDocument document = new(
            SchemaVersion,
            database.Key,
            [.. database.Descriptions.Values.OrderBy(d => d.Type).Select(d => new DescriptionDocument(
                d.Type, d.Name, d.Size, (byte)d.Flags, d.OptionType,
                [.. d.Units.Select(u => new UnitDocument(u.Minimum, u.Maximum, u.DisplayUnits))]))],
            [.. database.Root.DescendantsAndSelf().Select(n => new NodeDocument([.. n.Address.Levels], n.Type))],
            [.. database.Gaps.Select(g => g.Levels.ToArray())],
            [.. database.Algorithms.Select(a => new AlgorithmDocument((byte)a.Effect, a.Algorithm, a.Name, [.. a.Types]))]);
        return JsonSerializer.Serialize(document, CoreJsonContext.Default.DatabaseDocument);
    }

    /// <summary>Deserializes a database. Treat the input as untrusted (SEC-002).</summary>
    /// <param name="json">The JSON text.</param>
    /// <exception cref="JsonException">The JSON is invalid or uses an unknown schema.</exception>
    public static ParameterDatabase Deserialize(string json)
    {
        DatabaseDocument document = JsonSerializer.Deserialize(json, CoreJsonContext.Default.DatabaseDocument)
            ?? throw new JsonException("The database document is empty.");
        if (document.SchemaVersion != SchemaVersion)
            throw new JsonException($"Unsupported database schema version {document.SchemaVersion}.");

        DeviceId device = new(0);
        ParameterDatabaseBuilder builder = new(document.Key);
        foreach (DescriptionDocument d in document.Descriptions)
        {
            builder.AddDescription(new ParameterDescription(device, d.Type, d.Name, d.Size, (ParameterAttributes)d.Attributes, d.OptionType,
                [.. d.Units.Select(u => new UnitLimit(u.Minimum, u.Maximum, u.DisplayUnits))]));
        }
        foreach (NodeDocument node in document.Nodes)
            builder.AddNode(ControlAddress.From(node.Address), node.Type);
        foreach (ushort[] gap in document.Gaps)
            builder.AddGap(ControlAddress.From(gap));
        foreach (AlgorithmDocument a in document.Algorithms)
            builder.AddAlgorithm(new AlgorithmParameters((EffectType)a.Effect, a.Algorithm, a.Name, [.. a.Types]));

        try
        {
            return builder.Build();
        }
        catch (InvalidOperationException ex)
        {
            throw new JsonException(ex.Message, ex);
        }
    }
}

internal sealed record DatabaseDocument(
    int SchemaVersion,
    DatabaseKey Key,
    DescriptionDocument[] Descriptions,
    NodeDocument[] Nodes,
    ushort[][] Gaps,
    AlgorithmDocument[] Algorithms);

internal sealed record AlgorithmDocument(byte Effect, byte Algorithm, string Name, ushort[] Types);

internal sealed record DescriptionDocument(ushort Type, string Name, ushort Size, byte Attributes, ushort? OptionType, UnitDocument[] Units);

internal sealed record UnitDocument(short Minimum, short Maximum, ushort DisplayUnits);

internal sealed record NodeDocument(ushort[] Address, ushort Type);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(DatabaseDocument))]
internal sealed partial class CoreJsonContext : JsonSerializerContext;
