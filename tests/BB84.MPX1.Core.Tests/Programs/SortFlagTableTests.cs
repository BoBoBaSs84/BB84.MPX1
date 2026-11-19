using BB84.MPX1.Core.Programs;
using BB84.MPX1.Core.Protocol;

namespace BB84.MPX1.Core.Tests.Programs;

[TestClass]
public sealed class SortFlagTableTests
{
    // The first table bytes of the V1.00 unit tested in issue #25 (programs 1-2), then a running pattern.
    private static byte[] Table()
    {
        byte[] table = new byte[SortFlagTable.Size];
        for (int i = 0; i < table.Length; i++)
            table[i] = (byte)(i * 7);
        new byte[] { 0x1E, 0x04, 0xCC, 0x10, 0x01, 0xFE }.CopyTo(table, 0);
        return table;
    }

    [TestMethod]
    [Description("PRO-018: the table is 250 entries of effect word (little-endian) and source byte")]
    public void ToBytes_FromBytes_RoundTrip()
    {
        byte[] table = Table();

        var entries = SortFlagTable.FromBytes(table);

        Assert.HasCount(DatabaseDump.ProgramCount, entries);
        Assert.AreEqual(new ProgramCategories((EffectCategories)0x041E, (SourceCategories)0xCC), entries[0]);
        Assert.AreSequenceEqual(table, SortFlagTable.ToBytes(entries));
        Assert.ThrowsExactly<ArgumentException>(() => SortFlagTable.FromBytes(new byte[10]));
    }

    [TestMethod]
    [Description("PRO-018: a V1.00 unit stores received bytes 2-99 as 0-97 and FF FF as 98-99, the rest unchanged (spec 9, E18)")]
    public void ReceivedByV100_ShiftsFirstBlock()
    {
        byte[] sent = Table();

        byte[] stored = SortFlagTable.ToBytes(SortFlagTable.ReceivedByV100(SortFlagTable.FromBytes(sent)));

        Assert.AreSequenceEqual(new byte[] { 0xCC, 0x10, 0x01, 0xFE }, stored[..4]);
        Assert.AreSequenceEqual(sent[2..100], stored[..98]);
        Assert.AreSequenceEqual(new byte[] { 0xFF, 0xFF }, stored[98..100]);
        Assert.AreSequenceEqual(sent[100..], stored[100..]);
    }

    [TestMethod]
    [Description("PRO-018: a compensated dump is stored by V1.00 as wanted, except programs 33 and 34 (spec 9, E18)")]
    public void CompensateForV100_StoresWantedExceptUnwritable()
    {
        var wanted = SortFlagTable.FromBytes(Table());

        var stored = SortFlagTable.ReceivedByV100(SortFlagTable.CompensateForV100(wanted));

        int[] differing = [.. Enumerable.Range(0, DatabaseDump.ProgramCount).Where(i => stored[i] != wanted[i])];
        Assert.AreSequenceEqual(new[] { 32, 33 }, differing);
        Assert.AreSequenceEqual(new ushort[] { 32, 33 }, SortFlagTable.UnwritableOnV100.ToArray());
        Assert.IsTrue(SortFlagTable.HasReceiveOffset(1, 0));
        Assert.IsFalse(SortFlagTable.HasReceiveOffset(1, 10));
    }
}
