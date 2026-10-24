using CommunityToolkit.Mvvm.ComponentModel;

using BB84.MPX1.App.Services;
using BB84.MPX1.Core.Database;
using BB84.MPX1.Core.Values;
using BB84.MPX1.Midi.Editing;

namespace BB84.MPX1.App.ViewModels.Parameters;

/// <summary>On/off editor for parameters whose range is exactly 0–1.</summary>
public sealed partial class ToggleParameterViewModel : ParameterViewModel
{
    private readonly CoalescingWriter<int> _writer;

    /// <summary>Initializes a new instance of the <see cref="ToggleParameterViewModel"/> class.</summary>
    /// <param name="node">The tree node.</param>
    /// <param name="context">The connection context.</param>
    /// <param name="readOnlyReason">Why the parameter is read-only, or <see langword="null"/>.</param>
    public ToggleParameterViewModel(ControlTreeNode node, ParameterContext context, string? readOnlyReason)
        : base(node, context, readOnlyReason)
        => _writer = CreateWriter<int>((v, ct) => Context.Editor.WriteValueAsync(Node.Address, v, null, ct));

    /// <summary>Gets or sets a value indicating whether the parameter is on.</summary>
    [ObservableProperty]
    public partial bool IsOn { get; set; }

    /// <inheritdoc/>
    public override void CancelPendingWrites() => _writer.Dispose();

    /// <inheritdoc/>
    protected override void Apply(ParameterReading reading)
    {
        if (reading.Value is ParameterValue value)
            IsOn = value.Value != 0;
    }

    partial void OnIsOnChanged(bool value)
    {
        if (IsApplying || !IsEditable || !IsLoaded)
            return;
        _writer.Submit(value ? 1 : 0);
    }
}
