using BB84.MPX1.Core.Dumps;
using BB84.MPX1.Core.Protocol;

namespace BB84.MPX1.Simulator;

// A small model of the user interface, enough for the front panel mirror (UI-007): the display reports the
// last button event, Bypass toggles its LED, the encoder counts the right-hand digits.
public sealed partial class Mpx1Simulator
{
    private string DisplayTitle => $"  MPX 1  V{_fixture.MajorVersion}.{_fixture.MinorVersion:D2}";

    /// <summary>Gets the last Panel Button code received (PAR-007), or <see langword="null"/>.</summary>
    public byte? LastButtonCode { get; private set; }

    private void PressButton(byte code)
    {
        LastButtonCode = code;
        if (_displayAddress is { } display)
            _values[display] = DisplayText.Encode(DisplayTitle, DescribeButton(code));

        if (_ledsAddress is { } ledsAddress && _values.TryGetValue(ledsAddress, out byte[]? leds) && leds.Length == LedState.Size)
        {
            LedState state = LedState.FromBytes(leds);
            if (code == PanelButtons.Find("Bypass").Release)
                state = state.With(PanelLed.Bypass, !state.IsOn(PanelLed.Bypass));
            _values[ledsAddress] = [.. state.Data];
        }
    }

    private static string DescribeButton(byte code) => code switch
    {
        PanelButtons.EncoderClockwise => "Encoder +",
        PanelButtons.EncoderCounterClockwise => "Encoder -",
        PanelButtons.TapV110 => "Tap",
        PanelButtons.ToggleABV110 => "A/B",
        _ => PanelButtons.All.FirstOrDefault(b => b.Press == code) is { } pressed ? $"{pressed.Name} press"
            : PanelButtons.All.FirstOrDefault(b => b.Hold == code) is { } held ? $"{held.Name} hold"
            : PanelButtons.All.FirstOrDefault(b => b.Release == code) is { } released ? $"{released.Name}"
            : $"Code 0x{code:X2}",
    };
}
