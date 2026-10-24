using BB84.MPX1.Midi;
using BB84.MPX1.Midi.Library;

namespace BB84.MPX1.App.Services;

/// <summary>Turns a failed device operation into the status line text.</summary>
public static class OperationStatus
{
    /// <summary>Status shown when the device stops answering.</summary>
    public const string DeviceDidNotAnswer = "The device did not answer.";

    /// <summary>Status shown when the user cancels a transfer.</summary>
    public const string Cancelled = "Cancelled.";

    /// <summary>Describes a failure for the status line.</summary>
    /// <param name="exception">The failure.</param>
    /// <returns>A fixed text for a timeout, the exception message otherwise.</returns>
    public static string Describe(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception is Mpx1TimeoutException ? DeviceDidNotAnswer : exception.Message;
    }

    /// <summary>Gets a value indicating whether a failure is an expected device or file outcome that belongs on the status line.</summary>
    /// <param name="exception">The failure.</param>
    public static bool IsExpected(Exception exception)
        => exception is Mpx1TimeoutException or PresetOverwriteException or IOException or ArgumentException or UnauthorizedAccessException;

    /// <summary>Runs a device or file transfer and returns the text for the status line.</summary>
    /// <param name="work">The transfer; returns its status text.</param>
    /// <param name="cancellationToken">Cancels the transfer at the user's request.</param>
    /// <returns>
    /// The transfer's own text, <see cref="Cancelled"/>, a described expected failure, or <see langword="null"/> when the
    /// connection closed while the transfer was in flight (the caller keeps its current status).
    /// </returns>
    public static async Task<string?> RunAsync(Func<CancellationToken, Task<string>> work, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(work);
        try
        {
            return await work(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Cancelled;
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            return Describe(ex);
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
        {
            // The session was disposed while the call was in flight: it cancels pending requests and refuses new ones.
            return null;
        }
    }
}
