using System.Globalization;
using System.Text;

using BB84.MPX1.Core.Protocol;

namespace BB84.MPX1.Core.Values;

/// <summary>
/// Formats parameter values offline from their display-units type (Appendix B, PAR-003). Units that need string
/// tables the document does not publish fall back to the plain number; online, the device's Parameter Display
/// message (<c>02</c>) is authoritative.
/// </summary>
public static class DisplayUnitFormatter
{
    /// <summary>Returns <see langword="true"/> when the unit can be formatted without device string tables.</summary>
    /// <param name="displayUnits">The display-units type.</param>
    public static bool IsSupported(ushort displayUnits) => displayUnits switch
    {
        0x00 or 0x01 or 0x03 or 0x07 or 0x08 or 0x09 or 0x0A or 0x0E or 0x0F or 0x13 or 0x17 or 0x18
            or 0x25 or 0x26 or 0x28 or 0x29 or 0x2A or 0x2B or 0x30 or 0x35 or 0x3A or 0x3B or 0x3C or 0x3D or 0x3E
            or 0x40 or 0x43 or 0x44 or 0x45 or 0x46 or 0x48 or 0x49 or 0x4B
            or >= 0x80 and <= 0x89 => true,
        _ => false,
    };

    /// <summary>Formats a decoded value.</summary>
    /// <param name="description">The parameter description (name and limits).</param>
    /// <param name="value">The decoded value.</param>
    public static string Format(ParameterDescription description, ParameterValue value)
    {
        ArgumentNullException.ThrowIfNull(description);
        return Format(value.Unit, value.Value, value.RawValue, value.Option, description.Name.Trim());
    }

    /// <summary>Formats a value.</summary>
    /// <param name="unit">The unit/limit triple.</param>
    /// <param name="value">The value without unit flag (signed where the unit is signed).</param>
    /// <param name="rawValue">The value as stored, including any MSB flag.</param>
    /// <param name="option">The option value, or <see langword="null"/>.</param>
    /// <param name="name">The parameter name (for control levels).</param>
    public static string Format(UnitLimit unit, int value, int rawValue, int? option, string name)
    {
        CultureInfo c = CultureInfo.InvariantCulture;
        return unit.DisplayUnits switch
        {
            0x00 or 0x03 => $"{value.ToString(c)}%",
            0x07 => value == 0 ? "None" : value.ToString(c),
            0x08 or 0x0F => $"{((rawValue >> 8) & 0x7F).ToString(c)}:{(rawValue & 0xFF).ToString(c)}",
            0x09 => $"{value.ToString(c)}Hz",
            0x0A => $"{(value / 10).ToString(c)}.{Math.Abs(value % 10).ToString(c)} Q",
            0x0E => $"{value.ToString(c)}°",
            0x13 => (rawValue & 0x8000) != 0 || option == 1 ? "cycles:beat" : "Hz",
            0x17 => RateValue.FromRaw((ushort)rawValue).ToString(),
            0x18 => value switch
            {
                >= 0 and <= 15 => (value + 1).ToString(c),
                16 => "Off",
                17 => "Omni",
                _ => value.ToString(c),
            },
            0x26 => $"{value.ToString(c)} BPM",
            0x28 or 0x29 => FormatControllerSource(unit, value),
            0x2A => $"{value.ToString(c)} Beats",
            0x2B or 0x49 => (option ?? value).ToString(c),
            0x35 => $"{((value / 2) + 4).ToString(c)}.{((value & 1) == 1 ? "5" : "0")} M",
            0x3A => $"{(value * 2).ToString(c)}%",
            0x3B or 0x48 => $"{value.ToString(c)} ms",
            0x3E => value < 16 ? $"-{(16 - value).ToString(c)}" : $"+{(value - 16).ToString(c)}",
            0x40 => $"{(140 + (value * 5)).ToString(c)} ms",
            0x44 => "Dump",
            0x45 => $"{value.ToString(c)} Feet",
            0x46 => $"{value.ToString(c)} Meters",
            0x4B => name,
            0x80 => value == unit.Minimum ? "Off" : $"{value.ToString(c)}dB",
            0x81 => $"{(value < 0 ? "-" : "")}{(Math.Abs(value) / 100).ToString(c)}.{(Math.Abs(value) % 100).ToString("D2", c)}",
            0x82 => $"{value.ToString(c)}%",
            0x84 => value == 0 ? "C" : value < 0 ? $"{(-value).ToString(c)}L" : $"{value.ToString(c)}R",
            0x86 => (value & 0xFF) == 0xFF ? "None" : $"CC{value.ToString(c)}",
            0x87 => $"{value.ToString(c)}°",
            0x89 => $"{value.ToString(c)}dB",
            _ => value.ToString(c),
        };
    }

    /// <summary>Formats ASCII data (display units <c>0x25</c> and <c>0x30</c>).</summary>
    /// <param name="data">The parameter bytes.</param>
    public static string FormatText(ReadOnlySpan<byte> data) => Encoding.Latin1.GetString(data);

    private static string FormatControllerSource(UnitLimit unit, int value)
    {
        CultureInfo c = CultureInfo.InvariantCulture;
        if (value is 0 or 0xFFFF || (unit.DisplayUnits == 0x29 && value == unit.Minimum))
            return "None";
        if (value is >= 31 and <= 150)
            return $"CC{(value - 31).ToString(c)}";
        return value.ToString(c);
    }
}
