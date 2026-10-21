using System.Collections.ObjectModel;
using System.Globalization;

using CommunityToolkit.Mvvm.ComponentModel;

using BB84.MPX1.App.ViewModels.Parameters;
using BB84.MPX1.Core.Database;
using BB84.MPX1.Core.Programs;
using BB84.MPX1.Core.Protocol;
using BB84.MPX1.Core.Values;

namespace BB84.MPX1.App.ViewModels.Editing;

/// <summary>The program being edited, as seen by the editor sections.</summary>
public interface IProgramDocument
{
    /// <summary>Gets the current program.</summary>
    ProgramData Program { get; }

    /// <summary>Gets the learned database, or <see langword="null"/> when no device was ever learned.</summary>
    ParameterDatabase? Database { get; }

    /// <summary>Applies a change to the program (marks it dirty and auditions it).</summary>
    /// <param name="change">Returns the changed program.</param>
    void Update(Func<ProgramData, ProgramData> change);
}

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

/// <summary>One of the six effect blocks: on/off, algorithm and its parameters (UI-006).</summary>
public sealed partial class EffectBlockViewModel : ObservableObject
{
    private readonly IProgramDocument _document;
    private bool _applying;

    /// <summary>Initializes a new instance of the <see cref="EffectBlockViewModel"/> class.</summary>
    /// <param name="document">The program document.</param>
    /// <param name="index">Block index 0–5.</param>
    public EffectBlockViewModel(IProgramDocument document, int index)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        Index = index;
        Name = ProgramFields.EffectNames[index];
        ParameterDatabase? database = document.Database;
        int count = database?.AlgorithmCount((EffectType)index) ?? 0;
        byte current = document.Program.Algorithms[index];
        Algorithms = [.. Enumerable.Range(0, Math.Max(count, current + 1))
            .Select(a => new ChoiceItem(a, database?.FindAlgorithm((EffectType)index, (byte)a)?.Name ?? $"Algorithm {a}"))];
        Refresh();
    }

    /// <summary>Gets the block index.</summary>
    public int Index { get; }

    /// <summary>Gets the block name.</summary>
    public string Name { get; }

    /// <summary>Gets the algorithms the block offers.</summary>
    public IReadOnlyList<ChoiceItem> Algorithms { get; }

    /// <summary>Gets the parameters of the selected algorithm.</summary>
    public ObservableCollection<BlockParameterViewModel> Parameters { get; } = [];

    /// <summary>Gets or sets the selected algorithm.</summary>
    [ObservableProperty]
    public partial ChoiceItem? SelectedAlgorithm { get; set; }

    /// <summary>Gets or sets a value indicating whether the effect is active (not bypassed).</summary>
    [ObservableProperty]
    public partial bool IsOn { get; set; }

    /// <summary>Gets or sets a notice, for example when the algorithm's parameter list is unknown.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotice))]
    public partial string? Notice { get; set; }

    /// <summary>Gets a value indicating whether <see cref="Notice"/> is set.</summary>
    public bool HasNotice => Notice is not null;

    /// <summary>Raised after the algorithm changed (soft row and patch lists depend on it).</summary>
    public event EventHandler? AlgorithmChanged;

    private EffectStatus Bit => (EffectStatus)(1 << Index);

    /// <summary>Re-reads the block from the document.</summary>
    public void Refresh()
    {
        _applying = true;
        try
        {
            ProgramData program = _document.Program;
            IsOn = program.EffectStatus.HasFlag(Bit);
            SelectedAlgorithm = Algorithms.FirstOrDefault(a => a.Value == program.Algorithms[Index]);
            BuildParameters();
        }
        finally
        {
            _applying = false;
        }
    }

    partial void OnIsOnChanged(bool value)
    {
        if (!_applying)
            _document.Update(p => p with { EffectStatus = value ? p.EffectStatus | Bit : p.EffectStatus & ~Bit });
    }

    partial void OnSelectedAlgorithmChanged(ChoiceItem? value)
    {
        if (_applying || value is null)
            return;
        byte algorithm = (byte)value.Value;
        ParameterBlockLayout? layout = TryLayout(algorithm);
        _document.Update(p =>
        {
            ProgramData changed = p with { Algorithms = p.Algorithms.SetItem(Index, algorithm) };
            // Old values may not suit the new algorithm; clamp them to the new limits (document, "Algorithm numbers").
            return layout is null ? changed : changed.WithEffectData(Index, Clamp(layout, changed.EffectData[Index].AsSpan()));
        });
        BuildParameters();
        AlgorithmChanged?.Invoke(this, EventArgs.Empty);
    }

    private void BuildParameters()
    {
        Parameters.Clear();
        Notice = null;
        if (_document.Database is null)
        {
            Notice = "Learn a device once to edit effect parameters; the layout comes from its parameter lists.";
            return;
        }
        byte algorithm = _document.Program.Algorithms[Index];
        if (TryLayout(algorithm) is not { } layout)
        {
            Notice = algorithm == ProgramData.ClearedAlgorithm ? "This program is cleared." : $"No parameter list is known for algorithm {algorithm}.";
            return;
        }
        for (int i = 0; i < layout.Parameters.Length; i++)
        {
            Parameters.Add(new BlockParameterViewModel(_document, layout, i,
                p => p.EffectData[Index].AsSpan(), (p, bytes) => p.WithEffectData(Index, bytes)));
        }
    }

    private ParameterBlockLayout? TryLayout(byte algorithm)
    {
        try
        {
            return _document.Database?.Layout((EffectType)Index, algorithm);
        }
        catch (ArgumentException ex)
        {
            Notice = ex.Message;
            return null;
        }
    }

    internal static byte[] Clamp(ParameterBlockLayout layout, ReadOnlySpan<byte> block)
    {
        byte[] data = block.ToArray();
        for (int i = 0; i < layout.Parameters.Length; i++)
        {
            if (!layout.Parameters[i].IsNumeric)
                continue;
            ParameterValue value = layout.Read(data, i);
            (int min, int max) = ParameterValueCodec.GetRange(value.Unit, layout.Parameters[i].Description.Size);
            if (min <= max && (value.Value < min || value.Value > max))
                data = layout.Write(data, i, Math.Clamp(value.Value, min, max), value.Option);
        }
        return data;
    }
}

