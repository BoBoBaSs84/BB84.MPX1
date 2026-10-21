using System.Collections.Immutable;
using System.Text;

using BB84.MPX1.Core.Dumps;
using BB84.MPX1.Core.Programs;
using BB84.MPX1.Core.Values;
using BB84.MPX1.Core.Protocol;

namespace BB84.MPX1.Simulator.Fixtures;

/// <summary>
/// Builds the default V1.10 fixture: a partial control tree assembled from the examples in the MIDI
/// Implementation document, plus the three Appendix D bugs (SIM-002, SIM-005).
/// </summary>
public static class DefaultFixture
{
    /// <summary>Well-known parameter types used by the default fixture (V1.10 numbering).</summary>
    public static class Types
    {
        /// <summary>Mix.</summary>
        public const ushort Mix = 0x002D;
        /// <summary>Level.</summary>
        public const ushort Level = 0x002E;
        /// <summary>Tune (has option <see cref="TuneOption"/>).</summary>
        public const ushort Tune = 0x003B;
        /// <summary>Tune option.</summary>
        public const ushort TuneOption = 0x003C;
        /// <summary>Pre-delay.</summary>
        public const ushort PreDelay = 0x003D;
        /// <summary>EQ gain.</summary>
        public const ushort Gain = 0x0060;
        /// <summary>MIDI Speed (A:1 B:2 C:10).</summary>
        public const ushort MidiSpeed = 0x00F2;
        /// <summary>Panel Button (V1.10).</summary>
        public const ushort PanelButton = 0x011E;
        /// <summary>Display Dump (V1.10).</summary>
        public const ushort DisplayDump = 0x011F;
        /// <summary>Dump that reports size 0 but holds 542 bytes (Appendix D bug 1).</summary>
        public const ushort BrokenSizeDump = 0x014A;
        /// <summary>Pitch effect level.</summary>
        public const ushort Pitch = 0x014D;
        /// <summary>Chorus effect level.</summary>
        public const ushort Chorus = 0x014E;
        /// <summary>EQ effect level.</summary>
        public const ushort EQ = 0x014F;
        /// <summary>"no effect" algorithm.</summary>
        public const ushort NoEffect = 0x0150;
        /// <summary>Detune (M) algorithm.</summary>
        public const ushort DetuneMono = 0x0151;
        /// <summary>1-Band (M) EQ algorithm.</summary>
        public const ushort OneBandMono = 0x0152;
        /// <summary>Program level.</summary>
        public const ushort Program = 0x0153;
        /// <summary>System level.</summary>
        public const ushort System = 0x0154;
        /// <summary>Generic 1-byte system parameter.</summary>
        public const ushort SystemValue = 0x0155;
        /// <summary>MIDI level.</summary>
        public const ushort Midi = 0x0157;
        /// <summary>Generic MIDI parameter.</summary>
        public const ushort MidiValue = 0x0158;
        /// <summary>Panel level.</summary>
        public const ushort Panel = 0x015A;
        /// <summary>Control level that wrongly ends a branch at A:1 B:2 C:0 (Appendix D bug 2).</summary>
        public const ushort DeadEndLevel = 0x015B;
        /// <summary>Setups level.</summary>
        public const ushort Setups = 0x015C;
        /// <summary>One setup.</summary>
        public const ushort Setup = 0x015D;
        /// <summary>Root ("MPX 1").</summary>
        public const ushort Root = 0x0163;
        /// <summary>Remap Controllers dump (V1.10).</summary>
        public const ushort RemapDump = 0x012C;
        /// <summary>Bypass Controllers dump (V1.10).</summary>
        public const ushort BypassDump = 0x012D;
        /// <summary>Global Patches dump (V1.10).</summary>
        public const ushort GlobalPatchesDump = 0x012E;
        /// <summary>Current Choices dump (V1.10).</summary>
        public const ushort CurrentChoicesDump = 0x0126;
        /// <summary>Setup dump (V1.10).</summary>
        public const ushort SetupDump = 0x0129;
        /// <summary>Patches dump of the active program (V1.10).</summary>
        public const ushort PatchesDump = 0x012A;
        /// <summary>Soft Row dump of the active program (V1.10).</summary>
        public const ushort SoftRowDump = 0x012B;
        /// <summary>All LEDs dump (V1.10).</summary>
        public const ushort LedsDump = 0x0130;
        /// <summary>Custom Character Bitmap dump (V1.10).</summary>
        public const ushort CustomCharactersDump = 0x0131;
        /// <summary>Level that holds the six setup dumps.</summary>
        public const ushort SetupDumps = 0x015F;
        /// <summary>Level that holds the active-program dumps.</summary>
        public const ushort ProgramDumps = 0x0160;
        /// <summary>Level that holds the controller dumps.</summary>
        public const ushort Controllers = 0x015E;
        /// <summary>First controller parameter type of the fixture (LFO, ADSR, ... parameters follow).</summary>
        public const ushort FirstController = 0x0170;
        /// <summary>2-byte controller rate (Rate display units).</summary>
        public const ushort ControllerRate = 0x0170;
    }

