using CommunityToolkit.Mvvm.ComponentModel;

using BB84.MPX1.App.ViewModels.Parameters;
using BB84.MPX1.Core.Programs;
using BB84.MPX1.Core.Protocol;

namespace BB84.MPX1.App.ViewModels.Editing;

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