/// <summary>One modulator block: LFO, arpeggiator, ADSR, random, A/B, sample and hold or envelope (UI-006).</summary>
public sealed class ModulatorBlockViewModel
{
    /// <summary>Initializes a new instance of the <see cref="ModulatorBlockViewModel"/> class.</summary>
    /// <param name="document">The program document.</param>
    /// <param name="block">The modulator block.</param>
    public ModulatorBlockViewModel(IProgramDocument document, ModulatorBlock block)
    {
        ArgumentNullException.ThrowIfNull(document);
        Block = block ?? throw new ArgumentNullException(nameof(block));
        ParameterBlockLayout? layout = null;
        try
        {
            layout = document.Database?.Layout(block.Type, 0);
        }
        catch (ArgumentException ex)
        {
            Notice = ex.Message;
        }
        if (layout is null)
        {
            Notice ??= document.Database is null
                ? "Learn a device once to edit modulators."
                : $"No parameter list is known for {block.Name}.";
            return;
        }
        for (int i = 0; i < layout.Parameters.Length; i++)
            Parameters.Add(new BlockParameterViewModel(document, layout, i, p => p.GetModulatorBlock(block), (p, bytes) => p.WithModulatorBlock(block, bytes)));
    }

    /// <summary>Gets the block.</summary>
    public ModulatorBlock Block { get; }

    /// <summary>Gets the name.</summary>
    public string Name => Block.Name;

    /// <summary>Gets the parameters.</summary>
    public ObservableCollection<BlockParameterViewModel> Parameters { get; } = [];

    /// <summary>Gets a notice, or <see langword="null"/>.</summary>
    public string? Notice { get; }

    /// <summary>Gets a value indicating whether <see cref="Notice"/> is set.</summary>
    public bool HasNotice => Notice is not null;
}

/// <summary>
/// Two-step destination picker for soft-row entries and patches: block or controller first, then a parameter of
/// that block's current algorithm by name. The stored value is the parameter's index (document, Soft Values).
/// </summary>
public sealed partial class DestinationPickerViewModel : ObservableObject
{
    private readonly IProgramDocument _document;
    private readonly bool _allowSystem;
    private bool _applying;

