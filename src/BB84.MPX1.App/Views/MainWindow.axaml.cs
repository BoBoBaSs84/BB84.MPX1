using System.Collections.Specialized;

using Avalonia.Controls;

using BB84.MPX1.App.ViewModels;

namespace BB84.MPX1.App.Views;

/// <summary>
/// The main application window.
/// </summary>
public sealed partial class MainWindow : Window
{
    private readonly MainWindowViewModel? _viewModel;
    private bool _closing;

    /// <summary>
    /// Initializes a new instance of the <see cref="MainWindow"/> class. Used by the visual designer.
    /// </summary>
    public MainWindow()
        => InitializeComponent();

    /// <summary>
    /// Initializes a new instance of the <see cref="MainWindow"/> class.
    /// </summary>
    /// <param name="viewModel">The view model.</param>
    public MainWindow(MainWindowViewModel viewModel)
        : this()
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = viewModel;
        Opened += async (_, _) => await viewModel.InitializeAsync();
        viewModel.Monitor.Entries.CollectionChanged += OnMonitorChanged;
    }

    private void OnMonitorChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Follow the newest frame, like a terminal, unless the monitor is paused.
        if (e.Action == NotifyCollectionChangedAction.Add && _viewModel?.Monitor is { IsPaused: false, Entries: { Count: > 0 } entries })
            MonitorList.ScrollIntoView(entries[^1]);
    }

    /// <inheritdoc/>
    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (_viewModel is null || _closing || e.Cancel)
            return;

        // Close the MIDI ports before the process exits, then close for real.
        e.Cancel = true;
        _closing = true;
        try
        {
            await _viewModel.DisposeAsync();
        }
        finally
        {
            // A failed cleanup must not leave a window that can no longer be closed.
            Close();
        }
    }
}
