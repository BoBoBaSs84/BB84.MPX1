namespace BB84.MPX1.Midi.Tests;

[TestClass]
public sealed class MidiPortNamesTests
{
    [TestMethod]
    [Description("TRN-006: duplicate device names get stable, unique IDs")]
    public void MakeUnique_MakesDuplicateNamesUnique()
    {
        IReadOnlyList<MidiPortInfo> ports = MidiPortNames.MakeUnique(["USB MIDI", "Synth", "USB MIDI", "USB MIDI"]);

        Assert.AreSequenceEqual(
            new[] { "USB MIDI", "Synth", "USB MIDI (2)", "USB MIDI (3)" }, ports.Select(p => p.Id.Value).ToArray());
        Assert.IsTrue(ports.All(p => p.Name == p.Id.Value));
    }
}
