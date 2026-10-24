using System.Text;

using BB84.MPX1.Core.Database;
using BB84.MPX1.Midi.Editing;

namespace BB84.MPX1.App.ViewModels.Parameters;

/// <summary>Read-only view of a dump or other parameter that has no editor yet.</summary>
public sealed class DumpParameterViewModel(ControlTreeNode node, ParameterContext context, string readOnlyReason)
    : ParameterViewModel(node, context, readOnlyReason)
{
    private const int PreviewBytes = 48;
    private string _preview = "";

    /// <summary>Gets a hex preview of the data.</summary>
    public string Preview
    {
        get => _preview;
        private set => SetProperty(ref _preview, value);
    }

    /// <inheritdoc/>
    protected override void Apply(ParameterReading reading)
    {
        ReadOnlySpan<byte> data = reading.Data.Span;
        StringBuilder sb = new(Convert.ToHexString(data[..Math.Min(data.Length, PreviewBytes)]));
        for (int i = sb.Length - 2; i > 0; i -= 2)
            sb.Insert(i, ' ');
        if (data.Length > PreviewBytes)
            sb.Append(" …");
        Preview = $"{data.Length} bytes: {sb}";
    }
}
