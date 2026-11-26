namespace BB84.MPX1.Midi.Alsa;

/// <summary>An ALSA sequencer function returned an error, or the sequencer is not available.</summary>
public sealed class AlsaException : IOException
{
    /// <summary>Initializes a new instance of the <see cref="AlsaException"/> class.</summary>
    public AlsaException()
    { }

    /// <summary>Initializes a new instance of the <see cref="AlsaException"/> class.</summary>
    /// <param name="message">The error message.</param>
    public AlsaException(string message)
        : base(message)
    { }

    /// <summary>Initializes a new instance of the <see cref="AlsaException"/> class.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception.</param>
    public AlsaException(string message, Exception innerException)
        : base(message, innerException)
    { }

    /// <summary>Initializes a new instance of the <see cref="AlsaException"/> class.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="error">The negative errno code ALSA returned.</param>
    public AlsaException(string message, int error)
        : base(message)
        => Error = error;

    /// <summary>Gets the negative errno code ALSA returned, or 0.</summary>
    public int Error { get; }
}
