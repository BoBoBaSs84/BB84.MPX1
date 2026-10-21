using System.Collections.Concurrent;
using System.Text.Json;

using BB84.MPX1.Core.Database;

namespace BB84.MPX1.Midi.Learning;

/// <summary>Cache of learned databases keyed by firmware build (LRN-004).</summary>
public interface IParameterDatabaseStore
{
    /// <summary>Returns the cached database for a build, or <see langword="null"/>.</summary>
    /// <param name="key">The firmware build.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ParameterDatabase?> TryLoadAsync(DatabaseKey key, CancellationToken cancellationToken = default);

    /// <summary>Stores a database, replacing any entry for the same build.</summary>
    /// <param name="database">The database.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SaveAsync(ParameterDatabase database, CancellationToken cancellationToken = default);
}

/// <summary>In-memory store, for tests and short-lived sessions.</summary>
public sealed class InMemoryParameterDatabaseStore : IParameterDatabaseStore
{
    private readonly ConcurrentDictionary<DatabaseKey, ParameterDatabase> _entries = new();

    /// <inheritdoc/>
    public Task<ParameterDatabase?> TryLoadAsync(DatabaseKey key, CancellationToken cancellationToken = default)
        => Task.FromResult(_entries.GetValueOrDefault(key));

    /// <inheritdoc/>
    public Task SaveAsync(ParameterDatabase database, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(database);
        _entries[database.Key] = database;
        return Task.CompletedTask;
    }
}

/// <summary>Stores each database as <c>&lt;directory&gt;/&lt;key&gt;.json</c>.</summary>
/// <param name="directory">The cache directory; created on first save.</param>
public sealed class FileParameterDatabaseStore(string directory) : IParameterDatabaseStore
{
    /// <summary>Gets the cache directory.</summary>
    public string Directory { get; } = directory ?? throw new ArgumentNullException(nameof(directory));

    /// <inheritdoc/>
    public async Task<ParameterDatabase?> TryLoadAsync(DatabaseKey key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        string path = PathFor(key);
        if (!File.Exists(path))
            return null;

        try
        {
            ParameterDatabase database = ParameterDatabaseSerializer.Deserialize(await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false));
            return database.Key == key ? database : null;
        }
        catch (JsonException)
        {
            // A corrupt cache entry is treated as missing and will be overwritten (SEC-002).
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task SaveAsync(ParameterDatabase database, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(database);
        System.IO.Directory.CreateDirectory(Directory);
        string path = PathFor(database.Key);
        string temp = path + ".tmp";
        await File.WriteAllTextAsync(temp, ParameterDatabaseSerializer.Serialize(database), cancellationToken).ConfigureAwait(false);
        File.Move(temp, path, overwrite: true);
    }

    private string PathFor(DatabaseKey key) => Path.Combine(Directory, key.FileStem + ".json");
}
