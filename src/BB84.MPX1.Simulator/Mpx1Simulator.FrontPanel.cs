using BB84.MPX1.Core.Dumps;
using BB84.MPX1.Core.Protocol;

namespace BB84.MPX1.Simulator;

// A small model of the user interface, enough for the front panel mirror (UI-007): the display reports the
// last button event, Bypass toggles its LED, the encoder counts the right-hand digits.
public sealed partial class Mpx1Simulator
{
    private const string DisplayTitle = "  MPX 1  V1.10";

    private static readonly ControlAddress DisplayAddress = new(1, 8, 1);
    private static readonly ControlAddress LedsAddress = new(1, 8, 2);

    /// <summary>Gets the last Panel Button code received (PAR-007), or <see langword="null"/>.</summary>
    public byte? LastButtonCode { get; private set; }

    private void PressButton(byte code)
    {
        LastButtonCode = code;
        _values[DisplayAddress] = DisplayText.Encode(DisplayTitle, DescribeButton(code));

        if (_values.TryGetValue(LedsAddress, out byte[]? leds) && leds.Length == LedState.Size)
        {
            LedState state = LedState.FromBytes(leds);
            if (code == PanelButtons.Find("Bypass").Release)
                state = state.With(PanelLed.Bypass, !state.IsOn(PanelLed.Bypass));
            _values[LedsAddress] = [.. state.Data];
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
