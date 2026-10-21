using System.Buffers.Binary;

using BB84.MPX1.Core.Protocol;

namespace BB84.MPX1.Core.Values;

/// <summary>
/// A decoded parameter value: the numeric value, the option value (if any) and the unit/limit triple that applies.
/// </summary>
/// <param name="Value">The value with any unit flag (MSB) removed.</param>
/// <param name="RawValue">The value as stored, including a unit flag in the MSB.</param>
/// <param name="Option">The option value, or <see langword="null"/>.</param>
/// <param name="Unit">The unit/limit triple selected by the option (PAR-008).</param>
public readonly record struct ParameterValue(int Value, int RawValue, int? Option, UnitLimit Unit);

/// <summary>
/// Splits, decodes and encodes Parameter Data bytes using parameter descriptions (PAR-001, PAR-002, PAR-005, PAR-008).
/// </summary>
public static class ParameterValueCodec
{
    /// <summary>Display-unit bit that marks signed ("bipolar") units (Appendix B, <c>0x80</c>–<c>0x89</c>).</summary>
    public const ushort SignedUnitsFlag = 0x80;

    /// <summary>Returns <see langword="true"/> when values of this unit are signed (PRO-008).</summary>
    /// <param name="unit">The unit/limit triple.</param>
    public static bool IsSigned(UnitLimit unit) => (unit.DisplayUnits & SignedUnitsFlag) != 0 || unit.Minimum < 0;

    /// <summary>
    /// Decodes Parameter Data bytes. The data holds the parameter followed by option bytes (PAR-002); an option of
    /// size 0 (Rate units) lives in the MSB of the parameter itself (PAR-005).
    /// </summary>
    /// <param name="description">The parameter description.</param>
    /// <param name="option">The option description, or <see langword="null"/> when the parameter has none.</param>
    /// <param name="data">Parameter Data bytes.</param>
    /// <exception cref="ArgumentException">The data is shorter than the description requires.</exception>
    public static ParameterValue Decode(ParameterDescription description, ParameterDescription? option, ReadOnlySpan<byte> data)
    {
        ArgumentNullException.ThrowIfNull(description);
        int size = description.Size;
        if (size is < 1 or > 2)
            throw new ArgumentException($"Only 1- and 2-byte parameters have numeric values; {description.Name.Trim()} has {size}.", nameof(description));
        if (data.Length < size)
            throw new ArgumentException($"Expected at least {size} bytes, got {data.Length}.", nameof(data));

        int raw = size == 1 ? data[0] : BinaryPrimitives.ReadUInt16LittleEndian(data);
        int? optionValue = null;
        int value = raw;
        int msb = size == 1 ? 0x80 : 0x8000;

        if (option is not null)
        {
            if (option.Size == 0)
            {
                optionValue = (raw & msb) != 0 ? 1 : 0;
                value = raw & (msb - 1);
            }
            else if (data.Length >= size + option.Size)
            {
                optionValue = option.Size == 1 ? data[size] : BinaryPrimitives.ReadUInt16LittleEndian(data[size..]);
            }
        }

        UnitLimit unit = SelectUnit(description, optionValue);
        if (option is not { Size: 0 } && IsSigned(unit))
            value = size == 1 ? (sbyte)raw : (short)raw;

        return new ParameterValue(value, raw, optionValue, unit);
    }

    /// <summary>Selects the unit/limit triple for an option value (PAR-008).</summary>
    /// <param name="description">The parameter description.</param>
    /// <param name="optionValue">The option value, or <see langword="null"/>.</param>
    public static UnitLimit SelectUnit(ParameterDescription description, int? optionValue)
    {
        ArgumentNullException.ThrowIfNull(description);
        if (description.Units.IsDefaultOrEmpty)
            return new UnitLimit(0, 0, 0x01);
        int index = optionValue is int o && o >= 0 && o < description.Units.Length ? o : 0;
        return description.Units[index];
    }

    /// <summary>Encodes a value (and option) as Parameter Data bytes.</summary>
    /// <param name="description">The parameter description.</param>
    /// <param name="option">The option description, or <see langword="null"/>.</param>
    /// <param name="value">The value without unit flag.</param>
    /// <param name="optionValue">The option value, or <see langword="null"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException">The value is outside the selected unit's limits.</exception>
    public static byte[] Encode(ParameterDescription description, ParameterDescription? option, int value, int? optionValue = null)
    {
        ArgumentNullException.ThrowIfNull(description);
        int size = description.Size;
        if (size is < 1 or > 2)
            throw new ArgumentException($"Only 1- and 2-byte parameters have numeric values; {description.Name.Trim()} has {size}.", nameof(description));

        UnitLimit unit = SelectUnit(description, optionValue);
        ValidateRange(unit, size, value);

        int msb = size == 1 ? 0x80 : 0x8000;
        int raw = value & (size == 1 ? 0xFF : 0xFFFF);
        int optionSize = option?.Size ?? 0;
        if (option is { Size: 0 } && optionValue is int flag)
            raw = (raw & (msb - 1)) | (flag != 0 ? msb : 0);

        byte[] data = new byte[size + optionSize];
        if (size == 1)
            data[0] = (byte)raw;
        else
            BinaryPrimitives.WriteUInt16LittleEndian(data, (ushort)raw);

        if (optionSize > 0)
        {
            int o = optionValue ?? 0;
            if (optionSize == 1)
                data[size] = (byte)o;
            else
                BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(size), (ushort)o);
        }
        return data;
    }

    /// <summary>
    /// Returns the effective limits of a unit for a parameter of the given size. Min/max are signed words; for
    /// unsigned parameters they are read as unsigned bytes or words (PRO-008).
    /// </summary>
    /// <param name="unit">The unit/limit triple.</param>
    /// <param name="size">The parameter size in bytes (1 or 2).</param>
    public static (int Minimum, int Maximum) GetRange(UnitLimit unit, int size)
    {
        if (IsSigned(unit))
            return (unit.Minimum, unit.Maximum);
        return size == 1
            ? ((byte)unit.Minimum, (byte)unit.Maximum)
            : ((ushort)unit.Minimum, (ushort)unit.Maximum);
    }

    private static void ValidateRange(UnitLimit unit, int size, int value)
    {
        (int min, int max) = GetRange(unit, size);
        if (min <= max && (value < min || value > max))
            throw new ArgumentOutOfRangeException(nameof(value), value, $"Value must be between {min} and {max}.");
    }
}
