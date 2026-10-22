using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;

using BB84.MPX1.App.ViewModels.Parameters;
using BB84.MPX1.Core.Database;
using BB84.MPX1.Core.Protocol;
using BB84.MPX1.Midi;
using BB84.MPX1.Midi.Editing;

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

/// <summary>The parameters directly below one control level, read from the device.</summary>
public sealed partial class ParameterPageViewModel : ObservableObject, IDisposable
{
    private readonly CancellationTokenSource _cancel = new();
    private bool _closed;

    private ParameterPageViewModel(ControlTreeNode node)
    {
        Node = node;
        Title = PathOf(node);
    }

    /// <summary>Gets the control level.</summary>
    public ControlTreeNode Node { get; }

    /// <summary>Gets the breadcrumb title.</summary>
    public string Title { get; }

    /// <summary>Gets the parameter editors.</summary>
    public ObservableCollection<ParameterViewModel> Parameters { get; } = [];

    /// <summary>Gets or sets a notice shown above the editors, or <see langword="null"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotice))]
    public partial string? Notice { get; set; }

    /// <summary>Gets a value indicating whether <see cref="Notice"/> is set.</summary>
    public bool HasNotice => Notice is not null;

    /// <summary>Gets or sets a value indicating whether values are being read.</summary>
    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    /// <summary>Gets the task that reads all values, for tests.</summary>
    public Task Loading { get; private set; } = Task.CompletedTask;

    /// <summary>Creates the page and starts reading the values.</summary>
    /// <param name="node">The control level.</param>
    /// <param name="session">The session.</param>
    /// <param name="editor">The parameter editor.</param>
    /// <param name="time">Time source for live writes.</param>
    public static ParameterPageViewModel Open(ControlTreeNode node, Mpx1Session session, ParameterEditor editor, TimeProvider time)
    {
        ParameterPageViewModel page = new(node);
        page.Loading = page.LoadAsync(session, editor, time, page._cancel.Token);
        return page;
    }

    /// <summary>Stops reading and drops unsent edits.</summary>
    public void Dispose()
    {
        if (_closed)
            return;
        _closed = true;
        _cancel.Cancel();
        foreach (ParameterViewModel parameter in Parameters)
            parameter.CancelPendingWrites();
        _cancel.Dispose();
    }

    /// <summary>Re-reads a parameter the device reported as changed (SES-009).</summary>
    /// <param name="address">The control address.</param>
    public async Task OnDeviceChangedAsync(ControlAddress address)
    {
        ParameterViewModel? parameter = Parameters.FirstOrDefault(p => p.Node.Address == address);
        if (parameter is not null && !_closed)
            await parameter.LoadAsync(_cancel.Token);
    }

    private async Task LoadAsync(Mpx1Session session, ParameterEditor editor, TimeProvider time, CancellationToken cancellationToken)
    {
        IsLoading = true;
        try
        {
            // The LRN-008 guard needs the algorithms that are loaded right now; they may have changed on the unit.
            IReadOnlyList<byte> algorithms = Node.Address.Depth >= 2 && Node.Address[0] == 0
                ? await editor.RefreshActiveAlgorithmsAsync(cancellationToken)
                : [];
            ParameterContext context = new(session, editor, algorithms, time);
            foreach (ControlTreeNode child in Node.Children.Where(c => c.IsEditable))
                Parameters.Add(ParameterEditorFactory.Create(child, context));

            if (Parameters.Count > 0 && Parameters[0].ReadOnlyReason is string reason && Parameters.All(p => p.ReadOnlyReason == reason))
            {
                Notice = reason;
                foreach (ParameterViewModel parameter in Parameters)
                    parameter.IsReasonShownByPage = true;
            }

            foreach (ParameterViewModel parameter in Parameters)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await parameter.LoadAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Page closed.
        }
        catch (Mpx1TimeoutException)
        {
            Notice = "The device did not answer while reading this page.";
        }
        catch (ObjectDisposedException)
        {
            // The connection closed while the page was loading.
        }
        finally
        {
            IsLoading = false;
        }
    }

    private static string PathOf(ControlTreeNode node)
    {
        Stack<string> names = new();
        for (ControlTreeNode? n = node; n?.Parent is not null; n = n.Parent)
            names.Push(n.Name);
        return names.Count == 0 ? node.Name : string.Join(" › ", names);
    }
}

/// <summary>The control-tree browser and the page for the selected level (UI-003).</summary>
public sealed partial class ControlTreeViewModel : ObservableObject
{
    private Mpx1Session? _session;
    private ParameterEditor? _editor;
    private TimeProvider _time = TimeProvider.System;

    /// <summary>Gets the top-level control levels.</summary>
    public ObservableCollection<ControlNodeViewModel> Roots { get; } = [];

    /// <summary>Gets or sets the selected control level.</summary>
    [ObservableProperty]
    public partial ControlNodeViewModel? SelectedNode { get; set; }

    /// <summary>Gets or sets the page of the selected level.</summary>
    [ObservableProperty]
    public partial ParameterPageViewModel? Page { get; set; }

    /// <summary>Shows the tree of a learned database.</summary>
    /// <param name="database">The database.</param>
    /// <param name="session">The session.</param>
    /// <param name="editor">The parameter editor.</param>
    /// <param name="time">Time source for live writes.</param>
    public void Load(ParameterDatabase database, Mpx1Session session, ParameterEditor editor, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(database);
        Clear();
        _session = session;
        _editor = editor;
        _time = time;
        foreach (ControlTreeNode child in database.Root.Children.Where(c => !c.IsEditable))
            Roots.Add(new ControlNodeViewModel(child));
    }

    /// <summary>Removes the tree, for example after disconnecting.</summary>
    public void Clear()
    {
        SelectedNode = null;
        Page?.Dispose();
        Page = null;
        Roots.Clear();
        _session = null;
        _editor = null;
    }

    /// <summary>Forwards a device-side change to the open page.</summary>
    /// <param name="address">The control address.</param>
    public Task OnDeviceChangedAsync(ControlAddress address) => Page?.OnDeviceChangedAsync(address) ?? Task.CompletedTask;

    partial void OnSelectedNodeChanged(ControlNodeViewModel? value)
    {
        Page?.Dispose();
        Page = value is not null && _session is not null && _editor is not null
            ? ParameterPageViewModel.Open(value.Node, _session, _editor, _time)
            : null;
    }
}
