namespace BB84.MPX1.Midi;

/// <summary>
/// Thrown when the device does not answer a request after all retries (SES-003),
/// or does not send READY after BUSY in time (SES-005).
/// </summary>
public sealed class Mpx1TimeoutException : TimeoutException
{
    /// <summary>Initializes a new instance of the <see cref="Mpx1TimeoutException"/> class.</summary>
    public Mpx1TimeoutException()
    { }

    /// <summary>Initializes a new instance of the <see cref="Mpx1TimeoutException"/> class.</summary>
    /// <param name="message">The error message.</param>
    public Mpx1TimeoutException(string message)
        : base(message)
    { }

    /// <summary>Initializes a new instance of the <see cref="Mpx1TimeoutException"/> class.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception.</param>
    public Mpx1TimeoutException(string message, Exception innerException)
        : base(message, innerException)
    { }

    /// <summary>Initializes a new instance of the <see cref="Mpx1TimeoutException"/> class.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="frame">The frame that was sent.</param>
    /// <param name="attempts">How many times it was sent.</param>
    public Mpx1TimeoutException(string message, ReadOnlyMemory<byte> frame, int attempts)
        : base(message)
    {
        Frame = frame;
        Attempts = attempts;
    }

    /// <summary>Gets the frame that was sent.</summary>
    public ReadOnlyMemory<byte> Frame { get; }

    /// <summary>Gets how many times the frame was sent.</summary>
    public int Attempts { get; }
}
