using System.Collections.ObjectModel;

using BB84.SourceGenerators.Attributes;

using BB84.MPX1.Core.Programs;
using BB84.MPX1.Core.Protocol;

namespace BB84.MPX1.App.ViewModels;

/// <summary>Sort flags of the selected slot and the Database Dump write.</summary>
public sealed partial class LibrarianViewModel
{
    private bool _loadingFlags;

    /// <summary>Gets the sort flag checkboxes for the selected slot.</summary>
    public ObservableCollection<CategoryFlagViewModel> Flags { get; } = [];

    /// <summary>Gets the effect sort flags.</summary>
    public IEnumerable<CategoryFlagViewModel> EffectFlags => Flags.Where(f => !f.IsSource);

    /// <summary>Gets the source sort flags.</summary>
    public IEnumerable<CategoryFlagViewModel> SourceFlags => Flags.Where(f => f.IsSource);

    /// <summary>Gets or sets a value indicating whether sort flags were changed but not written.</summary>
    [NotifyCanExecuteChanged(nameof(WriteFlagsCommand))]
    public partial bool HasUnsavedFlags { get; set; }

    /// <summary>Writes the edited sort flags of all slots (Database Dump).</summary>
    [ActionCommand(CanExecute = nameof(CanWriteFlags))]
    public Task WriteFlagsAsync() => RunAsync("Writing sort flags…", async (librarian, _, ct) =>
    {
        IReadOnlyList<ushort> unset = await librarian.WriteDatabaseAsync([.. Slots.Select(s => s.Categories)], ct);
        HasUnsavedFlags = false;
        return $"Sort flags written.{KeptFlags(unset)}";
    });

    partial void OnSelectedSlotChanged(ProgramSlotViewModel? value)
    {
        RenameText = value?.Name ?? "";
        _loadingFlags = true;
        try
        {
            foreach (CategoryFlagViewModel flag in Flags)
            {
                flag.IsChecked = value is not null && (flag.IsSource
                    ? ((int)value.Categories.Sources & flag.Bit) != 0
                    : ((int)value.Categories.Effects & flag.Bit) != 0);
            }
        }
        finally
        {
            _loadingFlags = false;
        }
    }

    private void OnFlagChanged()
    {
        if (_loadingFlags || SelectedSlot is not { } slot)
            return;
        int effects = Flags.Where(f => !f.IsSource && f.IsChecked).Sum(f => f.Bit);
        int sources = Flags.Where(f => f.IsSource && f.IsChecked).Sum(f => f.Bit);
        ProgramCategories categories = new((EffectCategories)effects, (SourceCategories)sources);
        if (categories != slot.Categories)
        {
            slot.Categories = categories;
            HasUnsavedFlags = true;
        }
    }

    // A V1.00 unit cannot store every sort flag it receives (spec section 9, E18).
    private static string KeptFlags(IReadOnlyList<ushort> unset)
        => unset.Count == 0 ? "" : $" The MPX 1 kept different sort flags for {string.Join(", ", unset.Select(n => ProgramSlots.DisplayNumber(n)))}.";

    private bool CanWriteFlags() => CanRun() && IsLoaded && HasUnsavedFlags;
}
