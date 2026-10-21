using BB84.MPX1.Core.Database;
using BB84.MPX1.Core.Dumps;
using BB84.MPX1.Core.Protocol;

namespace BB84.MPX1.Midi.Dumps;

/// <summary>
/// Reads and writes system and front-panel dumps through Parameter Data at the addresses the learned tree gives
/// (DMP-001 to DMP-010). Documented layouts are validated before writing (DMP-011).
/// </summary>
/// <param name="session">A started session.</param>
/// <param name="database">The learned database.</param>
public sealed class DumpService(Mpx1Session session, ParameterDatabase database)
{
    private readonly Mpx1Session _session = session ?? throw new ArgumentNullException(nameof(session));

    /// <summary>Gets the learned database.</summary>
    public ParameterDatabase Database { get; } = database ?? throw new ArgumentNullException(nameof(database));

    /// <summary>Returns the nodes of a dump kind.</summary>
    /// <param name="kind">The dump kind.</param>
    public IReadOnlyList<ControlTreeNode> Nodes(SystemDumpKind kind) => SystemDumps.Find(Database, kind);

    /// <summary>Reads a dump.</summary>
    /// <param name="node">The dump node.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<byte[]> ReadAsync(ControlTreeNode node, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(node);
        ParameterData data = await _session.RequestAsync<ParameterData>(Request.ParameterData(_session.Device, node.Address), cancellationToken).ConfigureAwait(false);
        return data.Data.ToArray();
    }

    /// <summary>Reads the first dump of a kind, or <see langword="null"/> when the firmware has none.</summary>
    /// <param name="kind">The dump kind.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<byte[]?> ReadAsync(SystemDumpKind kind, CancellationToken cancellationToken = default)
        => Nodes(kind) is [var node, ..] ? await ReadAsync(node, cancellationToken).ConfigureAwait(false) : null;

    /// <summary>Writes a dump after validating it against its documented layout.</summary>
    /// <param name="kind">The dump kind.</param>
    /// <param name="node">The dump node.</param>
    /// <param name="data">The dump bytes.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ArgumentException">The data has the wrong size or holds illegal values (DMP-011).</exception>
    public Task WriteAsync(SystemDumpKind kind, ControlTreeNode node, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(node);
        Validate(kind, data.Span);
        return _session.SendAsync(new ParameterData(_session.Device, data, node.Address), cancellationToken);
    }

    /// <summary>Checks dump data against its documented size, ranges and, for LEDs, the scan bits.</summary>
    /// <param name="kind">The dump kind.</param>
    /// <param name="data">The dump bytes.</param>
    /// <exception cref="ArgumentException">The data is not valid.</exception>
    public static void Validate(SystemDumpKind kind, ReadOnlySpan<byte> data)
    {
        int size = SystemDumps.Size(kind);
        if (data.Length != size)
            throw new ArgumentException($"{SystemDumps.DisplayName(kind)} must be {size} bytes, got {data.Length}.", nameof(data));
        if (kind == SystemDumpKind.Leds && !LedState.FromBytes(data).HasValidScanBits())
            throw new ArgumentException("The LED dump must keep the documented scan bits in each column byte.", nameof(data));
        if (SystemDumps.Layout(kind) is { } layout && layout.InvalidFields(data.ToArray()).FirstOrDefault() is { } bad)
            throw new ArgumentException($"{bad.Name} holds an illegal value.", nameof(data));
    }
}
