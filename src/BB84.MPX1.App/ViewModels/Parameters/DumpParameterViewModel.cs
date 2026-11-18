using System.Text;

using BB84.SourceGenerators.Attributes;

using BB84.MPX1.Core.Database;
using BB84.MPX1.Midi.Editing;

namespace BB84.MPX1.App.ViewModels.Parameters;

/// <summary>Read-only view of a dump or other parameter that has no editor yet.</summary>
[GenerateNotifications(propertyChanging: false, partialMethods: true, fields: false)]
public sealed partial class DumpParameterViewModel : ParameterViewModel
{
    private const int PreviewBytes = 48;

    /// <summary>Initializes a new instance of the <see cref="DumpParameterViewModel"/> class.</summary>
    /// <param name="node">The tree node.</param>
    /// <param name="context">The connection context.</param>
    /// <param name="readOnlyReason">Why the parameter must not be written.</param>
    public DumpParameterViewModel(ControlTreeNode node, ParameterContext context, string readOnlyReason)
        : base(node, context, readOnlyReason)
    {
        // Generated partial properties cannot have initializers, so their backing fields start here.
        _preview = "";
    }

    /// <summary>Gets a hex preview of the data.</summary>
    public partial string Preview { get; private set; }

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
