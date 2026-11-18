using System.Collections.Immutable;

using BB84.SourceGenerators.Attributes;

using BB84.MPX1.Core.Dumps;

namespace BB84.MPX1.Core.Database;

/// <summary>System and front-panel dumps (DMP-001 to DMP-010).</summary>
[GenerateEnumeratorExtensions]
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
    /// <summary>
    /// The documented Setup dump (DMP-004, type <c>0x0128</c> in V1.00): all six setups in one, the active setup
    /// first, then stored setups 1–5.
    /// </summary>
    AllSetups,
}

/// <summary>
/// Finds system dumps in a learned database by name, because their parameter type numbers differ between
/// V1.00 and V1.10 (LRN-005). A dump is an editable node whose value is larger than a numeric parameter or
/// whose display units are "Dump" (<c>0x44</c>).
/// </summary>
public static class SystemDumps
{
    private const ushort DumpDisplayUnits = 0x44;

    /// <summary>Number of setups in an <see cref="SystemDumpKind.AllSetups"/> dump: the active setup and five stored ones.</summary>
    public const int SetupCount = 6;

    /// <summary>
    /// Gets the dumps a full backup can include (PRG-005). <see cref="SystemDumpKind.AllSetups"/> replaces the single
    /// setups when the firmware has it: it also holds the active setup, and a V1.00 unit answers every single-setup
    /// address with stored setup 1 (spec section 9, E13).
    /// </summary>
    public static ImmutableArray<SystemDumpKind> BackupKinds { get; } =
        [SystemDumpKind.AllSetups, SystemDumpKind.Setup, SystemDumpKind.GlobalPatches, SystemDumpKind.BypassControllers, SystemDumpKind.RemapControllers];

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
        SystemDumpKind.AllSetups => "All setups (active and stored)",
        _ => kind.ToStringFast(),
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

    /// <summary>
    /// Returns the layout of a dump kind for a dump of the given size: V1.00 setups are 43 bytes, V1.10 setups 44.
    /// <see langword="null"/> for front-panel dumps and sizes that match no documented layout.
    /// </summary>
    /// <param name="kind">The dump kind.</param>
    /// <param name="size">The dump size in bytes, as the learned description gives it.</param>
    public static DumpLayout? Layout(SystemDumpKind kind, int size)
        => kind == SystemDumpKind.Setup && size == DumpLayouts.SetupV100.Size
            ? DumpLayouts.SetupV100
            : Layout(kind) is { } layout && layout.Size == size ? layout : null;

    /// <summary>Returns the documented size of a dump kind (the V1.10 size where the firmware versions differ).</summary>
    /// <param name="kind">The dump kind.</param>
    public static int Size(SystemDumpKind kind) => kind switch
    {
        SystemDumpKind.Leds => LedState.Size,
        SystemDumpKind.Display => DisplayText.Size,
        SystemDumpKind.CustomCharacters => CustomCharacters.Size,
        SystemDumpKind.AllSetups => SetupCount * DumpLayouts.Setup.Size,
        _ => Layout(kind)!.Size,
    };

    /// <summary>Returns every documented size of a dump kind: V1.10 first, then V1.00 where it differs.</summary>
    /// <param name="kind">The dump kind.</param>
    public static ImmutableArray<int> Sizes(SystemDumpKind kind) => kind switch
    {
        SystemDumpKind.Setup => [DumpLayouts.Setup.Size, DumpLayouts.SetupV100.Size],
        SystemDumpKind.AllSetups => [SetupCount * DumpLayouts.Setup.Size, SetupCount * DumpLayouts.SetupV100.Size],
        _ => [Size(kind)],
    };

    /// <summary>Splits an <see cref="SystemDumpKind.AllSetups"/> dump into its six setups: active first, then stored 1–5.</summary>
    /// <param name="data">The dump bytes; 264 (V1.10) or 258 (V1.00).</param>
    /// <exception cref="ArgumentException">The size is not six setups.</exception>
    public static IReadOnlyList<ReadOnlyMemory<byte>> SplitSetups(ReadOnlyMemory<byte> data)
    {
        if (!Sizes(SystemDumpKind.AllSetups).Contains(data.Length))
            throw new ArgumentException($"{DisplayName(SystemDumpKind.AllSetups)} must be {string.Join(" or ", Sizes(SystemDumpKind.AllSetups))} bytes, got {data.Length}.", nameof(data));
        int size = data.Length / SetupCount;
        return [.. Enumerable.Range(0, SetupCount).Select(i => data.Slice(i * size, size))];
    }

    /// <summary>
    /// Returns every node of a dump kind with a documented size, in tree order; empty when the database has none.
    /// </summary>
    /// <param name="database">The learned database.</param>
    /// <param name="kind">The dump kind.</param>
    public static IReadOnlyList<ControlTreeNode> Find(ParameterDatabase database, SystemDumpKind kind)
    {
        ArgumentNullException.ThrowIfNull(database);
        ImmutableArray<int> sizes = Sizes(kind);
        return [.. database.Root.DescendantsAndSelf().Where(n => IsDump(n) && sizes.Contains(database.ValueSize(n.Type)) && Matches(n.Name, kind))];
    }

    /// <summary>Returns the dump kind a node holds, or <see langword="null"/> when it is not a known dump.</summary>
    /// <param name="database">The learned database.</param>
    /// <param name="node">The node.</param>
    public static SystemDumpKind? KindOf(ParameterDatabase database, ControlTreeNode node)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(node);
        foreach (SystemDumpKind kind in SystemDumpKindExtensions.GetValuesFast())
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

    // The unit abbreviates names to 10 characters; V1.00 names seen on hardware: SetupDump, GPatcDump, BypCtDump,
    // RemapDump, PanelLEDs, PnlDisply, CustmChar, CurChoics, PatchDump, SftRowDmp.
    private static bool Matches(string name, SystemDumpKind kind)
    {
        bool Has(string keyword) => name.Contains(keyword, StringComparison.OrdinalIgnoreCase);
        bool global = Has("Global") || Has("Glbl") || name.StartsWith("GPat", StringComparison.OrdinalIgnoreCase);
        return kind switch
        {
            SystemDumpKind.Setup or SystemDumpKind.AllSetups => Has("Setup"),
            SystemDumpKind.GlobalPatches => global,
            SystemDumpKind.BypassControllers => Has("Bypass") || Has("Byp"),
            SystemDumpKind.RemapControllers => Has("Remap"),
            SystemDumpKind.Leds => Has("LED"),
            SystemDumpKind.Display => Has("Display") || Has("Disply"),
            SystemDumpKind.CustomCharacters => Has("Custom") || Has("Char"),
            SystemDumpKind.CurrentChoices => Has("Choice") || Has("Choic"),
            SystemDumpKind.Patches => (Has("Patch") || Has("Ptch")) && !global,
            SystemDumpKind.SoftRow => Has("Soft") || Has("SftRow"),
            _ => false,
        };
    }
}