    /// <summary>Initializes a new instance of the <see cref="DestinationPickerViewModel"/> class.</summary>
    /// <param name="document">The program document.</param>
    /// <param name="allowSystem">Whether "System" (Master Mix/Level) is offered (patches only).</param>
    /// <param name="type">The stored destination type.</param>
    /// <param name="index">The stored parameter index.</param>
    public DestinationPickerViewModel(IProgramDocument document, bool allowSystem, byte type, byte index)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _allowSystem = allowSystem;
        List<ChoiceItem> types = [new(ControllerSources.Unassigned, "None")];
        for (int t = 0; t <= (int)EffectType.EnvelopeGenerator2; t++)
        {
            if (t == (int)EffectType.Arpeggiator && allowSystem)
                continue; // None of the arpeggiator's parameters can be patched.
            types.Add(new ChoiceItem(t, ProgramFields.DestinationName((byte)t)));
        }
        if (allowSystem)
            types.Add(new ChoiceItem(ProgramFields.SystemParameters, "System"));
        Types = types;
        Load(type, index);
    }

    /// <summary>Gets the destination types.</summary>
    public IReadOnlyList<ChoiceItem> Types { get; }

    /// <summary>Gets the parameters of the selected type.</summary>
    public ObservableCollection<ChoiceItem> Parameters { get; } = [];

    /// <summary>Gets or sets the selected type.</summary>
    [ObservableProperty]
    public partial ChoiceItem? SelectedType { get; set; }

    /// <summary>Gets or sets the selected parameter.</summary>
    [ObservableProperty]
    public partial ChoiceItem? SelectedParameter { get; set; }

    /// <summary>Raised when the user changed the destination.</summary>
    public event EventHandler? Changed;

    /// <summary>Gets the stored type value.</summary>
    public byte TypeValue => (byte)(SelectedType?.Value ?? ControllerSources.Unassigned);

    /// <summary>Gets the stored index value.</summary>
    public byte IndexValue => (byte)(SelectedParameter?.Value ?? ControllerSources.Unassigned);

    /// <summary>Returns the selected parameter's limits for patch destination values.</summary>
    public (int Minimum, int Maximum, bool IsSigned) Limits()
    {
        if (TypeValue == ProgramFields.SystemParameters)
            return IndexValue == 1 ? (ProgramFields.MasterLevelOff, 0, true) : (0, ProgramFields.MaxMasterMix, false);
        if (Describe() is not { } description || description.Size is not (1 or 2))
            return (0, 127, false);
        UnitLimit unit = ParameterValueCodec.SelectUnit(description, null);
        (int min, int max) = ParameterValueCodec.GetRange(unit, description.Size);
        return (Math.Min(min, max), Math.Max(min, max), ParameterValueCodec.IsSigned(unit));
    }

    /// <summary>Selects a stored destination without raising <see cref="Changed"/>.</summary>
    /// <param name="type">The stored type.</param>
    /// <param name="index">The stored index.</param>
    public void Load(byte type, byte index)
    {
        _applying = true;
        try
        {
            SelectedType = Types.FirstOrDefault(t => t.Value == type) ?? Types[0];
            FillParameters();
            SelectedParameter = Parameters.FirstOrDefault(p => p.Value == index);
        }
        finally
        {
            _applying = false;
        }
    }

    /// <summary>Rebuilds the parameter names, for example after an algorithm change.</summary>
    public void RefreshParameters() => Load(TypeValue, IndexValue);

    partial void OnSelectedTypeChanged(ChoiceItem? value)
    {
        if (_applying)
            return;
        _applying = true;
        try
        {
            FillParameters();
            SelectedParameter = Parameters.FirstOrDefault();
        }
        finally
        {
            _applying = false;
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    partial void OnSelectedParameterChanged(ChoiceItem? value)
    {
        if (!_applying)
            Changed?.Invoke(this, EventArgs.Empty);
    }

    private void FillParameters()
    {
        Parameters.Clear();
        byte type = TypeValue;
        if (type == ControllerSources.Unassigned)
            return;
        if (type == ProgramFields.SystemParameters && _allowSystem)
        {
            Parameters.Add(new ChoiceItem(0, "Master Mix"));
            Parameters.Add(new ChoiceItem(1, "Master Level"));
            return;
        }
        if (List(type) is { } list)
        {
            for (int i = 0; i < list.Types.Length; i++)
                Parameters.Add(new ChoiceItem(i, _document.Database!.Describe(list.Types[i])?.Name.Trim() ?? $"Parameter {i}"));
        }
    }

    private AlgorithmParameters? List(byte type)
    {
        if (_document.Database is not { } database || type > (byte)EffectType.EnvelopeGenerator2)
            return null;
        byte algorithm = type < ProgramData.EffectBlockCount ? _document.Program.Algorithms[type] : (byte)0;
        return database.FindAlgorithm((EffectType)type, algorithm);
    }

    private ParameterDescription? Describe()
        => List(TypeValue) is { } list && IndexValue < list.Types.Length ? _document.Database!.Describe(list.Types[IndexValue]) : null;
}

/// <summary>One of the ten soft-row entries (UI-006).</summary>
public sealed class SoftRowEntryViewModel
{
    /// <summary>Initializes a new instance of the <see cref="SoftRowEntryViewModel"/> class.</summary>
    /// <param name="document">The program document.</param>
    /// <param name="index">Entry 0–9.</param>
    public SoftRowEntryViewModel(IProgramDocument document, int index)
    {
        ArgumentNullException.ThrowIfNull(document);
        Index = index;
        SoftValue value = document.Program.SoftValues[index];
        Destination = new DestinationPickerViewModel(document, allowSystem: false, value.EffectType, value.ParameterIndex);
        Destination.Changed += (_, _) => document.Update(p => p with
        {
            SoftValues = p.SoftValues.SetItem(Index, new SoftValue(Destination.TypeValue, Destination.IndexValue)),
        });
    }

    /// <summary>Gets the entry index.</summary>
    public int Index { get; }

    /// <summary>Gets the label.</summary>
    public string Label => $"Soft {Index + 1}";

    /// <summary>Gets the destination picker.</summary>
    public DestinationPickerViewModel Destination { get; }
}

/// <summary>One of the five program patches: source, source range, destination and destination range (UI-006).</summary>
public sealed partial class PatchViewModel : ObservableObject
{
    private readonly IProgramDocument _document;
    private bool _applying;

    /// <summary>Initializes a new instance of the <see cref="PatchViewModel"/> class.</summary>
    /// <param name="document">The program document.</param>
    /// <param name="index">Patch 0–4.</param>
    public PatchViewModel(IProgramDocument document, int index)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        Index = index;
        Patch patch = document.Program.Patches[index];
        Destination = new DestinationPickerViewModel(document, allowSystem: true, patch.DestinationEffectType, patch.DestinationIndex);
        Destination.Changed += (_, _) => OnDestinationChanged();
        _applying = true;
        try
        {
            SelectedSource = Sources.FirstOrDefault(s => s.Value == patch.SourceController) ?? Sources[0];
            SourceMinimum = patch.SourceMinimum;
            SourceMid = patch.SourceMid == ControllerSources.Unassigned ? -1 : patch.SourceMid;
            SourceMaximum = patch.SourceMaximum;
            ApplyLimits();
            DestinationMinimum = FromStored(patch.DestinationMinimum);
            DestinationMid = FromStored(patch.DestinationMid);
            DestinationMaximum = FromStored(patch.DestinationMaximum);
        }
        finally
        {
            _applying = false;
        }
    }

    /// <summary>Gets the selectable sources (Appendix A), "None" first.</summary>
    public static IReadOnlyList<ChoiceItem> Sources { get; } =
        [new(ControllerSources.Unassigned, "None"), .. ControllerSources.PatchSources.Select(s => new ChoiceItem(s, ControllerSources.Name(s)))];

    /// <summary>Gets the patch index.</summary>
    public int Index { get; }

    /// <summary>Gets the label.</summary>
    public string Label => $"Patch {Index + 1}";

    /// <summary>Gets the destination picker.</summary>
    public DestinationPickerViewModel Destination { get; }

    /// <summary>Gets or sets the source controller.</summary>
    [ObservableProperty]
    public partial ChoiceItem? SelectedSource { get; set; }

    /// <summary>Gets or sets the source minimum (0–127).</summary>
    [ObservableProperty]
    public partial int SourceMinimum { get; set; }

    /// <summary>Gets or sets the source mid value (0–127, −1 = not used).</summary>
    [ObservableProperty]
    public partial int SourceMid { get; set; }

    /// <summary>Gets or sets the source maximum (0–127).</summary>
    [ObservableProperty]
    public partial int SourceMaximum { get; set; }

    /// <summary>Gets or sets the destination minimum.</summary>
    [ObservableProperty]
    public partial int DestinationMinimum { get; set; }

    /// <summary>Gets or sets the destination mid value.</summary>
    [ObservableProperty]
    public partial int DestinationMid { get; set; }

    /// <summary>Gets or sets the destination maximum.</summary>
    [ObservableProperty]
    public partial int DestinationMaximum { get; set; }

    /// <summary>Gets or sets the lowest destination value the parameter allows.</summary>
    [ObservableProperty]
    public partial int DestinationLimitMinimum { get; set; }

    /// <summary>Gets or sets the highest destination value the parameter allows.</summary>
    [ObservableProperty]
    public partial int DestinationLimitMaximum { get; set; }

    private bool IsSigned { get; set; }

    /// <summary>Rebuilds destination names after an algorithm change.</summary>
    public void RefreshDestination()
    {
        Destination.RefreshParameters();
        _applying = true;
        try
        {
            ApplyLimits();
        }
        finally
        {
            _applying = false;
        }
    }

    partial void OnSelectedSourceChanged(ChoiceItem? value) => Commit();

    partial void OnSourceMinimumChanged(int value) => Commit();

    partial void OnSourceMidChanged(int value) => Commit();

    partial void OnSourceMaximumChanged(int value) => Commit();

    partial void OnDestinationMinimumChanged(int value) => Commit();

    partial void OnDestinationMidChanged(int value) => Commit();

    partial void OnDestinationMaximumChanged(int value) => Commit();

    private void OnDestinationChanged()
    {
        _applying = true;
        try
        {
            ApplyLimits();
            DestinationMinimum = DestinationLimitMinimum;
            DestinationMaximum = DestinationLimitMaximum;
            DestinationMid = DestinationLimitMinimum + ((DestinationLimitMaximum - DestinationLimitMinimum) / 2);
        }
        finally
        {
            _applying = false;
        }
        Commit();
    }

    private void ApplyLimits()
    {
        (int min, int max, bool signed) = Destination.Limits();
        IsSigned = signed;
        DestinationLimitMinimum = min;
        DestinationLimitMaximum = max;
    }

    private int FromStored(ushort stored) => IsSigned ? (short)stored : stored;

    private static ushort ToStored(int value) => unchecked((ushort)value);

    private void Commit()
    {
        if (_applying)
            return;
        Patch patch = new(
            (byte)(SelectedSource?.Value ?? ControllerSources.Unassigned),
            (byte)Math.Clamp(SourceMinimum, 0, 127),
            SourceMid < 0 ? ControllerSources.Unassigned : (byte)Math.Clamp(SourceMid, 0, 127),
            (byte)Math.Clamp(SourceMaximum, 0, 127),
            Destination.TypeValue,
            Destination.IndexValue,
            ToStored(Math.Clamp(DestinationMinimum, DestinationLimitMinimum, DestinationLimitMaximum)),
            ToStored(Math.Clamp(DestinationMid, DestinationLimitMinimum, DestinationLimitMaximum)),
            ToStored(Math.Clamp(DestinationMaximum, DestinationLimitMinimum, DestinationLimitMaximum)));
        _document.Update(p => p with { Patches = p.Patches.SetItem(Index, patch) });
    }
}

