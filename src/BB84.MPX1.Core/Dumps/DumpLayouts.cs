using System.Collections.Immutable;

using BB84.MPX1.Core.Programs;
using BB84.MPX1.Core.Values;

namespace BB84.MPX1.Core.Dumps;

/// <summary>
/// The documented dump layouts as data (GUD-004): Setup (DMP-004), Global Patches (DMP-005), Bypass Controllers
/// (DMP-006), Remap Controllers (DMP-007), Current Choices (DMP-008), Patches (DMP-009) and Soft Row (DMP-010).
/// </summary>
public static class DumpLayouts
{
    private static readonly ImmutableArray<DumpChoice> OffOn = [new(0, "Off"), new(1, "On")];

    private static readonly ImmutableArray<DumpChoice> MidiChannels =
        [.. Enumerable.Range(0, 16).Select(c => new DumpChoice(c, (c + 1).ToString(System.Globalization.CultureInfo.InvariantCulture))), new(16, "Off"), new(17, "Omni")];

    /// <summary>Gets the Setup layout: 44 bytes (DMP-004).</summary>
    public static DumpLayout Setup { get; } = Build("Setup",
    [
        Choice("MIDI receive channel", 1, MidiChannels),
        Choice("MIDI transmit speed", 2, [.. MidiSpeed.AllowedValues.Select(v => new DumpChoice(v, MidiSpeed.Name(v)!))]),
        Number("SysEx device ID", 1, 0, 126),
        Toggle("SysEx receive"),
        Choice("MIDI transmit channel", 1, [.. MidiChannels.Take(17)]),
        Number("Program number offset", 2, 0, ProgramSlots.Count - 1),
        Toggle("MIDI automation"),
        Toggle("MIDI clock send"),
        Choice("Program sort mode", 1, Labels("Name", "Number", "Input", "Effect type", "Input and effect", "MIDI maps", "MIDI chains", "Top 10")),
        Number("LCD contrast", 1, 0, 15),
        Number("Automation transmit ID", 1, 0, 127, help: "127 sends to all devices."),
        Toggle("MIDI program change"),
        Number("MIDI controller smoothing", 1, 0, 100),
        Choice("Bypass mode", 1, Labels("Normal", "All mute", "Input mute")),
        Toggle("Memory protect"),
        Toggle("Store prompt"),
        Internal("patch_update_mode", 1, "Not implemented on the unit."),
        Choice("Sleep mode", 1, Labels("None", "Help", "English promo", "French promo", "German promo", "Italian promo", "Spanish promo")),
        Choice("Mix mode", 1, Labels("Global", "Program")),
        Choice("Program load audio", 1, Labels("Bypass", "All mute")),
        Controller("Program + controller", ControllerSources.PatchSources),
        Controller("Program − controller", ControllerSources.PatchSources),
        Choice("Input / clock source", 1, Labels("Analog, internal clock", "Analog, external clock", "Digital, external clock")),
        Choice("Audio output", 1, Labels("Analog", "Digital")),
        Signed("Digital input level", 1, -90, 6),
        Choice("Channel status", 1, Labels("Pass through", "Generate")),
        Toggle("Soft saturation"),
        Internal("Pedal max (calibration)", 1, "Set by pedal calibration on the unit."),
        Internal("Pedal delta (calibration)", 1, "Set by pedal calibration on the unit."),
        Choice("Tempo mode", 1, Labels("Global", "Program")),
        Choice("Audio input mode", 1, Labels("Stereo", "Left mono", "Right mono")),
        Choice("Program autoload", 1, Labels("Manual", "Auto-load")),
        Text("Setup name", 9),
        Number("Global bypass level", 1, 0, 255, help: "Level of the dry signal in bypass and during program loads (raw value)."),
    ]);

    /// <summary>Gets the Global Patches layout: 10 sources, then 10 destinations (DMP-005).</summary>
    public static DumpLayout GlobalPatches { get; } = Build("Global Patches",
    [
        .. Enumerable.Range(1, 10).Select(i => Controller($"Patch {i} source", ControllerSources.GlobalPatchSources)),
        .. Enumerable.Range(1, 10).Select(i => Choice($"Patch {i} destination", 1, Labels(
            "None", "Master mix", "Pitch mix", "Chorus mix", "EQ mix", "Mod mix", "Delay mix", "Reverb mix",
            "Master level", "Pitch level", "Chorus level", "EQ level", "Mod level", "Delay level", "Reverb level"))),
    ]);

    /// <summary>Gets the Bypass Controllers layout: seven sources (DMP-006).</summary>
    public static DumpLayout BypassControllers { get; } = Build("Bypass Controllers",
    [
        .. new[] { "Master", "Pitch", "Chorus", "Modulation", "EQ", "Reverb", "Delay" }
            .Select(n => Controller($"{n} bypass", ControllerSources.BypassSources)),
    ]);

    /// <summary>Gets the Remap Controllers layout: one MIDI controller number per Appendix A source, 0xFF = not remapped (DMP-007).</summary>
    public static DumpLayout RemapControllers { get; } = Build("Remap Controllers",
    [
        .. Enumerable.Range(0, ControllerSources.Last + 1).Select(i => new DumpField(ControllerSources.Name((byte)i), 0, 1, DumpFieldKind.Choice)
        {
            Choices = [.. Enumerable.Range(0, 128).Select(c => new DumpChoice(c, $"CC{c}")), new(ControllerSources.Unassigned, "Not remapped")],
        }),
    ]);

