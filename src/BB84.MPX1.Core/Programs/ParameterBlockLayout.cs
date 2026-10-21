using System.Collections.Immutable;

using BB84.MPX1.Core.Protocol;
using BB84.MPX1.Core.Values;

namespace BB84.MPX1.Core.Programs;

/// <summary>One parameter inside a program data block.</summary>
/// <param name="Index">Index in the algorithm's parameter list (used by soft values and patches).</param>
/// <param name="Description">The parameter description.</param>
/// <param name="Option">The option description, or <see langword="null"/>.</param>
/// <param name="Offset">Byte offset inside the block.</param>
/// <param name="Size">Bytes used, including option bytes (PAR-002).</param>
public sealed record BlockParameter(int Index, ParameterDescription Description, ParameterDescription? Option, int Offset, int Size)
{
    /// <summary>Gets the parameter type.</summary>
    public ushort Type => Description.Type;

    /// <summary>Gets the trimmed name.</summary>
    public string Name => Description.Name.Trim();

    /// <summary>Gets a value indicating whether the parameter has a numeric value (1 or 2 bytes).</summary>
    public bool IsNumeric => Description.Size is 1 or 2;
}

/// <summary>
/// Locates parameters inside a program data block (an effect block or a modulator block) by walking the
/// algorithm's parameter types and summing each one's size, option bytes included (PRG-003). Other bytes in the
/// block are left untouched.
/// </summary>
public sealed class ParameterBlockLayout
{
    private ParameterBlockLayout(ImmutableArray<BlockParameter> parameters, int capacity)
    {
        Parameters = parameters;
        Capacity = capacity;
    }

    /// <summary>Gets the parameters in list order.</summary>
    public ImmutableArray<BlockParameter> Parameters { get; }

    /// <summary>Gets the block size in bytes.</summary>
    public int Capacity { get; }

    /// <summary>Gets the bytes used by the parameters.</summary>
    public int UsedBytes => Parameters.IsEmpty ? 0 : Parameters[^1].Offset + Parameters[^1].Size;

    /// <summary>Builds the layout of a block.</summary>
    /// <param name="types">The algorithm's parameter types in order.</param>
    /// <param name="describe">Returns a learned description, or <see langword="null"/> when unknown.</param>
    /// <param name="capacity">The block size in bytes.</param>
    /// <exception cref="ArgumentException">A type is unknown, or the parameters do not fit into the block.</exception>
    public static ParameterBlockLayout Create(IEnumerable<ushort> types, Func<ushort, ParameterDescription?> describe, int capacity)
    {
        ArgumentNullException.ThrowIfNull(types);
        ArgumentNullException.ThrowIfNull(describe);
        ArgumentOutOfRangeException.ThrowIfNegative(capacity);

        var parameters = ImmutableArray.CreateBuilder<BlockParameter>();
        int offset = 0;
        foreach (ushort type in types)
        {
            ParameterDescription description = describe(type)
                ?? throw new ArgumentException($"Parameter type 0x{type:X4} is not in the learned database.", nameof(types));
            ParameterDescription? option = null;
            if (description.OptionType is ushort optionType)
            {
                option = describe(optionType)
                    ?? throw new ArgumentException($"Option type 0x{optionType:X4} of 0x{type:X4} is not in the learned database.", nameof(types));
            }

            int size = description.Size + (option?.Size ?? 0);
            if (offset + size > capacity)
                throw new ArgumentException($"Parameters need more than the {capacity} bytes of the block (0x{type:X4} at offset {offset}).", nameof(types));
            parameters.Add(new BlockParameter(parameters.Count, description, option, offset, size));
            offset += size;
        }
        return new ParameterBlockLayout(parameters.ToImmutable(), capacity);
    }

    /// <summary>Returns the bytes of one parameter, option bytes included.</summary>
    /// <param name="block">The block data.</param>
    /// <param name="index">The parameter index.</param>
    public ReadOnlySpan<byte> Slice(ReadOnlySpan<byte> block, int index)
    {
        BlockParameter parameter = Parameters[index];
        RequireCapacity(block.Length);
        return block.Slice(parameter.Offset, parameter.Size);
    }

    /// <summary>Decodes a numeric parameter.</summary>
    /// <param name="block">The block data.</param>
    /// <param name="index">The parameter index.</param>
    /// <exception cref="ArgumentException">The parameter is not numeric.</exception>
    public ParameterValue Read(ReadOnlySpan<byte> block, int index)
    {
        BlockParameter parameter = Parameters[index];
        return ParameterValueCodec.Decode(parameter.Description, parameter.Option, Slice(block, index));
    }

