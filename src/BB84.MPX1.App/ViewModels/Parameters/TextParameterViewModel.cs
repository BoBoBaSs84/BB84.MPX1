using CommunityToolkit.Mvvm.ComponentModel;

using BB84.MPX1.App.Services;
using BB84.MPX1.Core.Database;
using BB84.MPX1.Core.Values;
using BB84.MPX1.Midi.Editing;

namespace BB84.MPX1.App.ViewModels.Parameters;

/// <summary>Text editor for name parameters (display units <c>0x25</c>, <c>0x30</c>).</summary>
public sealed partial class TextParameterViewModel : ParameterViewModel
{
    private readonly CoalescingWriter<string> _writer;

    /// <summary>Initializes a new instance of the <see cref="TextParameterViewModel"/> class.</summary>
    /// <param name="node">The tree node.</param>
    /// <param name="context">The connection context.</param>
    /// <param name="readOnlyReason">Why the parameter is read-only, or <see langword="null"/>.</param>
    public TextParameterViewModel(ControlTreeNode node, ParameterContext context, string? readOnlyReason)
        : base(node, context, readOnlyReason)
        => _writer = CreateWriter<string>((v, ct) => Context.Editor.WriteAsync(Node.Address, Encode(v), ct));

    /// <summary>Gets the maximum text length.</summary>
    public int MaxLength => Node.Description.Size;

    /// <summary>Gets or sets the text.</summary>
    [ObservableProperty]
    public partial string Text { get; set; } = "";

    /// <inheritdoc/>
    public override void CancelPendingWrites() => _writer.Dispose();

    /// <inheritdoc/>
    protected override void Apply(ParameterReading reading)
        => Text = DisplayUnitFormatter.FormatText(reading.Data.Span[..Math.Min(reading.Data.Length, MaxLength)]).TrimEnd();

    partial void OnTextChanged(string value)
    {
        if (IsApplying || !IsEditable || !IsLoaded)
            return;
        _writer.Submit(value);
    }

    private byte[] Encode(string text)
    {
        // Names are ASCII, space-padded, not null-terminated (PRO-007).
        byte[] data = new byte[Context.Editor.Database.ValueSize(Node.Type)];
        data.AsSpan(0, MaxLength).Fill((byte)' ');
        int length = Math.Min(text.Length, MaxLength);
        for (int i = 0; i < length; i++)
            data[i] = text[i] is >= ' ' and <= '~' ? (byte)text[i] : (byte)' ';
        return data;
    }
}