    // Controller parameter lists (types 6-16). The real lists are unknown until hardware captures exist (R3);
    // these only need to fill each block of the modulator data exactly (spec 4.5).
    private static readonly (EffectType Type, string Name, string[] Parameters)[] ControllerLists =
    [
        (EffectType.Knob, "Knob", ["Value", "Min", "Max"]),
        (EffectType.Lfo1, "LFO 1", ["Mode", "Rate*", "PW", "Phase", "Depth", "OnLvl", "OnSrc"]),
        (EffectType.Lfo2, "LFO 2", ["Mode", "Rate*", "PW", "Phase", "Depth", "OnLvl", "OnSrc"]),
        (EffectType.Arpeggiator, "Arp", ["Mode", "Pattern", "Note", "Octave", "Speed"]),
        (EffectType.Adsr1, "ADSR 1", ["Trig", "TLvl", "ATime", "ALvl", "DTime", "SLvl", "STime", "RTime", "Vel"]),
        (EffectType.Adsr2, "ADSR 2", ["Trig", "TLvl", "ATime", "ALvl", "DTime", "SLvl", "STime", "RTime", "Vel"]),
        (EffectType.Random, "Random", ["High", "Low", "Rate*"]),
        (EffectType.AB, "A/B", ["Mode", "ARate", "BRate", "OnLvl", "OnSrc"]),
        (EffectType.SampleAndHold, "S&H", ["Src", "Rate*", "Thrsh", "Mode"]),
        (EffectType.EnvelopeGenerator1, "Env 1", ["Src1", "Src2", "ATrim", "Resp"]),
        (EffectType.EnvelopeGenerator2, "Env 2", ["Src1", "Src2", "ATrim", "Resp"]),
    ];

    private static (List<FixtureDescription> Descriptions, List<FixtureController> Controllers) CreateControllers()
    {
        Dictionary<string, ushort> types = new(StringComparer.Ordinal) { ["Rate*"] = Types.ControllerRate };
        List<FixtureDescription> descriptions = [new(Types.ControllerRate, "Rate       ", 2, (byte)(ParameterAttributes.Patchable | ParameterAttributes.Automation), null, [new(0, 5000, 0x17)])];
        List<FixtureController> controllers = [];
        foreach ((EffectType type, string name, string[] parameters) in ControllerLists)
        {
            List<ushort> list = [];
            foreach (string parameter in parameters)
            {
                if (!types.TryGetValue(parameter, out ushort number))
                {
                    number = (ushort)(Types.FirstController + types.Count);
                    types[parameter] = number;
                    descriptions.Add(new(number, parameter.PadRight(11), 1, (byte)ParameterAttributes.Patchable, null, [new(0, 127, 0x05)]));
                }
                list.Add(number);
            }
            controllers.Add(new FixtureController((byte)type, name, list));
        }
        return (descriptions, controllers);
    }

    /// <summary>Builds a valid setup: channel 1, fast MIDI, device 0, no program +/- controllers, named "Setup n".</summary>
    /// <param name="number">Setup 0–5 (5 = active).</param>
    public static byte[] InitialSetup(int number)
    {
        DumpLayout layout = DumpLayouts.Setup;
        byte[] data = new byte[layout.Size];
        DumpField Field(string name) => layout.Fields.First(f => f.Name == name);
        data = Field("MIDI transmit speed").Write(data, MidiSpeed.Fast);
        data = Field("Program + controller").Write(data, ControllerSources.Unassigned);
        data = Field("Program − controller").Write(data, ControllerSources.Unassigned);
        data = Field("LCD contrast").Write(data, 8);
        return Field("Setup name").WriteText(data, number == 5 ? "Active" : $"Setup {number + 1}");
    }

