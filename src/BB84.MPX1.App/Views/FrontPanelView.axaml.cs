using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

using BB84.MPX1.App.ViewModels;

namespace BB84.MPX1.App.Views;

/// <summary>
/// The Front panel tab. A right-click or a press-and-hold gesture on a button sends HOLD + RELEASE (PAR-007).
/// </summary>
public sealed partial class FrontPanelView : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FrontPanelView"/> class.
    /// </summary>
    public FrontPanelView()
    {
        InitializeComponent();
        ButtonList.AddHandler(PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        ButtonList.AddHandler(InputElement.HoldingEvent, OnHolding, RoutingStrategies.Bubble);
    }

    private static PanelButtonViewModel? ButtonOf(object? source)
        => (source as Control)?.DataContext as PanelButtonViewModel;

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton == MouseButton.Right && ButtonOf(e.Source) is { } button && button.HoldCommand.CanExecute(null))
        {
            button.HoldCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnHolding(object? sender, HoldingRoutedEventArgs e)
    {
        if (e.HoldingState == HoldingState.Started && ButtonOf(e.Source) is { } button && button.HoldCommand.CanExecute(null))
        {
            button.HoldCommand.Execute(null);
            e.Handled = true;
        }
    }
}
