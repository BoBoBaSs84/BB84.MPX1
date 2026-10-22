using System.Globalization;

using CommunityToolkit.Mvvm.ComponentModel;

using BB84.MPX1.App.ViewModels.Parameters;
using BB84.MPX1.Core.Programs;
using BB84.MPX1.Core.Protocol;
using BB84.MPX1.Core.Values;

namespace BB84.MPX1.App.ViewModels.Editing;

/// <summary>
/// One parameter inside an effect or modulator block of the program, edited offline from its learned description
/// and the block layout (PRG-003, UI-006).
/// </summary>
public sealed partial class BlockParameterViewModel : ObservableObject
{
    private readonly IProgramDocument _document;
    private readonly ParameterBlockLayout _layout;
    private readonly int _index;
    private readonly Func<ProgramData, ReadOnlySpan<byte>> _read;
    private readonly Func<ProgramData, byte[], ProgramData> _write;
    private bool _applying;

    /// <summary>Initializes a new instance of the <see cref="BlockParameterViewModel"/> class.</summary>
    /// <param name="document">The program document.</param>
    /// <param name="layout">The block layout.</param>
    /// <param name="index">The parameter index.</param>
    /// <param name="read">Returns the block bytes of a program.</param>
    /// <param name="write">Returns a program with new block bytes.</param>
    public BlockParameterViewModel(IProgramDocument document, ParameterBlockLayout layout, int index,
        Func<ProgramData, ReadOnlySpan<byte>> read, Func<ProgramData, byte[], ProgramData> write)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _layout = layout ?? throw new ArgumentNullException(nameof(layout));
        _index = index;
        _read = read ?? throw new ArgumentNullException(nameof(read));
        _write = write ?? throw new ArgumentNullException(nameof(write));
        Parameter = layout.Parameters[index];
        if (Parameter.Option is { } option)
        {
            UnitLimit optionUnit = ParameterValueCodec.SelectUnit(option, null);
            (int min, int max) = option.Size == 0 ? (0, 1) : ParameterValueCodec.GetRange(optionUnit, option.Size);
            if (max >= min && max - min < ParameterEditorFactory.MaxChoices)
                Options = [.. Enumerable.Range(min, max - min + 1).Select(v => new ChoiceItem(v, ParameterEditorFactory.Label(optionUnit, v, option.Name.Trim())))];
        }
        Refresh();
    }

    /// <summary>Gets the parameter.</summary>
    public BlockParameter Parameter { get; }

    /// <summary>Gets the name.</summary>
    public string Name => Parameter.Name;

    /// <summary>Gets details for the tooltip.</summary>
    public string Details => $"#{_index} · type 0x{Parameter.Type:X4} · offset {Parameter.Offset} · {Parameter.Size} byte(s)";

    /// <summary>Gets a value indicating whether the parameter has a numeric editor.</summary>
    public bool IsNumeric => Parameter.IsNumeric;

    /// <summary>Gets the option choices; empty without an option.</summary>
    public IReadOnlyList<ChoiceItem> Options { get; } = [];

    /// <summary>Gets a value indicating whether there are option choices.</summary>
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

    /// <summary>Gets or sets the option.</summary>
    [ObservableProperty]
    public partial ChoiceItem? SelectedOption { get; set; }

    /// <summary>Gets or sets the offline display text (PAR-003).</summary>
    [ObservableProperty]
    public partial string DisplayText { get; set; } = "";

    /// <summary>Re-reads the value from the document.</summary>
    public void Refresh()
    {
        _applying = true;
        try
        {
            ReadOnlySpan<byte> block = _read(_document.Program);
            if (!IsNumeric)
            {
                DisplayText = Convert.ToHexString(_layout.Slice(block, _index));
                return;
            }
            ParameterValue value = _layout.Read(block, _index);
            if (value.Option is int option)
                SelectedOption = Options.FirstOrDefault(o => o.Value == option);
            ApplyUnit(value.Unit);
            Value = value.Value;
            Format(value.Unit, value.Value, value.RawValue, value.Option);
        }
        finally
        {
            _applying = false;
        }
    }

    partial void OnValueChanged(int value) => Commit();

    partial void OnSelectedOptionChanged(ChoiceItem? value)
    {
        if (_applying)
            return;
        ApplyUnit(ParameterValueCodec.SelectUnit(Parameter.Description, value?.Value));
        Commit();
    }

    private void ApplyUnit(UnitLimit unit)
    {
        (int min, int max) = ParameterValueCodec.GetRange(unit, Parameter.Description.Size);
        Minimum = Math.Min(min, max);
        Maximum = Math.Max(min, max);
    }

    private void Commit()
    {
        if (_applying || !IsNumeric)
            return;
        int value = Math.Clamp(Value, Minimum, Maximum);
        int? option = Parameter.Option is null ? null : SelectedOption?.Value ?? 0;
        _document.Update(p => _write(p, _layout.Write(_read(p), _index, value, option)));
        UnitLimit unit = ParameterValueCodec.SelectUnit(Parameter.Description, option);
        Format(unit, value, value, option);
    }

    private void Format(UnitLimit unit, int value, int raw, int? option)
        => DisplayText = DisplayUnitFormatter.IsSupported(unit.DisplayUnits)
            ? DisplayUnitFormatter.Format(unit, value, raw, option, Name)
            : value.ToString(CultureInfo.InvariantCulture);
}
