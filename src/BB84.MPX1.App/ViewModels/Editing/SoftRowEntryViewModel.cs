using BB84.MPX1.Core.Programs;

namespace BB84.MPX1.App.ViewModels.Editing;

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
