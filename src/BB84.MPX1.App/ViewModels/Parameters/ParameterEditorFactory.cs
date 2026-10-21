using System.Globalization;

using BB84.MPX1.Core.Database;
using BB84.MPX1.Core.Protocol;
using BB84.MPX1.Core.Values;

namespace BB84.MPX1.App.ViewModels.Parameters;

/// <summary>Chooses the editor for a parameter from its description and display units (UI-004).</summary>
public static class ParameterEditorFactory
{
    /// <summary>Largest value list shown as a drop-down.</summary>
    public const int MaxChoices = 32;

    private const ushort MidiChannelUnits = 0x18;
    private const ushort RateUnits = 0x17;
    private const ushort LastRateType = 0x0010;

    /// <summary>Creates the editor view model for a leaf node.</summary>
    /// <param name="node">An editable node.</param>
    /// <param name="context">The connection context.</param>
    public static ParameterViewModel Create(ControlTreeNode node, ParameterContext context)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(context);

        ParameterDescription description = node.Description;
        ParameterDescription? option = description.OptionType is ushort o ? context.Editor.Database.Describe(o) : null;
        UnitLimit unit = ParameterValueCodec.SelectUnit(description, null);
        string? reason = ReadOnlyReason(node, context);

        if (unit.DisplayUnits is 0x25 or 0x30 && description.Size > 0)
            return new TextParameterViewModel(node, context, reason);
        if (description.Size is not (1 or 2))
            return new DumpParameterViewModel(node, context, reason ?? "Dumps are shown read-only; dump editors come with the system editors.");
        if (string.Equals(node.Name, "MIDI Speed", StringComparison.OrdinalIgnoreCase))
            return new ChoiceParameterViewModel(node, [.. MidiSpeed.AllowedValues.Select(v => new ChoiceItem(v, MidiSpeed.Name(v) ?? Hex(v)))], context, reason);
        if (unit.DisplayUnits == RateUnits || (description.Size == 2 && description.Type <= LastRateType))
            return new RateParameterViewModel(node, context, reason);

        (int min, int max) = ParameterValueCodec.GetRange(unit, description.Size);
        if (option is null && description.Units.Length <= 1)
        {
            if (unit.DisplayUnits == MidiChannelUnits && max >= min && max - min < MaxChoices)
                return new ChoiceParameterViewModel(node, [.. Enumerable.Range(min, max - min + 1).Select(v => new ChoiceItem(v, Label(unit, v, node.Name)))], context, reason);
            if (min == 0 && max == 1)
                return new ToggleParameterViewModel(node, context, reason);
        }
        return new NumericParameterViewModel(node, option, context, reason);
    }

    /// <summary>
    /// Returns why a parameter must not be written, or <see langword="null"/>. Parameters of an algorithm that is
    /// not loaded can crash the unit when written (Appendix D bug 3, LRN-008, AC-015).
    /// </summary>
    /// <param name="node">The node.</param>
    /// <param name="context">The connection context.</param>
    public static string? ReadOnlyReason(ControlTreeNode node, ParameterContext context)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(context);
        byte[] algorithms = [.. context.ActiveAlgorithms];
        if (KnownBugs.IsUnloadedAlgorithmParameter(node.Address, algorithms))
        {
            string algorithm = node.Parent?.Name ?? "This algorithm";
            return $"{algorithm} is not loaded in the active program. Writing its parameters can crash the MPX 1, so editing is disabled.";
        }
        if (node.Name.Contains("Button", StringComparison.OrdinalIgnoreCase))
            return "Panel buttons are pressed from the virtual front panel, not edited as values.";
        return null;
    }

    /// <summary>Formats a value for a choice label, falling back to the number (PAR-003).</summary>
    /// <param name="unit">The unit/limit triple.</param>
    /// <param name="value">The value.</param>
    /// <param name="name">The parameter name.</param>
    public static string Label(UnitLimit unit, int value, string name)
        => DisplayUnitFormatter.IsSupported(unit.DisplayUnits)
            ? DisplayUnitFormatter.Format(unit, value, value, null, name)
            : value.ToString(CultureInfo.InvariantCulture);

    private static string Hex(byte value) => $"0x{value:X2}";
}
