using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Text;

namespace BB84.MPX1.Core.Protocol;

/// <summary>
/// Reads nibble-ized values from the payload of an MPX 1 frame. Every read is bounded by the
/// remaining payload, so untrusted length fields can never cause over-reads (SEC-001).
/// </summary>
internal ref struct NibbleReader
{
    private readonly ReadOnlySpan<byte> _data;
    private readonly int _frameOffset;
    private int _position;

    /// <param name="data">The nibble-ized payload (between header and EOX).</param>
    /// <param name="frameOffset">Frame offset of <paramref name="data"/>[0], used in error reports.</param>
    public NibbleReader(ReadOnlySpan<byte> data, int frameOffset)
    {
        _data = data;
        _frameOffset = frameOffset;
        _position = 0;
    }

    /// <summary>Remaining raw nibble bytes.</summary>
    public readonly int RemainingNibbles => _data.Length - _position;

    /// <summary>Current position as a frame offset.</summary>
    public readonly int FrameOffset => _frameOffset + _position;

    public byte ReadNibble()
    {
        Require(1, "nibble");
        return _data[_position++];
    }

    public byte ReadByte()
    {
        Require(2, "byte");
        byte value = (byte)(_data[_position] | (_data[_position + 1] << 4));
        _position += 2;
        return value;
    }

    public ushort ReadWord()
    {
        Require(4, "word");
        return (ushort)(ReadByte() | (ReadByte() << 8));
    }

    public byte[] ReadBytes(int count)
    {
        Require(checked(count * 2), $"{count} data bytes");
        byte[] result = new byte[count];
        for (int i = 0; i < count; i++)
            result[i] = ReadByte();
        return result;
    }

    /// <summary>Reads <paramref name="count"/> ASCII characters, preserving custom characters 0x00–0x07 (PRO-007).</summary>
    public string ReadString(int count)
        => Encoding.Latin1.GetString(ReadBytes(count));

    public ImmutableArray<ushort> ReadWords(int count)
    {
        Require(checked(count * 4), $"{count} words");
        ushort[] result = new ushort[count];
        for (int i = 0; i < count; i++)
            result[i] = ReadWord();
        return ImmutableCollectionsMarshal.AsImmutableArray(result);
    }

    public ControlAddress ReadControlAddress()
    {
        int depthOffset = FrameOffset;
        ushort depth = ReadWord();
        if (depth * 4 > RemainingNibbles)
            throw new Mpx1FormatException(new Mpx1FormatError(depthOffset, $"Control address claims {depth} levels but the frame is too short"));
        return new ControlAddress(ReadWords(depth));
    }

    /// <summary>Throws when unread payload remains (PRO-005).</summary>
    public readonly void EnsureEnd()
    {
        if (RemainingNibbles != 0)
            throw new Mpx1FormatException(new Mpx1FormatError(FrameOffset, $"{RemainingNibbles} unexpected trailing nibble(s)"));
    }

    private readonly void Require(int nibbles, string what)
    {
        if (RemainingNibbles < nibbles)
            throw new Mpx1FormatException(new Mpx1FormatError(FrameOffset, $"Frame too short: expected {what}"));
    }
}
