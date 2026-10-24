using CommunityToolkit.Mvvm.ComponentModel;

using BB84.MPX1.Core.Database;

namespace BB84.MPX1.App.ViewModels;

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
