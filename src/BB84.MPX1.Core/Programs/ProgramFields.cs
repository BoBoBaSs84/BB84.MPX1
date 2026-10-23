using System.Collections.Immutable;
using System.Globalization;

using BB84.MPX1.Core.Protocol;

namespace BB84.MPX1.Core.Programs;

/// <summary>Value lists and limits of the fixed program fields (Program Dump detail, DMP-012).</summary>
public static class ProgramFields
{
    /// <summary>Lowest tempo in BPM.</summary>
    public const int MinTempo = 41;

    /// <summary>Highest tempo in BPM.</summary>
    public const int MaxTempo = 400;

    /// <summary>Master level that means "Off" (<c>0xA0</c>, −96).</summary>
    public const int MasterLevelOff = -96;

    /// <summary>Highest master mix in percent.</summary>
    public const int MaxMasterMix = 100;

    /// <summary>System Parameters destination type of a patch (0 = Master Mix, 1 = Master Level).</summary>
    public const byte SystemParameters = 17;

    /// <summary>Gets the tempo sources.</summary>
    public static ImmutableArray<string> TempoSources { get; } = ["Internal", "MIDI"];

    /// <summary>Gets the beat values, 0–6.</summary>
    public static ImmutableArray<string> BeatValues { get; } =
        ["Eighth", "Dotted eighth", "Quarter", "Dotted quarter", "Half", "Dotted half", "Whole"];

    /// <summary>Gets the meter sources, 0–23.</summary>
    public static ImmutableArray<string> Meters { get; } =
    [
        "Inputs", "Outputs",
        "Pitch in", "Pitch out", "Pitch in & out",
        "Chorus in", "Chorus out", "Chorus in & out",
        "EQ in", "EQ out", "EQ in & out",
        "Mod in", "Mod out", "Mod in & out",
        "Delay in", "Delay out", "Delay in & out",
        "Reverb in", "Reverb out", "Reverb in & out",
        "LFOs", "Envelopes", "Foot pedal", "ADRs",
    ];

    /// <summary>Gets the names of the effect blocks, Pitch through Delay.</summary>
    public static ImmutableArray<string> EffectNames { get; } = ["Pitch", "Chorus", "EQ", "Mod", "Reverb", "Delay"];

    /// <summary>Returns the name of a patch or soft-row destination type (effect, controller or System).</summary>
    /// <param name="type">Destination type 0–17, or <c>0xFF</c>.</param>
    public static string DestinationName(byte type) => type switch
    {
        < ProgramData.EffectBlockCount => EffectNames[type],
        (byte)EffectType.Knob => "Knob",
        ControllerSources.Unassigned => "None",
        SystemParameters => "System",
        _ when ModulatorBlocks.Find((EffectType)type) is { } block => block.Name,
        _ => $"Type {type.ToString(CultureInfo.InvariantCulture)}",
    };

    /// <summary>
    /// Creates a blank program: no effect algorithms, series routing, all effects on, 120 BPM, master 0 dB and
    /// 100 % mix, no patches, an empty soft row and an unassigned tap source.
    /// </summary>
    /// <param name="name">The program name.</param>
    public static ProgramData NewProgram(string name = "New Program")
    {
        ArgumentNullException.ThrowIfNull(name);
        Patch noPatch = new(ControllerSources.Unassigned, 0, ControllerSources.Unassigned, 127, ControllerSources.Unassigned, ControllerSources.Unassigned, 0, 0, 0);
        return new ProgramData
        {
            EffectData = [.. Enumerable.Range(0, ProgramData.EffectBlockCount).Select(_ => ImmutableArray.Create(new byte[ProgramData.EffectDataSize]))],
            Categories = default,
            Routing = AudioRouting.Default,
            Algorithms = [0, 0, 0, 0, 0, 0],
            Name = name.PadRight(ProgramData.NameLength)[..ProgramData.NameLength],
            EffectStatus = EffectStatus.Pitch | EffectStatus.Chorus | EffectStatus.EQ | EffectStatus.Mod | EffectStatus.Reverb | EffectStatus.Delay,
            SoftValues = [.. Enumerable.Repeat(new SoftValue(ControllerSources.Unassigned, ControllerSources.Unassigned), ProgramData.SoftValueCount)],
            Tempo = 120,
            TempoSource = 0,
            BeatValue = 2,
            TapSource = ControllerSources.Unassigned,
            TapAverage = 2,
            TapSourceLevel = 64,
            Meter = 0,
            MasterLevel = 0,
            MasterMix = MaxMasterMix,
            Patches = [.. Enumerable.Repeat(noPatch, ProgramData.PatchCount)],
            Knob = new Knob(0, 0, 127, "Knob     "),
            ModulatorData = [.. new byte[ProgramData.ModulatorDataSize]],
        };
    }

    /// <summary>Converts the stored master level to dB (−96 = Off … 0).</summary>
    /// <param name="stored">The stored byte.</param>
    public static int MasterLevelToDb(byte stored) => (sbyte)stored;

    /// <summary>Converts a master level in dB (−96 … 0) to the stored byte.</summary>
    /// <param name="db">The level; clamped to −96 … 0.</param>
    public static byte MasterLevelFromDb(int db) => (byte)(sbyte)Math.Clamp(db, MasterLevelOff, 0);

    /// <summary>Formats a master level: <c>Off</c>, <c>-12dB</c> or <c>0dB</c> (DMP-012).</summary>
    /// <param name="stored">The stored byte.</param>
    public static string FormatMasterLevel(byte stored)
    {
        int db = MasterLevelToDb(stored);
        return db <= MasterLevelOff ? "Off" : $"{db.ToString(CultureInfo.InvariantCulture)}dB";
    }

    /// <summary>Normalizes a program name: printable ASCII, 12 characters, space padded (PRO-007).</summary>
    /// <param name="name">The name.</param>
    public static string NormalizeName(string? name)
    {
        char[] chars = [.. (name ?? "").Select(c => c is >= ' ' and <= '~' ? c : ' ')];
        return new string(chars).PadRight(ProgramData.NameLength)[..ProgramData.NameLength];
    }

    /// <summary>Formats sort flags: effect flags, then source flags, comma separated.</summary>
    /// <param name="categories">The flags.</param>
    public static string DescribeCategories(ProgramCategories categories)
    {
        IEnumerable<string> effects = Enum.GetValues<EffectCategories>().Where(f => f != EffectCategories.None && categories.Effects.HasFlag(f)).Select(f => f.ToString());
        IEnumerable<string> sources = Enum.GetValues<SourceCategories>().Where(f => f != SourceCategories.None && categories.Sources.HasFlag(f)).Select(f => f.ToString());
        return string.Join(", ", effects.Concat(sources));
    }
}
