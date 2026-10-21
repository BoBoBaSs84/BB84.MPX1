using System.Collections.Immutable;

using BB84.MPX1.Core.Programs;
using BB84.MPX1.Core.Protocol;

namespace BB84.MPX1.Core.Tests;

internal static class TestData
{
    public static readonly DeviceId Device = new(0x10);

    /// <summary>A program where every byte position holds a distinct, non-zero pattern.</summary>
    public static ProgramData Program()
    {
        byte seed = 1;
        byte Next() => seed++;

        return new ProgramData
        {
            EffectData = [.. Enumerable.Range(0, ProgramData.EffectBlockCount)
                .Select(_ => Enumerable.Range(0, ProgramData.EffectDataSize).Select(_ => Next()).ToImmutableArray())],
            Categories = new ProgramCategories(EffectCategories.Chorus | EffectCategories.Hall, SourceCategories.Guitar),
            Routing = [.. Enumerable.Range(0, ProgramData.RoutingBlockCount).Select(_ => new RoutingBlock(Next(), Next(), Next(), Next(), Next()))],
            Algorithms = [1, 2, 3, 4, 5, 6],
            Name = "Test Program",
            EffectStatus = EffectStatus.Pitch | EffectStatus.Reverb,
            SoftValues = [.. Enumerable.Range(0, ProgramData.SoftValueCount).Select(_ => new SoftValue(Next(), Next()))],
            Tempo = 120,
            TempoSource = 1,
            BeatValue = 2,
            TapSource = 0x40,
            TapAverage = 4,
            TapSourceLevel = 64,
            Meter = 20,
            MasterLevel = 0xF0,
            MasterMix = 100,
            Patches = [.. Enumerable.Range(0, ProgramData.PatchCount)
                .Select(i => new Patch(Next(), Next(), Next(), Next(), Next(), Next(), (ushort)(0x1000 + i), (ushort)(0x2000 + i), 0xFFFF))],
            Knob = new Knob(10, 0, 127, "Knob Name"),
            ModulatorData = [.. Enumerable.Range(0, ProgramData.ModulatorDataSize).Select(_ => Next())],
        };
    }
}
