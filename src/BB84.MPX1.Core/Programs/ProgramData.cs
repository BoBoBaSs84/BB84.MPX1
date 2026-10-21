using System.Collections.Immutable;

using BB84.MPX1.Core.Protocol;

namespace BB84.MPX1.Core.Programs;

/// <summary>Program sort flags (classification used by the MPX 1 database).</summary>
/// <param name="Effects">Effect type flags.</param>
/// <param name="Sources">Source type flags.</param>
public readonly record struct ProgramCategories(EffectCategories Effects, SourceCategories Sources)
{
    /// <summary>Size in bytes.</summary>
    public const int Size = 3;

    internal void Write(NibbleWriter writer)
    {
        writer.WriteWord((ushort)Effects);
        writer.WriteByte((byte)Sources);
    }

    internal static ProgramCategories Read(ref NibbleReader reader)
        => new((EffectCategories)reader.ReadWord(), (SourceCategories)reader.ReadByte());
}

/// <summary>Audio routing of one block (input, pitch, chorus, EQ, mod, delay, reverb, output).</summary>
/// <param name="EffectType">Effect type.</param>
/// <param name="UpperInput">Upper input connection.</param>
/// <param name="LowerInput">Lower input connection.</param>
/// <param name="Routing">Routing.</param>
/// <param name="PatchType">Patch type.</param>
public readonly record struct RoutingBlock(byte EffectType, byte UpperInput, byte LowerInput, byte Routing, byte PatchType)
{
    /// <summary>Size in bytes.</summary>
    public const int Size = 5;

    internal void Write(NibbleWriter writer)
        => writer.WriteBytes([EffectType, UpperInput, LowerInput, Routing, PatchType]);

    internal static RoutingBlock Read(ref NibbleReader reader)
        => new(reader.ReadByte(), reader.ReadByte(), reader.ReadByte(), reader.ReadByte(), reader.ReadByte());
}

/// <summary>A soft row assignment: an effect/controller and a parameter index within it.</summary>
/// <param name="EffectType">Effect or controller type.</param>
/// <param name="ParameterIndex">Index into the effect's parameters (not a parameter type number).</param>
public readonly record struct SoftValue(byte EffectType, byte ParameterIndex)
{
    /// <summary>Size in bytes.</summary>
    public const int Size = 2;
}

/// <summary>A program patch: maps a controller onto an effect parameter.</summary>
/// <param name="SourceController">Source controller (Appendix A); <c>0xFF</c> = unassigned.</param>
/// <param name="SourceMinimum">Source minimum value.</param>
/// <param name="SourceMid">Source mid value.</param>
/// <param name="SourceMaximum">Source maximum value.</param>
/// <param name="DestinationEffectType">Destination effect/controller type; <c>0xFF</c> = unassigned.</param>
/// <param name="DestinationIndex">Destination parameter index.</param>
/// <param name="DestinationMinimum">Destination minimum value.</param>
/// <param name="DestinationMid">Destination mid value.</param>
/// <param name="DestinationMaximum">Destination maximum value.</param>
public readonly record struct Patch(
    byte SourceController,
    byte SourceMinimum,
    byte SourceMid,
    byte SourceMaximum,
    byte DestinationEffectType,
    byte DestinationIndex,
    ushort DestinationMinimum,
    ushort DestinationMid,
    ushort DestinationMaximum)
{
    /// <summary>Size in bytes.</summary>
    public const int Size = 12;

    internal void Write(NibbleWriter writer)
    {
        writer.WriteBytes([SourceController, SourceMinimum, SourceMid, SourceMaximum, DestinationEffectType, DestinationIndex]);
        writer.WriteWord(DestinationMinimum);
        writer.WriteWord(DestinationMid);
        writer.WriteWord(DestinationMaximum);
    }

    internal static Patch Read(ref NibbleReader reader)
        => new(reader.ReadByte(), reader.ReadByte(), reader.ReadByte(), reader.ReadByte(), reader.ReadByte(), reader.ReadByte(),
            reader.ReadWord(), reader.ReadWord(), reader.ReadWord());
}

/// <summary>The knob (custom controller) of a program.</summary>
/// <param name="Value">Initial value.</param>
/// <param name="Minimum">Minimum value.</param>
/// <param name="Maximum">Maximum value.</param>
/// <param name="Name">Controller name (9 characters).</param>
public readonly record struct Knob(byte Value, byte Minimum, byte Maximum, string Name)
{
    /// <summary>Size in bytes.</summary>
    public const int Size = 12;

    /// <summary>Length of <see cref="Name"/>.</summary>
    public const int NameLength = 9;
}

