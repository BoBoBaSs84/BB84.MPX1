using BB84.MPX1.Core.Database;
using BB84.MPX1.Core.Protocol;
using BB84.MPX1.Core.Values;

namespace BB84.MPX1.Midi.Editing;

/// <summary>
/// Thrown instead of writing a parameter of an effect algorithm that is not loaded; such writes can crash the
/// unit (Appendix D bug 3, LRN-008).
/// </summary>
public sealed class UnloadedAlgorithmException : InvalidOperationException
{
    /// <summary>Initializes a new instance of the <see cref="UnloadedAlgorithmException"/> class.</summary>
    public UnloadedAlgorithmException()
    { }

    /// <summary>Initializes a new instance of the <see cref="UnloadedAlgorithmException"/> class.</summary>
    /// <param name="message">The error message.</param>
    public UnloadedAlgorithmException(string message)
        : base(message)
    { }

    /// <summary>Initializes a new instance of the <see cref="UnloadedAlgorithmException"/> class.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception.</param>
    public UnloadedAlgorithmException(string message, Exception innerException)
        : base(message, innerException)
    { }
}

/// <summary>A parameter value read from the device.</summary>
/// <param name="Node">The tree node.</param>
/// <param name="Data">Raw Parameter Data bytes (parameter followed by option).</param>
/// <param name="Value">The decoded value, or <see langword="null"/> for dumps and text.</param>
/// <param name="DisplayText">The device's own formatting (Parameter Display, PAR-003).</param>
public sealed record ParameterReading(ControlTreeNode Node, ReadOnlyMemory<byte> Data, ParameterValue? Value, string DisplayText);

/// <summary>
/// Reads and writes parameters by control address using a learned database (PAR-001), with the safety
/// checks the document calls for (PAR-006, LRN-008).
/// </summary>
/// <param name="session">A started session.</param>
/// <param name="database">The learned database for the connected firmware.</param>
public sealed class ParameterEditor(Mpx1Session session, ParameterDatabase database)
{
    private readonly Mpx1Session _session = session ?? throw new ArgumentNullException(nameof(session));
    private byte[]? _activeAlgorithms;

    /// <summary>Gets the learned database.</summary>
    public ParameterDatabase Database { get; } = database ?? throw new ArgumentNullException(nameof(database));

    /// <summary>
    /// Sets the active program's algorithm numbers (Pitch through Delay) used by the LRN-008 guard, for example
    /// after loading a program. Pass <see langword="null"/> to force a refresh on the next write.
    /// </summary>
    /// <param name="algorithms">Six algorithm numbers, or <see langword="null"/>.</param>
    public void SetActiveAlgorithms(ReadOnlySpan<byte> algorithms)
        => _activeAlgorithms = algorithms.IsEmpty ? null : algorithms.ToArray();

    /// <summary>Reads the active program's algorithm numbers from the device.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<IReadOnlyList<byte>> RefreshActiveAlgorithmsAsync(CancellationToken cancellationToken = default)
    {
        ProgramDump active = await _session.RequestAsync<ProgramDump>(Request.ProgramDump(_session.Device, Request.ActiveProgram), cancellationToken).ConfigureAwait(false);
        _activeAlgorithms = [.. active.Program.Algorithms];
        return _activeAlgorithms;
    }

    /// <summary>Reads a parameter value and the device's display text.</summary>
    /// <param name="address">The control address.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ArgumentException">The address is unknown or not editable.</exception>
    public async Task<ParameterReading> ReadAsync(ControlAddress address, CancellationToken cancellationToken = default)
    {
        ControlTreeNode node = RequireEditable(address);
        ParameterData data = await _session.RequestAsync<ParameterData>(Request.ParameterData(_session.Device, address), cancellationToken).ConfigureAwait(false);
        ParameterDisplay display = await _session.RequestAsync<ParameterDisplay>(Request.ParameterDisplay(_session.Device, address), cancellationToken).ConfigureAwait(false);

        ParameterValue? value = null;
        if (node.Description.Size is 1 or 2 && data.Data.Length >= node.Description.Size)
            value = ParameterValueCodec.Decode(node.Description, OptionOf(node), data.Data.Span);

        return new ParameterReading(node, data.Data, value, display.Text);
    }

    /// <summary>Writes a numeric value (and option).</summary>
    /// <param name="address">The control address.</param>
    /// <param name="value">The value.</param>
    /// <param name="optionValue">The option value, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="UnloadedAlgorithmException">The parameter belongs to an algorithm that is not loaded.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The value is outside the parameter's limits.</exception>
    public Task WriteValueAsync(ControlAddress address, int value, int? optionValue = null, CancellationToken cancellationToken = default)
    {
        ControlTreeNode node = RequireEditable(address);
        byte[] data = ParameterValueCodec.Encode(node.Description, OptionOf(node), value, optionValue);
        return WriteAsync(address, data, cancellationToken);
    }

    /// <summary>Writes raw Parameter Data (for dumps and text).</summary>
    /// <param name="address">The control address.</param>
    /// <param name="data">Parameter bytes followed by option bytes.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="UnloadedAlgorithmException">The parameter belongs to an algorithm that is not loaded.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A MIDI Speed value is not one of the allowed values.</exception>
    public async Task WriteAsync(ControlAddress address, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        ControlTreeNode node = RequireEditable(address);

        if (string.Equals(node.Name, "MIDI Speed", StringComparison.OrdinalIgnoreCase) && (data.Length < 1 || !MidiSpeed.IsValid(data.Span[0])))
            throw new ArgumentOutOfRangeException(nameof(data), "MIDI Speed accepts only 0x01, 0x02, 0x04 and 0x40.");

        byte[] algorithms = _activeAlgorithms ?? [.. await RefreshActiveAlgorithmsAsync(cancellationToken).ConfigureAwait(false)];
        if (KnownBugs.IsUnloadedAlgorithmParameter(address, algorithms))
        {
            string algorithm = node.Parent?.Name ?? "this algorithm";
            throw new UnloadedAlgorithmException(
                $"{node.Name} belongs to {algorithm}, which is not loaded in the active program. Writing it can crash the MPX 1, so it was not sent.");
        }

        await _session.SendAsync(new ParameterData(_session.Device, data, address), cancellationToken).ConfigureAwait(false);
    }

    private ControlTreeNode RequireEditable(ControlAddress address)
    {
        ControlTreeNode node = Database.Find(address) ?? throw new ArgumentException($"Unknown control address {address}.", nameof(address));
        return node.IsEditable ? node : throw new ArgumentException($"{node.Name} at {address} is a control level, not a parameter.", nameof(address));
    }

    private ParameterDescription? OptionOf(ControlTreeNode node)
        => node.Description.OptionType is ushort option ? Database.Describe(option) : null;
}
