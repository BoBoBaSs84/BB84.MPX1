using BB84.MPX1.Core.Database;
using BB84.MPX1.Core.Dumps;
using BB84.MPX1.Core.Programs;
using BB84.MPX1.Core.Protocol;
using BB84.MPX1.Midi.Dumps;

namespace BB84.MPX1.Simulator.Fixtures;

/// <summary>
/// Builds a full-tree fixture from a learned database, for example the one learned from a hardware recording (SIM-002).
/// Special cases are found by name and size, like the editor finds them (LRN-005), never by type number. Values are
/// synthetic: valid setups, unassigned controllers and the deterministic programs of <see cref="DefaultFixture"/>.
/// </summary>
public static class FixtureBuilder
{
    /// <summary>Creates a fixture that answers like the unit the database was learned from.</summary>
    /// <param name="database">The learned database.</param>
    /// <param name="configuration">The unit's System Configuration.</param>
    public static SimulatorFixture FromDatabase(ParameterDatabase database, SystemConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(configuration);

        List<FixtureDescription> descriptions = [.. database.Descriptions.Values.OrderBy(d => d.Type).Select(d => new FixtureDescription(
            d.Type, d.Name, d.Size, (byte)d.Flags, d.OptionType, [.. d.Units.Select(u => new FixtureUnit(u.Minimum, u.Maximum, u.DisplayUnits))]))];
        List<FixtureNode> nodes = [.. database.Root.DescendantsAndSelf().Select(n => new FixtureNode([.. n.Address.Levels], n.Type))];
        List<FixtureController> controllers = [.. database.Algorithms
            .Where(a => (int)a.Effect >= ProgramData.EffectBlockCount)
            .Select(a => new FixtureController((byte)a.Effect, a.Name, [.. a.Types]))];

        List<FixtureValue> values = [];
        void Value(ControlTreeNode node, byte[] data) => values.Add(new FixtureValue([.. node.Address.Levels], Convert.ToHexString(data)));
        ControlTreeNode? Single(SystemDumpKind kind) => SystemDumps.Find(database, kind) is { Count: > 0 } found ? found[0] : null;

        string title = $"  MPX 1  V{configuration.MajorVersion}.{configuration.MinorVersion:D2}";
        ControlTreeNode? display = Single(SystemDumpKind.Display);
        ControlTreeNode? leds = Single(SystemDumpKind.Leds);
        if (display is not null)
            Value(display, DisplayText.Encode(title, "  Simulator"));
        if (leds is not null)
            Value(leds, DefaultFixture.InitialLeds());
        if (Single(SystemDumpKind.CustomCharacters) is { } characters)
            Value(characters, DefaultFixture.InitialCustomCharacters());
        foreach (ControlTreeNode node in SystemDumps.Find(database, SystemDumpKind.GlobalPatches))
            values.Add(new FixtureValue([.. node.Address.Levels], DefaultFixture.InitialGlobalPatchesHex));
        foreach (SystemDumpKind kind in new[] { SystemDumpKind.BypassControllers, SystemDumpKind.RemapControllers })
        {
            foreach (ControlTreeNode node in SystemDumps.Find(database, kind))
                values.Add(new FixtureValue([.. node.Address.Levels], DefaultFixture.UnassignedHex(database.ValueSize(node.Type))));
        }

        // Single setups in tree order: stored setups 1-5, then the active setup where the firmware has an address for it.
        IReadOnlyList<ControlTreeNode> singleSetups = SystemDumps.Find(database, SystemDumpKind.Setup);
        ControlTreeNode[] stored = [.. singleSetups.Take(SystemDumps.SetupCount - 1)];
        ControlTreeNode? active = singleSetups.Count >= SystemDumps.SetupCount ? singleSetups[SystemDumps.SetupCount - 1] : null;
        ControlTreeNode? allSetups = Single(SystemDumpKind.AllSetups);
        int setupSize = allSetups is not null ? database.ValueSize(allSetups.Type) / SystemDumps.SetupCount
            : singleSetups.Count > 0 ? database.ValueSize(singleSetups[0].Type) : 0;
        DumpLayout? layout = SystemDumps.Layout(SystemDumpKind.Setup, setupSize);
        FixtureSetups? setups = null;
        if (layout is not null)
        {
            byte[][] setupData = [DefaultFixture.InitialSetup(5, layout), .. Enumerable.Range(0, SystemDumps.SetupCount - 1).Select(i => DefaultFixture.InitialSetup(i, layout))];
            if (active is not null)
                Value(active, setupData[0]);
            for (int i = 0; i < stored.Length; i++)
                Value(stored[i], setupData[i + 1]);
            if (allSetups is not null)
                Value(allSetups, [.. setupData.SelectMany(s => s)]);
            // A V1.00 unit answers every stored-setup address with stored setup 1 (spec section 9, E13).
            setups = new FixtureSetups(active is null ? null : [.. active.Address.Levels], [.. stored.Select(n => (IReadOnlyList<ushort>)[.. n.Address.Levels])],
                StoredReadAsFirst: configuration is { MajorVersion: 1, MinorVersion: < 10 });
        }

        return new SimulatorFixture
        {
            MajorVersion = configuration.MajorVersion,
            MinorVersion = configuration.MinorVersion,
            BuildTime = configuration.BuildTime,
            BuildDate = configuration.BuildDate,
            ParameterTypeCount = configuration.ParameterTypeCount,
            BottomParameter = configuration.BottomParameter,
            MaxControlLevels = configuration.MaxControlLevels,
            Descriptions = descriptions,
            Nodes = nodes,
            Values = values,
            Programs = [.. Enumerable.Range(0, DatabaseDump.ProgramCount)
                .Select(i => new FixtureProgram((ushort)i, Convert.ToHexString(DefaultFixture.CreateProgram(i).ToBytes())))],
            Controllers = controllers,
            Specials = new FixtureSpecials
            {
                PanelButton = FrontPanelController.FindButton(database)?.Type,
                Display = display?.Type,
                Leds = leds?.Type,
                Patches = Single(SystemDumpKind.Patches)?.Type,
                SoftRow = Single(SystemDumpKind.SoftRow)?.Type,
                AllSetups = layout is null ? null : allSetups?.Type,
                Setups = setups,
            },
        };
    }
}