/// <summary>
/// The contents of one MPX 1 program as carried by the Program Dump (<c>1B</c>), spec section 4.5.
/// </summary>
public sealed record ProgramData
{
    /// <summary>Number of effect blocks.</summary>
    public const int EffectBlockCount = 6;
    /// <summary>Size of one effect data block in bytes.</summary>
    public const int EffectDataSize = 32;
    /// <summary>Number of routing blocks.</summary>
    public const int RoutingBlockCount = 8;
    /// <summary>Number of soft row values.</summary>
    public const int SoftValueCount = 10;
    /// <summary>Number of patches.</summary>
    public const int PatchCount = 5;
    /// <summary>Length of the program name.</summary>
    public const int NameLength = 12;
    /// <summary>Size of the modulator block (LFOs through envelope generators).</summary>
    public const int ModulatorDataSize = 61;
    /// <summary>Size of the program data in bytes (excluding the program number).</summary>
    public const int Size = 417;

    /// <summary>Gets the six 32-byte effect data blocks: Pitch, Chorus, EQ, Mod, Reverb, Delay.</summary>
    public required ImmutableArray<ImmutableArray<byte>> EffectData { get; init; }
    /// <summary>Gets the sort categories.</summary>
    public required ProgramCategories Categories { get; init; }
    /// <summary>Gets the eight routing blocks: input, pitch, chorus, EQ, mod, delay, reverb, output.</summary>
    public required ImmutableArray<RoutingBlock> Routing { get; init; }
    /// <summary>Gets the algorithm numbers: Pitch, Chorus, EQ, Mod, Reverb, Delay.</summary>
    public required ImmutableArray<byte> Algorithms { get; init; }
    /// <summary>Gets the program name (12 characters, space padded).</summary>
    public required string Name { get; init; }
    /// <summary>Gets the effect on/bypass status.</summary>
    public required EffectStatus EffectStatus { get; init; }
    /// <summary>Gets the ten soft row assignments.</summary>
    public required ImmutableArray<SoftValue> SoftValues { get; init; }
    /// <summary>Gets the tempo in BPM (41–400).</summary>
    public required ushort Tempo { get; init; }
    /// <summary>Gets the tempo source (0 internal, 1 MIDI).</summary>
    public required byte TempoSource { get; init; }
    /// <summary>Gets the beat value (0 eighth … 6 whole note).</summary>
    public required byte BeatValue { get; init; }
    /// <summary>Gets the tap source controller.</summary>
    public required byte TapSource { get; init; }
    /// <summary>Gets the tap average (1–8 beats).</summary>
    public required byte TapAverage { get; init; }
    /// <summary>Gets the tap source level (0–127).</summary>
    public required byte TapSourceLevel { get; init; }
    /// <summary>Gets the meter selection (0–24).</summary>
    public required byte Meter { get; init; }
    /// <summary>Gets the master level (<c>A0</c> off, <c>A1</c>–<c>FF</c> −95…−1 dB, <c>00</c> 0 dB).</summary>
    public required byte MasterLevel { get; init; }
    /// <summary>Gets the master mix (0–100 %).</summary>
    public required byte MasterMix { get; init; }
    /// <summary>Gets the five patches.</summary>
    public required ImmutableArray<Patch> Patches { get; init; }
    /// <summary>Gets the knob.</summary>
    public required Knob Knob { get; init; }
    /// <summary>Gets the raw modulator block: LFO 1, LFO 2, arpeggiator, ADSR 1/2, random, A/B, S&amp;H, envelope 1/2.</summary>
    public required ImmutableArray<byte> ModulatorData { get; init; }

    /// <summary>Pitch algorithm value that marks a cleared (empty) program.</summary>
    public const byte ClearedAlgorithm = 0xFF;

    /// <summary>Gets a value indicating whether the unit treats the program as cleared (pitch algorithm <c>0xFF</c>).</summary>
    public bool IsCleared => !Algorithms.IsDefaultOrEmpty && Algorithms[0] == ClearedAlgorithm;

