using CommunityToolkit.Mvvm.ComponentModel;

using BB84.MPX1.App.Services;
using BB84.MPX1.Core.Database;
using BB84.MPX1.Core.Protocol;
using BB84.MPX1.Midi;
using BB84.MPX1.Midi.Editing;

namespace BB84.MPX1.App.ViewModels.Parameters;

/// <summary>
/// Base of the parameter editors shown for the leaves of a control level (UI-003, UI-004). Reads the value and
/// the device's display text; edits are sent live through a <see cref="CoalescingWriter{T}"/>.
/// </summary>
public abstract partial class ParameterViewModel : ObservableObject
{
    private bool _applying;

    /// <summary>Initializes a new instance of the <see cref="ParameterViewModel"/> class.</summary>
    /// <param name="node">The tree node.</param>
    /// <param name="context">The connection context.</param>
    /// <param name="readOnlyReason">Why the parameter must not be written, or <see langword="null"/>.</param>
    protected ParameterViewModel(ControlTreeNode node, ParameterContext context, string? readOnlyReason)
    {
        Node = node ?? throw new ArgumentNullException(nameof(node));
        Context = context ?? throw new ArgumentNullException(nameof(context));
        ReadOnlyReason = readOnlyReason;
    }

    /// <summary>Gets the tree node.</summary>
    public ControlTreeNode Node { get; }

    /// <summary>Gets the parameter name.</summary>
    public string Name => Node.Name;

    /// <summary>Gets the control address and type, for tooltips.</summary>
    public string Details => $"{Node.Address} · type 0x{Node.Type:X4} · {Node.Description.Size} byte(s)";

    /// <summary>Gets why the parameter is read-only, or <see langword="null"/> when it is editable.</summary>
    public string? ReadOnlyReason { get; }

    /// <summary>Gets a value indicating whether the parameter can be edited.</summary>
    public bool IsEditable => ReadOnlyReason is null;

    /// <summary>Gets or sets a value indicating whether the page already explains why the parameter is read-only.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowReadOnlyReason))]
    public partial bool IsReasonShownByPage { get; set; }

    /// <summary>Gets a value indicating whether the row shows its own read-only reason.</summary>
    public bool ShowReadOnlyReason => !IsEditable && !IsReasonShownByPage;

    /// <summary>Gets or sets the display text: the device's own formatting, or an offline preview while editing.</summary>
    [ObservableProperty]
    public partial string DisplayText { get; set; } = "";

    /// <summary>Gets or sets the last error, or <see langword="null"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? Error { get; set; }

    /// <summary>Gets a value indicating whether <see cref="Error"/> is set.</summary>
    public bool HasError => Error is not null;

    /// <summary>Gets or sets a value indicating whether the value was read from the device.</summary>
    [ObservableProperty]
    public partial bool IsLoaded { get; set; }

    /// <summary>Gets the connection context.</summary>
    protected ParameterContext Context { get; }

    /// <summary>Gets a value indicating whether a device value is being applied (edits must not be sent).</summary>
    protected bool IsApplying => _applying;

    /// <summary>Reads the value and display text from the device.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            ParameterReading reading = await Context.Editor.ReadAsync(Node.Address, cancellationToken);
            _applying = true;
            try
            {
                Apply(reading);
            }
            finally
            {
                _applying = false;
            }
            DisplayText = reading.DisplayText.Trim();
            Error = null;
            IsLoaded = true;
        }
        catch (Exception ex) when (ex is Mpx1TimeoutException or ArgumentException)
        {
            Error = Describe(ex);
        }
    }

    /// <summary>Stops pending writes, for example when the page is closed.</summary>
    public virtual void CancelPendingWrites()
    { }

    /// <summary>Applies a value read from the device without sending it back.</summary>
    /// <param name="reading">The reading.</param>
    protected abstract void Apply(ParameterReading reading);

    /// <summary>Reads the device's display text after a burst of writes (PAR-003).</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    protected async Task RefreshDisplayAsync(CancellationToken cancellationToken)
    {
        ParameterDisplay display = await Context.Session.RequestAsync<ParameterDisplay>(Request.ParameterDisplay(Context.Session.Device, Node.Address), cancellationToken);
        DisplayText = display.Text.Trim();
        Error = null;
    }

    /// <summary>Creates the live writer for this parameter; it reads the display text back after each burst.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="write">Sends one value.</param>
    protected CoalescingWriter<T> CreateWriter<T>(Func<T, CancellationToken, Task> write)
        => new(write, RefreshDisplayAsync, ReportError, Context.Time, CoalescingWriter<T>.DefaultInterval, CoalescingWriter<T>.DefaultSettleDelay);

    /// <summary>Shows a write or read failure.</summary>
    /// <param name="exception">The failure.</param>
    protected void ReportError(Exception exception) => Error = Describe(exception);

    private static string Describe(Exception exception) => exception switch
    {
        UnloadedAlgorithmException => exception.Message,
        Mpx1TimeoutException => "The device did not answer.",
        ArgumentException => exception.Message,
        _ => $"{exception.GetType().Name}: {exception.Message}",
    };
}
