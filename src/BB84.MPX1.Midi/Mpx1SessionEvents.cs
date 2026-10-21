using BB84.MPX1.Core.Protocol;

namespace BB84.MPX1.Midi;

/// <summary>Direction of a frame on the wire.</summary>
public enum FrameDirection
{
    /// <summary>Sent to the device.</summary>
    Sent,
    /// <summary>Received from the device.</summary>
    Received,
}

/// <summary>A SysEx frame seen by the session, for monitors (UI-009).</summary>
/// <param name="direction">Sent or received.</param>
/// <param name="timestamp">Time of the event.</param>
/// <param name="frame">The raw frame.</param>
/// <param name="message">The decoded message, or <see langword="null"/> when decoding failed.</param>
/// <param name="error">The decode error, or <see langword="null"/>.</param>
public sealed class FrameEventArgs(
    FrameDirection direction,
    DateTimeOffset timestamp,
    ReadOnlyMemory<byte> frame,
    Mpx1Message? message,
    Mpx1FormatError? error) : EventArgs
{
    /// <summary>Gets the direction.</summary>
    public FrameDirection Direction { get; } = direction;
    /// <summary>Gets the time of the event.</summary>
    public DateTimeOffset Timestamp { get; } = timestamp;
    /// <summary>Gets the raw frame.</summary>
    public ReadOnlyMemory<byte> Frame { get; } = frame;
    /// <summary>Gets the decoded message, or <see langword="null"/> when decoding failed.</summary>
    public Mpx1Message? Message { get; } = message;
    /// <summary>Gets the decode error, or <see langword="null"/>.</summary>
    public Mpx1FormatError? Error { get; } = error;
}

/// <summary>A decoded message that did not answer a pending request (SES-009).</summary>
/// <param name="message">The message.</param>
public sealed class MessageEventArgs(Mpx1Message message) : EventArgs
{
    /// <summary>Gets the message.</summary>
    public Mpx1Message Message { get; } = message;
}

/// <summary>A short MIDI message received from the input port.</summary>
/// <param name="timestamp">Time of receipt.</param>
/// <param name="message">The packed message.</param>
public sealed class ShortMessageEventArgs(DateTimeOffset timestamp, int message) : EventArgs
{
    /// <summary>Gets the time of receipt.</summary>
    public DateTimeOffset Timestamp { get; } = timestamp;
    /// <summary>Gets the packed message.</summary>
    public int Message { get; } = message;
}

/// <summary>Firmware information detected by <see cref="Mpx1Session.DetectVersionAsync"/> (SES-007).</summary>
/// <param name="MajorVersion">Major software version.</param>
/// <param name="MinorVersion">Minor software version.</param>
/// <param name="Identity">The Device Inquiry reply, if the device answered it.</param>
/// <param name="Configuration">The System Configuration message.</param>
public sealed record DeviceVersion(byte MajorVersion, byte MinorVersion, IdentityReply? Identity, SystemConfiguration Configuration)
{
    /// <inheritdoc/>
    public override string ToString() => $"V{MajorVersion}.{MinorVersion:D2}";
}
