using BB84.MPX1.Midi;

namespace BB84.MPX1.App.Services;

/// <summary>Turns a failed device operation into the status line text.</summary>
public static class OperationStatus
{
    /// <summary>Status shown when the device stops answering.</summary>
    public const string DeviceDidNotAnswer = "The device did not answer.";

    /// <summary>Describes a failure for the status line.</summary>
    /// <param name="exception">The failure.</param>
    /// <returns>A fixed text for a timeout, the exception message otherwise.</returns>
    public static string Describe(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception is Mpx1TimeoutException ? DeviceDidNotAnswer : exception.Message;
    }
}
