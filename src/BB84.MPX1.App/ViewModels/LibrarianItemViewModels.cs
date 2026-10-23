using System.Globalization;

using CommunityToolkit.Mvvm.ComponentModel;

using BB84.MPX1.Core.Database;
using BB84.MPX1.Core.Programs;
using BB84.MPX1.Midi.Library;

namespace BB84.MPX1.App.ViewModels;

/// <summary>One sort flag checkbox.</summary>
public sealed partial class CategoryFlagViewModel(string name, int bit, bool isSource) : ObservableObject
{
    /// <summary>Gets the flag name.</summary>
    public string Name { get; } = name;

    /// <summary>Gets the bit value.</summary>
    public int Bit { get; } = bit;

    /// <summary>Gets a value indicating whether this is a source (input) flag rather than an effect flag.</summary>
    public bool IsSource { get; } = isSource;

    /// <summary>Gets or sets a value indicating whether the flag is set.</summary>
    [ObservableProperty]
    public partial bool IsChecked { get; set; }
}

/// <summary>A system dump the user can include in a backup (PRG-005).</summary>
public sealed partial class DumpOptionViewModel(SystemDumpKind kind) : ObservableObject
{
    /// <summary>Gets the dump kind.</summary>
    public SystemDumpKind Kind { get; } = kind;

    /// <summary>Gets the display name.</summary>
    public string Name { get; } = SystemDumps.DisplayName(kind);

    /// <summary>Gets or sets a value indicating whether the dump is included.</summary>
    [ObservableProperty]
    public partial bool IsChecked { get; set; } = true;
}

/// <summary>One of the 250 librarian slots (UI-005).</summary>
public sealed partial class ProgramSlotViewModel : ObservableObject
{
    /// <summary>Initializes a new instance of the <see cref="ProgramSlotViewModel"/> class.</summary>
    /// <param name="summary">The slot contents.</param>
    /// <param name="algorithmNames">Formats algorithm numbers.</param>
    public ProgramSlotViewModel(ProgramSummary summary, Func<IReadOnlyList<byte>, string> algorithmNames)
    {
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentNullException.ThrowIfNull(algorithmNames);
        Number = summary.Number;
        Name = summary.Name;
        Algorithms = algorithmNames(summary.Algorithms);
        Categories = summary.Categories;
    }

    /// <summary>Gets the program number.</summary>
    public ushort Number { get; }

    /// <summary>Gets the number as shown in the list.</summary>
    public string NumberText => Number.ToString("D3", CultureInfo.InvariantCulture);

    /// <summary>Gets a value indicating whether the slot is a factory preset.</summary>
    public bool IsPreset => ProgramSlots.IsPreset(Number);

    /// <summary>Gets "Preset" or "User".</summary>
    public string Kind => IsPreset ? "Preset" : "User";

    /// <summary>Gets or sets the program name.</summary>
    [ObservableProperty]
    public partial string Name { get; set; }

    /// <summary>Gets or sets the loaded algorithms, as names.</summary>
    [ObservableProperty]
    public partial string Algorithms { get; set; }

    /// <summary>Gets or sets the sort flags.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CategoriesText))]
    public partial ProgramCategories Categories { get; set; }

    /// <summary>Gets the sort flags as text.</summary>
    public string CategoriesText => ProgramFields.DescribeCategories(Categories);
}
