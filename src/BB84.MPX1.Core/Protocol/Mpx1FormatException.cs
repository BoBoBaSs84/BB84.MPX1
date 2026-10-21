namespace BB84.MPX1.Core.Protocol;

/// <summary>
/// Describes why a SysEx frame could not be decoded.
/// </summary>
/// <param name="Offset">Zero-based byte offset in the frame where the problem was detected.</param>
/// <param name="Reason">Human-readable reason.</param>
public sealed record Mpx1FormatError(int Offset, string Reason)
{
    /// <inheritdoc/>
    public override string ToString() => $"{Reason} (offset {Offset})";
}

/// <summary>
/// Thrown when a SysEx frame is malformed (PRO-003, PRO-005, SEC-001).
/// </summary>
public sealed class Mpx1FormatException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="Mpx1FormatException"/> class.</summary>
    public Mpx1FormatException()
        : this(new Mpx1FormatError(0, "Malformed frame"))
    { }

    /// <summary>Initializes a new instance of the <see cref="Mpx1FormatException"/> class.</summary>
    /// <param name="message">The error message.</param>
    public Mpx1FormatException(string message)
        : this(new Mpx1FormatError(0, message))
    { }

    /// <summary>Initializes a new instance of the <see cref="Mpx1FormatException"/> class.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception.</param>
    public Mpx1FormatException(string message, Exception innerException)
        : base(message, innerException)
        => Error = new Mpx1FormatError(0, message);

    /// <summary>Initializes a new instance of the <see cref="Mpx1FormatException"/> class.</summary>
    /// <param name="error">The decode error.</param>
    public Mpx1FormatException(Mpx1FormatError error)
        : base(error?.ToString())
        => Error = error ?? throw new ArgumentNullException(nameof(error));

    /// <summary>Gets the decode error.</summary>
    public Mpx1FormatError Error { get; }
}
