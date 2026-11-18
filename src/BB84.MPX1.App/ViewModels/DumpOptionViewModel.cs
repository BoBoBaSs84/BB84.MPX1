using BB84.SourceGenerators.Attributes;

using BB84.MPX1.Core.Database;

namespace BB84.MPX1.App.ViewModels;

/// <summary>A system dump the user can include in a backup (PRG-005).</summary>
[GenerateNotifications(propertyChanging: false, partialMethods: true, fields: false)]
public sealed partial class DumpOptionViewModel
{
    /// <summary>Initializes a new instance of the <see cref="DumpOptionViewModel"/> class.</summary>
    /// <param name="kind">The dump kind.</param>
    public DumpOptionViewModel(SystemDumpKind kind)
    {
        // Generated partial properties cannot have initializers, so their backing fields start here.
        _isChecked = true;
        Kind = kind;
        Name = SystemDumps.DisplayName(kind);
    }

    /// <summary>Gets the dump kind.</summary>
    public SystemDumpKind Kind { get; }

    /// <summary>Gets the display name.</summary>
    public string Name { get; }

    /// <summary>Gets or sets a value indicating whether the dump is included.</summary>
    public partial bool IsChecked { get; set; }
}
