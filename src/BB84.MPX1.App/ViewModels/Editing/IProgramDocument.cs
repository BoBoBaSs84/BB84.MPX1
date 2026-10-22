using BB84.MPX1.Core.Database;
using BB84.MPX1.Core.Programs;

namespace BB84.MPX1.App.ViewModels.Editing;

/// <summary>The program being edited, as seen by the editor sections.</summary>
public interface IProgramDocument
{
    /// <summary>Gets the current program.</summary>
    ProgramData Program { get; }

    /// <summary>Gets the learned database, or <see langword="null"/> when no device was ever learned.</summary>
    ParameterDatabase? Database { get; }

    /// <summary>Applies a change to the program (marks it dirty and auditions it).</summary>
    /// <param name="change">Returns the changed program.</param>
    void Update(Func<ProgramData, ProgramData> change);
}
