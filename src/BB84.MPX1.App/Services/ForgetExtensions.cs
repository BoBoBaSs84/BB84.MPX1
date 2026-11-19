namespace BB84.MPX1.App.Services;

/// <summary>Helpers for tasks that are started but not awaited.</summary>
public static class ForgetExtensions
{
    /// <summary>
    /// Lets a task run without awaiting it, but hands any failure to <paramref name="onError"/> instead of leaving it
    /// unobserved. The handler runs on the caller's synchronization context, so it may update view-model state.
    /// </summary>
    /// <param name="task">The task.</param>
    /// <param name="onError">Receives the exception when the task faults or is cancelled.</param>
    public static void Forget(this Task task, Action<Exception> onError)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(onError);
        _ = ObserveAsync(task, onError);
    }

    private static async Task ObserveAsync(Task task, Action<Exception> onError)
    {
        try
        {
            await task;
        }
        catch (Exception ex)
        {
            onError(ex);
        }
    }
}
