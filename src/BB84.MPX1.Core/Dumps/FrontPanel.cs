using System.Collections.Immutable;
using System.Text;

namespace BB84.MPX1.Core.Dumps;

/// <summary>Front-panel LEDs that are single lamps (not headroom bars or 7-segment digits).</summary>
public enum PanelLed
{
    /// <summary>Pitch.</summary>
    Pitch,
    /// <summary>Chorus.</summary>
    Chorus,
    /// <summary>EQ.</summary>
    EQ,
    /// <summary>Program.</summary>
    Program,
    /// <summary>Modulation.</summary>
    Mod,
    /// <summary>Delay.</summary>
    Delay,
    /// <summary>Reverb.</summary>
    Reverb,
    /// <summary>Edit.</summary>
    Edit,
    /// <summary>Mix.</summary>
    Mix,
    /// <summary>Patch.</summary>
    Patch,
    /// <summary>Bypass.</summary>
    Bypass,
    /// <summary>System.</summary>
    System,
    /// <summary>Tempo.</summary>
    Tempo,
    /// <summary>A.</summary>
    A,
    /// <summary>Value.</summary>
    Value,
    /// <summary>Store.</summary>
    Store,
    /// <summary>MIDI.</summary>
    Midi,
    /// <summary>B.</summary>
    B,
    /// <summary>Options.</summary>
    Options,
    /// <summary>Digital In (decimal point of the left digit).</summary>
    DigitalIn,
    /// <summary>Clock (decimal point of the middle digit).</summary>
    Clock,
    /// <summary>MIDI activity (decimal point of the right digit).</summary>
    MidiActivity,
}

/// <summary>
/// The decoded All LEDs dump (DMP-001): headroom bars, single LEDs and the three 7-segment digits. Column bytes are
/// active low; their three scan bits must be kept as documented when the dump is sent back.
/// </summary>
public sealed record LedState
{
    /// <summary>Size of the dump in bytes.</summary>
    public const int Size = 10;

    // (byte index, bit) of each single LED; columns are bytes 1, 3, 5, 7 and 9 (0-based).
    private static readonly ImmutableDictionary<PanelLed, (int Byte, int Bit, bool ActiveLow)> Map = new Dictionary<PanelLed, (int, int, bool)>
    {
        [PanelLed.Pitch] = (1, 3, true), [PanelLed.Chorus] = (1, 4, true), [PanelLed.EQ] = (1, 5, true), [PanelLed.Program] = (1, 6, true),
        [PanelLed.Mod] = (3, 3, true), [PanelLed.Delay] = (3, 4, true), [PanelLed.Reverb] = (3, 5, true), [PanelLed.Edit] = (3, 6, true),
        [PanelLed.Mix] = (5, 3, true), [PanelLed.Patch] = (5, 4, true), [PanelLed.Bypass] = (5, 5, true), [PanelLed.System] = (5, 6, true),
        [PanelLed.Tempo] = (7, 3, true), [PanelLed.A] = (7, 4, true), [PanelLed.Value] = (7, 5, true), [PanelLed.Store] = (7, 6, true),
        [PanelLed.Midi] = (9, 3, true), [PanelLed.B] = (9, 4, true), [PanelLed.Options] = (9, 5, true),
        [PanelLed.DigitalIn] = (4, 7, false), [PanelLed.Clock] = (6, 7, false), [PanelLed.MidiActivity] = (8, 7, false),
    }.ToImmutableDictionary();

    // Segment images of the digits and "P" (dump bit 0 = segment a ... bit 6 = segment g).
    private static readonly ImmutableDictionary<byte, char> Digits = new Dictionary<byte, char>
    {
        [0x3F] = '0', [0x30] = '1', [0x5B] = '2', [0x79] = '3', [0x74] = '4', [0x6D] = '5', [0x6F] = '6', [0x38] = '7',
        [0x7F] = '8', [0x7C] = '9', [0x5E] = 'P', [0x00] = ' ',
    }.ToImmutableDictionary();

