using System.Collections.Immutable;

namespace BB84.MPX1.Core.Programs;

/// <summary>Block types in the audio routing (Program Dump bytes 400–479).</summary>
public enum RoutingBlockType : byte
{
    /// <summary>Pitch.</summary>
    Pitch = 0,
    /// <summary>Chorus.</summary>
    Chorus = 1,
    /// <summary>EQ.</summary>
    EQ = 2,
    /// <summary>Modulator.</summary>
    Mod = 3,
    /// <summary>Reverb.</summary>
    Reverb = 4,
    /// <summary>Delay.</summary>
    Delay = 5,
    /// <summary>Input block (always first).</summary>
    Input = 6,
    /// <summary>Output block (always last).</summary>
    Output = 7,
}

/// <summary>How a block uses the upper and lower audio paths.</summary>
public enum RoutingMode : byte
{
    /// <summary>On the upper path only.</summary>
    Upper = 0,
    /// <summary>On the lower path only (after a split).</summary>
    Lower = 1,
    /// <summary>Inputs from both paths, outputs to both.</summary>
    Parallel = 2,
    /// <summary>Inputs from both paths, output on the upper path; ends a double path.</summary>
    Merge = 3,
    /// <summary>Input from the upper path, outputs to both; starts a double path.</summary>
    Split = 4,
}

/// <summary>How the signal is fed into a block input.</summary>
public enum InputConnection : byte
{
    /// <summary>Stereo to stereo.</summary>
    Stereo = 0,
    /// <summary>Left to left.</summary>
    LeftToLeft = 1,
    /// <summary>Right to right.</summary>
    RightToRight = 2,
    /// <summary>Left to left and right (mono).</summary>
    LeftToBoth = 3,
    /// <summary>Right to left and right (mono).</summary>
    RightToBoth = 4,
}

/// <summary>Where a block sits in the drawn signal-flow map.</summary>
/// <param name="Block">The routing block.</param>
/// <param name="Position">Position 0 (input) to 7 (output).</param>
/// <param name="IsDoublePath">Whether the block is on a double path.</param>
/// <param name="OnUpper">Whether the block processes the upper path.</param>
/// <param name="OnLower">Whether the block processes the lower path.</param>
public sealed record RoutingSlot(RoutingBlock Block, int Position, bool IsDoublePath, bool OnUpper, bool OnLower)
{
    /// <summary>Gets the block type.</summary>
    public RoutingBlockType Type => (RoutingBlockType)Block.EffectType;

    /// <summary>Gets the routing mode.</summary>
    public RoutingMode Mode => (RoutingMode)Block.Routing;
}

/// <summary>
/// Audio routing rules from the document ("Rules", Program Dump detail): which routings a block may use given
/// the blocks before it, and how the rest of the chain is repaired after a change (PRG-002, UI-006).
/// </summary>
public static class AudioRouting
{
    /// <summary>Number of routing blocks: input, six effects, output.</summary>
    public const int BlockCount = ProgramData.RoutingBlockCount;

    /// <summary>Returns how many merge and parallel blocks the firmware supports: 3 in V1.00, 2 in V1.10.</summary>
    /// <param name="majorVersion">Firmware major version.</param>
    /// <param name="minorVersion">Firmware minor version.</param>
    public static int MaxMixingBlocks(byte majorVersion, byte minorVersion) => majorVersion == 1 && minorVersion == 0 ? 3 : 2;

    /// <summary>Returns the default series routing: Input, Pitch, Chorus, EQ, Mod, Delay, Reverb, Output, all upper.</summary>
    public static ImmutableArray<RoutingBlock> Default { get; } =
    [
        Block(RoutingBlockType.Input), Block(RoutingBlockType.Pitch), Block(RoutingBlockType.Chorus), Block(RoutingBlockType.EQ),
        Block(RoutingBlockType.Mod), Block(RoutingBlockType.Delay), Block(RoutingBlockType.Reverb), Block(RoutingBlockType.Output),
    ];

