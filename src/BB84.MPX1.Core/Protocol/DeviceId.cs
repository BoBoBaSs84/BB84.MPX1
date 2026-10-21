namespace BB84.MPX1.Core.Protocol;

/// <summary>
/// A 7-bit MPX 1 device ID. <see cref="All"/> (127) addresses every device.
/// </summary>
public readonly record struct DeviceId
{
    /// <summary>The broadcast device ID (127).</summary>
    public static readonly DeviceId All = new(0x7F);

    /// <summary>Initializes a new instance of the <see cref="DeviceId"/> struct.</summary>
    /// <param name="value">Device ID in the range 0–127.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is above 127.</exception>
    public DeviceId(byte value)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value, (byte)0x7F);
        Value = value;
    }

    /// <summary>Gets the raw device ID.</summary>
    public byte Value { get; }

    /// <inheritdoc/>
    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