/// <summary>One position of the audio routing table, with only the routings the rules allow (UI-006).</summary>
public sealed partial class RoutingRowViewModel : ObservableObject
{
    private readonly Action<RoutingRowViewModel> _changed;
    private readonly bool _applying;

    /// <summary>Initializes a new instance of the <see cref="RoutingRowViewModel"/> class.</summary>
    /// <param name="slot">The block and its lanes.</param>
    /// <param name="allowedModes">Routings allowed at this position.</param>
    /// <param name="changed">Called after the user changed the row.</param>
    public RoutingRowViewModel(RoutingSlot slot, IReadOnlyList<RoutingMode> allowedModes, Action<RoutingRowViewModel> changed)
    {
        ArgumentNullException.ThrowIfNull(slot);
        _changed = changed ?? throw new ArgumentNullException(nameof(changed));
        Slot = slot;
        Modes = [.. allowedModes.Select(m => new ChoiceItem((int)m, m.ToString()))];
        _applying = true;
        SelectedEffect = Effects.FirstOrDefault(e => e.Value == slot.Block.EffectType);
        SelectedMode = Modes.FirstOrDefault(m => m.Value == slot.Block.Routing) ?? new ChoiceItem(slot.Block.Routing, ((RoutingMode)slot.Block.Routing).ToString());
        SelectedUpperInput = Inputs.FirstOrDefault(i => i.Value == slot.Block.UpperInput);
        SelectedLowerInput = Inputs.FirstOrDefault(i => i.Value == slot.Block.LowerInput);
        _applying = false;
    }

