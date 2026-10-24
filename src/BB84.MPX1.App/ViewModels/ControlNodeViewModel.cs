using BB84.MPX1.Core.Database;

namespace BB84.MPX1.App.ViewModels;

/// <summary>A control level in the tree browser (UI-003). Leaves are shown on the parameter page instead.</summary>
public sealed class ControlNodeViewModel
{
    /// <summary>Initializes a new instance of the <see cref="ControlNodeViewModel"/> class.</summary>
    /// <param name="node">The control-level node.</param>
    public ControlNodeViewModel(ControlTreeNode node)
    {
        Node = node ?? throw new ArgumentNullException(nameof(node));
        Children = [.. node.Children.Where(c => !c.IsEditable).Select(c => new ControlNodeViewModel(c))];
        ParameterCount = node.Children.Count(c => c.IsEditable);
    }

    /// <summary>Gets the tree node.</summary>
    public ControlTreeNode Node { get; }

    /// <summary>Gets the name.</summary>
    public string Name => Node.Name;

    /// <summary>Gets the child control levels.</summary>
    public IReadOnlyList<ControlNodeViewModel> Children { get; }

    /// <summary>Gets the number of parameters directly below this level.</summary>
    public int ParameterCount { get; }

    /// <summary>Gets the address, type and dead-end note, for tooltips.</summary>
    public string Details => Node.IsDeadEnd
        ? $"{Node.Address} · not browsed (Appendix D bug 2)"
        : $"{Node.Address} · {ParameterCount} parameter(s)";
}
