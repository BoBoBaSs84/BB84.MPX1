using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;

using BB84.MPX1.App.Services;
using BB84.MPX1.Core.Database;
using BB84.MPX1.Core.Protocol;
using BB84.MPX1.Midi;
using BB84.MPX1.Midi.Editing;

namespace BB84.MPX1.App.ViewModels;

/// <summary>The control-tree browser and the page for the selected level (UI-003).</summary>
public sealed partial class ControlTreeViewModel : ObservableObject, IDeviceTab
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

    void IDeviceTab.Attach(DeviceConnection connection, TimeProvider time)
        => Load(connection.Database!, connection.Session, connection.Editor!, time);

    void IDeviceTab.Detach() => Clear();

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
