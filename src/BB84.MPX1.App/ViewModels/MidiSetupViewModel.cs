using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;

using BB84.MPX1.App.Services;
using BB84.MPX1.Midi.Learning;

namespace BB84.MPX1.App.ViewModels;

/// <summary>MIDI setup form: ports, device ID and session timing (UI-001).</summary>
public sealed partial class MidiSetupViewModel : ObservableObject
{
    private readonly IMidiPortCatalog _catalog;
    private bool _pairing;

    /// <summary>Initializes a new instance of the <see cref="MidiSetupViewModel"/> class.</summary>
    /// <param name="catalog">The port catalog.</param>
    public MidiSetupViewModel(IMidiPortCatalog catalog)
        => _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));

    /// <summary>Gets the input ports.</summary>
    public ObservableCollection<MidiPortChoice> Inputs { get; } = [];

    /// <summary>Gets the output ports.</summary>
    public ObservableCollection<MidiPortChoice> Outputs { get; } = [];

    /// <summary>Gets or sets the input port.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary), nameof(IsComplete))]
    public partial MidiPortChoice? SelectedInput { get; set; }

    /// <summary>Gets or sets the output port.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary), nameof(IsComplete))]
    public partial MidiPortChoice? SelectedOutput { get; set; }

    /// <summary>Gets or sets the device ID, 0–127 (127 addresses all devices).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    public partial int DeviceId { get; set; }

    /// <summary>Gets or sets the request timeout in milliseconds.</summary>
    [ObservableProperty]
    public partial int RequestTimeoutMs { get; set; } = 1000;

    /// <summary>Gets or sets the retry count.</summary>
    [ObservableProperty]
    public partial int Retries { get; set; } = 2;

    /// <summary>Gets or sets the READY timeout in milliseconds.</summary>
    [ObservableProperty]
    public partial int ReadyTimeoutMs { get; set; } = 3000;

    /// <summary>Gets or sets the gap between outgoing frames in milliseconds.</summary>
    [ObservableProperty]
    public partial int InterFrameGapMs { get; set; }

    /// <summary>Gets a value indicating whether both ports are chosen.</summary>
    public bool IsComplete => SelectedInput is not null && SelectedOutput is not null;

    /// <summary>Gets a one-line description for the toolbar.</summary>
    public string Summary => !IsComplete
        ? "No MIDI ports selected"
        : SelectedInput!.IsSimulator
            ? $"{SelectedInput.Name} · ID {DeviceId}"
            : $"{SelectedInput.Name} / {SelectedOutput!.Name} · ID {DeviceId}";

    /// <summary>Re-reads the port lists and keeps the selection where possible.</summary>
    public void RefreshPorts()
    {
        string? input = SelectedInput?.Key;
        string? output = SelectedOutput?.Key;
        Replace(Inputs, _catalog.GetInputs());
        Replace(Outputs, _catalog.GetOutputs());
        Select(input, output);
    }

    /// <summary>Fills the form from settings.</summary>
    /// <param name="settings">The settings.</param>
    public void Apply(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        RefreshPorts();
        Select(settings.InputPort, settings.OutputPort);
        DeviceId = Math.Clamp((int)settings.DeviceId, 0, 127);
        RequestTimeoutMs = settings.RequestTimeoutMs;
        Retries = settings.Retries;
        ReadyTimeoutMs = settings.ReadyTimeoutMs;
        InterFrameGapMs = settings.InterFrameGapMs;
    }

    /// <summary>Stores the form in settings.</summary>
    /// <param name="settings">The previous settings.</param>
    public AppSettings ToSettings(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings with
        {
            InputPort = SelectedInput?.Key,
            OutputPort = SelectedOutput?.Key,
            DeviceId = (byte)Math.Clamp(DeviceId, 0, 127),
            RequestTimeoutMs = RequestTimeoutMs,
            Retries = Retries,
            ReadyTimeoutMs = ReadyTimeoutMs,
            InterFrameGapMs = InterFrameGapMs,
        };
    }

    // The simulator is one device: picking it on one side picks it on the other, and leaving it on one side
    // leaves it on the other.
    partial void OnSelectedInputChanged(MidiPortChoice? value) => Pair(value, Outputs, o => SelectedOutput = o, SelectedOutput);

    partial void OnSelectedOutputChanged(MidiPortChoice? value) => Pair(value, Inputs, i => SelectedInput = i, SelectedInput);

    private void Pair(MidiPortChoice? chosen, IList<MidiPortChoice> otherList, Action<MidiPortChoice?> setOther, MidiPortChoice? other)
    {
        if (_pairing || chosen is null)
            return;
        _pairing = true;
        try
        {
            if (chosen.IsSimulator && other?.IsSimulator != true)
                setOther(otherList.FirstOrDefault(p => p.IsSimulator));
            else if (!chosen.IsSimulator && other?.IsSimulator == true)
                setOther(otherList.FirstOrDefault(p => !p.IsSimulator));
        }
        finally
        {
            _pairing = false;
        }
    }

    private void Select(string? input, string? output)
    {
        _pairing = true;
        try
        {
            SelectedInput = Inputs.FirstOrDefault(p => p.Key == input);
            SelectedOutput = Outputs.FirstOrDefault(p => p.Key == output);
        }
        finally
        {
            _pairing = false;
        }
    }

    private static void Replace(ObservableCollection<MidiPortChoice> target, IReadOnlyList<MidiPortChoice> ports)
    {
        target.Clear();
        foreach (MidiPortChoice port in ports)
            target.Add(port);
    }
}

