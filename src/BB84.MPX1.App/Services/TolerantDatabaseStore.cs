using BB84.MPX1.Core.Database;
using BB84.MPX1.Midi.Learning;

namespace BB84.MPX1.App.Services;

/// <summary>
/// Wraps the learned-database cache so that a cache that cannot be read or written never stops connecting or
/// learning (LRN-004): a failed load counts as a cache miss, a failed save is skipped, and both set
/// <see cref="LastError"/> for the status line.
/// </summary>
/// <param name="inner">The cache.</param>
public sealed class TolerantDatabaseStore(IParameterDatabaseStore inner) : IParameterDatabaseStore
{
    private readonly IParameterDatabaseStore _inner = inner ?? throw new ArgumentNullException(nameof(inner));

    /// <summary>Gets the failure of the last cache access, or <see langword="null"/> when it succeeded.</summary>
    public string? LastError { get; private set; }

    /// <inheritdoc/>
    public async Task<ParameterDatabase?> TryLoadAsync(DatabaseKey key, CancellationToken cancellationToken = default)
    {
        try
        {
            ParameterDatabase? database = await _inner.TryLoadAsync(key, cancellationToken).ConfigureAwait(false);
            LastError = null;
            return database;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LastError = $"The database cache could not be read: {ex.Message}";
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task SaveAsync(ParameterDatabase database, CancellationToken cancellationToken = default)
    {
        try
        {
            await _inner.SaveAsync(database, cancellationToken).ConfigureAwait(false);
            LastError = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LastError = $"The database cache could not be saved: {ex.Message}";
        }
    }
}