    /// <summary>Builds the LED state of an idle unit: Program LED on, digits "001".</summary>
    public static byte[] InitialLeds()
    {
        byte[] data = [.. LedState.AllOff().With(PanelLed.Program, true).Data];
        data[4] = 0x3F;
        data[6] = 0x3F;
        data[8] = 0x30;
        return data;
    }

    /// <summary>Builds eight custom characters: character 0 is the "edited" dot, the others are bars of rising height.</summary>
    public static byte[] InitialCustomCharacters()
    {
        byte[] data = new byte[CustomCharacters.Size];
        data[5] = 0x0E;
        data[6] = 0x0E;
        for (int c = 1; c < CustomCharacters.Count; c++)
        {
            for (int row = CustomCharacters.Rows - 1 - c; row < CustomCharacters.Rows - 1; row++)
                data[(c * CustomCharacters.Rows) + row] = 0x1F;
        }
        return data;
    }

    /// <summary>Real size of <see cref="Types.BrokenSizeDump"/>.</summary>
    public const int BrokenSizeDumpLength = 542;

    /// <summary>Creates the default V1.10 fixture.</summary>
    public static SimulatorFixture CreateV110()
    {
        const byte patchAuto = (byte)(ParameterAttributes.Patchable | ParameterAttributes.Automation);
        const byte level = (byte)ParameterAttributes.ControlLevel;
        const byte bottom = (byte)(ParameterAttributes.ControlLevel | ParameterAttributes.BottomControlLevel);

        List<FixtureDescription> descriptions =
        [
            new(Types.Root, "MPX 1      ", 1, level, null, [new(0, 1, 0x00)]),
            new(Types.Program, "Program    ", 1, level, null, [new(0, 2, 0x00)]),
            new(Types.Pitch, "Pitch      ", 1, level, null, [new(0, 1, 0x00)]),
            new(Types.Chorus, "Chorus     ", 1, level, null, [new(0, 0, 0x00)]),
            new(Types.EQ, "EQ         ", 1, level, null, [new(0, 1, 0x00)]),
            new(Types.NoEffect, "no effect  ", 1, bottom, null, [new(0, 1, 0x00)]),
            new(Types.DetuneMono, "Detune (M) ", 1, bottom, null, [new(0, 3, 0x00)]),
            new(Types.OneBandMono, "1-Band (M) ", 1, bottom, null, [new(0, 2, 0x00)]),
            new(Types.Mix, "Mix  ", 1, patchAuto, null, [new(0, 100, 0x00)]),
            new(Types.Level, "Level", 1, patchAuto, null, [new(-96, 0, 0x01)]),
            new(Types.Tune, "Tune ", 1, patchAuto, Types.TuneOption, [new(-100, 100, 0x02)]),
            new(Types.TuneOption, "TunOp", 1, 0, null, [new(0, 1, 0x03)]),
            new(Types.PreDelay, "P Dly", 1, patchAuto, null, [new(0, 200, 0x04)]),
            new(Types.Gain, "Gain ", 1, patchAuto, null, [new(-72, 24, 0x01)]),
            new(Types.System, "System     ", 1, level, null, [new(0, 14, 0x00)]),
            new(Types.SystemValue, "Sys Value  ", 1, 0, null, [new(0, 127, 0x05)]),
            new(Types.Midi, "MIDI       ", 1, level, null, [new(0, 10, 0x00)]),
            new(Types.MidiValue, "MIDI Value ", 1, 0, null, [new(0, 127, 0x05)]),
            new(Types.DeadEndLevel, "MIDI Dumps ", 1, level, null, [new(0, 2, 0x00)]),
            new(Types.MidiSpeed, "MIDI Speed ", 1, 0, null, [new(1, 64, 0x05)]),
            new(Types.Panel, "Panel      ", 1, level, null, [new(0, 3, 0x00)]),
            new(Types.PanelButton, "Button     ", 1, 0, null, [new(0, 127, 0x05)]),
            new(Types.DisplayDump, "Display    ", 32, 0, null, [new(0, -1, 0x44)]),
            new(Types.Setups, "Setups     ", 1, level, null, [new(0, 2, 0x00)]),
            new(Types.Setup, "Setup      ", 1, bottom, null, [new(0, 0, 0x00)]),
            // The document does not name the 542-byte dump of Appendix D bug 1.
            new(Types.BrokenSizeDump, "Bulk Dump  ", 0, 0, null, [new(0, -1, 0x44)]),
            new(Types.LedsDump, "LEDs       ", LedState.Size, 0, null, [new(0, -1, 0x44)]),
            new(Types.CustomCharactersDump, "Custom Char", CustomCharacters.Size, 0, null, [new(0, -1, 0x44)]),
            new(Types.SetupDumps, "Setup Dumps", 1, bottom, null, [new(0, 5, 0x00)]),
            new(Types.SetupDump, "Setup      ", (ushort)DumpLayouts.Setup.Size, 0, null, [new(0, -1, 0x44)]),
            new(Types.ProgramDumps, "Pgm Dumps  ", 1, bottom, null, [new(0, 2, 0x00)]),
            new(Types.CurrentChoicesDump, "Cur Choices", (ushort)DumpLayouts.CurrentChoices.Size, 0, null, [new(0, -1, 0x44)]),
            new(Types.PatchesDump, "Patches    ", (ushort)DumpLayouts.Patches.Size, 0, null, [new(0, -1, 0x44)]),
            new(Types.SoftRowDump, "Soft Row   ", (ushort)DumpLayouts.SoftRow.Size, 0, null, [new(0, -1, 0x44)]),
            new(Types.Controllers, "Controllers", 1, bottom, null, [new(0, 2, 0x00)]),
            new(Types.GlobalPatchesDump, "Global Ptch", 20, 0, null, [new(0, -1, 0x44)]),
            new(Types.BypassDump, "Bypass Ctrl", 7, 0, null, [new(0, -1, 0x44)]),
            new(Types.RemapDump, "Remap Ctrl ", 163, 0, null, [new(0, -1, 0x44)]),
        ];

        (List<FixtureDescription> controllerDescriptions, List<FixtureController> controllers) = CreateControllers();
        descriptions.AddRange(controllerDescriptions);

        List<FixtureNode> nodes = [];
        void Node(ushort type, params ushort[] address) => nodes.Add(new FixtureNode(address, type));

        Node(Types.Root);
        Node(Types.Program, 0);
        Node(Types.Pitch, 0, 0);
        Node(Types.NoEffect, 0, 0, 0);
        Node(Types.Mix, 0, 0, 0, 0);
        Node(Types.Level, 0, 0, 0, 1);
        Node(Types.DetuneMono, 0, 0, 1);
        Node(Types.Mix, 0, 0, 1, 0);
        Node(Types.Level, 0, 0, 1, 1);
        Node(Types.Tune, 0, 0, 1, 2);
        Node(Types.PreDelay, 0, 0, 1, 3);
        Node(Types.Chorus, 0, 1);
        Node(Types.NoEffect, 0, 1, 0);
        Node(Types.Mix, 0, 1, 0, 0);
        Node(Types.Level, 0, 1, 0, 1);
        Node(Types.EQ, 0, 2);
        Node(Types.NoEffect, 0, 2, 0);
        Node(Types.Mix, 0, 2, 0, 0);
        Node(Types.Level, 0, 2, 0, 1);
        Node(Types.OneBandMono, 0, 2, 1);
        Node(Types.Mix, 0, 2, 1, 0);
        Node(Types.Level, 0, 2, 1, 1);
        Node(Types.Gain, 0, 2, 1, 2);

        Node(Types.System, 1);
        for (ushort b = 0; b <= 14; b++)
        {
            switch (b)
            {
                case 2:
                    Node(Types.Midi, 1, 2);
                    Node(Types.DeadEndLevel, 1, 2, 0);
                    for (ushort c = 1; c <= 9; c++)
                        Node(Types.MidiValue, 1, 2, c);
                    Node(Types.MidiSpeed, 1, 2, 10);
                    break;
                case 8:
                    Node(Types.Panel, 1, 8);
                    Node(Types.PanelButton, 1, 8, 0);
                    Node(Types.DisplayDump, 1, 8, 1);
                    Node(Types.LedsDump, 1, 8, 2);
                    Node(Types.CustomCharactersDump, 1, 8, 3);
                    break;
                case 11:
                    Node(Types.Setups, 1, 11);
                    for (ushort c = 0; c <= 2; c++)
                    {
                        Node(Types.Setup, 1, 11, c);
                        Node(Types.BrokenSizeDump, 1, 11, c, 0);
                    }
                    break;
                case 12:
                    Node(Types.Controllers, 1, 12);
                    Node(Types.GlobalPatchesDump, 1, 12, 0);
                    Node(Types.BypassDump, 1, 12, 1);
                    Node(Types.RemapDump, 1, 12, 2);
                    break;
                case 13:
                    Node(Types.SetupDumps, 1, 13);
                    for (ushort c = 0; c <= 5; c++)
                        Node(Types.SetupDump, 1, 13, c);
                    break;
                case 14:
                    Node(Types.ProgramDumps, 1, 14);
                    Node(Types.CurrentChoicesDump, 1, 14, 0);
                    Node(Types.PatchesDump, 1, 14, 1);
                    Node(Types.SoftRowDump, 1, 14, 2);
                    break;
                default:
                    Node(Types.SystemValue, 1, b);
                    break;
            }
        }

        List<FixtureValue> values =
        [
            new([0, 0, 1, 0], "64"),
            new([0, 0, 1, 2], "0A01"),
            new([1, 2, 10], "01"),
            new([1, 8, 1], Convert.ToHexString(Encoding.ASCII.GetBytes("  MPX 1  V1.10  Simulator       "))),
        ];
        values.Add(new([1, 8, 2], Convert.ToHexString(InitialLeds())));
        values.Add(new([1, 8, 3], Convert.ToHexString(InitialCustomCharacters())));
        for (ushort c = 0; c <= 5; c++)
            values.Add(new([1, 13, c], Convert.ToHexString(InitialSetup(c))));
        values.Add(new([1, 14, 0], new string('0', DumpLayouts.CurrentChoices.Size * 2)));

        // Controller dumps start unassigned (0xFF); global patch destinations start at 0 ("none").
        values.Add(new([1, 12, 0], new string('F', 20) + new string('0', 20)));
        values.Add(new([1, 12, 1], new string('F', 7 * 2)));
        values.Add(new([1, 12, 2], new string('F', 163 * 2)));
        for (ushort c = 0; c <= 2; c++)
            values.Add(new([1, 11, c, 0], Convert.ToHexString(Enumerable.Range(0, BrokenSizeDumpLength).Select(i => (byte)(i + c)).ToArray())));

        List<FixtureProgram> programs = [.. Enumerable.Range(0, DatabaseDump.ProgramCount)
            .Select(i => new FixtureProgram((ushort)i, Convert.ToHexString(CreateProgram(i).ToBytes())))];

        return new SimulatorFixture
        {
            MajorVersion = 1,
            MinorVersion = 10,
            DevelopmentPhase = 0,
            BuildTime = "17:51:03",
            BuildDate = "Mar 30 1998",
            ParameterTypeCount = 0x01C0,
            BottomParameter = 0x0164,
            MaxControlLevels = 5,
            Descriptions = descriptions,
            Nodes = nodes,
            Values = values,
            Programs = programs,
            ActiveProgram = 0,
            Controllers = controllers,
        };
    }

