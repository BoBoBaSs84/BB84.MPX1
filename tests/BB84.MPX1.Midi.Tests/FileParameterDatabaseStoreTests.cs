using BB84.MPX1.Core.Database;
using BB84.MPX1.Core.Protocol;
using BB84.MPX1.Midi.Learning;

namespace BB84.MPX1.Midi.Tests;

[TestClass]
public sealed class FileParameterDatabaseStoreTests
{
    private const string Directory = "cache";
    private static readonly DatabaseKey Key = new(1, 10, "Mar 30 1998", "17:51:03");
    private static readonly string FilePath = Path.Combine(Directory, Key.FileStem + ".json");

    public TestContext TestContext { get; set; } = null!;

    private static ParameterDatabase Sample(DatabaseKey key)
    {
        ParameterDatabaseBuilder builder = new(key);
        builder.AddDescription(new ParameterDescription(new DeviceId(0), 1, "MPX 1", 1, ParameterAttributes.ControlLevel, null, [new UnitLimit(0, 0, 0x4B)]));
        builder.AddNode(ControlAddress.Root, 1);
        return builder.Build();
    }

    [TestMethod]
    [Description("LRN-004: the store creates its directory and replaces the cache file through a temporary file")]
    public async Task Save_WritesTemporaryFileThenMoves()
    {
        InMemoryFileSystem fs = new();
        FileParameterDatabaseStore store = new(Directory, fs.File, fs.Directory);

        await store.SaveAsync(Sample(Key), TestContext.CancellationToken);

        Assert.AreSequenceEqual(
            new[] { $"CreateDirectory {Directory}", $"WriteAllTextAsync {FilePath}.tmp", $"Move {FilePath}.tmp {FilePath}" },
            fs.Calls);
        Assert.AreSequenceEqual(new[] { FilePath }, fs.Files.Keys);
        Assert.AreEqual(Key, (await store.TryLoadAsync(Key, TestContext.CancellationToken))?.Key);
    }

    [TestMethod]
    [Description("LRN-004, SEC-002: a missing, corrupt (also null members) or foreign cache file is treated as missing")]
    public async Task TryLoad_ReturnsNullForMissingCorruptOrForeignFiles()
    {
        InMemoryFileSystem fs = new();
        FileParameterDatabaseStore store = new(Directory, fs.File, fs.Directory);

        Assert.IsNull(await store.TryLoadAsync(Key, TestContext.CancellationToken));

        fs.Files[FilePath] = "{ not json";
        Assert.IsNull(await store.TryLoadAsync(Key, TestContext.CancellationToken));

        fs.Files[FilePath] = ParameterDatabaseSerializer.Serialize(Sample(Key with { BuildTime = "00:00:00" }));
        Assert.IsNull(await store.TryLoadAsync(Key, TestContext.CancellationToken));

        // Valid JSON with a null member (issue #48).
        fs.Files[FilePath] = ParameterDatabaseSerializer.Serialize(Sample(Key)).Replace("\"descriptions\":[", "\"descriptions\":null,\"unused\":[", StringComparison.Ordinal);
        Assert.IsNull(await store.TryLoadAsync(Key, TestContext.CancellationToken));
    }
}
