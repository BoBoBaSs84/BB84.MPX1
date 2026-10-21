using System.Buffers;
using System.Text;

namespace BB84.MPX1.Core.Protocol;

/// <summary>
/// Writes values in MPX 1 nibble-ized form: each byte becomes two bytes, low nibble first (PRO-001, PRO-002).
/// </summary>
internal sealed class NibbleWriter
{
    private readonly ArrayBufferWriter<byte> _buffer;

    public NibbleWriter(int capacity = 256)
        => _buffer = new ArrayBufferWriter<byte>(capacity);

    public int Length => _buffer.WrittenCount;

    /// <summary>Writes raw bytes that are not nibble-ized (header, EOX).</summary>
    public void WriteRaw(ReadOnlySpan<byte> bytes)
        => _buffer.Write(bytes);

    /// <summary>Writes a single raw nibble (only used to reproduce non-conforming frames).</summary>
    public void WriteRawNibble(byte nibble)
        => _buffer.Write([(byte)(nibble & 0x0F)]);

    public void WriteByte(byte value)
    {
        Span<byte> span = _buffer.GetSpan(2);
        span[0] = (byte)(value & 0x0F);
        span[1] = (byte)(value >> 4);
        _buffer.Advance(2);
    }

    public void WriteWord(ushort value)
    {
        WriteByte((byte)value);
        WriteByte((byte)(value >> 8));
    }

    public void WriteBytes(ReadOnlySpan<byte> values)
    {
        foreach (byte value in values)
            WriteByte(value);
    }

    /// <summary>Writes exactly <paramref name="length"/> characters, space padded or truncated.</summary>
    public void WriteFixedString(string value, int length)
    {
        Span<byte> bytes = stackalloc byte[length];
        bytes.Fill((byte)' ');
        int count = Math.Min(value.Length, length);
        Encoding.Latin1.GetBytes(value.AsSpan(0, count), bytes);
        WriteBytes(bytes);
    }

    /// <summary>Writes the string as-is (length is written separately by the caller).</summary>
    public void WriteString(string value)
        => WriteBytes(Encoding.Latin1.GetBytes(value));

    public void WriteControlAddress(ControlAddress address)
    {
        WriteWord(checked((ushort)address.Depth));
        foreach (ushort level in address.Levels)
            WriteWord(level);
    }

    public byte[] ToArray() => _buffer.WrittenSpan.ToArray();
}
