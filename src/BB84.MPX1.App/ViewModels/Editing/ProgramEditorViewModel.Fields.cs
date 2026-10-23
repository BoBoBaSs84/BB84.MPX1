using CommunityToolkit.Mvvm.ComponentModel;

using BB84.MPX1.App.ViewModels.Parameters;
using BB84.MPX1.Core.Programs;
using BB84.MPX1.Core.Protocol;

namespace BB84.MPX1.App.ViewModels.Editing;

/// <summary>Scalar program fields: name, tempo, taps, meter, master and knob.</summary>
public sealed partial class ProgramEditorViewModel
{
    /// <summary>Gets the tap sources, "None" first.</summary>
    public static IReadOnlyList<ChoiceItem> TapSources { get; } =
        [new(ControllerSources.Unassigned, "None"), .. ControllerSources.TapSources.Select(s => new ChoiceItem(s, ControllerSources.Name(s)))];

    /// <summary>Gets the tempo sources.</summary>
    public static IReadOnlyList<ChoiceItem> TempoSources { get; } = [.. ProgramFields.TempoSources.Select((n, i) => new ChoiceItem(i, n))];

    /// <summary>Gets the beat values.</summary>
    public static IReadOnlyList<ChoiceItem> BeatValues { get; } = [.. ProgramFields.BeatValues.Select((n, i) => new ChoiceItem(i, n))];

    /// <summary>Gets the meter sources.</summary>
    public static IReadOnlyList<ChoiceItem> Meters { get; } = [.. ProgramFields.Meters.Select((n, i) => new ChoiceItem(i, n))];

    /// <summary>Gets or sets the name (12 characters).</summary>
    [ObservableProperty]
    public partial string Name { get; set; } = "";

    /// <summary>Gets or sets the tempo in BPM.</summary>
    [ObservableProperty]
    public partial int Tempo { get; set; }

    /// <summary>Gets or sets the tempo source.</summary>
    [ObservableProperty]
    public partial ChoiceItem? SelectedTempoSource { get; set; }

    /// <summary>Gets or sets the beat value.</summary>
    [ObservableProperty]
    public partial ChoiceItem? SelectedBeatValue { get; set; }

    /// <summary>Gets or sets the tap source.</summary>
    [ObservableProperty]
    public partial ChoiceItem? SelectedTapSource { get; set; }

    /// <summary>Gets or sets the tap average (1–8 beats).</summary>
    [ObservableProperty]
    public partial int TapAverage { get; set; }

    /// <summary>Gets or sets the tap source level (0–127).</summary>
    [ObservableProperty]
    public partial int TapSourceLevel { get; set; }

    /// <summary>Gets or sets the meter source.</summary>
    [ObservableProperty]
    public partial ChoiceItem? SelectedMeter { get; set; }

    /// <summary>Gets or sets the master level in dB (−96 = Off … 0).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MasterLevelText))]
    public partial int MasterLevel { get; set; }

    /// <summary>Gets the master level as text.</summary>
    public string MasterLevelText => ProgramFields.FormatMasterLevel(ProgramFields.MasterLevelFromDb(MasterLevel));

    /// <summary>Gets or sets the master mix in percent.</summary>
    [ObservableProperty]
    public partial int MasterMix { get; set; }

    /// <summary>Gets or sets the knob value.</summary>
    [ObservableProperty]
    public partial int KnobValue { get; set; }

    /// <summary>Gets or sets the knob minimum.</summary>
    [ObservableProperty]
    public partial int KnobMinimum { get; set; }

    /// <summary>Gets or sets the knob maximum.</summary>
    [ObservableProperty]
    public partial int KnobMaximum { get; set; }

    /// <summary>Gets or sets the knob name (9 characters).</summary>
    [ObservableProperty]
    public partial string KnobName { get; set; } = "";

    private void ApplyFields()
    {
        _applying = true;
        try
        {
            ProgramData p = Program;
            Name = p.Name.TrimEnd();
            Tempo = p.Tempo;
            SelectedTempoSource = TempoSources.FirstOrDefault(t => t.Value == p.TempoSource);
            SelectedBeatValue = BeatValues.FirstOrDefault(b => b.Value == p.BeatValue);
            SelectedTapSource = TapSources.FirstOrDefault(t => t.Value == p.TapSource) ?? TapSources[0];
            TapAverage = p.TapAverage;
            TapSourceLevel = p.TapSourceLevel;
            SelectedMeter = Meters.FirstOrDefault(m => m.Value == p.Meter);
            MasterLevel = ProgramFields.MasterLevelToDb(p.MasterLevel);
            MasterMix = p.MasterMix;
            KnobValue = p.Knob.Value;
            KnobMinimum = p.Knob.Minimum;
            KnobMaximum = p.Knob.Maximum;
            KnobName = p.Knob.Name.TrimEnd();
        }
        finally
        {
            _applying = false;
        }
    }

    partial void OnNameChanged(string value)
        => Edit(p => p with { Name = ProgramFields.NormalizeName(value) });

    partial void OnTempoChanged(int value)
        => Edit(p => p with { Tempo = (ushort)Math.Clamp(value, ProgramFields.MinTempo, ProgramFields.MaxTempo) });

    partial void OnSelectedTempoSourceChanged(ChoiceItem? value) => Edit(p => p with { TempoSource = (byte)(value?.Value ?? 0) });

    partial void OnSelectedBeatValueChanged(ChoiceItem? value) => Edit(p => p with { BeatValue = (byte)(value?.Value ?? 0) });

    partial void OnSelectedTapSourceChanged(ChoiceItem? value) => Edit(p => p with { TapSource = (byte)(value?.Value ?? ControllerSources.Unassigned) });

    partial void OnTapAverageChanged(int value) => Edit(p => p with { TapAverage = (byte)Math.Clamp(value, 1, 8) });

    partial void OnTapSourceLevelChanged(int value) => Edit(p => p with { TapSourceLevel = (byte)Math.Clamp(value, 0, 127) });

    partial void OnSelectedMeterChanged(ChoiceItem? value) => Edit(p => p with { Meter = (byte)(value?.Value ?? 0) });

    partial void OnMasterLevelChanged(int value) => Edit(p => p with { MasterLevel = ProgramFields.MasterLevelFromDb(value) });

    partial void OnMasterMixChanged(int value) => Edit(p => p with { MasterMix = (byte)Math.Clamp(value, 0, ProgramFields.MaxMasterMix) });

    partial void OnKnobValueChanged(int value) => EditKnob();

    partial void OnKnobMinimumChanged(int value) => EditKnob();

    partial void OnKnobMaximumChanged(int value) => EditKnob();

    partial void OnKnobNameChanged(string value) => EditKnob();

    private void EditKnob()
        => Edit(p => p with
        {
            Knob = new Knob((byte)Math.Clamp(KnobValue, 0, 127), (byte)Math.Clamp(KnobMinimum, 0, 127), (byte)Math.Clamp(KnobMaximum, 0, 127),
                (KnobName ?? "").PadRight(Knob.NameLength)[..Knob.NameLength]),
        });
}
