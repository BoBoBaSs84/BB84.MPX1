namespace BB84.MPX1.App.Services;

/// <summary>Reports progress on the UI thread, in order.</summary>
/// <typeparam name="T">The progress value type.</typeparam>
/// <param name="dispatcher">UI dispatcher.</param>
/// <param name="report">Receives each value on the UI thread.</param>
public sealed class DispatchedProgress<T>(IUiDispatcher dispatcher, Action<T> report) : IProgress<T>
{
    /// <inheritdoc/>
    public void Report(T value) => dispatcher.Post(() => report(value));
}
