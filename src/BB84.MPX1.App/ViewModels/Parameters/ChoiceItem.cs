namespace BB84.MPX1.App.ViewModels.Parameters;

/// <summary>An entry of a choice list.</summary>
/// <param name="Value">The raw value.</param>
/// <param name="Label">The label.</param>
public sealed record ChoiceItem(int Value, string Label)
{
    /// <inheritdoc/>
    public override string ToString() => Label;
}
