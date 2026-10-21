using Avalonia.Threading;

namespace BB84.MPX1.App.Services;

/// <summary>Marshals work to the UI thread. View models use it for events raised on background threads.</summary>
public interface IUiDispatcher
{
    /// <summary>Queues an action on the UI thread.</summary>
    /// <param name="action">The action.</param>
    void Post(Action action);
}

/// <summary>Dispatcher backed by Avalonia's UI thread.</summary>
public sealed class AvaloniaUiDispatcher : IUiDispatcher
{
    /// <inheritdoc/>
    public void Post(Action action) => Dispatcher.UIThread.Post(action);
}
