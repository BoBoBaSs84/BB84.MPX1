using System.Collections.Immutable;

using BB84.MPX1.Core.Programs;

namespace BB84.MPX1.Core.Protocol;

/// <summary>Database Dump message, class <c>16</c> (PRO-018): sort flags of all 250 programs.</summary>
/// <param name="Device">The device ID.</param>
/// <param name="Entries">Exactly <see cref="ProgramCount"/> entries, program 0 first.</param>
public sealed record DatabaseDump(DeviceId Device, ImmutableArray<ProgramCategories> Entries) : LexiconMessage(Device)
{
    /// <summary>Number of programs in the database.</summary>
    public const int ProgramCount = 250;

    /// <inheritdoc/>
    public override MessageClass MessageClass => MessageClass.DatabaseDump;

    internal override void WritePayload(NibbleWriter writer)
    {
        if (Entries.IsDefault || Entries.Length != ProgramCount)
            throw new InvalidOperationException($"{nameof(Entries)} must contain exactly {ProgramCount} elements.");
        foreach (ProgramCategories entry in Entries)
            entry.Write(writer);
    }

    internal static DatabaseDump Read(DeviceId device, ref NibbleReader reader)
    {
        var entries = ImmutableArray.CreateBuilder<ProgramCategories>(ProgramCount);
        for (int i = 0; i < ProgramCount; i++)
            entries.Add(ProgramCategories.Read(ref reader));
        return new(device, entries.MoveToImmutable());
    }
}

/// <summary>
/// The parameter list of one effect or controller as reported by classes <c>18</c> and <c>19</c>.
/// The device always transmits <see cref="SlotCount"/> slots; only the first <see cref="Count"/> are used.
/// </summary>
/// <param name="Name">Effect/algorithm name (11 characters).</param>
/// <param name="Count">Number of used parameter slots.</param>
/// <param name="Slots">All <see cref="SlotCount"/> parameter-type slots as transmitted.</param>
public sealed record EffectParameterList(string Name, byte Count, ImmutableArray<ushort> Slots)
{
    /// <summary>Number of parameter slots in the message.</summary>
    public const int SlotCount = 30;

    /// <summary>Length of <see cref="Name"/>.</summary>
    public const int NameLength = 11;

    /// <summary>Gets the parameter type numbers that are in use.</summary>
    public ImmutableArray<ushort> ParameterTypes => Slots[..Math.Min(Count, Slots.Length)];

    internal void Write(NibbleWriter writer)
    {
        if (Slots.IsDefault || Slots.Length != SlotCount)
            throw new InvalidOperationException($"{nameof(Slots)} must contain exactly {SlotCount} elements.");
        writer.WriteFixedString(Name, NameLength);
        writer.WriteByte(Count);
        foreach (ushort slot in Slots)
            writer.WriteWord(slot);
    }

    internal static EffectParameterList Read(ref NibbleReader reader)
        => new(reader.ReadString(NameLength), reader.ReadByte(), reader.ReadWords(SlotCount));
}

/// <summary>Report Effect Parameters message, class <c>18</c> (PRO-019).</summary>
/// <param name="Device">The device ID.</param>
/// <param name="EffectType">The effect or controller type.</param>
/// <param name="Number">The effect/controller (algorithm) number.</param>
/// <param name="Parameters">The parameter list.</param>
public sealed record EffectParameters(DeviceId Device, EffectType EffectType, byte Number, EffectParameterList Parameters) : LexiconMessage(Device)
{
    /// <inheritdoc/>
    public override MessageClass MessageClass => MessageClass.EffectParameters;

    internal override void WritePayload(NibbleWriter writer)
    {
        writer.WriteByte((byte)EffectType);
        writer.WriteByte(Number);
        Parameters.Write(writer);
    }

    internal static EffectParameters Read(DeviceId device, ref NibbleReader reader)
        => new(device, (EffectType)reader.ReadByte(), reader.ReadByte(), EffectParameterList.Read(ref reader));
}

