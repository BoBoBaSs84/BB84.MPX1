using CommunityToolkit.Mvvm.Input;

using BB84.MPX1.Core.Dumps;

namespace BB84.MPX1.App.ViewModels;

/// <summary>A front-panel button: click = PRESS + RELEASE, hold = HOLD + RELEASE (PAR-007).</summary>
public sealed class PanelButtonViewModel
{
    /// <summary>Initializes a new instance of the <see cref="PanelButtonViewModel"/> class.</summary>
    /// <param name="button">The button.</param>
    /// <param name="owner">The front panel.</param>
    public PanelButtonViewModel(PanelButton button, FrontPanelViewModel owner)
    {
        Button = button ?? throw new ArgumentNullException(nameof(button));
        ArgumentNullException.ThrowIfNull(owner);
        ClickCommand = new AsyncRelayCommand(() => owner.ClickAsync(button), () => owner.IsAttached);
        HoldCommand = new AsyncRelayCommand(() => owner.HoldAsync(button), () => owner.IsAttached);
    }

    /// <summary>Gets the button.</summary>
    public PanelButton Button { get; }

    /// <summary>Gets the label.</summary>
    public string Name => Button.Name;

    /// <summary>Gets the click command.</summary>
    public IAsyncRelayCommand ClickCommand { get; }

    /// <summary>Gets the hold command.</summary>
    public IAsyncRelayCommand HoldCommand { get; }
}
