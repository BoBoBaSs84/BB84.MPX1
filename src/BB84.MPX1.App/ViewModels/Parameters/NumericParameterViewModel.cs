using CommunityToolkit.Mvvm.ComponentModel;

using BB84.MPX1.App.Services;
using BB84.MPX1.Core.Database;
using BB84.MPX1.Core.Protocol;
using BB84.MPX1.Core.Values;
using BB84.MPX1.Midi.Editing;

namespace BB84.MPX1.App.ViewModels.Parameters;

/// <summary>Slider/spinner editor for 1- and 2-byte numeric parameters, with an optional unit option (PAR-008).</summary>
public sealed partial class NumericParameterViewModel : ParameterViewModel
{
    private readonly ParameterDescription? _option;
    private readonly CoalescingWriter<(int Value, int? Option)> _writer;

    /// <summary>Initializes a new instance of the <see cref="NumericParameterViewModel"/> class.</summary>
    /// <param name="node">The tree node.</param>
    /// <param name="option">The option description, or <see langword="null"/>.</param>
    /// <param name="context">The connection context.</param>
    /// <param name="readOnlyReason">Why the parameter is read-only, or <see langword="null"/>.</param>
    public NumericParameterViewModel(ControlTreeNode node, ParameterDescription? option, ParameterContext context, string? readOnlyReason)
        : base(node, context, readOnlyReason)
    {
        _option = option;
        _writer = CreateWriter<(int Value, int? Option)>((v, ct) => Context.Editor.WriteValueAsync(Node.Address, v.Value, v.Option, ct));
        Options = option is { Size: > 0 } ? BuildOptionChoices(option) : [];
        SelectedOption = Options.Count > 0 ? Options[0] : null;
        ApplyUnit(SelectedOption?.Value);
    }

    /// <summary>Gets the option choices; empty when the parameter has no option.</summary>
    public IReadOnlyList<ChoiceItem> Options { get; }

    /// <summary>Gets a value indicating whether the parameter has a unit option.</summary>
    public bool HasOptions => Options.Count > 0;

    /// <summary>Gets or sets the minimum of the selected unit.</summary>
    [ObservableProperty]
    public partial int Minimum { get; set; }

    /// <summary>Gets or sets the maximum of the selected unit.</summary>
    [ObservableProperty]
    public partial int Maximum { get; set; }

    /// <summary>Gets or sets the value.</summary>
    [ObservableProperty]
    public partial int Value { get; set; }

    /// <summary>Gets or sets the selected option.</summary>
    [ObservableProperty]
    public partial ChoiceItem? SelectedOption { get; set; }

    /// <summary>Gets the selected unit/limit triple.</summary>
    public UnitLimit Unit { get; private set; }

    /// <summary>Gets the writer, for tests.</summary>
    internal CoalescingWriter<(int Value, int? Option)> Writer => _writer;

    /// <inheritdoc/>
    public override void CancelPendingWrites() => _writer.Dispose();

    /// <inheritdoc/>
    protected override void Apply(ParameterReading reading)
    {
        if (reading.Value is not ParameterValue value)
            return;
        if (value.Option is int option)
            SelectedOption = Options.FirstOrDefault(o => o.Value == option) ?? SelectedOption;
        ApplyUnit(value.Option);
        Value = value.Value;
    }

    partial void OnValueChanged(int value) => Submit();

    partial void OnSelectedOptionChanged(ChoiceItem? value)
    {
        ApplyUnit(value?.Value);
        Value = Math.Clamp(Value, Math.Min(Minimum, Maximum), Math.Max(Minimum, Maximum));
        Submit();
    }

    private void ApplyUnit(int? option)
    {
        Unit = ParameterValueCodec.SelectUnit(Node.Description, option);
        (int min, int max) = ParameterValueCodec.GetRange(Unit, Node.Description.Size);
        Minimum = min;
        Maximum = max;
    }

    private void Submit()
    {
        if (IsApplying || !IsEditable || !IsLoaded)
            return;
        if (DisplayUnitFormatter.IsSupported(Unit.DisplayUnits))
            DisplayText = DisplayUnitFormatter.Format(Unit, Value, Value, SelectedOption?.Value, Name);
        _writer.Submit((Value, _option is null ? null : SelectedOption?.Value ?? 0));
    }

    private static List<ChoiceItem> BuildOptionChoices(ParameterDescription option)
    {
        UnitLimit unit = ParameterValueCodec.SelectUnit(option, null);
        (int min, int max) = ParameterValueCodec.GetRange(unit, option.Size);
        if (max < min || max - min > ParameterEditorFactory.MaxChoices)
            return [];
        return [.. Enumerable.Range(min, max - min + 1).Select(v => new ChoiceItem(v, ParameterEditorFactory.Label(unit, v, option.Name.Trim())))];
    }
}
