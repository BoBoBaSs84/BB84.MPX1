using System.Buffers.Binary;

using CommunityToolkit.Mvvm.ComponentModel;

using BB84.MPX1.App.Services;
using BB84.MPX1.Core.Database;
using BB84.MPX1.Midi.Editing;

namespace BB84.MPX1.App.ViewModels.Parameters;

/// <summary>Drop-down editor for parameters with a short, nameable value list (MIDI Speed, MIDI channel).</summary>
public sealed partial class ChoiceParameterViewModel : ParameterViewModel
{
    private readonly CoalescingWriter<int> _writer;

    /// <summary>Initializes a new instance of the <see cref="ChoiceParameterViewModel"/> class.</summary>
    /// <param name="node">The tree node.</param>
    /// <param name="choices">The allowed values.</param>
    /// <param name="context">The connection context.</param>
    /// <param name="readOnlyReason">Why the parameter is read-only, or <see langword="null"/>.</param>
    public ChoiceParameterViewModel(ControlTreeNode node, IReadOnlyList<ChoiceItem> choices, ParameterContext context, string? readOnlyReason)
        : base(node, context, readOnlyReason)
    {
        Choices = choices;
        _writer = CreateWriter<int>((v, ct) => Context.Editor.WriteAsync(Node.Address, Encode(v), ct));
    }

    /// <summary>Gets the allowed values.</summary>
    public IReadOnlyList<ChoiceItem> Choices { get; }

    /// <summary>Gets or sets the selected value.</summary>
    [ObservableProperty]
    public partial ChoiceItem? Selected { get; set; }

    /// <inheritdoc/>
    public override void CancelPendingWrites() => _writer.Dispose();

    /// <inheritdoc/>
    protected override void Apply(ParameterReading reading)
    {
        if (reading.Data.Length < 1)
            return;
        int raw = Node.Description.Size == 2 && reading.Data.Length >= 2
            ? BinaryPrimitives.ReadUInt16LittleEndian(reading.Data.Span)
            : reading.Data.Span[0];
        Selected = Choices.FirstOrDefault(c => c.Value == raw);
    }

    partial void OnSelectedChanged(ChoiceItem? value)
    {
        if (value is null || IsApplying || !IsEditable || !IsLoaded)
            return;
        DisplayText = value.Label;
        _writer.Submit(value.Value);
    }

    private byte[] Encode(int value)
    {
        // Choices are raw stored values (MIDI Speed is a bit pattern, not a range), so they bypass range checks.
        byte[] data = new byte[Context.Editor.Database.ValueSize(Node.Type)];
        if (Node.Description.Size == 2)
            BinaryPrimitives.WriteUInt16LittleEndian(data, (ushort)value);
        else
            data[0] = (byte)value;
        return data;
    }
}