/// <summary>Learning status (UI-002): progress, cache status and the learn prompt.</summary>
public sealed partial class LearningViewModel : ObservableObject
{
    /// <summary>Gets or sets a value indicating whether learning runs.</summary>
    [ObservableProperty]
    public partial bool IsLearning { get; set; }

    /// <summary>Gets or sets a value indicating whether a database is available.</summary>
    [ObservableProperty]
    public partial bool IsLearned { get; set; }

    /// <summary>Gets or sets a value indicating whether the connected firmware still has to be learned.</summary>
    [ObservableProperty]
    public partial bool NeedsLearning { get; set; }

    /// <summary>Gets or sets the progress in percent.</summary>
    [ObservableProperty]
    public partial double Percent { get; set; }

    /// <summary>Gets or sets a value indicating whether the progress cannot be expressed in percent (tree walk).</summary>
    [ObservableProperty]
    public partial bool IsIndeterminate { get; set; }

    /// <summary>Gets or sets the progress or cache text.</summary>
    [ObservableProperty]
    public partial string Status { get; set; } = "Not connected";

    /// <summary>Shows a progress report.</summary>
    /// <param name="progress">The report.</param>
    public void Report(LearningProgress progress)
    {
        ArgumentNullException.ThrowIfNull(progress);
        switch (progress.Phase)
        {
            case LearningPhase.Configuration:
                IsIndeterminate = true;
                Status = "Reading configuration…";
                break;
            case LearningPhase.Descriptions:
                IsIndeterminate = false;
                Percent = progress.DescriptionsTotal == 0 ? 0 : 100.0 * progress.DescriptionsDone / progress.DescriptionsTotal;
                Status = $"Descriptions {progress.DescriptionsDone}/{progress.DescriptionsTotal}";
                break;
            case LearningPhase.Tree:
                IsIndeterminate = true;
                Status = $"Control tree: {progress.NodesVisited} nodes";
                break;
            case LearningPhase.Algorithms:
                IsIndeterminate = true;
                Status = "Algorithm parameter lists…";
                break;
            case LearningPhase.Cached:
            case LearningPhase.Done:
                IsIndeterminate = false;
                Percent = 100;
                break;
        }
    }
}
