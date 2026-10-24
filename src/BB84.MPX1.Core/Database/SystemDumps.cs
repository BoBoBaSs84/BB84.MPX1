using System.Collections.Immutable;

using BB84.MPX1.Core.Dumps;

namespace BB84.MPX1.Core.Database;

/// <summary>System and front-panel dumps (DMP-001 to DMP-010).</summary>
public enum SystemDumpKind
{
    /// <summary>Setup dumps: five stored setups and the active one (DMP-004).</summary>
    Setup,
    /// <summary>Global Patches dump (DMP-005).</summary>
    GlobalPatches,
    /// <summary>Bypass Controllers dump (DMP-006).</summary>
    BypassControllers,
    /// <summary>Remap Controllers dump (DMP-007).</summary>
    RemapControllers,
    /// <summary>All LEDs dump (DMP-001).</summary>
    Leds,
    /// <summary>Display dump (DMP-002).</summary>
    Display,
    /// <summary>Custom Character Bitmap dump (DMP-003).</summary>
    CustomCharacters,
    /// <summary>Current Choices dump (DMP-008).</summary>
    CurrentChoices,
    /// <summary>Patches dump of the active program (DMP-009).</summary>
    Patches,
    /// <summary>Soft Row dump of the active program (DMP-010).</summary>
    SoftRow,
}

/// <summary>
/// Finds system dumps in a learned database by name, because their parameter type numbers differ between
/// V1.00 and V1.10 (LRN-005). A dump is an editable node whose value is larger than a numeric parameter or
/// whose display units are "Dump" (<c>0x44</c>).
/// </summary>
public static class SystemDumps
{
    private const ushort DumpDisplayUnits = 0x44;

    /// <summary>Gets the dumps a full backup can include (PRG-005).</summary>
    public static ImmutableArray<SystemDumpKind> BackupKinds { get; } =
        [SystemDumpKind.Setup, SystemDumpKind.GlobalPatches, SystemDumpKind.BypassControllers, SystemDumpKind.RemapControllers];

    /// <summary>Gets the display name of a dump kind.</summary>
    /// <param name="kind">The dump kind.</param>
    public static string DisplayName(SystemDumpKind kind) => kind switch
    {
        SystemDumpKind.Setup => "Setups",
        SystemDumpKind.GlobalPatches => "Global Patches",
        SystemDumpKind.BypassControllers => "Bypass Controllers",
        SystemDumpKind.RemapControllers => "Remap Controllers",
        SystemDumpKind.Leds => "LEDs",
        SystemDumpKind.CustomCharacters => "Custom Characters",
        SystemDumpKind.CurrentChoices => "Current Choices",
        SystemDumpKind.SoftRow => "Soft Row",
        _ => kind.ToString(),
    };

    /// <summary>Returns the documented field layout of a dump kind, or <see langword="null"/> for front-panel dumps.</summary>
    /// <param name="kind">The dump kind.</param>
    public static DumpLayout? Layout(SystemDumpKind kind) => kind switch
    {
        SystemDumpKind.Setup => DumpLayouts.Setup,
        SystemDumpKind.GlobalPatches => DumpLayouts.GlobalPatches,
        SystemDumpKind.BypassControllers => DumpLayouts.BypassControllers,
        SystemDumpKind.RemapControllers => DumpLayouts.RemapControllers,
        SystemDumpKind.CurrentChoices => DumpLayouts.CurrentChoices,
        SystemDumpKind.Patches => DumpLayouts.Patches,
        SystemDumpKind.SoftRow => DumpLayouts.SoftRow,
        _ => null,
    };

    /// <summary>Returns the documented size of a dump kind.</summary>
    /// <param name="kind">The dump kind.</param>
    public static int Size(SystemDumpKind kind) => kind switch
    {
        SystemDumpKind.Leds => LedState.Size,
        SystemDumpKind.Display => DisplayText.Size,
        SystemDumpKind.CustomCharacters => CustomCharacters.Size,
        _ => Layout(kind)!.Size,
    };

    /// <summary>
    /// Returns every node of a dump kind with the documented size, in tree order; empty when the database has none.
    /// </summary>
    /// <param name="database">The learned database.</param>
    /// <param name="kind">The dump kind.</param>
    public static IReadOnlyList<ControlTreeNode> Find(ParameterDatabase database, SystemDumpKind kind)
    {
        ArgumentNullException.ThrowIfNull(database);
        int size = Size(kind);
        return [.. database.Root.DescendantsAndSelf().Where(n => IsDump(n) && database.ValueSize(n.Type) == size && Matches(n.Name, kind))];
    }

    /// <summary>Returns the dump kind a node holds, or <see langword="null"/> when it is not a known dump.</summary>
    /// <param name="database">The learned database.</param>
    /// <param name="node">The node.</param>
    public static SystemDumpKind? KindOf(ParameterDatabase database, ControlTreeNode node)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(node);
        foreach (SystemDumpKind kind in Enum.GetValues<SystemDumpKind>())
        {
            if (Find(database, kind).Contains(node))
                return kind;
        }
        return null;
    }

    /// <summary>Returns <see langword="true"/> when a node holds a dump rather than a numeric parameter.</summary>
    /// <param name="node">The node.</param>
    public static bool IsDump(ControlTreeNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return node.IsEditable
            && (node.Description.Size > 2 || node.Description.Units.Any(u => u.DisplayUnits == DumpDisplayUnits));
    }

    private static bool Matches(string name, SystemDumpKind kind)
    {
        bool Has(string keyword) => name.Contains(keyword, StringComparison.OrdinalIgnoreCase);
        bool global = Has("Global") || Has("Glbl");
        return kind switch
        {
            SystemDumpKind.Setup => Has("Setup"),
            SystemDumpKind.GlobalPatches => global,
            SystemDumpKind.BypassControllers => Has("Bypass") || Has("Byp"),
            SystemDumpKind.RemapControllers => Has("Remap"),
            SystemDumpKind.Leds => Has("LED"),
            SystemDumpKind.Display => Has("Display"),
            SystemDumpKind.CustomCharacters => Has("Custom") || Has("Char"),
            SystemDumpKind.CurrentChoices => Has("Choice"),
            SystemDumpKind.Patches => (Has("Patch") || Has("Ptch")) && !global,
            SystemDumpKind.SoftRow => Has("Soft"),
            _ => false,
        };
    }
}
