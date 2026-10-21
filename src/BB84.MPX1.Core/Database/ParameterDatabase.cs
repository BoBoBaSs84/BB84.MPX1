using System.Collections.Frozen;
using System.Collections.Immutable;

using BB84.MPX1.Core.Programs;
using BB84.MPX1.Core.Protocol;

namespace BB84.MPX1.Core.Database;

/// <summary>A node of the learned control tree.</summary>
public sealed class ControlTreeNode
{
    private readonly List<ControlTreeNode> _children = [];

    internal ControlTreeNode(ControlAddress address, ParameterDescription description, ControlTreeNode? parent, bool isDeadEnd)
    {
        Address = address;
        Description = description;
        Parent = parent;
        IsDeadEnd = isDeadEnd;
    }

    /// <summary>Gets the control address.</summary>
    public ControlAddress Address { get; }

    /// <summary>Gets the parameter type.</summary>
    public ushort Type => Description.Type;

    /// <summary>Gets the parameter description.</summary>
    public ParameterDescription Description { get; }

    /// <summary>Gets the trimmed parameter name.</summary>
    public string Name => Description.Name.Trim();

    /// <summary>Gets the parent node, or <see langword="null"/> for the root.</summary>
    public ControlTreeNode? Parent { get; }

    /// <summary>Gets the child nodes in index order.</summary>
    public IReadOnlyList<ControlTreeNode> Children => _children;

    /// <summary>Gets a value indicating whether this node is an editable parameter (not a control level).</summary>
    public bool IsEditable => !Description.IsControlLevel;

    /// <summary>Gets a value indicating whether this is a control level the walker did not descend (Appendix D bug 2).</summary>
    public bool IsDeadEnd { get; }

    internal void AddChild(ControlTreeNode child) => _children.Add(child);

    /// <summary>Enumerates this node and all descendants depth-first.</summary>
    public IEnumerable<ControlTreeNode> DescendantsAndSelf()
    {
        yield return this;
        foreach (ControlTreeNode child in _children)
        {
            foreach (ControlTreeNode node in child.DescendantsAndSelf())
                yield return node;
        }
    }

    /// <inheritdoc/>
    public override string ToString() => $"{Address} {Name} (0x{Type:X4})";
}

/// <summary>
/// The parameter types of one effect algorithm or controller, in data order, as reported by Report Effect
/// Parameters (<c>18</c>). They define the layout of the program data block (PRG-003).
/// </summary>
/// <param name="Effect">The effect or controller type.</param>
/// <param name="Algorithm">The algorithm number; 0 for controllers (types 6–16).</param>
/// <param name="Name">The algorithm name.</param>
/// <param name="Types">The parameter types.</param>
public sealed record AlgorithmParameters(EffectType Effect, byte Algorithm, string Name, ImmutableArray<ushort> Types);

/// <summary>
/// The learned parameter descriptions and control tree of one firmware build (LRN-001 to LRN-005).
/// </summary>
public sealed class ParameterDatabase
{
    private readonly FrozenDictionary<ushort, ParameterDescription> _descriptions;
    private readonly FrozenDictionary<ControlAddress, ControlTreeNode> _nodes;
    private readonly FrozenDictionary<(EffectType, byte), AlgorithmParameters> _algorithms;

    internal ParameterDatabase(DatabaseKey key, IEnumerable<ParameterDescription> descriptions, ControlTreeNode root, ImmutableArray<ControlAddress> gaps,
        IEnumerable<AlgorithmParameters> algorithms)
    {
        Key = key;
        _descriptions = descriptions.ToFrozenDictionary(d => d.Type);
        Root = root;
        _nodes = root.DescendantsAndSelf().ToFrozenDictionary(n => n.Address);
        Gaps = gaps;
        _algorithms = algorithms.ToFrozenDictionary(a => (a.Effect, a.Algorithm));
    }

    /// <summary>Gets the learned algorithm and controller parameter lists, ordered by effect and algorithm.</summary>
    public IEnumerable<AlgorithmParameters> Algorithms => _algorithms.Values.OrderBy(a => a.Effect).ThenBy(a => a.Algorithm);

    /// <summary>Gets the firmware build this database belongs to.</summary>
    public DatabaseKey Key { get; }

    /// <summary>Gets the root node ("MPX 1").</summary>
    public ControlTreeNode Root { get; }

    /// <summary>Gets all parameter descriptions keyed by type.</summary>
    public IReadOnlyDictionary<ushort, ParameterDescription> Descriptions => _descriptions;

    /// <summary>Gets the number of nodes in the tree.</summary>
    public int NodeCount => _nodes.Count;

