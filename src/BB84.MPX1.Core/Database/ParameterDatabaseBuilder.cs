using System.Collections.Immutable;

using BB84.MPX1.Core.Programs;
using BB84.MPX1.Core.Protocol;

namespace BB84.MPX1.Core.Database;

/// <summary>
/// Collects parameter descriptions and tree nodes, applies the Appendix D workarounds and builds a
/// <see cref="ParameterDatabase"/>.
/// </summary>
/// <param name="key">The firmware build.</param>
public sealed class ParameterDatabaseBuilder(DatabaseKey key)
{
    private readonly Dictionary<ushort, ParameterDescription> _descriptions = [];
    private readonly Dictionary<ControlAddress, ushort> _nodes = [];
    private readonly List<ControlAddress> _gaps = [];
    private readonly Dictionary<(EffectType, byte), AlgorithmParameters> _algorithms = [];

    /// <summary>Gets the firmware build.</summary>
    public DatabaseKey Key { get; } = key ?? throw new ArgumentNullException(nameof(key));

    /// <summary>Gets the number of descriptions added.</summary>
    public int DescriptionCount => _descriptions.Count;

    /// <summary>Gets the number of nodes added.</summary>
    public int NodeCount => _nodes.Count;

    /// <summary>Adds a description; known bugs are corrected (LRN-006).</summary>
    /// <param name="description">The description as received.</param>
    public ParameterDescription AddDescription(ParameterDescription description)
    {
        ParameterDescription fixedDescription = KnownBugs.Fix(description);
        _descriptions[fixedDescription.Type] = fixedDescription;
        return fixedDescription;
    }

    /// <summary>Returns a description added earlier, or <see langword="null"/>.</summary>
    /// <param name="type">The parameter type number.</param>
    public ParameterDescription? Describe(ushort type) => _descriptions.GetValueOrDefault(type);

    /// <summary>Records the parameter type at an address.</summary>
    /// <param name="address">The control address.</param>
    /// <param name="type">The parameter type number.</param>
    public void AddNode(ControlAddress address, ushort type) => _nodes[address] = type;

    /// <summary>Records the parameter list of an algorithm or controller (Report Effect Parameters).</summary>
    /// <param name="parameters">The list; controllers are stored under algorithm 0.</param>
    public void AddAlgorithm(AlgorithmParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        byte algorithm = (int)parameters.Effect < ProgramData.EffectBlockCount ? parameters.Algorithm : (byte)0;
        _algorithms[(parameters.Effect, algorithm)] = parameters with { Algorithm = algorithm, Name = parameters.Name.Trim() };
    }

    /// <summary>Records the parameter list carried by a Report Effect Parameters message.</summary>
    /// <param name="message">The message.</param>
    /// <returns><see langword="false"/> when the message lists no parameters.</returns>
    public bool AddAlgorithm(EffectParameters message)
    {
        ArgumentNullException.ThrowIfNull(message);
        ImmutableArray<ushort> types = message.Parameters.ParameterTypes;
        if (types.IsEmpty)
            return false;
        AddAlgorithm(new AlgorithmParameters(message.EffectType, message.Number, message.Parameters.Name, types));
        return true;
    }

    /// <summary>Returns the number of recorded children of a node.</summary>
    /// <param name="address">The node address.</param>
    public int RecordedChildCount(ControlAddress address)
        => _nodes.Keys.Count(a => a.Depth == address.Depth + 1 && a.Parent() == address);

    /// <summary>Records an address inside a branch's range that did not answer.</summary>
    /// <param name="address">The control address.</param>
    public void AddGap(ControlAddress address) => _gaps.Add(address);

    /// <summary>
    /// Returns the child indices a node may have: <c>0</c> through the maximum of its first unit/limit,
    /// or none for editable parameters and dead ends (LRN-002, LRN-007).
    /// </summary>
    /// <param name="address">The node address.</param>
    /// <param name="description">The node's description.</param>
    public static int ChildCount(ControlAddress address, ParameterDescription description)
    {
        ArgumentNullException.ThrowIfNull(description);
        if (!description.IsControlLevel || description.Units.IsDefaultOrEmpty || KnownBugs.IsDeadEnd(address, description.Type))
            return 0;
        int max = description.Units[0].Maximum;
        return max < 0 ? 0 : max + 1;
    }

    /// <summary>Builds the database.</summary>
    /// <exception cref="InvalidOperationException">The root node or a description is missing.</exception>
    public ParameterDatabase Build()
    {
        if (!_nodes.TryGetValue(ControlAddress.Root, out ushort rootType))
            throw new InvalidOperationException("The root node is missing.");

        ILookup<ControlAddress, KeyValuePair<ControlAddress, ushort>> children = _nodes
            .Where(kv => !kv.Key.IsRoot)
            .ToLookup(kv => kv.Key.Parent());

        ControlTreeNode root = CreateNode(ControlAddress.Root, rootType, null);
        Attach(root, children);
        return new ParameterDatabase(Key, _descriptions.Values, root, [.. _gaps], _algorithms.Values);
    }

    private void Attach(ControlTreeNode parent, ILookup<ControlAddress, KeyValuePair<ControlAddress, ushort>> children)
    {
        if (parent.IsDeadEnd || parent.IsEditable)
            return;

        // Children are every recorded node one level deeper, in index order.
        foreach (KeyValuePair<ControlAddress, ushort> entry in children[parent.Address].OrderBy(kv => kv.Key[parent.Address.Depth]))
        {
            ControlTreeNode child = CreateNode(entry.Key, entry.Value, parent);
            parent.AddChild(child);
            Attach(child, children);
        }
    }

    private ControlTreeNode CreateNode(ControlAddress address, ushort type, ControlTreeNode? parent)
    {
        ParameterDescription description = _descriptions.GetValueOrDefault(type)
            ?? throw new InvalidOperationException($"No description for parameter type 0x{type:X4} at {address}.");
        return new ControlTreeNode(address, description, parent, KnownBugs.IsDeadEnd(address, type));
    }
}