    /// <summary>Scan bits each column byte must carry, in column order 1–5.</summary>
    public static ImmutableArray<byte> ScanBits { get; } = [0b000, 0b001, 0b010, 0b011, 0b100];

    private LedState(byte[] data) => Data = [.. data];

    /// <summary>Gets the raw dump bytes.</summary>
    public ImmutableArray<byte> Data { get; }

    /// <summary>Gets the lit LEDs of the left headroom bar (0–6; 6 = clip).</summary>
    public int LeftHeadroom => Bars(Data[0]);

    /// <summary>Gets the lit LEDs of the right headroom bar (0–6; 6 = clip).</summary>
    public int RightHeadroom => Bars(Data[2]);

    /// <summary>Gets the three 7-segment digits as text; unknown images show as <c>?</c>.</summary>
    public string Digits3 => new([Digit(Data[4]), Digit(Data[6]), Digit(Data[8])]);

    /// <summary>Parses the dump.</summary>
    /// <param name="data">Exactly <see cref="Size"/> bytes.</param>
    public static LedState FromBytes(ReadOnlySpan<byte> data)
    {
        if (data.Length != Size)
            throw new ArgumentException($"The LED dump is {Size} bytes.", nameof(data));
        return new LedState(data.ToArray());
    }

    /// <summary>Returns a state with every LED off and valid scan bits.</summary>
    public static LedState AllOff()
    {
        byte[] data = new byte[Size];
        for (int column = 0; column < ScanBits.Length; column++)
            data[1 + (column * 2)] = (byte)(ScanBits[column] | 0x78);
        return new LedState(data);
    }

    /// <summary>Returns whether an LED is lit.</summary>
    /// <param name="led">The LED.</param>
    public bool IsOn(PanelLed led)
    {
        (int index, int bit, bool activeLow) = Map[led];
        bool set = (Data[index] & (1 << bit)) != 0;
        return activeLow ? !set : set;
    }

    /// <summary>Returns a copy with one LED switched; the scan bits are kept.</summary>
    /// <param name="led">The LED.</param>
    /// <param name="on">Whether it is lit.</param>
    public LedState With(PanelLed led, bool on)
    {
        (int index, int bit, bool activeLow) = Map[led];
        byte[] data = [.. Data];
        bool set = activeLow ? !on : on;
        data[index] = (byte)(set ? data[index] | (1 << bit) : data[index] & ~(1 << bit));
        return new LedState(data);
    }

    /// <summary>Returns <see langword="true"/> when every column carries its documented scan bits.</summary>
    public bool HasValidScanBits()
        => Enumerable.Range(0, ScanBits.Length).All(c => (Data[1 + (c * 2)] & 0b111) == ScanBits[c]);

    private static int Bars(byte value)
    {
        int bars = 0;
        while (bars < 6 && (value & (1 << bars)) != 0)
            bars++;
        return bars;
    }

    private static char Digit(byte value) => Digits.TryGetValue((byte)(value & 0x7F), out char c) ? c : '?';
}

/// <summary>The 2×16 LCD contents (Display Dump, DMP-002). Bytes 0–7 select custom characters (PRO-007).</summary>
public static class DisplayText
{
    /// <summary>Characters in the dump.</summary>
    public const int Size = 32;

    /// <summary>Characters per line.</summary>
    public const int LineLength = 16;

    /// <summary>Splits the dump into two lines; custom characters are kept as their codes 0–7.</summary>
    /// <param name="data">The dump bytes.</param>
    public static (string Top, string Bottom) Lines(ReadOnlySpan<byte> data)
    {
        string text = Encoding.Latin1.GetString(data[..Math.Min(data.Length, Size)]).PadRight(Size);
        return (text[..LineLength], text[LineLength..Size]);
    }

