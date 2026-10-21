using System.Collections.Immutable;
using System.Text;

namespace BB84.MPX1.Core.Protocol;

/// <summary>
/// Locates a node in the MPX 1 control tree as an ordered list of control level values (A, B, C, …).
/// The empty address is the root of the tree.
/// </summary>
public readonly struct ControlAddress : IEquatable<ControlAddress>
{
    private readonly ImmutableArray<ushort> _levels;

    /// <summary>The root (null) address with zero control levels.</summary>
    public static readonly ControlAddress Root = new(ImmutableArray<ushort>.Empty);

    /// <summary>Initializes a new instance of the <see cref="ControlAddress"/> struct.</summary>
    /// <param name="levels">The control level values, level A first.</param>
    public ControlAddress(ImmutableArray<ushort> levels)
        => _levels = levels.IsDefault ? [] : levels;

    /// <summary>Initializes a new instance of the <see cref="ControlAddress"/> struct.</summary>
    /// <param name="levels">The control level values, level A first.</param>
    public ControlAddress(params ReadOnlySpan<ushort> levels)
        => _levels = [.. levels];

    /// <summary>Creates an address from a sequence of control level values, level A first.</summary>
    /// <param name="levels">The control level values.</param>
    public static ControlAddress From(IEnumerable<ushort> levels)
        => new(ImmutableArray.CreateRange(levels));

    /// <summary>Gets the control level values, level A first.</summary>
    public ImmutableArray<ushort> Levels => _levels.IsDefault ? [] : _levels;

    /// <summary>Gets the number of control levels.</summary>
    public int Depth => Levels.Length;

    /// <summary>Gets a value indicating whether this is the root address.</summary>
    public bool IsRoot => Depth == 0;

    /// <summary>Gets the value of the control level at <paramref name="index"/>.</summary>
    /// <param name="index">Zero-based level index (0 = A).</param>
    public ushort this[int index] => Levels[index];

    /// <summary>Returns the address of the child node with the given value one level deeper.</summary>
    /// <param name="value">The value of the new last level.</param>
    public ControlAddress Child(ushort value) => new(Levels.Add(value));

    /// <summary>Returns the parent address, or the root for the root itself.</summary>
    public ControlAddress Parent() => IsRoot ? this : new(Levels.RemoveAt(Depth - 1));

    /// <inheritdoc/>
    public bool Equals(ControlAddress other) => Levels.AsSpan().SequenceEqual(other.Levels.AsSpan());

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is ControlAddress other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        HashCode hash = new();
        foreach (ushort level in Levels)
            hash.Add(level);
        return hash.ToHashCode();
    }

    /// <summary>Formats the address as <c>A:0 B:2 C:1</c>, or <c>(root)</c>.</summary>
    public override string ToString()
    {
        if (IsRoot)
            return "(root)";

        StringBuilder sb = new();
        for (int i = 0; i < Depth; i++)
        {
            if (i > 0)
                sb.Append(' ');
            sb.Append(LevelName(i)).Append(':').Append(Levels[i]);
        }
        return sb.ToString();
    }

    /// <summary>Equality operator.</summary>
    public static bool operator ==(ControlAddress left, ControlAddress right) => left.Equals(right);

    /// <summary>Inequality operator.</summary>
    public static bool operator !=(ControlAddress left, ControlAddress right) => !left.Equals(right);

    private static string LevelName(int index)
        => index < 26 ? ((char)('A' + index)).ToString() : $"L{index}";
}
