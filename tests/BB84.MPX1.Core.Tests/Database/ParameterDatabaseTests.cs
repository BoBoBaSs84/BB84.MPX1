using System.Text.Json;

using BB84.MPX1.Core.Database;
using BB84.MPX1.Core.Protocol;

namespace BB84.MPX1.Core.Tests.Database;

[TestClass]
public sealed class ParameterDatabaseTests
{
    private static readonly DeviceId Device = new(0);
    private static readonly DatabaseKey Key = new(1, 10, "Mar 30 1998", "17:51:03");

    private static ParameterDescription Level(ushort type, string name, short max)
        => new(Device, type, name, 1, ParameterAttributes.ControlLevel, null, [new UnitLimit(0, max, 0x4B)]);

    private static ParameterDescription Leaf(ushort type, string name, ushort size = 1)
        => new(Device, type, name, size, ParameterAttributes.Patchable, null, [new UnitLimit(0, 100, 0x00)]);

    private static ParameterDatabaseBuilder SampleBuilder()
    {
        ParameterDatabaseBuilder builder = new(Key);
        builder.AddDescription(Level(1, "MPX 1", 1));
        builder.AddDescription(Level(2, "Program", 0));
        builder.AddDescription(Level(3, "System", 2));
        builder.AddDescription(Leaf(4, "Mix  "));
        builder.AddDescription(Level(KnownBugs.DeadEndType, "Dead End", 3));
        builder.AddDescription(Leaf(KnownBugs.BrokenSizeType, "Dump", 0));
        builder.AddNode(ControlAddress.Root, 1);
        builder.AddNode(new ControlAddress(0), 2);
        builder.AddNode(new ControlAddress(0, 0), 4);
        builder.AddNode(new ControlAddress(1), 3);
        builder.AddNode(new ControlAddress(1, 0), KnownBugs.BrokenSizeType);
        builder.AddNode(new ControlAddress(1, 2), 3);
        builder.AddNode(new ControlAddress(1, 2, 0), KnownBugs.DeadEndType);
        builder.AddGap(new ControlAddress(1, 1));
        return builder;
    }

    [TestMethod]
    [Description("LRN-002: tree built from recorded nodes")]
    public void Build_CreatesTree()
    {
        ParameterDatabase db = SampleBuilder().Build();

        Assert.AreEqual(7, db.NodeCount);
        Assert.AreEqual("MPX 1", db.Root.Name);
        Assert.HasCount(2, db.Root.Children);
        Assert.AreEqual("Mix", db.Find(new ControlAddress(0, 0))!.Name);
        Assert.IsTrue(db.Find(new ControlAddress(0, 0))!.IsEditable);
        Assert.AreSame(db.Root, db.Find(new ControlAddress(0))!.Parent);
        Assert.HasCount(1, db.Gaps);
    }

    [TestMethod]
    [Description("LRN-005: resolve by name path, name and type")]
    public void Find_ByPathNameAndType()
    {
        ParameterDatabase db = SampleBuilder().Build();

        Assert.AreEqual(new ControlAddress(0, 0), db.FindByPath("program", "MIX")!.Address);
        Assert.IsNull(db.FindByPath("Program", "Level"));
        Assert.AreEqual(1, db.FindByName("Mix").Count());
        Assert.AreEqual(2, db.FindByType(3).Count());
    }

    [TestMethod]
    [Description("AC-014, LRN-006: 0x014A size corrected to 542")]
    public void KnownBug1_SizeIsCorrected()
    {
        ParameterDatabase db = SampleBuilder().Build();
        Assert.AreEqual((ushort)542, db.Describe(KnownBugs.BrokenSizeType)!.Size);
        Assert.AreEqual(542, db.ValueSize(KnownBugs.BrokenSizeType));
    }

    [TestMethod]
    [Description("AC-014, LRN-007: no children below the dead end")]
    public void KnownBug2_DeadEndHasNoChildren()
    {
        ParameterDatabaseBuilder builder = SampleBuilder();
        builder.AddNode(new ControlAddress(1, 2, 0, 0), 4);

        ParameterDatabase db = builder.Build();

        ControlTreeNode deadEnd = db.Find(KnownBugs.DeadEndAddress)!;
        Assert.IsTrue(deadEnd.IsDeadEnd);
        Assert.IsEmpty(deadEnd.Children);
        Assert.AreEqual(0, ParameterDatabaseBuilder.ChildCount(KnownBugs.DeadEndAddress, deadEnd.Description));
        Assert.AreEqual(4, ParameterDatabaseBuilder.ChildCount(new ControlAddress(9), deadEnd.Description));
    }

