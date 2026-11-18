using BB84.SourceGenerators.Attributes;

using BB84.MPX1.Core.Dumps;

namespace BB84.MPX1.App.ViewModels;

/// <summary>A single front-panel LED.</summary>
/// <param name="led">The LED.</param>
[GenerateNotifications(propertyChanging: false, partialMethods: true, fields: false)]
public sealed partial class LedViewModel(PanelLed led)
{
    /// <summary>Gets the LED.</summary>
    public PanelLed Led { get; } = led;

    /// <summary>Gets the label.</summary>
    public string Name { get; } = led switch
    {
        PanelLed.DigitalIn => "Digital in",
        PanelLed.MidiActivity => "MIDI in",
        _ => led.ToStringFast(),
    };

    /// <summary>Gets or sets a value indicating whether the LED is lit.</summary>
    public partial bool IsOn { get; set; }
}
