namespace BB84.MPX1.Core.Protocol;

/// <summary>
/// Public helpers for MPX 1 nibble-ization (PRO-001, PRO-002).
/// </summary>
public static class Nibbles
{
    /// <summary>Nibble-izes bytes: each byte becomes low nibble, then high nibble.</summary>
    /// <param name="bytes">The bytes to encode.</param>
    public static byte[] Encode(ReadOnlySpan<byte> bytes)
    {
        byte[] result = new byte[bytes.Length * 2];
        for (int i = 0; i < bytes.Length; i++)
        {
            result[i * 2] = (byte)(bytes[i] & 0x0F);
            result[(i * 2) + 1] = (byte)(bytes[i] >> 4);
        }
        return result;
    }

    /// <summary>Nibble-izes a 16-bit word as 4 bytes, least-significant nibble first.</summary>
    /// <param name="value">The word.</param>
    public static byte[] EncodeWord(ushort value)
        => Encode([(byte)value, (byte)(value >> 8)]);

    /// <summary>Joins nibble pairs back into bytes.</summary>
    /// <param name="nibbles">An even number of nibble bytes, each 0x00–0x0F.</param>
    /// <exception cref="Mpx1FormatException">The input has odd length or a value above 0x0F.</exception>
    public static byte[] Decode(ReadOnlySpan<byte> nibbles)
    {
        if (nibbles.Length % 2 != 0)
            throw new Mpx1FormatException(new Mpx1FormatError(nibbles.Length - 1, "Odd number of nibbles"));

        byte[] result = new byte[nibbles.Length / 2];
        for (int i = 0; i < nibbles.Length; i++)
        {
            if (nibbles[i] > 0x0F)
                throw new Mpx1FormatException(new Mpx1FormatError(i, $"Invalid nibble 0x{nibbles[i]:X2}"));
        }
        for (int i = 0; i < result.Length; i++)
            result[i] = (byte)(nibbles[i * 2] | (nibbles[(i * 2) + 1] << 4));
        return result;
    }
}