    /// <summary>Gets addresses inside a branch's range that did not answer while learning.</summary>
    public ImmutableArray<ControlAddress> Gaps { get; }

    /// <summary>Returns the node at an address, or <see langword="null"/>.</summary>
    /// <param name="address">The control address.</param>
    public ControlTreeNode? Find(ControlAddress address) => _nodes.GetValueOrDefault(address);

    /// <summary>Returns the description of a parameter type, or <see langword="null"/>.</summary>
    /// <param name="type">The parameter type number.</param>
    public ParameterDescription? Describe(ushort type) => _descriptions.GetValueOrDefault(type);

    /// <summary>
    /// Finds a node by its path of trimmed names below the root, case-insensitively, for example
    /// <c>"System", "Panel", "Display"</c>. Used instead of hard-coded type numbers (LRN-005).
    /// </summary>
    /// <param name="names">Names from level A downwards.</param>
    public ControlTreeNode? FindByPath(params ReadOnlySpan<string> names)
    {
        ControlTreeNode? current = Root;
        foreach (string name in names)
        {
            current = current.Children.FirstOrDefault(c => string.Equals(c.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
            if (current is null)
                return null;
        }
        return current;
    }

    /// <summary>Returns every node that uses a parameter type.</summary>
    /// <param name="type">The parameter type number.</param>
    public IEnumerable<ControlTreeNode> FindByType(ushort type) => Root.DescendantsAndSelf().Where(n => n.Type == type);

    /// <summary>Returns every node whose trimmed name matches, case-insensitively.</summary>
    /// <param name="name">The parameter name.</param>
    public IEnumerable<ControlTreeNode> FindByName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return Root.DescendantsAndSelf().Where(n => string.Equals(n.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Returns the parameter list of an effect algorithm or controller (PRG-003). Learned Report Effect Parameters
    /// lists come first; for effect blocks the control tree (<c>A:0 B:effect C:algorithm</c>) is the fallback.
    /// </summary>
    /// <param name="effect">The effect or controller type.</param>
    /// <param name="algorithm">The algorithm number; ignored for controllers.</param>
    public AlgorithmParameters? FindAlgorithm(EffectType effect, byte algorithm)
    {
        bool isEffect = (int)effect < ProgramData.EffectBlockCount;
        if (_algorithms.TryGetValue((effect, isEffect ? algorithm : (byte)0), out AlgorithmParameters? learned))
            return learned;
        if (!isEffect || Find(new ControlAddress(0, (ushort)effect, algorithm)) is not { } node)
            return null;
        return new AlgorithmParameters(effect, algorithm, node.Name, [.. node.Children.Where(c => c.IsEditable).Select(c => c.Type)]);
    }

    /// <summary>Returns the number of algorithms an effect block offers, from the learned lists or the tree.</summary>
    /// <param name="effect">Effect type 0–5.</param>
    public int AlgorithmCount(EffectType effect)
    {
        int learned = _algorithms.Keys.Where(k => k.Item1 == effect).Select(k => k.Item2 + 1).DefaultIfEmpty(0).Max();
        int tree = Find(new ControlAddress(0, (ushort)effect))?.Children.Count ?? 0;
        return Math.Max(learned, tree);
    }

    /// <summary>Builds the data layout of an effect algorithm or of a modulator block (PRG-003).</summary>
    /// <param name="effect">The effect or controller type.</param>
    /// <param name="algorithm">The algorithm number; ignored for controllers.</param>
    /// <returns>The layout, or <see langword="null"/> when the list is unknown or the type has no program block.</returns>
    /// <exception cref="ArgumentException">The learned list does not fit into the block.</exception>
    public ParameterBlockLayout? Layout(EffectType effect, byte algorithm)
    {
        int capacity = (int)effect < ProgramData.EffectBlockCount
            ? ProgramData.EffectDataSize
            : ModulatorBlocks.Find(effect)?.Size ?? 0;
        if (capacity == 0 || FindAlgorithm(effect, algorithm) is not { } parameters)
            return null;
        return ParameterBlockLayout.Create(parameters.Types, Describe, capacity);
    }

    /// <summary>Returns the size of a parameter including its option bytes (PAR-002).</summary>
    /// <param name="type">The parameter type number.</param>
    public int ValueSize(ushort type)
    {
        ParameterDescription? description = Describe(type);
        if (description is null)
            return 0;
        int size = description.Size;
        if (description.OptionType is ushort option && Describe(option) is { } optionDescription)
            size += optionDescription.Size;
        return size;
    }
}
