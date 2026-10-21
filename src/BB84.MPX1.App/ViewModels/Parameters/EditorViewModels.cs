using System.Buffers.Binary;
using System.Text;

using CommunityToolkit.Mvvm.ComponentModel;

using BB84.MPX1.App.Services;
using BB84.MPX1.Core.Database;
using BB84.MPX1.Core.Protocol;
using BB84.MPX1.Core.Values;
using BB84.MPX1.Midi.Editing;

namespace BB84.MPX1.App.ViewModels.Parameters;

/// <summary>An entry of a choice list.</summary>
/// <param name="Value">The raw value.</param>
/// <param name="Label">The label.</param>
public sealed record ChoiceItem(int Value, string Label)
{
    /// <inheritdoc/>
    public override string ToString() => Label;
}

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

/// <summary>Rate editor with Hz and ratio modes (PAR-004, UI-004).</summary>
public sealed partial class RateParameterViewModel : ParameterViewModel
{
    private readonly CoalescingWriter<RateValue> _writer;

    /// <summary>Initializes a new instance of the <see cref="RateParameterViewModel"/> class.</summary>
    /// <param name="node">The tree node.</param>
    /// <param name="context">The connection context.</param>
    /// <param name="readOnlyReason">Why the parameter is read-only, or <see langword="null"/>.</param>
    public RateParameterViewModel(ControlTreeNode node, ParameterContext context, string? readOnlyReason)
        : base(node, context, readOnlyReason)
        => _writer = CreateWriter<RateValue>((v, ct) => Context.Editor.WriteAsync(Node.Address, Encode(v), ct));

    /// <summary>Gets the ratio term choices (1–24).</summary>
    public IReadOnlyList<int> RatioTerms { get; } = [.. Enumerable.Range(1, RateValue.MaxRatioTerm)];

    /// <summary>Gets or sets a value indicating whether the rate is a tempo ratio instead of a frequency.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsHertz))]
    public partial bool IsRatio { get; set; }

    /// <summary>Gets a value indicating whether the rate is a frequency.</summary>
    public bool IsHertz => !IsRatio;

    /// <summary>Gets or sets the frequency in Hz (0.00–50.00).</summary>
    [ObservableProperty]
    public partial double Hertz { get; set; }

    /// <summary>Gets or sets the ratio numerator (1–24).</summary>
    [ObservableProperty]
    public partial int Numerator { get; set; } = 1;

    /// <summary>Gets or sets the ratio denominator (1–24).</summary>
    [ObservableProperty]
    public partial int Denominator { get; set; } = 1;

    /// <summary>Gets the current value.</summary>
    public RateValue Current => IsRatio
        ? RateValue.FromRatio(Math.Clamp(Numerator, 1, RateValue.MaxRatioTerm), Math.Clamp(Denominator, 1, RateValue.MaxRatioTerm))
        : RateValue.FromCentihertz((int)Math.Round(Math.Clamp(Hertz, 0, RateValue.MaxCentihertz / 100.0) * 100));

    /// <inheritdoc/>
    public override void CancelPendingWrites() => _writer.Dispose();

    /// <inheritdoc/>
    protected override void Apply(ParameterReading reading)
    {
        if (reading.Data.Length < 2)
            return;
        RateValue rate = RateValue.FromRaw(BinaryPrimitives.ReadUInt16LittleEndian(reading.Data.Span));
        IsRatio = rate.IsRatio;
        if (rate.IsRatio)
        {
            Numerator = rate.Numerator;
            Denominator = rate.Denominator;
        }
        else
        {
            Hertz = rate.Centihertz / 100.0;
        }
    }

    partial void OnIsRatioChanged(bool value) => Submit();

    partial void OnHertzChanged(double value) => Submit();

    partial void OnNumeratorChanged(int value) => Submit();

    partial void OnDenominatorChanged(int value) => Submit();

    private void Submit()
    {
        if (IsApplying || !IsEditable || !IsLoaded)
            return;
        RateValue rate = Current;
        DisplayText = rate.ToString();
        _writer.Submit(rate);
    }

    private byte[] Encode(RateValue rate)
    {
        // The ratio flag lives in the MSB (PAR-004/005), so the value is written raw rather than range-checked.
        byte[] data = new byte[Math.Max(2, Context.Editor.Database.ValueSize(Node.Type))];
        BinaryPrimitives.WriteUInt16LittleEndian(data, rate.ToRaw());
        return data;
    }
}

/// <summary>Read-only view of a dump or other parameter that has no editor yet.</summary>
public sealed class DumpParameterViewModel(ControlTreeNode node, ParameterContext context, string readOnlyReason)
    : ParameterViewModel(node, context, readOnlyReason)
{
    private const int PreviewBytes = 48;
    private string _preview = "";

    /// <summary>Gets a hex preview of the data.</summary>
    public string Preview
    {
        get => _preview;
        private set => SetProperty(ref _preview, value);
    }

    /// <inheritdoc/>
    protected override void Apply(ParameterReading reading)
    {
        ReadOnlySpan<byte> data = reading.Data.Span;
        StringBuilder sb = new(Convert.ToHexString(data[..Math.Min(data.Length, PreviewBytes)]));
        for (int i = sb.Length - 2; i > 0; i -= 2)
            sb.Insert(i, ' ');
        if (data.Length > PreviewBytes)
            sb.Append(" …");
        Preview = $"{data.Length} bytes: {sb}";
    }
}