    /// <summary>Gets the effect choices (positions 1–6).</summary>
    public static IReadOnlyList<ChoiceItem> Effects { get; } = [.. Enumerable.Range(0, 6).Select(e => new ChoiceItem(e, ((RoutingBlockType)e).ToString()))];

    /// <summary>Gets the input connection choices.</summary>
    public static IReadOnlyList<ChoiceItem> Inputs { get; } =
    [
        new((int)InputConnection.Stereo, "Stereo"), new((int)InputConnection.LeftToLeft, "L → L"), new((int)InputConnection.RightToRight, "R → R"),
        new((int)InputConnection.LeftToBoth, "L → L+R"), new((int)InputConnection.RightToBoth, "R → L+R"),
    ];

    /// <summary>Gets the block and lanes.</summary>
    public RoutingSlot Slot { get; }

    /// <summary>Gets the position.</summary>
    public int Position => Slot.Position;

    /// <summary>Gets the block name.</summary>
    public string Name => Slot.Type.ToString();

    /// <summary>Gets a value indicating whether the effect at this position can be changed (not input/output).</summary>
    public bool IsEffect => Position is > 0 and < AudioRouting.BlockCount - 1;

    /// <summary>Gets a value indicating whether the routing can be chosen (not the output block).</summary>
    public bool CanChooseMode => Position < AudioRouting.BlockCount - 1;