    [TestMethod]
    [Description("LRN-008")]
    public void KnownBug3_UnloadedAlgorithm()
    {
        byte[] algorithms = [1, 0, 0, 0, 0, 0];
        Assert.IsFalse(KnownBugs.IsUnloadedAlgorithmParameter(new ControlAddress(0, 0, 1, 2), algorithms));
        Assert.IsTrue(KnownBugs.IsUnloadedAlgorithmParameter(new ControlAddress(0, 0, 3, 2), algorithms));
        Assert.IsFalse(KnownBugs.IsUnloadedAlgorithmParameter(new ControlAddress(1, 0, 3, 2), algorithms));
        Assert.IsFalse(KnownBugs.IsUnloadedAlgorithmParameter(new ControlAddress(0, 0, 3), algorithms));
    }

    [TestMethod]
    [Description("LRN-004: JSON round trip")]
    public void Serializer_RoundTrip()
    {
        ParameterDatabase db = SampleBuilder().Build();

        string json = ParameterDatabaseSerializer.Serialize(db);
        ParameterDatabase copy = ParameterDatabaseSerializer.Deserialize(json);

        Assert.AreEqual(db.Key, copy.Key);
        Assert.AreEqual(db.NodeCount, copy.NodeCount);
        Assert.AreEqual(db.Descriptions.Count, copy.Descriptions.Count);
        Assert.AreEqual(json, ParameterDatabaseSerializer.Serialize(copy));
    }

    [TestMethod]
    [Description("SEC-002: corrupt documents raise JsonException")]
    public void Serializer_RejectsCorruptDocuments()
    {
        Assert.ThrowsExactly<JsonException>(() => ParameterDatabaseSerializer.Deserialize("{\"schemaVersion\":99}"));
        Assert.ThrowsExactly<JsonException>(() => ParameterDatabaseSerializer.Deserialize(
            "{\"schemaVersion\":1,\"key\":{\"majorVersion\":1,\"minorVersion\":0,\"buildDate\":\"\",\"buildTime\":\"\"},\"descriptions\":[],\"nodes\":[{\"address\":[],\"type\":5}],\"gaps\":[]}"));
    }

    [TestMethod]
    [Description("LRN-004")]
    public void DatabaseKey_FileStemAndFrom()
    {
        DatabaseKey key = DatabaseKey.From(new SystemConfiguration(Device, 1, 10, "17:51:03", "Mar 30 1998", 0, 0, 0));
        Assert.AreEqual(Key, key);
        Assert.AreEqual("V1.10_Mar-30-1998_17-51-03", key.FileStem);
    }

    [TestMethod]
    [Description("LRN-009: learn from a capture of both directions")]
    public void CaptureLearner_PairsTypeAnswersWithRequests()
    {
        Mpx1Message[] capture =
        [
            Request.SystemConfiguration(Device),
            new SystemConfiguration(Device, 1, 10, "17:51:03", "Mar 30 1998", 3, 0, 2),
            Level(1, "MPX 1", 0),
            Leaf(2, "Mix  "),
            Request.ParameterType(Device, ControlAddress.Root),
            new ParameterType(Device, 1),
            Request.ParameterType(Device, new ControlAddress(0)),
            new ParameterType(Device, 2),
        ];

        ParameterDatabase fromMessages = CaptureLearner.Learn(capture);
        ParameterDatabase fromBytes = CaptureLearner.Learn(SysExFrames.Join(capture));

        Assert.AreEqual(2, fromMessages.NodeCount);
        Assert.AreEqual("Mix", fromBytes.Find(new ControlAddress(0))!.Name);
        Assert.ThrowsExactly<InvalidOperationException>(() => CaptureLearner.Learn(capture[2..]));
    }

    [TestMethod]
    [Description("PRG-006: .syx data is split into frames")]
    public void SysExFrames_SplitSkipsNoise()
    {
        IReadOnlyList<byte[]> frames = SysExFrames.Split([0x00, 0xF0, 0x01, 0xF7, 0x55, 0xF7, 0xF0, 0xF7]);
        Assert.HasCount(2, frames);
        CollectionAssert.AreEqual(new byte[] { 0xF0, 0x01, 0xF7 }, frames[0]);
    }
}
