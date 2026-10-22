using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;

using BB84.MPX1.App.ViewModels.Parameters;
using BB84.MPX1.Core.Database;
using BB84.MPX1.Core.Programs;
using BB84.MPX1.Core.Protocol;
using BB84.MPX1.Core.Values;

namespace BB84.MPX1.App.ViewModels.Editing;

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
