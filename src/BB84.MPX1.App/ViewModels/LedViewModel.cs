using CommunityToolkit.Mvvm.ComponentModel;

using BB84.MPX1.Core.Dumps;

namespace BB84.MPX1.App.ViewModels;

/// <summary>A single front-panel LED.</summary>
/// <param name="led">The LED.</param>
public sealed partial class LedViewModel(PanelLed led) : ObservableObject
{
    /// <summary>Gets the LED.</summary>
    public PanelLed Led { get; } = led;

    /// <summary>Gets the label.</summary>
    public string Name { get; } = led switch
    {
        PanelLed.DigitalIn => "Digital in",
        PanelLed.MidiActivity => "MIDI in",
        _ => led.ToString(),
    };

    /// <summary>Gets or sets a value indicating whether the LED is lit.</summary>
    [ObservableProperty]
    public partial bool IsOn { get; set; }
}
