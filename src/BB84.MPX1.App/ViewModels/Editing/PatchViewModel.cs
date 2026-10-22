using CommunityToolkit.Mvvm.ComponentModel;

using BB84.MPX1.App.ViewModels.Parameters;
using BB84.MPX1.Core.Programs;

namespace BB84.MPX1.App.ViewModels.Editing;

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