    /// <summary>Gets the Current Choices layout: menu state of the unit, read-only (DMP-008).</summary>
    public static DumpLayout CurrentChoices { get; } = Build("Current Choices",
    [
        .. new (string Name, int Size)[]
        {
            ("Program bank", 1), ("Effect edit parameter", 1), ("Algorithm select", 1), ("(unused)", 1), ("(unused)", 1),
            ("Compare mode", 1), ("Database choice", 1), ("Soft row play choice", 1), ("Soft row choice", 1), ("Soft row edit choice", 1),
            ("Map block", 1), ("Routing item", 1), ("(unused)", 1), ("Order choice", 1), ("Mix choice", 1), ("Patch choice", 1),
            ("Audio choice", 1), ("Audio options choice", 1), ("MIDI choice", 1), ("Chains choice", 1), ("Store choice", 1),
            ("Edit page", 1), ("Edit page name pointer", 2), ("Edit parameter index", 1), ("Edit parameter count", 1),
            ("System page", 1), ("System page name pointer", 2), ("System parameter index", 1), ("System parameter count", 1),
            ("Selected program", 2), ("Running program", 2), ("Operating mode", 1), ("Button/encoder mode", 1),
            ("System init choice", 1), ("Active setup", 1), ("Setup choice", 1), ("(unused)", 1), ("Bypass state", 1),
            ("Bypass patch choice", 1), ("Tools parameter", 1), ("Tools tool", 1), ("Global patch", 1), ("Global patch choice", 1),
            ("MIDI dump type", 1), ("Remap choice", 1), ("Patch parameter", 1), ("(unused)", 1), ("Auto store choice", 1),
            ("Tempo choice", 1), ("Tempo source error choice", 1), ("MIDI map choice", 1), ("Invalid data choice", 1),
            ("Verify choice", 1), ("Verify store choice", 1), ("(unused)", 1), ("(unused)", 1),
            ("Menu pointer", 2), ("Mix return pointer", 2), ("Patch return pointer", 2), ("Auto store return pointer", 2),
            ("Verify return pointer", 2), ("Store return pointer", 2),
        }.Select(f => Internal(f.Name, f.Size, null)),
    ]);

    /// <summary>Gets the Patches layout: the five patches of the active program, 12 bytes each (DMP-009).</summary>
    public static DumpLayout Patches { get; } = Build("Patches",
    [
        .. Enumerable.Range(1, ProgramData.PatchCount).SelectMany(i => new[]
        {
            Controller($"Patch {i} source", ControllerSources.PatchSources),
            Number($"Patch {i} source min", 1, 0, 127),
            Number($"Patch {i} source mid", 1, 0, 255, help: "0xFF = not used."),
            Number($"Patch {i} source max", 1, 0, 127),
            Number($"Patch {i} destination type", 1, 0, 255, help: "0-16 effect or controller, 17 System, 0xFF unassigned."),
            Number($"Patch {i} destination index", 1, 0, 255),
            Number($"Patch {i} destination min", 2, 0, ushort.MaxValue),
            Number($"Patch {i} destination mid", 2, 0, ushort.MaxValue),
            Number($"Patch {i} destination max", 2, 0, ushort.MaxValue),
        }),
    ]);

    /// <summary>Gets the Soft Row layout: ten type/index pairs of the active program (DMP-010).</summary>
    public static DumpLayout SoftRow { get; } = Build("Soft Row",
    [
        .. Enumerable.Range(1, ProgramData.SoftValueCount).SelectMany(i => new[]
        {
            Number($"Soft {i} type", 1, 0, 255, help: "0-16 effect or controller, 0xFF unassigned."),
            Number($"Soft {i} index", 1, 0, 255),
        }),
    ]);

    // Fields are declared without offsets; Build assigns them in order.
    private static DumpLayout Build(string name, IEnumerable<DumpField> fields)
    {
        var placed = ImmutableArray.CreateBuilder<DumpField>();
        int offset = 0;
        foreach (DumpField field in fields)
        {
            placed.Add(field with { Offset = offset });
            offset += field.Size;
        }
        return new DumpLayout(name, offset, placed.ToImmutable());
    }

    private static DumpField Number(string name, int size, int min, int max, string? help = null)
        => new(name, 0, size, DumpFieldKind.Number) { Minimum = min, Maximum = max, Help = help };

    private static DumpField Signed(string name, int size, int min, int max)
        => new(name, 0, size, DumpFieldKind.SignedNumber) { Minimum = min, Maximum = max };

    private static DumpField Toggle(string name) => new(name, 0, 1, DumpFieldKind.Toggle) { Choices = OffOn };

    private static DumpField Choice(string name, int size, ImmutableArray<DumpChoice> choices)
        => new(name, 0, size, DumpFieldKind.Choice) { Choices = choices };

    private static DumpField Controller(string name, ImmutableArray<byte> sources)
        => new(name, 0, 1, DumpFieldKind.Controller) { Sources = sources };

    private static DumpField Text(string name, int size) => new(name, 0, size, DumpFieldKind.Text);

    private static DumpField Internal(string name, int size, string? help)
        => new(name, 0, size, DumpFieldKind.Number) { Maximum = size == 1 ? 255 : ushort.MaxValue, IsReadOnly = true, Help = help };

    private static ImmutableArray<DumpChoice> Labels(params string[] labels) => [.. labels.Select((l, i) => new DumpChoice(i, l))];
}