    /// <summary>Returns <see langword="true"/> when the byte selects a custom character.</summary>
    /// <param name="value">The byte.</param>
    public static bool IsCustom(byte value) => value < CustomCharacters.Count;

    /// <summary>Encodes two lines as a dump (space padded; codes 0–7 select custom characters).</summary>
    /// <param name="top">The top line.</param>
    /// <param name="bottom">The bottom line.</param>
    public static byte[] Encode(string top, string bottom)
    {
        ArgumentNullException.ThrowIfNull(top);
        ArgumentNullException.ThrowIfNull(bottom);
        string text = top.PadRight(LineLength)[..LineLength] + bottom.PadRight(LineLength)[..LineLength];
        return [.. text.Select(c => c is < (char)CustomCharacters.Count or (>= ' ' and <= '~') ? (byte)c : (byte)' ')];
    }
}

/// <summary>The eight 5×8 custom LCD characters (Custom Character Bitmap Dump, DMP-003).</summary>
public static class CustomCharacters
{
    /// <summary>Number of custom characters.</summary>
    public const int Count = 8;

    /// <summary>Rows per character (7 pixel rows plus the underline row).</summary>
    public const int Rows = 8;

    /// <summary>Pixels per row.</summary>
    public const int Columns = 5;

    /// <summary>Size of the dump in bytes.</summary>
    public const int Size = Count * Rows;

    /// <summary>Returns whether a pixel is on; column 0 is the leftmost pixel (bit 4).</summary>
    /// <param name="data">The dump bytes.</param>
    /// <param name="character">Character 0–7.</param>
    /// <param name="row">Row 0–7 (7 = underline).</param>
    /// <param name="column">Column 0–4.</param>
    public static bool IsSet(ReadOnlySpan<byte> data, int character, int row, int column)
        => (data[(character * Rows) + row] & (1 << (Columns - 1 - column))) != 0;
}

/// <summary>A front-panel button and its Panel Button codes (PAR-007).</summary>
/// <param name="Name">Display name.</param>
/// <param name="Press">PRESS code.</param>
/// <param name="Hold">HOLD code.</param>
/// <param name="Release">RELEASE code.</param>
public sealed record PanelButton(string Name, byte Press, byte Hold, byte Release);

/// <summary>
/// Panel Button codes from the document (PAR-007). A click is PRESS then RELEASE; hold is HOLD then RELEASE;
/// combinations have their own codes and are never sent as two PRESS messages.
/// </summary>
public static class PanelButtons
{
    /// <summary>Encoder one step clockwise.</summary>
    public const byte EncoderClockwise = 0x42;

    /// <summary>Encoder one step counter-clockwise.</summary>
    public const byte EncoderCounterClockwise = 0x43;

    /// <summary>Single-message Tap (V1.10 and later).</summary>
    public const byte TapV110 = 0x44;

    /// <summary>Single-message A/B toggle (V1.10 and later).</summary>
    public const byte ToggleABV110 = 0x45;

    /// <summary>Gets the buttons in code order, including the foot switch inputs.</summary>
    public static ImmutableArray<PanelButton> All { get; } =
    [
        .. new[]
        {
            "Pitch", "Chorus", "EQ", "Program", "Mod", "Delay", "Reverb", "Edit", "Mix", "Patch", "Bypass", "System", "Tap",
            "Left", "Value", "Store", "A/B", "Right", "Options", "Foot switch tip", "Foot switch ring", "Left + Right",
        }.Select((name, i) => new PanelButton(name, (byte)i, (byte)(0x16 + i), (byte)(0x2C + i))),
    ];

    /// <summary>Gets the Left + Right combination (dedicated codes).</summary>
    public static PanelButton LeftAndRight => All[^1];

    /// <summary>Finds a button by name.</summary>
    /// <param name="name">The name.</param>
    public static PanelButton Find(string name) => All.First(b => string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase));
}
