using BB84.SourceGenerators.Attributes;

namespace BB84.MPX1.App.ViewModels;

/// <summary>One sort flag checkbox.</summary>
[GenerateNotifications(propertyChanging: false, partialMethods: true, fields: false)]
public sealed partial class CategoryFlagViewModel(string name, int bit, bool isSource)
{
    /// <summary>Gets the flag name.</summary>
    public string Name { get; } = name;

    /// <summary>Gets the bit value.</summary>
    public int Bit { get; } = bit;

    /// <summary>Gets a value indicating whether this is a source (input) flag rather than an effect flag.</summary>
    public bool IsSource { get; } = isSource;

    /// <summary>Gets or sets a value indicating whether the flag is set.</summary>
    public partial bool IsChecked { get; set; }
}