    /// <summary>Returns the routings a block may use, given the blocks before it.</summary>
    /// <param name="blocks">The eight routing blocks.</param>
    /// <param name="position">Position 0–6; the output block (7) is set automatically.</param>
    /// <param name="maxMixingBlocks">Merge/parallel limit, see <see cref="MaxMixingBlocks"/>.</param>
    public static IReadOnlyList<RoutingMode> AllowedModes(IReadOnlyList<RoutingBlock> blocks, int position, int maxMixingBlocks)
    {
        RequireBlocks(blocks);
        ArgumentOutOfRangeException.ThrowIfNegative(position);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(position, BlockCount - 1);

        // Rule 2: the input block is either upper or split.
        if (position == 0)
            return [RoutingMode.Upper, RoutingMode.Split];

        (bool isDouble, RoutingMode previous) = StateBefore(blocks, position);
        int mixingElsewhere = Enumerable.Range(1, BlockCount - 2)
            .Count(i => i != position && (RoutingMode)blocks[i].Routing is RoutingMode.Merge or RoutingMode.Parallel);
        bool canMix = mixingElsewhere < maxMixingBlocks;

        List<RoutingMode> modes = [RoutingMode.Upper];
        if (!isDouble)
        {
            // Single path: upper, or split it.
            modes.Add(RoutingMode.Split);
            return modes;
        }

        modes.Add(RoutingMode.Lower);
        if (canMix && previous is RoutingMode.Upper or RoutingMode.Lower)
        {
            modes.Add(RoutingMode.Parallel);
            modes.Add(RoutingMode.Merge);
        }
        return modes;
    }

    /// <summary>
    /// Sets a block's routing, resets its inputs to stereo (rule note 6) and repairs the chain: blocks whose
    /// routing became illegal are set to upper (notes 4 and 5), the output block becomes merge after a double
    /// path and upper otherwise (note 3), and the path types are recomputed.
    /// </summary>
    /// <param name="blocks">The eight routing blocks.</param>
    /// <param name="position">Position 0–6.</param>
    /// <param name="mode">The new routing; must be in <see cref="AllowedModes"/>.</param>
    /// <param name="maxMixingBlocks">Merge/parallel limit.</param>
    /// <exception cref="ArgumentException">The routing is not allowed at this position.</exception>
    public static ImmutableArray<RoutingBlock> SetMode(IReadOnlyList<RoutingBlock> blocks, int position, RoutingMode mode, int maxMixingBlocks)
    {
        if (!AllowedModes(blocks, position, maxMixingBlocks).Contains(mode))
            throw new ArgumentException($"{mode} is not allowed at position {position}.", nameof(mode));
        RoutingBlock[] copy = [.. blocks];
        copy[position] = copy[position] with { Routing = (byte)mode, UpperInput = (byte)InputConnection.Stereo, LowerInput = (byte)InputConnection.Stereo };
        return Repair(copy);
    }

    /// <summary>Moves an effect to a position by swapping it with the effect currently there.</summary>
    /// <param name="blocks">The eight routing blocks.</param>
    /// <param name="position">Position 1–6.</param>
    /// <param name="effect">The effect to place there (Pitch through Delay).</param>
    public static ImmutableArray<RoutingBlock> PlaceEffect(IReadOnlyList<RoutingBlock> blocks, int position, RoutingBlockType effect)
    {
        RequireBlocks(blocks);
        if (position is < 1 or > BlockCount - 2)
            throw new ArgumentOutOfRangeException(nameof(position), position, "Effects sit at positions 1-6.");
        if (effect is RoutingBlockType.Input or RoutingBlockType.Output)
            throw new ArgumentException("Input and output blocks cannot be moved.", nameof(effect));

        RoutingBlock[] copy = [.. blocks];
        int from = Array.FindIndex(copy, b => b.EffectType == (byte)effect);
        if (from < 0 || from == position)
            return [.. copy];

        // Only the effects move; the routing of each position stays where it is.
        (byte a, byte b) = (copy[position].EffectType, copy[from].EffectType);
        copy[position] = copy[position] with { EffectType = b };
        copy[from] = copy[from] with { EffectType = a };
        return [.. copy];
    }

    /// <summary>Changes the input connections of a block.</summary>
    /// <param name="blocks">The eight routing blocks.</param>
    /// <param name="position">Position 1–7.</param>
    /// <param name="upper">Upper input connection.</param>
    /// <param name="lower">Lower input connection.</param>
    public static ImmutableArray<RoutingBlock> SetInputs(IReadOnlyList<RoutingBlock> blocks, int position, InputConnection upper, InputConnection lower)
    {
        RequireBlocks(blocks);
        RoutingBlock[] copy = [.. blocks];
        copy[position] = copy[position] with { UpperInput = (byte)upper, LowerInput = (byte)lower };
        return [.. copy];
    }

