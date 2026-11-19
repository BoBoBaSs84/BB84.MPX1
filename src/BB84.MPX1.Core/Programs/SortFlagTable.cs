using System.Collections.Immutable;

using BB84.MPX1.Core.Protocol;

namespace BB84.MPX1.Core.Programs;

/// <summary>
/// The sort-flag table carried by the Database Dump (PRO-018) and how a V1.00 unit stores a received one
/// (spec section 9, E18).
/// </summary>
/// <remarks>
/// A V1.00 unit (Nov 21 1996) drops the first two table bytes it receives and fills the end of the first
/// <see cref="FirstBlockSize"/>-byte block with <c>FF</c>: it stores received bytes 2–99 as bytes 0–97, <c>FF FF</c>
/// as bytes 98–99, and bytes 100–749 unchanged. Its transmitted table has no such offset. Bytes 98 and 99 (the
/// source flags of program 33 and the low effect byte of program 34) can therefore not be written on V1.00.
/// </remarks>
public static class SortFlagTable
{
    /// <summary>Size of the table in bytes.</summary>
    public const int Size = DatabaseDump.ProgramCount * ProgramCategories.Size;

    /// <summary>Size of the first block, which a V1.00 unit receives with an offset.</summary>
    public const int FirstBlockSize = 100;

    /// <summary>Number of leading bytes a V1.00 unit drops.</summary>
    public const int ReceiveOffset = 2;

    /// <summary>Gets the programs (0-based) whose sort flags a V1.00 unit cannot take from a Database Dump.</summary>
    public static ImmutableArray<ushort> UnwritableOnV100 { get; } = [(FirstBlockSize - ReceiveOffset) / ProgramCategories.Size, (FirstBlockSize - 1) / ProgramCategories.Size];

    /// <summary>Returns whether the firmware stores a received Database Dump with the first-block offset (V1.00).</summary>
    /// <param name="majorVersion">Firmware major version.</param>
    /// <param name="minorVersion">Firmware minor version.</param>
    public static bool HasReceiveOffset(byte majorVersion, byte minorVersion) => majorVersion == 1 && minorVersion == 0;

    /// <summary>Returns the table as bytes: per program the effect flags (little-endian word), then the source flags.</summary>
    /// <param name="entries">Exactly <see cref="DatabaseDump.ProgramCount"/> entries.</param>
    public static byte[] ToBytes(IReadOnlyList<ProgramCategories> entries)
    {
        RequireEntries(entries);
        byte[] table = new byte[Size];
        for (int i = 0; i < entries.Count; i++)
        {
            ushort effects = (ushort)entries[i].Effects;
            table[i * ProgramCategories.Size] = (byte)effects;
            table[(i * ProgramCategories.Size) + 1] = (byte)(effects >> 8);
            table[(i * ProgramCategories.Size) + 2] = (byte)entries[i].Sources;
        }
        return table;
    }

    /// <summary>Returns the entries of a table in bytes.</summary>
    /// <param name="table">Exactly <see cref="Size"/> bytes.</param>
    public static ImmutableArray<ProgramCategories> FromBytes(ReadOnlySpan<byte> table)
    {
        if (table.Length != Size)
            throw new ArgumentException($"The sort-flag table has {Size} bytes.", nameof(table));
        var entries = ImmutableArray.CreateBuilder<ProgramCategories>(DatabaseDump.ProgramCount);
        for (int i = 0; i < Size; i += ProgramCategories.Size)
            entries.Add(new((EffectCategories)(table[i] | (table[i + 1] << 8)), (SourceCategories)table[i + 2]));
        return entries.MoveToImmutable();
    }

    /// <summary>Returns what a V1.00 unit stores when it receives <paramref name="sent"/>.</summary>
    /// <param name="sent">The entries sent.</param>
    public static ImmutableArray<ProgramCategories> ReceivedByV100(IReadOnlyList<ProgramCategories> sent)
    {
        byte[] table = ToBytes(sent);
        byte[] stored = [.. table];
        Array.Copy(table, ReceiveOffset, stored, 0, FirstBlockSize - ReceiveOffset);
        stored.AsSpan(FirstBlockSize - ReceiveOffset, ReceiveOffset).Fill(0xFF);
        return FromBytes(stored);
    }

    /// <summary>
    /// Returns the entries to send so that a V1.00 unit stores <paramref name="wanted"/>, except for the programs in
    /// <see cref="UnwritableOnV100"/>.
    /// </summary>
    /// <param name="wanted">The entries the unit should store.</param>
    public static ImmutableArray<ProgramCategories> CompensateForV100(IReadOnlyList<ProgramCategories> wanted)
    {
        byte[] table = ToBytes(wanted);
        byte[] sent = [.. table];
        Array.Copy(table, 0, sent, ReceiveOffset, FirstBlockSize - ReceiveOffset);
        return FromBytes(sent);
    }

    private static void RequireEntries(IReadOnlyList<ProgramCategories> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (entries.Count != DatabaseDump.ProgramCount)
            throw new ArgumentException($"The database has {DatabaseDump.ProgramCount} entries.", nameof(entries));
    }
}