/// <summary>Report All Effect Parameters message, class <c>19</c> (PRO-020).</summary>
/// <param name="Device">The device ID.</param>
/// <param name="ProgramNumber">Program number 0–249 or <c>0xFFFF</c> (active).</param>
/// <param name="Effects">Six lists: Pitch, Chorus, EQ, Mod, Reverb, Delay.</param>
public sealed record AllEffectParameters(DeviceId Device, ushort ProgramNumber, ImmutableArray<EffectParameterList> Effects) : LexiconMessage(Device)
{
    /// <inheritdoc/>
    public override MessageClass MessageClass => MessageClass.AllEffectParameters;

    internal override void WritePayload(NibbleWriter writer)
    {
        if (Effects.IsDefault || Effects.Length != ProgramData.EffectBlockCount)
            throw new InvalidOperationException($"{nameof(Effects)} must contain exactly {ProgramData.EffectBlockCount} elements.");
        writer.WriteWord(ProgramNumber);
        foreach (EffectParameterList list in Effects)
            list.Write(writer);
    }

    internal static AllEffectParameters Read(DeviceId device, ref NibbleReader reader)
    {
        ushort program = reader.ReadWord();
        var effects = ImmutableArray.CreateBuilder<EffectParameterList>(ProgramData.EffectBlockCount);
        for (int i = 0; i < ProgramData.EffectBlockCount; i++)
            effects.Add(EffectParameterList.Read(ref reader));
        return new(device, program, effects.MoveToImmutable());
    }
}

/// <summary>Program Information message, class <c>1A</c> (PRO-021).</summary>
/// <param name="Device">The device ID.</param>
/// <param name="ProgramNumber">Program number 0–249.</param>
/// <param name="Name">Program name (12 characters).</param>
/// <param name="Algorithms">Algorithm numbers: Pitch, Chorus, EQ, Mod, Reverb, Delay.</param>
public sealed record ProgramInformation(DeviceId Device, ushort ProgramNumber, string Name, ImmutableArray<byte> Algorithms) : LexiconMessage(Device)
{
    /// <inheritdoc/>
    public override MessageClass MessageClass => MessageClass.ProgramInformation;

    internal override void WritePayload(NibbleWriter writer)
    {
        if (Algorithms.IsDefault || Algorithms.Length != ProgramData.EffectBlockCount)
            throw new InvalidOperationException($"{nameof(Algorithms)} must contain exactly {ProgramData.EffectBlockCount} elements.");
        writer.WriteWord(ProgramNumber);
        writer.WriteFixedString(Name, ProgramData.NameLength);
        writer.WriteBytes(Algorithms.AsSpan());
    }

    internal static ProgramInformation Read(DeviceId device, ref NibbleReader reader)
        => new(device, reader.ReadWord(), reader.ReadString(ProgramData.NameLength), [.. reader.ReadBytes(ProgramData.EffectBlockCount)]);
}

/// <summary>Program Dump message, class <c>1B</c> (PRO-022).</summary>
/// <param name="Device">The device ID.</param>
/// <param name="ProgramNumber">Program number 0–249 or <c>0xFFFF</c> (active).</param>
/// <param name="Program">The program contents.</param>
public sealed record ProgramDump(DeviceId Device, ushort ProgramNumber, ProgramData Program) : LexiconMessage(Device)
{
    /// <summary>Total frame length in bytes.</summary>
    public const int FrameLength = Mpx1Frame.HeaderLength + ((2 + ProgramData.Size) * 2) + 1;

    /// <inheritdoc/>
    public override MessageClass MessageClass => MessageClass.ProgramDump;

    internal override void WritePayload(NibbleWriter writer)
    {
        writer.WriteWord(ProgramNumber);
        Program.Write(writer);
    }

    internal static ProgramDump Read(DeviceId device, ref NibbleReader reader)
        => new(device, reader.ReadWord(), ProgramData.Read(ref reader));
}
