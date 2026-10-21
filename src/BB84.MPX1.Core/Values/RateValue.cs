using System.Globalization;

namespace BB84.MPX1.Core.Values;

/// <summary>
/// A Rate parameter (types <c>0x0000</c>–<c>0x0010</c>, PAR-004): MSB set = tempo ratio (high byte numerator,
/// low byte denominator, each 1–24); MSB clear = frequency in hundredths of a hertz (max 5000 = 50 Hz).
/// </summary>
public readonly record struct RateValue
{
    /// <summary>Highest frequency in hundredths of a hertz.</summary>
    public const int MaxCentihertz = 5000;

    /// <summary>Largest numerator or denominator.</summary>
    public const int MaxRatioTerm = 24;

    private RateValue(bool isRatio, int centihertz, int numerator, int denominator)
    {
        IsRatio = isRatio;
        Centihertz = centihertz;
        Numerator = numerator;
        Denominator = denominator;
    }

    /// <summary>Gets a value indicating whether the rate is a tempo ratio.</summary>
    public bool IsRatio { get; }

    /// <summary>Gets the frequency in hundredths of a hertz (0 for ratios).</summary>
    public int Centihertz { get; }

    /// <summary>Gets the ratio numerator (0 for frequencies).</summary>
    public int Numerator { get; }

    /// <summary>Gets the ratio denominator (0 for frequencies).</summary>
    public int Denominator { get; }

    /// <summary>Creates a frequency rate.</summary>
    /// <param name="centihertz">Hundredths of a hertz, 0–5000.</param>
    public static RateValue FromCentihertz(int centihertz)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(centihertz);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(centihertz, MaxCentihertz);
        return new(false, centihertz, 0, 0);
    }

    /// <summary>Creates a tempo-ratio rate.</summary>
    /// <param name="numerator">Numerator 1–24.</param>
    /// <param name="denominator">Denominator 1–24.</param>
    public static RateValue FromRatio(int numerator, int denominator)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(numerator, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(numerator, MaxRatioTerm);
        ArgumentOutOfRangeException.ThrowIfLessThan(denominator, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(denominator, MaxRatioTerm);
        return new(true, 0, numerator, denominator);
    }

    /// <summary>Decodes the 16-bit stored value.</summary>
    /// <param name="raw">The stored word.</param>
    public static RateValue FromRaw(ushort raw)
        => (raw & 0x8000) != 0
            ? new(true, 0, (raw >> 8) & 0x7F, raw & 0xFF)
            : new(false, raw, 0, 0);

    /// <summary>Encodes the 16-bit stored value.</summary>
    public ushort ToRaw()
        => IsRatio ? (ushort)(0x8000 | (Numerator << 8) | Denominator) : (ushort)Centihertz;

    /// <summary>Formats as <c>3:4</c> or <c>1.00 Hz</c>.</summary>
    public override string ToString()
        => IsRatio
            ? string.Create(CultureInfo.InvariantCulture, $"{Numerator}:{Denominator}")
            : string.Create(CultureInfo.InvariantCulture, $"{Centihertz / 100}.{Centihertz % 100:D2} Hz");
}

/// <summary>MIDI Speed parameter rules (type <c>0x00F2</c>, PAR-006).</summary>
public static class MidiSpeed
{
    /// <summary>Fast.</summary>
    public const byte Fast = 0x01;
    /// <summary>Medium fast.</summary>
    public const byte MediumFast = 0x02;
    /// <summary>Medium slow.</summary>
    public const byte MediumSlow = 0x04;
    /// <summary>Slow.</summary>
    public const byte Slow = 0x40;

    /// <summary>Gets the only values the device accepts.</summary>
    public static IReadOnlyList<byte> AllowedValues { get; } = [Fast, MediumFast, MediumSlow, Slow];

    /// <summary>Returns <see langword="true"/> for an allowed value.</summary>
    /// <param name="value">The value.</param>
    public static bool IsValid(byte value) => value is Fast or MediumFast or MediumSlow or Slow;

    /// <summary>Returns the display name of an allowed value, or <see langword="null"/>.</summary>
    /// <param name="value">The value.</param>
    public static string? Name(byte value) => value switch
    {
        Fast => "Fast",
        MediumFast => "Medium Fast",
        MediumSlow => "Medium Slow",
        Slow => "Slow",
        _ => null,
    };
}