    /// <summary>Gets a value indicating whether input connections apply (not the input block).</summary>
    public bool HasInputs => Position > 0;

    /// <summary>Gets a value indicating whether the lower input is used.</summary>
    public bool UsesLowerInput => HasInputs && Slot.OnLower;

    /// <summary>Gets a value indicating whether the upper input is used.</summary>
    public bool UsesUpperInput => HasInputs && Slot.OnUpper && Slot.Mode != RoutingMode.Lower;

    /// <summary>Gets the allowed routings.</summary>
    public IReadOnlyList<ChoiceItem> Modes { get; }

    /// <summary>Gets or sets the effect at this position.</summary>
    [ObservableProperty]
    public partial ChoiceItem? SelectedEffect { get; set; }

    /// <summary>Gets or sets the routing.</summary>
    [ObservableProperty]
    public partial ChoiceItem? SelectedMode { get; set; }

    /// <summary>Gets or sets the upper input connection.</summary>
    [ObservableProperty]
    public partial ChoiceItem? SelectedUpperInput { get; set; }

    /// <summary>Gets or sets the lower input connection.</summary>
    [ObservableProperty]
    public partial ChoiceItem? SelectedLowerInput { get; set; }

    /// <summary>Gets what the user changed last.</summary>
    public string LastChange { get; private set; } = "";

    partial void OnSelectedEffectChanged(ChoiceItem? value) => Notify(nameof(SelectedEffect));

    partial void OnSelectedModeChanged(ChoiceItem? value) => Notify(nameof(SelectedMode));

    partial void OnSelectedUpperInputChanged(ChoiceItem? value) => Notify(nameof(SelectedUpperInput));

    partial void OnSelectedLowerInputChanged(ChoiceItem? value) => Notify(nameof(SelectedLowerInput));

    private void Notify(string change)
    {
        if (_applying)
            return;
        LastChange = change;
        _changed(this);
    }
}
