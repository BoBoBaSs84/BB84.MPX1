using BB84.MPX1.Core.Programs;
using BB84.MPX1.Core.Protocol;

namespace BB84.MPX1.Core.Database;

/// <summary>
/// Workarounds for the SysEx bugs listed in Appendix D of the MIDI Implementation document (LRN-006 to LRN-008).
/// </summary>
public static class KnownBugs
{
    /// <summary>Parameter type that reports size 0 but holds <see cref="BrokenSizeActualLength"/> bytes (bug 1).</summary>
    public const ushort BrokenSizeType = 0x014A;

    /// <summary>Actual size of <see cref="BrokenSizeType"/>.</summary>
    public const ushort BrokenSizeActualLength = 542;

    /// <summary>Control-level type that ends a branch without children (bug 2).</summary>
    public const ushort DeadEndType = 0x015B;

    /// <summary>Address of the dead-end control level (bug 2).</summary>
    public static readonly ControlAddress DeadEndAddress = new(1, 2, 0);

    /// <summary>Corrects a parameter description reported by the device (LRN-006).</summary>
    /// <param name="description">The description as received.</param>
    public static ParameterDescription Fix(ParameterDescription description)
    {
        ArgumentNullException.ThrowIfNull(description);
        return description.Type == BrokenSizeType && description.Size == 0
            ? description with { Size = BrokenSizeActualLength }
            : description;
    }

    /// <summary>Returns <see langword="true"/> when the tree walker must not descend below this node (LRN-007).</summary>
    /// <param name="address">The node address.</param>
    /// <param name="type">The parameter type at the node.</param>
    public static bool IsDeadEnd(ControlAddress address, ushort type)
        => type == DeadEndType && address == DeadEndAddress;

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="address"/> is a parameter of an effect algorithm that is
    /// not loaded in its effect block; writing it can crash the unit (LRN-008).
    /// </summary>
    /// <param name="address">The parameter address (<c>A:0 B:effect C:algorithm D:parameter</c>).</param>
    /// <param name="activeAlgorithms">The active program's algorithm numbers, Pitch through Delay.</param>
    public static bool IsUnloadedAlgorithmParameter(ControlAddress address, ReadOnlySpan<byte> activeAlgorithms)
    {
        if (address.Depth < 4 || address[0] != 0 || address[1] >= ProgramData.EffectBlockCount || address[1] >= activeAlgorithms.Length)
            return false;
        return activeAlgorithms[address[1]] != address[2];
    }
}
