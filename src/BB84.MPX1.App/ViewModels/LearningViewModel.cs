using CommunityToolkit.Mvvm.ComponentModel;

using BB84.MPX1.Midi.Learning;

namespace BB84.MPX1.App.ViewModels;

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
