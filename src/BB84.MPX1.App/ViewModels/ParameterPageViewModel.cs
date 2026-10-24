using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;

using BB84.MPX1.App.ViewModels.Parameters;
using BB84.MPX1.Core.Database;
using BB84.MPX1.Core.Protocol;
using BB84.MPX1.Midi;
using BB84.MPX1.Midi.Editing;

namespace BB84.MPX1.App.ViewModels;

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