    /// <summary>Creates a deterministic program for slot <paramref name="number"/>.</summary>
    /// <param name="number">Program number 0–249.</param>
    public static ProgramData CreateProgram(int number)
    {
        byte[][] effectData = [.. Enumerable.Range(0, ProgramData.EffectBlockCount).Select(e =>
        {
            byte[] block = new byte[ProgramData.EffectDataSize];
            block[0] = 100;
            block[1] = (byte)((number + e) % 0x60);
            return block;
        })];

        string name = number < 200 ? $"Preset {number + 1:D3}" : $"User {number - 199:D2}";
        return new ProgramData
        {
            EffectData = [.. effectData.Select(b => b.ToImmutableArray())],
            Categories = new ProgramCategories((EffectCategories)(1 << (number % 12)), (SourceCategories)(1 << (number % 8))),
            Routing = AudioRouting.Default,
            Algorithms = [(byte)(number % 2), 0, (byte)(number % 2), 0, 0, 0],
            Name = name,
            EffectStatus = EffectStatus.Pitch | EffectStatus.EQ,
            SoftValues = [.. Enumerable.Range(0, ProgramData.SoftValueCount).Select(i => new SoftValue((byte)(i % 6), 0))],
            Tempo = (ushort)(60 + (number % 120)),
            TempoSource = 0,
            BeatValue = 2,
            TapSource = 0xFF,
            TapAverage = 2,
            TapSourceLevel = 64,
            Meter = 0,
            MasterLevel = 0,
            MasterMix = 100,
            Patches = [.. Enumerable.Repeat(new Patch(0xFF, 0, 64, 127, 0xFF, 0, 0, 0, 0), ProgramData.PatchCount)],
            Knob = new Knob(64, 0, 127, "Knob"),
            ModulatorData = [.. new byte[ProgramData.ModulatorDataSize]],
        };
    }
}