    /// <summary>Returns <see langword="true"/> when the routing follows every rule (useful for imported programs).</summary>
    /// <param name="blocks">The eight routing blocks.</param>
    /// <param name="maxMixingBlocks">Merge/parallel limit.</param>
    public static bool IsValid(IReadOnlyList<RoutingBlock> blocks, int maxMixingBlocks)
    {
        if (blocks is null || blocks.Count != BlockCount)
            return false;
        if (blocks[0].EffectType != (byte)RoutingBlockType.Input || blocks[^1].EffectType != (byte)RoutingBlockType.Output)
            return false;
        if (blocks.Skip(1).Take(BlockCount - 2).Select(b => b.EffectType).Order().SequenceEqual(Enumerable.Range(0, 6).Select(i => (byte)i)) is false)
            return false;
        for (int i = 0; i < BlockCount - 1; i++)
        {
            if (!AllowedModes(blocks, i, maxMixingBlocks).Contains((RoutingMode)blocks[i].Routing))
                return false;
        }
        return Repair([.. blocks]).SequenceEqual(blocks);
    }

    /// <summary>Lays the blocks out on the upper and lower lanes for drawing (UI-006).</summary>
    /// <param name="blocks">The eight routing blocks.</param>
    public static IReadOnlyList<RoutingSlot> Layout(IReadOnlyList<RoutingBlock> blocks)
    {
        RequireBlocks(blocks);
        List<RoutingSlot> slots = new(BlockCount);
        bool isDouble = false;
        for (int i = 0; i < BlockCount; i++)
        {
            RoutingMode mode = (RoutingMode)blocks[i].Routing;
            bool startsDouble = mode == RoutingMode.Split;
            bool doubleHere = isDouble || startsDouble;
            bool onUpper = mode != RoutingMode.Lower;
            bool onLower = doubleHere && mode is RoutingMode.Lower or RoutingMode.Parallel or RoutingMode.Merge;
            slots.Add(new RoutingSlot(blocks[i], i, doubleHere, onUpper, onLower));
            if (startsDouble)
                isDouble = true;
            else if (mode == RoutingMode.Merge)
                isDouble = false;
        }
        return slots;
    }

    private static ImmutableArray<RoutingBlock> Repair(RoutingBlock[] blocks)
    {
        bool isDouble = false;
        RoutingMode previous = RoutingMode.Upper;
        for (int i = 0; i < BlockCount - 1; i++)
        {
            RoutingMode mode = (RoutingMode)blocks[i].Routing;
            bool legal = i == 0
                ? mode is RoutingMode.Upper or RoutingMode.Split
                : isDouble
                    ? mode is RoutingMode.Upper or RoutingMode.Lower
                        || (mode is RoutingMode.Parallel or RoutingMode.Merge && previous is RoutingMode.Upper or RoutingMode.Lower)
                    : mode is RoutingMode.Upper or RoutingMode.Split;
            if (!legal)
            {
                mode = RoutingMode.Upper;
                blocks[i] = blocks[i] with { Routing = (byte)mode, UpperInput = (byte)InputConnection.Stereo, LowerInput = (byte)InputConnection.Stereo };
            }

            bool doubleHere = isDouble || mode == RoutingMode.Split;
            blocks[i] = blocks[i] with { PatchType = (byte)(doubleHere ? 1 : 0) };
            if (mode == RoutingMode.Split)
                isDouble = true;
            else if (mode == RoutingMode.Merge)
                isDouble = false;
            previous = mode;
        }

        // Note 3: the output block merges an open double path, otherwise it is upper.
        blocks[^1] = blocks[^1] with { Routing = (byte)(isDouble ? RoutingMode.Merge : RoutingMode.Upper), PatchType = (byte)(isDouble ? 1 : 0) };
        return [.. blocks];
    }

    private static (bool IsDouble, RoutingMode Previous) StateBefore(IReadOnlyList<RoutingBlock> blocks, int position)
    {
        bool isDouble = false;
        RoutingMode previous = RoutingMode.Upper;
        for (int i = 0; i < position; i++)
        {
            previous = (RoutingMode)blocks[i].Routing;
            if (previous == RoutingMode.Split)
                isDouble = true;
            else if (previous == RoutingMode.Merge)
                isDouble = false;
        }
        return (isDouble, previous);
    }

    private static RoutingBlock Block(RoutingBlockType type) => new((byte)type, 0, 0, (byte)RoutingMode.Upper, 0);

    private static void RequireBlocks(IReadOnlyList<RoutingBlock> blocks)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        if (blocks.Count != BlockCount)
            throw new ArgumentException($"The routing has {BlockCount} blocks.", nameof(blocks));
    }
}
