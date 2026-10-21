using System.Collections.Immutable;
using System.Globalization;

namespace BB84.MPX1.Core.Programs;

/// <summary>
/// Appendix A controller indices (DMP-011): internal controllers, foot controls, MIDI controllers and MIDI-derived
/// sources, plus the source ranges each use allows. <c>0xFF</c> means "unassigned" everywhere.
/// </summary>
public static class ControllerSources
{
    /// <summary>Value of an unassigned source.</summary>
    public const byte Unassigned = 0xFF;

    /// <summary>Last controller index (TSw).</summary>
    public const byte Last = 0xA2;

    /// <summary>Foot pedal, the first Global Patch source.</summary>
    public const byte Pedal = 0x18;

    /// <summary>Latched foot switch 1, the first Bypass source.</summary>
    public const byte Toggle1 = 0x19;

    /// <summary>Momentary foot switch 1, used as the first Tap source (spec 9, E8).</summary>
    public const byte Switch1 = 0x1C;

    /// <summary>MIDI controller 0 (bank select).</summary>
    public const byte Cc0 = 0x1F;

    /// <summary>MIDI controller 119, the last Bypass source.</summary>
    public const byte Cc119 = 0x96;

    private const byte Cc32 = 0x3F;

    private static readonly string[] Internal =
    [
        "Off", "On", "Knob", "Puls1", "Tri1", "Sine1", "Cos1", "Puls2", "Tri2", "Sine2", "Cos2", "Rand", "Arp", "ADR1", "ADR2", "S/H",
        "Env1", "Env2", "Mtr1", "Mtr2", "A/B", "ATrg", "BTrg", "ABTrg", "Pedal", "Tog1", "Tog2", "Tog3", "Sw1", "Sw2", "Sw3",
    ];

    private static readonly string[] Midi = ["Bend", "Touch", "Vel", "Last•", "Low•", "High•", "Tempo", "Cmnds", "Gate", "Trig", "LGate", "TSw"];

    /// <summary>Gets every selectable source for program patches (all controllers except the bank selects).</summary>
    public static ImmutableArray<byte> PatchSources { get; } = Range(0x00, Last);

    /// <summary>Gets the Global Patch sources, Pedal through TSw (DMP-005).</summary>
    public static ImmutableArray<byte> GlobalPatchSources { get; } = Range(Pedal, Last);

    /// <summary>Gets the Bypass Controller sources, Tog1 through CC119 (DMP-006).</summary>
    public static ImmutableArray<byte> BypassSources { get; } = Range(Toggle1, Cc119);

    /// <summary>Gets the Tap sources (program Tap Source field).</summary>
    public static ImmutableArray<byte> TapSources { get; } = Range(Switch1, Last);

    /// <summary>Returns the display name of a source, for example <c>Sine1</c>, <c>CC7</c> or <c>None</c>.</summary>
    /// <param name="index">The controller index.</param>
    public static string Name(byte index) => index switch
    {
        Unassigned => "None",
        < Cc0 => Internal[index],
        <= Cc119 => $"CC{(index - Cc0).ToString(CultureInfo.InvariantCulture)}",
        <= Last => Midi[index - Cc119 - 1],
        _ => $"0x{index:X2}",
    };

    /// <summary>Returns <see langword="true"/> for a source the unit lets you select (not the bank selects).</summary>
    /// <param name="index">The controller index.</param>
    public static bool IsSelectable(byte index) => index <= Last && index is not Cc0 and not Cc32;

    /// <summary>Returns <see langword="true"/> when a value is unassigned or inside an allowed range.</summary>
    /// <param name="value">The stored value.</param>
    /// <param name="allowed">One of the source ranges.</param>
    public static bool IsValid(byte value, ImmutableArray<byte> allowed) => value == Unassigned || allowed.Contains(value);

    private static ImmutableArray<byte> Range(byte first, byte last)
        => [.. Enumerable.Range(first, last - first + 1).Select(i => (byte)i).Where(IsSelectable)];
}
