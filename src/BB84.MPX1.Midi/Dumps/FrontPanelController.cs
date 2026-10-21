using BB84.MPX1.Core.Database;
using BB84.MPX1.Core.Dumps;
using BB84.MPX1.Core.Protocol;

namespace BB84.MPX1.Midi.Dumps;

/// <summary>The front panel as read back from the unit (UI-007).</summary>
/// <param name="Display">The 32 display bytes.</param>
/// <param name="Leds">The LED state, or <see langword="null"/> when the firmware has no LED dump.</param>
public sealed record PanelSnapshot(byte[] Display, LedState? Leds);

/// <summary>
/// Drives the virtual front panel (PAR-007, UI-007): button clicks are PRESS then RELEASE, holds are HOLD then
/// RELEASE, the Left + Right combination uses its own codes, and the display, LEDs and custom characters are read
/// through their dumps.
/// </summary>
public sealed class FrontPanelController
{
    private readonly Mpx1Session _session;
    private readonly DumpService _dumps;
    private readonly ControlTreeNode _button;

    /// <summary>Initializes a new instance of the <see cref="FrontPanelController"/> class.</summary>
    /// <param name="session">A started session.</param>
    /// <param name="dumps">The dump service.</param>
    /// <exception cref="InvalidOperationException">The learned database has no Panel Button parameter.</exception>
    public FrontPanelController(Mpx1Session session, DumpService dumps)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _dumps = dumps ?? throw new ArgumentNullException(nameof(dumps));
        _button = FindButton(dumps.Database) ?? throw new InvalidOperationException("The learned database has no Panel Button parameter.");
    }

    /// <summary>Gets a value indicating whether the firmware supports the single-message Tap and A/B toggle (V1.10).</summary>
    public bool HasV110Codes => _dumps.Database.Key is { MajorVersion: > 1 } or { MajorVersion: 1, MinorVersion: >= 10 };

    /// <summary>Returns the Panel Button node: an editable 1-byte parameter named "Button" (LRN-005), or <see langword="null"/>.</summary>
    /// <param name="database">The learned database.</param>
    public static ControlTreeNode? FindButton(ParameterDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        return database.Root.DescendantsAndSelf()
            .FirstOrDefault(n => n.IsEditable && n.Description.Size == 1 && n.Name.Contains("Button", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Clicks a button: PRESS, then RELEASE (PAR-007).</summary>
    /// <param name="button">The button.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task ClickAsync(PanelButton button, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(button);
        await SendAsync(button.Press, cancellationToken).ConfigureAwait(false);
        await SendAsync(button.Release, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Holds a button: HOLD, then RELEASE (PAR-007). A PRESS without RELEASE is never sent.</summary>
    /// <param name="button">The button.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task HoldAsync(PanelButton button, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(button);
        await SendAsync(button.Hold, cancellationToken).ConfigureAwait(false);
        await SendAsync(button.Release, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Turns the encoder by a number of steps (positive = clockwise).</summary>
    /// <param name="steps">The steps.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task TurnAsync(int steps, CancellationToken cancellationToken = default)
    {
        byte code = steps >= 0 ? PanelButtons.EncoderClockwise : PanelButtons.EncoderCounterClockwise;
        for (int i = 0; i < Math.Abs(steps); i++)
            await SendAsync(code, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Sends one raw Panel Button code (for example the V1.10 Tap or A/B toggle).</summary>
    /// <param name="code">The code.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task SendAsync(byte code, CancellationToken cancellationToken = default)
        => _session.SendAsync(new ParameterData(_session.Device, new[] { code }, _button.Address), cancellationToken);

    /// <summary>Reads the display and LED dumps (DMP-001, DMP-002).</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<PanelSnapshot> ReadAsync(CancellationToken cancellationToken = default)
    {
        byte[] display = await _dumps.ReadAsync(SystemDumpKind.Display, cancellationToken).ConfigureAwait(false) ?? new byte[DisplayText.Size];
        byte[]? leds = await _dumps.ReadAsync(SystemDumpKind.Leds, cancellationToken).ConfigureAwait(false);
        return new PanelSnapshot(display, leds is { Length: LedState.Size } ? LedState.FromBytes(leds) : null);
    }

    /// <summary>Reads the eight custom characters (DMP-003), or <see langword="null"/> when the firmware has no such dump.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<byte[]?> ReadCustomCharactersAsync(CancellationToken cancellationToken = default)
        => _dumps.ReadAsync(SystemDumpKind.CustomCharacters, cancellationToken);

    /// <summary>Shows text on the unit's display (DMP-002).</summary>
    /// <param name="top">Top line.</param>
    /// <param name="bottom">Bottom line.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task WriteDisplayAsync(string top, string bottom, CancellationToken cancellationToken = default)
        => _dumps.Nodes(SystemDumpKind.Display) is [var node, ..]
            ? _dumps.WriteAsync(SystemDumpKind.Display, node, DisplayText.Encode(top, bottom), cancellationToken)
            : Task.CompletedTask;
}
