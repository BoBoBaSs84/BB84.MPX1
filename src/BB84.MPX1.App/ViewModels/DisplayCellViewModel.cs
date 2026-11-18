using BB84.SourceGenerators.Attributes;

namespace BB84.MPX1.App.ViewModels;

/// <summary>One character cell of the 2×16 display; custom characters (codes 0–7) are drawn from their bitmaps.</summary>
[GenerateNotifications(propertyChanging: false, partialMethods: true, fields: false)]
public sealed partial class DisplayCellViewModel
{
    /// <summary>Initializes a new instance of the <see cref="DisplayCellViewModel"/> class.</summary>
    public DisplayCellViewModel()
    {
        // Generated partial properties cannot have initializers, so their backing fields start here.
        _text = " ";
        _pixels = [];
    }

    /// <summary>Gets or sets the character as text (empty for custom characters).</summary>
    public partial string Text { get; set; }

    /// <summary>Gets or sets a value indicating whether the cell shows a custom character.</summary>
    public partial bool IsCustom { get; set; }

    /// <summary>Gets or sets the 5×8 pixels of a custom character, row by row.</summary>
    public partial IReadOnlyList<bool> Pixels { get; set; }
}
