using System.Collections.ObjectModel;

using BB84.MPX1.Core.Programs;

namespace BB84.MPX1.App.ViewModels.Editing;

/// <summary>One modulator block: LFO, arpeggiator, ADSR, random, A/B, sample and hold or envelope (UI-006).</summary>
public sealed class ModulatorBlockViewModel
{
    /// <summary>Initializes a new instance of the <see cref="ModulatorBlockViewModel"/> class.</summary>
    /// <param name="document">The program document.</param>
    /// <param name="block">The modulator block.</param>
    public ModulatorBlockViewModel(IProgramDocument document, ModulatorBlock block)
    {
        ArgumentNullException.ThrowIfNull(document);
        Block = block ?? throw new ArgumentNullException(nameof(block));
        ParameterBlockLayout? layout = null;
        try
        {
            layout = document.Database?.Layout(block.Type, 0);
        }
        catch (ArgumentException ex)
        {
            Notice = ex.Message;
        }
        if (layout is null)
        {
            Notice ??= document.Database is null
                ? "Learn a device once to edit modulators."
                : $"No parameter list is known for {block.Name}.";
            return;
        }
        for (int i = 0; i < layout.Parameters.Length; i++)
            Parameters.Add(new BlockParameterViewModel(document, layout, i, p => p.GetModulatorBlock(block), (p, bytes) => p.WithModulatorBlock(block, bytes)));
    }

    /// <summary>Gets the block.</summary>
    public ModulatorBlock Block { get; }

    /// <summary>Gets the name.</summary>
    public string Name => Block.Name;

    /// <summary>Gets the parameters.</summary>
    public ObservableCollection<BlockParameterViewModel> Parameters { get; } = [];

    /// <summary>Gets a notice, or <see langword="null"/>.</summary>
    public string? Notice { get; }

    /// <summary>Gets a value indicating whether <see cref="Notice"/> is set.</summary>
    public bool HasNotice => Notice is not null;
}