    /// <summary>Returns a copy with one effect data block replaced.</summary>
    /// <param name="block">Block index 0–5 (Pitch through Delay).</param>
    /// <param name="data">Exactly <see cref="EffectDataSize"/> bytes.</param>
    public ProgramData WithEffectData(int block, ReadOnlySpan<byte> data)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(block);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(block, EffectBlockCount);
        if (data.Length != EffectDataSize)
            throw new ArgumentException($"Effect data must be {EffectDataSize} bytes.", nameof(data));
        return this with { EffectData = EffectData.SetItem(block, [.. data]) };
    }

    /// <summary>Returns a copy with one modulator block replaced.</summary>
    /// <param name="block">The modulator block.</param>
    /// <param name="data">Exactly <see cref="ModulatorBlock.Size"/> bytes.</param>
    public ProgramData WithModulatorBlock(ModulatorBlock block, ReadOnlySpan<byte> data)
    {
        ArgumentNullException.ThrowIfNull(block);
        if (data.Length != block.Size)
            throw new ArgumentException($"{block.Name} data must be {block.Size} bytes.", nameof(data));
        byte[] copy = [.. ModulatorData];
        data.CopyTo(copy.AsSpan(block.Offset));
        return this with { ModulatorData = [.. copy] };
    }

    // Offsets of the soft values and patches in the program bytes (spec 4.5).
    private const int SoftValuesOffset = (EffectBlockCount * EffectDataSize) + ProgramCategories.Size + (RoutingBlockCount * RoutingBlock.Size) + EffectBlockCount + NameLength + 1;
    private const int PatchesOffset = SoftValuesOffset + (SoftValueCount * SoftValue.Size) + 2 + 8;

    /// <summary>Returns the soft values in Soft Row dump format (DMP-010).</summary>
    public byte[] GetSoftRowDump() => ToBytes().AsSpan(SoftValuesOffset, SoftValueCount * SoftValue.Size).ToArray();

    /// <summary>Returns the patches in Patches dump format (DMP-009).</summary>
    public byte[] GetPatchesDump() => ToBytes().AsSpan(PatchesOffset, PatchCount * Patch.Size).ToArray();

    /// <summary>Returns a copy with the soft values replaced from a Soft Row dump (DMP-010).</summary>
    /// <param name="dump">20 bytes.</param>
    public ProgramData WithSoftRowDump(ReadOnlySpan<byte> dump) => Replace(SoftValuesOffset, SoftValueCount * SoftValue.Size, dump);

    /// <summary>Returns a copy with the patches replaced from a Patches dump (DMP-009).</summary>
    /// <param name="dump">60 bytes.</param>
    public ProgramData WithPatchesDump(ReadOnlySpan<byte> dump) => Replace(PatchesOffset, PatchCount * Patch.Size, dump);

    private ProgramData Replace(int offset, int length, ReadOnlySpan<byte> data)
    {
        if (data.Length != length)
            throw new ArgumentException($"Expected {length} bytes, got {data.Length}.", nameof(data));
        byte[] bytes = ToBytes();
        data.CopyTo(bytes.AsSpan(offset));
        return FromBytes(bytes);
    }

    /// <summary>Returns the bytes of one modulator block.</summary>
    /// <param name="block">The modulator block.</param>
    public ReadOnlySpan<byte> GetModulatorBlock(ModulatorBlock block)
    {
        ArgumentNullException.ThrowIfNull(block);
        return ModulatorData.AsSpan(block.Offset, block.Size);
    }

    /// <summary>Returns the program as <see cref="Size"/> raw (un-nibble-ized) bytes in dump order.</summary>
    public byte[] ToBytes()
    {
        NibbleWriter writer = new(Size * 2);
        Write(writer);
        return Nibbles.Decode(writer.ToArray());
    }

    /// <summary>Parses <see cref="Size"/> raw (un-nibble-ized) bytes in dump order.</summary>
    /// <param name="bytes">Exactly <see cref="Size"/> bytes.</param>
    /// <exception cref="Mpx1FormatException">The length is wrong.</exception>
    public static ProgramData FromBytes(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != Size)
            throw new Mpx1FormatException(new Mpx1FormatError(0, $"Program data must be {Size} bytes, got {bytes.Length}"));
        byte[] nibbles = Nibbles.Encode(bytes);
        NibbleReader reader = new(nibbles, 0);
        ProgramData program = Read(ref reader);
        reader.EnsureEnd();
        return program;
    }

    internal void Write(NibbleWriter writer)
    {
        RequireLength(EffectData, EffectBlockCount, nameof(EffectData));
        foreach (ImmutableArray<byte> block in EffectData)
        {
            RequireLength(block, EffectDataSize, nameof(EffectData));
            writer.WriteBytes(block.AsSpan());
        }

        Categories.Write(writer);

        RequireLength(Routing, RoutingBlockCount, nameof(Routing));
        foreach (RoutingBlock block in Routing)
            block.Write(writer);

        RequireLength(Algorithms, EffectBlockCount, nameof(Algorithms));
        writer.WriteBytes(Algorithms.AsSpan());

        writer.WriteFixedString(Name, NameLength);
        writer.WriteByte((byte)EffectStatus);

        RequireLength(SoftValues, SoftValueCount, nameof(SoftValues));
        foreach (SoftValue value in SoftValues)
            writer.WriteBytes([value.EffectType, value.ParameterIndex]);

        writer.WriteWord(Tempo);
        writer.WriteBytes([TempoSource, BeatValue, TapSource, TapAverage, TapSourceLevel, Meter, MasterLevel, MasterMix]);

        RequireLength(Patches, PatchCount, nameof(Patches));
        foreach (Patch patch in Patches)
            patch.Write(writer);

        writer.WriteBytes([Knob.Value, Knob.Minimum, Knob.Maximum]);
        writer.WriteFixedString(Knob.Name, Knob.NameLength);

        RequireLength(ModulatorData, ModulatorDataSize, nameof(ModulatorData));
        writer.WriteBytes(ModulatorData.AsSpan());
    }

    internal static ProgramData Read(ref NibbleReader reader)
    {
        var effectData = ImmutableArray.CreateBuilder<ImmutableArray<byte>>(EffectBlockCount);
        for (int i = 0; i < EffectBlockCount; i++)
            effectData.Add([.. reader.ReadBytes(EffectDataSize)]);

        ProgramCategories categories = ProgramCategories.Read(ref reader);

        var routing = ImmutableArray.CreateBuilder<RoutingBlock>(RoutingBlockCount);
        for (int i = 0; i < RoutingBlockCount; i++)
            routing.Add(RoutingBlock.Read(ref reader));

        ImmutableArray<byte> algorithms = [.. reader.ReadBytes(EffectBlockCount)];
        string name = reader.ReadString(NameLength);
        EffectStatus status = (EffectStatus)reader.ReadByte();

        var softValues = ImmutableArray.CreateBuilder<SoftValue>(SoftValueCount);
        for (int i = 0; i < SoftValueCount; i++)
            softValues.Add(new SoftValue(reader.ReadByte(), reader.ReadByte()));

        ushort tempo = reader.ReadWord();
        byte tempoSource = reader.ReadByte();
        byte beatValue = reader.ReadByte();
        byte tapSource = reader.ReadByte();
        byte tapAverage = reader.ReadByte();
        byte tapSourceLevel = reader.ReadByte();
        byte meter = reader.ReadByte();
        byte masterLevel = reader.ReadByte();
        byte masterMix = reader.ReadByte();

        var patches = ImmutableArray.CreateBuilder<Patch>(PatchCount);
        for (int i = 0; i < PatchCount; i++)
            patches.Add(Patch.Read(ref reader));

        Knob knob = new(reader.ReadByte(), reader.ReadByte(), reader.ReadByte(), reader.ReadString(Knob.NameLength));
        ImmutableArray<byte> modulators = [.. reader.ReadBytes(ModulatorDataSize)];

        return new ProgramData
        {
            EffectData = effectData.MoveToImmutable(),
            Categories = categories,
            Routing = routing.MoveToImmutable(),
            Algorithms = algorithms,
            Name = name,
            EffectStatus = status,
            SoftValues = softValues.MoveToImmutable(),
            Tempo = tempo,
            TempoSource = tempoSource,
            BeatValue = beatValue,
            TapSource = tapSource,
            TapAverage = tapAverage,
            TapSourceLevel = tapSourceLevel,
            Meter = meter,
            MasterLevel = masterLevel,
            MasterMix = masterMix,
            Patches = patches.MoveToImmutable(),
            Knob = knob,
            ModulatorData = modulators,
        };
    }

    private static void RequireLength<T>(ImmutableArray<T> array, int length, string name)
    {
        if (array.IsDefault || array.Length != length)
            throw new InvalidOperationException($"{name} must contain exactly {length} elements.");
    }
}
