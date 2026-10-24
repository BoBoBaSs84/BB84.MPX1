using System.Collections.Concurrent;
using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using BB84.MPX1.App.Services;
using BB84.MPX1.Midi;

namespace BB84.MPX1.App.ViewModels;

/// <summary>
/// MIDI monitor panel (UI-009). Frames arrive on background threads; they are queued and moved to the UI
/// in batches so that a learning run (thousands of frames) does not flood the dispatcher.
/// </summary>
public sealed partial class MonitorViewModel : ObservableObject, IDisposable
{
    /// <summary>Most entries kept.</summary>
    public const int Capacity = 2000;

    private static readonly TimeSpan FlushInterval = TimeSpan.FromMilliseconds(100);

    private readonly ConcurrentQueue<MonitorEntry> _queue = new();
    private readonly IUiDispatcher _dispatcher;
    private readonly ITimer _timer;
    private int _flushQueued;

    /// <summary>Initializes a new instance of the <see cref="MonitorViewModel"/> class.</summary>
    /// <param name="dispatcher">UI dispatcher.</param>
    /// <param name="time">Time source for the flush timer.</param>
    public MonitorViewModel(IUiDispatcher dispatcher, TimeProvider time)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        ArgumentNullException.ThrowIfNull(time);
        _timer = time.CreateTimer(_ => QueueFlush(), null, FlushInterval, FlushInterval);
    }

    /// <summary>Gets the entries, oldest first.</summary>
    public ObservableCollection<MonitorEntry> Entries { get; } = [];

    /// <summary>Gets or sets a value indicating whether new frames are ignored.</summary>
    [ObservableProperty]
    public partial bool IsPaused { get; set; }

    /// <summary>Records a frame. Safe to call from any thread.</summary>
    /// <param name="sender">Ignored.</param>
    /// <param name="frame">The traffic event.</param>
    public void Record(object? sender, FrameEventArgs frame)
    {
        if (!IsPaused)
            _queue.Enqueue(MonitorEntry.From(frame));
    }

    /// <summary>Moves queued frames into <see cref="Entries"/>. Must run on the UI thread.</summary>
    public void Flush()
    {
        Interlocked.Exchange(ref _flushQueued, 0);
        List<MonitorEntry> batch = [];
        while (_queue.TryDequeue(out MonitorEntry? entry))
            batch.Add(entry);

        // Entries that would be evicted at once are never added, so a burst raises no needless change events.
        int skip = Math.Max(0, batch.Count - Capacity);
        int evict = Math.Min(Entries.Count, Entries.Count + batch.Count - skip - Capacity);
        for (int i = 0; i < evict; i++)
            Entries.RemoveAt(0);
        for (int i = skip; i < batch.Count; i++)
            Entries.Add(batch[i]);
    }

    /// <summary>Removes all entries.</summary>
    [RelayCommand]
    public void Clear()
    {
        _queue.Clear();
        Entries.Clear();
    }

    /// <inheritdoc/>
    public void Dispose() => _timer.Dispose();

    private void QueueFlush()
    {
        if (!_queue.IsEmpty && Interlocked.Exchange(ref _flushQueued, 1) == 0)
            _dispatcher.Post(Flush);
    }
}
