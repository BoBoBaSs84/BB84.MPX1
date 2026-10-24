using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;

using BB84.MPX1.App.Services;
using BB84.MPX1.Core.Database;
using BB84.MPX1.Core.Protocol;
using BB84.MPX1.Midi.Dumps;

namespace BB84.MPX1.App.ViewModels;

/// <summary>
/// The System tab (UI-008): Setups (5 stored + active), Global Patches, Bypass and Remap Controllers, and the
/// read-only Current Choices, each with typed fields, Read/Write and <c>.syx</c> import/export.
/// </summary>
public sealed partial class SystemViewModel : ObservableObject, IDeviceTab
{
    private static readonly SystemDumpKind[] Kinds =
    [
        SystemDumpKind.Setup, SystemDumpKind.GlobalPatches, SystemDumpKind.BypassControllers,
        SystemDumpKind.RemapControllers, SystemDumpKind.CurrentChoices,
    ];

    private DumpService? _dumps;

    /// <summary>Initializes a new instance of the <see cref="SystemViewModel"/> class.</summary>
    /// <param name="dialogs">Confirmation and file dialogs.</param>
    public SystemViewModel(IUserDialogs dialogs) => Dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));

    /// <summary>Gets the dialogs.</summary>
    public IUserDialogs Dialogs { get; }

    /// <summary>Gets the device ID for exported files.</summary>
    public DeviceId Device { get; private set; }

    /// <summary>Gets the sections the connected firmware offers.</summary>
    public ObservableCollection<DumpSectionViewModel> Sections { get; } = [];

    /// <summary>Gets or sets the selected section.</summary>
    [ObservableProperty]
    public partial DumpSectionViewModel? SelectedSection { get; set; }

    /// <summary>Gets or sets a value indicating whether a device is attached.</summary>
    [ObservableProperty]
    public partial bool IsAttached { get; set; }

    /// <summary>Gets or sets a value indicating whether a transfer runs.</summary>
    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    /// <summary>Gets or sets the status line.</summary>
    [ObservableProperty]
    public partial string Status { get; set; } = "Connect and learn a device to edit system dumps.";

    /// <summary>Connects the tab to a learned device.</summary>
    /// <param name="dumps">The dump service.</param>
    /// <param name="device">The device ID.</param>
    public void Attach(DumpService dumps, DeviceId device)
    {
        _dumps = dumps ?? throw new ArgumentNullException(nameof(dumps));
        Device = device;
        IsAttached = false;
        Sections.Clear();
        foreach (SystemDumpKind kind in Kinds)
        {
            IReadOnlyList<ControlTreeNode> nodes = dumps.Nodes(kind);
            if (nodes.Count > 0)
                Sections.Add(new DumpSectionViewModel(this, kind, nodes));
        }
        SelectedSection = Sections.FirstOrDefault();
        IsAttached = true;
        Status = Sections.Count == 0 ? "The learned database contains no system dumps." : "Select a section; it is read from the unit.";
    }

    void IDeviceTab.Attach(DeviceConnection connection, TimeProvider time)
        => Attach(connection.Dumps!, connection.Session.Device);

    /// <summary>Disconnects the tab.</summary>
    public void Detach()
    {
        _dumps = null;
        IsAttached = false;
        SelectedSection = null;
        Sections.Clear();
        Status = "Connect and learn a device to edit system dumps.";
    }

    /// <summary>Reads the selected section when the tab is shown.</summary>
    public Task EnsureLoadedAsync()
        => SelectedSection is { IsLoaded: false } section && IsAttached ? section.ReadAsync() : Task.CompletedTask;

    /// <summary>Runs a transfer and reports its outcome in <see cref="Status"/>.</summary>
    /// <param name="busy">Status while running.</param>
    /// <param name="work">The transfer.</param>
    public async Task RunAsync(string busy, Func<DumpService, CancellationToken, Task<string>> work)
    {
        ArgumentNullException.ThrowIfNull(work);
        if (_dumps is not { } dumps)
            return;
        IsBusy = true;
        Status = busy;
        try
        {
            if (await OperationStatus.RunAsync(ct => work(dumps, ct)) is { } status && IsAttached)
                Status = status;
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnSelectedSectionChanged(DumpSectionViewModel? value)
    {
        if (value is { IsLoaded: false } && IsAttached)
            _ = value.ReadAsync();
    }
}
