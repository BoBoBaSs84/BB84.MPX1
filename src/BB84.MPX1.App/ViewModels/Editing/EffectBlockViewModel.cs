using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;

using BB84.MPX1.App.ViewModels.Parameters;
using BB84.MPX1.Core.Database;
using BB84.MPX1.Core.Programs;
using BB84.MPX1.Core.Protocol;
using BB84.MPX1.Core.Values;

namespace BB84.MPX1.App.ViewModels.Editing;

/// <summary>One of the six effect blocks: on/off, algorithm and its parameters (UI-006).</summary>
public sealed partial class EffectBlockViewModel : ObservableObject
{
    private readonly IProgramDocument _document;
    private bool _applying;

    /// <summary>Initializes a new instance of the <see cref="EffectBlockViewModel"/> class.</summary>
    /// <param name="document">The program document.</param>
    /// <param name="index">Block index 0–5.</param>
    public EffectBlockViewModel(IProgramDocument document, int index)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        Index = index;
        Name = ProgramFields.EffectNames[index];
        ParameterDatabase? database = document.Database;
        int count = database?.AlgorithmCount((EffectType)index) ?? 0;
        byte current = document.Program.Algorithms[index];
        Algorithms = [.. Enumerable.Range(0, Math.Max(count, current + 1))
            .Select(a => new ChoiceItem(a, database?.FindAlgorithm((EffectType)index, (byte)a)?.Name ?? $"Algorithm {a}"))];
        Refresh();
    }

    /// <summary>Gets the block index.</summary>
    public int Index { get; }

    /// <summary>Gets the block name.</summary>
    public string Name { get; }

    /// <summary>Gets the algorithms the block offers.</summary>
    public IReadOnlyList<ChoiceItem> Algorithms { get; }

    /// <summary>Gets the parameters of the selected algorithm.</summary>
    public ObservableCollection<BlockParameterViewModel> Parameters { get; } = [];

    /// <summary>Gets or sets the selected algorithm.</summary>
    [ObservableProperty]
    public partial ChoiceItem? SelectedAlgorithm { get; set; }

    /// <summary>Gets or sets a value indicating whether the effect is active (not bypassed).</summary>
    [ObservableProperty]
    public partial bool IsOn { get; set; }

    /// <summary>Gets or sets a notice, for example when the algorithm's parameter list is unknown.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotice))]
    public partial string? Notice { get; set; }

    /// <summary>Gets a value indicating whether <see cref="Notice"/> is set.</summary>
    public bool HasNotice => Notice is not null;

    /// <summary>Raised after the algorithm changed (soft row and patch lists depend on it).</summary>
    public event EventHandler? AlgorithmChanged;

    private EffectStatus Bit => (EffectStatus)(1 << Index);

    /// <summary>Re-reads the block from the document.</summary>
    public void Refresh()
    {
        _applying = true;
        try
        {
            ProgramData program = _document.Program;
            IsOn = program.EffectStatus.HasFlag(Bit);
            SelectedAlgorithm = Algorithms.FirstOrDefault(a => a.Value == program.Algorithms[Index]);
            BuildParameters();
        }
        finally
        {
            _applying = false;
        }
    }

    partial void OnIsOnChanged(bool value)
    {
        if (!_applying)
            _document.Update(p => p with { EffectStatus = value ? p.EffectStatus | Bit : p.EffectStatus & ~Bit });
    }

    partial void OnSelectedAlgorithmChanged(ChoiceItem? value)
    {
        if (_applying || value is null)
            return;
        byte algorithm = (byte)value.Value;
        ParameterBlockLayout? layout = TryLayout(algorithm);
        _document.Update(p =>
        {
            ProgramData changed = p with { Algorithms = p.Algorithms.SetItem(Index, algorithm) };
            // Old values may not suit the new algorithm; clamp them to the new limits (document, "Algorithm numbers").
            return layout is null ? changed : changed.WithEffectData(Index, Clamp(layout, changed.EffectData[Index].AsSpan()));
        });
        BuildParameters();
        AlgorithmChanged?.Invoke(this, EventArgs.Empty);
    }

    private void BuildParameters()
    {
        Parameters.Clear();
        Notice = null;
        if (_document.Database is null)
        {
            Notice = "Learn a device once to edit effect parameters; the layout comes from its parameter lists.";
            return;
        }
        byte algorithm = _document.Program.Algorithms[Index];
        if (TryLayout(algorithm) is not { } layout)
        {
            Notice = algorithm == ProgramData.ClearedAlgorithm ? "This program is cleared." : $"No parameter list is known for algorithm {algorithm}.";
            return;
        }
        for (int i = 0; i < layout.Parameters.Length; i++)
        {
            Parameters.Add(new BlockParameterViewModel(_document, layout, i,
                p => p.EffectData[Index].AsSpan(), (p, bytes) => p.WithEffectData(Index, bytes)));
        }
    }

    private ParameterBlockLayout? TryLayout(byte algorithm)
    {
        try
        {
            return _document.Database?.Layout((EffectType)Index, algorithm);
        }
        catch (ArgumentException ex)
        {
            Notice = ex.Message;
            return null;
        }
    }

    internal static byte[] Clamp(ParameterBlockLayout layout, ReadOnlySpan<byte> block)
    {
        byte[] data = block.ToArray();
        for (int i = 0; i < layout.Parameters.Length; i++)
        {
            if (!layout.Parameters[i].IsNumeric)
                continue;
            ParameterValue value = layout.Read(data, i);
            (int min, int max) = ParameterValueCodec.GetRange(value.Unit, layout.Parameters[i].Description.Size);
            if (min <= max && (value.Value < min || value.Value > max))
                data = layout.Write(data, i, Math.Clamp(value.Value, min, max), value.Option);
        }
        return data;
    }
}
