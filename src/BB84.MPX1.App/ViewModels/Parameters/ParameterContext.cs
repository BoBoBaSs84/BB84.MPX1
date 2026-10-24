using BB84.MPX1.Midi;
using BB84.MPX1.Midi.Editing;

namespace BB84.MPX1.App.ViewModels.Parameters;

/// <summary>What a parameter editor needs from the live connection.</summary>
/// <param name="Session">The session.</param>
/// <param name="Editor">The parameter editor.</param>
/// <param name="ActiveAlgorithms">The active program's algorithm numbers, Pitch through Delay.</param>
/// <param name="Time">Time source for throttling.</param>
public sealed record ParameterContext(Mpx1Session Session, ParameterEditor Editor, IReadOnlyList<byte> ActiveAlgorithms, TimeProvider Time);
