using CommunityToolkit.Mvvm.ComponentModel;

namespace BB84.MPX1.App.ViewModels;

/// <summary>One character cell of the 2×16 display; custom characters (codes 0–7) are drawn from their bitmaps.</summary>
public sealed partial class DisplayCellViewModel : ObservableObject
{
    /// <summary>Gets or sets the character as text (empty for custom characters).</summary>
    [ObservableProperty]
    public partial string Text { get; set; } = " ";

    /// <summary>Gets or sets a value indicating whether the cell shows a custom character.</summary>
    [ObservableProperty]
    public partial bool IsCustom { get; set; }

    /// <summary>Gets or sets the 5×8 pixels of a custom character, row by row.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<bool> Pixels { get; set; } = [];
}
