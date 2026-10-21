using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using BB84.MPX1.App.Services;
using BB84.MPX1.Midi;

namespace BB84.MPX1.App.ViewModels;

/// <summary>One line of the MIDI monitor.</summary>
/// <param name="Time">Local time of the frame.</param>
/// <param name="Direction">Arrow: sent or received.</param>
/// <param name="Hex">The frame as hex, shortened for long frames.</param>
/// <param name="Summary">Decoded summary.</param>
/// <param name="IsError">Whether the frame failed to decode.</param>
public sealed record MonitorEntry(string Time, string Direction, string Hex, string Summary, bool IsError)
{
    /// <summary>Longest frame shown in full.</summary>
    public const int MaxHexBytes = 40;

    /// <summary>Creates an entry from session traffic.</summary>
    /// <param name="frame">The traffic event.</param>
    public static MonitorEntry From(FrameEventArgs frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ReadOnlySpan<byte> bytes = frame.Frame.Span;
        StringBuilder hex = new(MaxHexBytes * 3);
        int shown = Math.Min(bytes.Length, MaxHexBytes);
        for (int i = 0; i < shown; i++)
        {
            if (i > 0)
                hex.Append(' ');
            hex.Append(bytes[i].ToString("X2", CultureInfo.InvariantCulture));
        }
        if (bytes.Length > shown)
            hex.Append(CultureInfo.InvariantCulture, $" … ({bytes.Length} bytes)");

        return new MonitorEntry(
            frame.Timestamp.ToLocalTime().ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture),
            frame.Direction == FrameDirection.Sent ? "→" : "←",
            hex.ToString(),
            MessageSummary.Describe(frame.Message, frame.Error),
            frame.Message is null);
    }
}

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
        while (_queue.TryDequeue(out MonitorEntry? entry))
            Entries.Add(entry);
        while (Entries.Count > Capacity)
            Entries.RemoveAt(0);
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
