namespace BB84.MPX1.Core.Tests;

internal static class Hex
{
    public static byte[] Parse(string hex)
        => Convert.FromHexString(hex.Replace(" ", "", StringComparison.Ordinal));

    public static string Format(ReadOnlySpan<byte> bytes)
        => string.Join(' ', bytes.ToArray().Select(b => b.ToString("X2", System.Globalization.CultureInfo.InvariantCulture)));
}
