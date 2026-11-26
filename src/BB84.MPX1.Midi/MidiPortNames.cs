namespace BB84.MPX1.Midi;

/// <summary>Builds stable port identifiers from operating-system port names (TRN-006).</summary>
public static class MidiPortNames
{
    /// <summary>Makes port names unique by appending <c> (2)</c>, <c> (3)</c> … to duplicates, in enumeration order.</summary>
    /// <param name="names">Port names in enumeration order.</param>
    /// <returns>One port per name; the identifier and the display name are the unique name.</returns>
    public static IReadOnlyList<MidiPortInfo> MakeUnique(IReadOnlyList<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);
        Dictionary<string, int> seen = new(StringComparer.Ordinal);
        List<MidiPortInfo> ports = new(names.Count);
        foreach (string name in names)
        {
            int n = seen.TryGetValue(name, out int previous) ? previous + 1 : 1;
            seen[name] = n;
            string id = n == 1 ? name : $"{name} ({n})";
            ports.Add(new MidiPortInfo(new MidiPortId(id), id));
        }
        return ports;
    }
}
