namespace BB84.MPX1.Midi.WinMM;

/// <summary>A WinMM MIDI function returned an error code.</summary>
public sealed class WinMMException : IOException
{
    /// <summary>Initializes a new instance of the <see cref="WinMMException"/> class.</summary>
    public WinMMException()
    { }

    /// <summary>Initializes a new instance of the <see cref="WinMMException"/> class.</summary>
    /// <param name="message">The error message.</param>
    public WinMMException(string message)
        : base(message)
    { }

    /// <summary>Initializes a new instance of the <see cref="WinMMException"/> class.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception.</param>
    public WinMMException(string message, Exception innerException)
        : base(message, innerException)
    { }

    /// <summary>Initializes a new instance of the <see cref="WinMMException"/> class.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="result">The MMRESULT code.</param>
    public WinMMException(string message, uint result)
        : base(message)
        => Result = result;

    /// <summary>Gets the MMRESULT error code.</summary>
    public uint Result { get; }
}