    /// <summary>Encodes a numeric parameter (and option) into a copy of the block.</summary>
    /// <param name="block">The block data.</param>
    /// <param name="index">The parameter index.</param>
    /// <param name="value">The value.</param>
    /// <param name="option">The option value, or <see langword="null"/> to keep the stored one.</param>
    /// <exception cref="ArgumentOutOfRangeException">The value is outside the parameter's limits.</exception>
    public byte[] Write(ReadOnlySpan<byte> block, int index, int value, int? option = null)
    {
        BlockParameter parameter = Parameters[index];
        if (option is null && parameter.Option is not null)
            option = Read(block, index).Option;
        return WriteRaw(block, index, ParameterValueCodec.Encode(parameter.Description, parameter.Option, value, option));
    }

    /// <summary>Replaces the raw bytes of one parameter in a copy of the block.</summary>
    /// <param name="block">The block data.</param>
    /// <param name="index">The parameter index.</param>
    /// <param name="data">Exactly <see cref="BlockParameter.Size"/> bytes.</param>
    public byte[] WriteRaw(ReadOnlySpan<byte> block, int index, ReadOnlySpan<byte> data)
    {
        BlockParameter parameter = Parameters[index];
        RequireCapacity(block.Length);
        if (data.Length != parameter.Size)
            throw new ArgumentException($"{parameter.Name} takes {parameter.Size} bytes, got {data.Length}.", nameof(data));
        byte[] copy = block.ToArray();
        data.CopyTo(copy.AsSpan(parameter.Offset));
        return copy;
    }

    private void RequireCapacity(int length)
    {
        if (length < Capacity)
            throw new ArgumentException($"The block must be {Capacity} bytes, got {length}.", nameof(length));
    }
}

/// <summary>
/// Location of the LFO, arpeggiator, ADSR, random, A/B, sample-and-hold and envelope blocks inside
/// <see cref="ProgramData.ModulatorData"/> (spec 4.5, bytes 722–843). Their parameter lists come from Report
/// Effect Parameters (<c>18</c>) with effect types 7–16.
/// </summary>
public static class ModulatorBlocks
{
    /// <summary>Gets the blocks in dump order.</summary>
    public static ImmutableArray<ModulatorBlock> All { get; } =
    [
        new(EffectType.Lfo1, "LFO 1", 0, 8),
        new(EffectType.Lfo2, "LFO 2", 8, 8),
        new(EffectType.Arpeggiator, "Arpeggiator", 16, 5),
        new(EffectType.Adsr1, "ADSR 1", 21, 9),
        new(EffectType.Adsr2, "ADSR 2", 30, 9),
        new(EffectType.Random, "Random", 39, 4),
        new(EffectType.AB, "A/B", 43, 5),
        new(EffectType.SampleAndHold, "Sample & Hold", 48, 5),
        new(EffectType.EnvelopeGenerator1, "Envelope 1", 53, 4),
        new(EffectType.EnvelopeGenerator2, "Envelope 2", 57, 4),
    ];

    /// <summary>Returns the block of a controller type, or <see langword="null"/>.</summary>
    /// <param name="type">Effect type 7–16.</param>
    public static ModulatorBlock? Find(EffectType type) => All.FirstOrDefault(b => b.Type == type);
}

/// <summary>One modulator block.</summary>
/// <param name="Type">The controller type (Report Effect Parameters effect type).</param>
/// <param name="Name">Display name.</param>
/// <param name="Offset">Offset inside the modulator data.</param>
/// <param name="Size">Size in bytes.</param>
public sealed record ModulatorBlock(EffectType Type, string Name, int Offset, int Size);

/// <summary>Program numbering: presets 0–199, user registers 200–249, active <c>0xFFFF</c> (PRG-001, PRG-004).</summary>
public static class ProgramSlots
{
    /// <summary>Number of stored programs.</summary>
    public const int Count = DatabaseDump.ProgramCount;

    /// <summary>First user register.</summary>
    public const int FirstUser = 200;

    /// <summary>Returns <see langword="true"/> for a factory preset slot; writing one needs confirmation (PRG-004).</summary>
    /// <param name="number">Program number.</param>
    public static bool IsPreset(int number) => number is >= 0 and < FirstUser;

    /// <summary>Returns <see langword="true"/> for a user register.</summary>
    /// <param name="number">Program number.</param>
    public static bool IsUser(int number) => number is >= FirstUser and < Count;

    /// <summary>Returns <see langword="true"/> for a stored slot or the active program.</summary>
    /// <param name="number">Program number.</param>
    public static bool IsValid(int number) => number is >= 0 and < Count || number == Request.ActiveProgram;
}
