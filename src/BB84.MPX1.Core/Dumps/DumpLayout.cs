using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Globalization;
using System.Text;

using BB84.MPX1.Core.Programs;

namespace BB84.MPX1.Core.Dumps;

/// <summary>How a dump field is edited.</summary>
public enum DumpFieldKind
{
    /// <summary>Unsigned number within <see cref="DumpField.Minimum"/>–<see cref="DumpField.Maximum"/>.</summary>
    Number,
    /// <summary>Signed number (two's complement of the field size).</summary>
    SignedNumber,
    /// <summary>One of <see cref="DumpField.Choices"/>.</summary>
    Choice,
    /// <summary>Off (0) or on (1).</summary>
    Toggle,
    /// <summary>An Appendix A controller source from <see cref="DumpField.Sources"/>, or unassigned (<c>0xFF</c>).</summary>
    Controller,
    /// <summary>Space-padded ASCII text (PRO-007).</summary>
    Text,
}

/// <summary>A named value of a choice field.</summary>
/// <param name="Value">The stored value.</param>
/// <param name="Label">The label.</param>
public sealed record DumpChoice(int Value, string Label);

/// <summary>One field of a documented dump layout (DMP-001 to DMP-010).</summary>
/// <param name="Name">Display name.</param>
/// <param name="Offset">Byte offset in the dump data.</param>
/// <param name="Size">Size in bytes.</param>
/// <param name="Kind">How the field is edited.</param>
public sealed record DumpField(string Name, int Offset, int Size, DumpFieldKind Kind)
{
    /// <summary>Gets the minimum (numbers).</summary>
    public int Minimum { get; init; }

    /// <summary>Gets the maximum (numbers).</summary>
    public int Maximum { get; init; } = 255;

    /// <summary>Gets the choices (choice fields).</summary>
    public ImmutableArray<DumpChoice> Choices { get; init; } = [];

    /// <summary>Gets the allowed controller sources (controller fields, DMP-011).</summary>
    public ImmutableArray<byte> Sources { get; init; } = [];

    /// <summary>Gets a value indicating whether the field is internal and shown read-only (pointers, calibration, unused).</summary>
    public bool IsReadOnly { get; init; }

    /// <summary>Gets a short explanation for tooltips.</summary>
    public string? Help { get; init; }

    /// <summary>Reads the stored value (numbers, choices, toggles, controllers).</summary>
    /// <param name="data">The dump data.</param>
    public int Read(ReadOnlySpan<byte> data)
    {
        ReadOnlySpan<byte> bytes = data.Slice(Offset, Size);
        return (Size, Kind) switch
        {
            (1, DumpFieldKind.SignedNumber) => (sbyte)bytes[0],
            (2, DumpFieldKind.SignedNumber) => BinaryPrimitives.ReadInt16LittleEndian(bytes),
            (1, _) => bytes[0],
            (2, _) => BinaryPrimitives.ReadUInt16LittleEndian(bytes),
            _ => throw new InvalidOperationException($"{Name} is not a numeric field."),
        };
    }

    /// <summary>Reads a text field.</summary>
    /// <param name="data">The dump data.</param>
    public string ReadText(ReadOnlySpan<byte> data) => Encoding.Latin1.GetString(data.Slice(Offset, Size));

    /// <summary>Returns <see langword="true"/> when a value is legal for this field.</summary>
    /// <param name="value">The value.</param>
    public bool IsValid(int value) => Kind switch
    {
        DumpFieldKind.Choice => Choices.Any(c => c.Value == value),
        DumpFieldKind.Toggle => value is 0 or 1,
        DumpFieldKind.Controller => value is >= 0 and <= 255 && ControllerSources.IsValid((byte)value, Sources),
        DumpFieldKind.Text => false,
        _ => value >= Minimum && value <= Maximum,
    };

    /// <summary>Writes a value into a copy of the dump data.</summary>
    /// <param name="data">The dump data.</param>
    /// <param name="value">The value.</param>
    /// <exception cref="ArgumentOutOfRangeException">The value is not legal for the field (DMP-011).</exception>
    public byte[] Write(ReadOnlySpan<byte> data, int value)
    {
        if (!IsValid(value))
            throw new ArgumentOutOfRangeException(nameof(value), value, $"{value.ToString(CultureInfo.InvariantCulture)} is not a legal value for {Name}.");
        byte[] copy = data.ToArray();
        Span<byte> bytes = copy.AsSpan(Offset, Size);
        if (Size == 1)
            bytes[0] = unchecked((byte)value);
        else
            BinaryPrimitives.WriteUInt16LittleEndian(bytes, unchecked((ushort)value));
        return copy;
    }

    /// <summary>Writes a text field, space padded, printable ASCII only (PRO-007).</summary>
    /// <param name="data">The dump data.</param>
    /// <param name="text">The text.</param>
    public byte[] WriteText(ReadOnlySpan<byte> data, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (Kind != DumpFieldKind.Text)
            throw new InvalidOperationException($"{Name} is not a text field.");
        byte[] copy = data.ToArray();
        Span<byte> bytes = copy.AsSpan(Offset, Size);
        bytes.Fill((byte)' ');
        for (int i = 0; i < Math.Min(text.Length, Size); i++)
            bytes[i] = text[i] is >= ' ' and <= '~' ? (byte)text[i] : (byte)' ';
        return copy;
    }

    /// <summary>Formats the stored value for display.</summary>
    /// <param name="data">The dump data.</param>
    public string Format(ReadOnlySpan<byte> data)
    {
        if (Kind == DumpFieldKind.Text)
            return ReadText(data).TrimEnd();
        int value = Read(data);
        return Kind switch
        {
            DumpFieldKind.Choice => Choices.FirstOrDefault(c => c.Value == value)?.Label ?? value.ToString(CultureInfo.InvariantCulture),
            DumpFieldKind.Toggle => value == 0 ? "Off" : "On",
            DumpFieldKind.Controller => ControllerSources.Name((byte)value),
            _ => value.ToString(CultureInfo.InvariantCulture),
        };
    }
}

/// <summary>A documented dump: its size and fields (DMP-001 to DMP-010).</summary>
/// <param name="Name">Display name.</param>
/// <param name="Size">Size in bytes.</param>
/// <param name="Fields">The fields in data order.</param>
public sealed record DumpLayout(string Name, int Size, ImmutableArray<DumpField> Fields)
{
    /// <summary>Returns <see langword="true"/> when every editable field holds a legal value.</summary>
    /// <param name="data">The dump data.</param>
    public bool IsValid(ReadOnlySpan<byte> data)
    {
        if (data.Length != Size)
            return false;
        foreach (DumpField field in Fields)
        {
            if (!field.IsReadOnly && field.Kind != DumpFieldKind.Text && !field.IsValid(field.Read(data)))
                return false;
        }
        return true;
    }

    /// <summary>Returns the fields whose stored value is not legal (DMP-011 validation).</summary>
    /// <param name="data">The dump data.</param>
    public IEnumerable<DumpField> InvalidFields(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return Fields.Where(f => !f.IsReadOnly && f.Kind != DumpFieldKind.Text && !f.IsValid(f.Read(data)));
    }
}
