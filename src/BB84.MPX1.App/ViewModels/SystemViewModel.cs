using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using BB84.MPX1.App.Services;
using BB84.MPX1.App.ViewModels.Parameters;
using BB84.MPX1.Core.Database;
using BB84.MPX1.Core.Dumps;
using BB84.MPX1.Core.Files;
using BB84.MPX1.Core.Programs;
using BB84.MPX1.Core.Protocol;
using BB84.MPX1.Midi;
using BB84.MPX1.Midi.Dumps;

namespace BB84.MPX1.App.ViewModels;

/// <summary>One field of a dump section, with the editor its kind needs (UI-008).</summary>
public sealed partial class DumpFieldViewModel : ObservableObject
{
    private readonly DumpSectionViewModel _section;
    private bool _applying;

    /// <summary>Initializes a new instance of the <see cref="DumpFieldViewModel"/> class.</summary>
    /// <param name="section">The owning section.</param>
    /// <param name="field">The field.</param>
    public DumpFieldViewModel(DumpSectionViewModel section, DumpField field)
    {
        _section = section ?? throw new ArgumentNullException(nameof(section));
        Field = field ?? throw new ArgumentNullException(nameof(field));
        Options = field.Kind switch
        {
            DumpFieldKind.Choice => [.. field.Choices.Select(c => new ChoiceItem(c.Value, c.Label))],
            DumpFieldKind.Controller => [new(ControllerSources.Unassigned, "None"), .. field.Sources.Select(s => new ChoiceItem(s, ControllerSources.Name(s)))],
            _ => [],
        };
    }

    /// <summary>Gets the field.</summary>
    public DumpField Field { get; }

    /// <summary>Gets the name.</summary>
    public string Name => Field.Name;

    /// <summary>Gets the help text, or the field position.</summary>
    public string Help => Field.Help ?? $"Byte {Field.Offset}, {Field.Size} byte(s)";

    /// <summary>Gets the drop-down options (choice and controller fields).</summary>
    public IReadOnlyList<ChoiceItem> Options { get; }

    /// <summary>Gets a value indicating whether the field uses a drop-down.</summary>
    public bool IsChoice => !Field.IsReadOnly && Field.Kind is DumpFieldKind.Choice or DumpFieldKind.Controller;

    /// <summary>Gets a value indicating whether the field uses a number box.</summary>
    public bool IsNumber => !Field.IsReadOnly && Field.Kind is DumpFieldKind.Number or DumpFieldKind.SignedNumber;

    /// <summary>Gets a value indicating whether the field uses a switch.</summary>
    public bool IsToggle => !Field.IsReadOnly && Field.Kind == DumpFieldKind.Toggle;

    /// <summary>Gets a value indicating whether the field uses a text box.</summary>
    public bool IsText => !Field.IsReadOnly && Field.Kind == DumpFieldKind.Text;

    /// <summary>Gets a value indicating whether the field is shown read-only.</summary>
    public bool IsReadOnly => Field.IsReadOnly;

    /// <summary>Gets or sets the selected option.</summary>
    [ObservableProperty]
    public partial ChoiceItem? Selected { get; set; }

    /// <summary>Gets or sets the number.</summary>
    [ObservableProperty]
    public partial int Number { get; set; }

    /// <summary>Gets or sets the switch state.</summary>
    [ObservableProperty]
    public partial bool IsOn { get; set; }

    /// <summary>Gets or sets the text.</summary>
    [ObservableProperty]
    public partial string Text { get; set; } = "";

    /// <summary>Gets or sets the formatted value (read-only fields) or a warning about an illegal stored value.</summary>
    [ObservableProperty]
    public partial string Display { get; set; } = "";

    /// <summary>Gets or sets a value indicating whether the stored value is illegal (DMP-011).</summary>
    [ObservableProperty]
    public partial bool IsInvalid { get; set; }

    /// <summary>Shows the field's value from the section data.</summary>
    /// <param name="data">The dump bytes.</param>
    public void Load(byte[] data)
    {
        _applying = true;
        try
        {
            Display = Field.Format(data);
            if (Field.Kind == DumpFieldKind.Text)
            {
                Text = Field.ReadText(data).TrimEnd();
                IsInvalid = false;
                return;
            }
            int value = Field.Read(data);
            IsInvalid = !Field.IsReadOnly && !Field.IsValid(value);
            Number = value;
            IsOn = value != 0;
            Selected = Options.FirstOrDefault(o => o.Value == value);
        }
        finally
        {
            _applying = false;
        }
    }

    partial void OnSelectedChanged(ChoiceItem? value)
    {
        if (!_applying && value is not null)
            _section.Edit(this, d => Field.Write(d, value.Value));
    }

    partial void OnNumberChanged(int value)
    {
        if (!_applying && IsNumber)
            _section.Edit(this, d => Field.Write(d, Math.Clamp(value, Field.Minimum, Field.Maximum)));
    }

    partial void OnIsOnChanged(bool value)
    {
        if (!_applying && IsToggle)
            _section.Edit(this, d => Field.Write(d, value ? 1 : 0));
    }

    partial void OnTextChanged(string value)
    {
        if (!_applying && IsText)
            _section.Edit(this, d => Field.WriteText(d, value ?? ""));
    }
}

/// <summary>One System tab section: a dump kind, its instances (for example the six setups) and its fields.</summary>
public sealed partial class DumpSectionViewModel : ObservableObject
{
    private readonly SystemViewModel _owner;
    private byte[] _data = [];

    /// <summary>Initializes a new instance of the <see cref="DumpSectionViewModel"/> class.</summary>
    /// <param name="owner">The System tab.</param>
    /// <param name="kind">The dump kind.</param>
    /// <param name="nodes">The dump's nodes in the learned tree.</param>
    public DumpSectionViewModel(SystemViewModel owner, SystemDumpKind kind, IReadOnlyList<ControlTreeNode> nodes)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        ArgumentNullException.ThrowIfNull(nodes);
        Kind = kind;
        Layout = SystemDumps.Layout(kind)!;
        Instances = [.. nodes.Select((n, i) => new ChoiceItem(i, InstanceName(kind, i, nodes.Count)))];
        Nodes = nodes;
        Fields = [.. Layout.Fields.Select(f => new DumpFieldViewModel(this, f))];
        SelectedInstance = Instances.Count > 0 ? Instances[0] : null;
    }

    /// <summary>Gets the dump kind.</summary>
    public SystemDumpKind Kind { get; }

    /// <summary>Gets the section title.</summary>
    public string Title => SystemDumps.DisplayName(Kind);

    /// <summary>Gets the layout.</summary>
    public DumpLayout Layout { get; }

    /// <summary>Gets the nodes.</summary>
    public IReadOnlyList<ControlTreeNode> Nodes { get; }

    /// <summary>Gets the instances (one per node).</summary>
    public IReadOnlyList<ChoiceItem> Instances { get; }

    /// <summary>Gets a value indicating whether there is more than one instance.</summary>
    public bool HasInstances => Instances.Count > 1;

    /// <summary>Gets a value indicating whether the dump can be written (Current Choices is read-only).</summary>
    public bool IsWritable => Kind != SystemDumpKind.CurrentChoices;

    /// <summary>Gets the field editors.</summary>
    public IReadOnlyList<DumpFieldViewModel> Fields { get; }

    /// <summary>Gets the current dump bytes.</summary>
    public byte[] Data => _data;

    /// <summary>Gets or sets the selected instance.</summary>
    [ObservableProperty]
    public partial ChoiceItem? SelectedInstance { get; set; }

    /// <summary>Gets or sets a value indicating whether the data was read.</summary>
    [ObservableProperty]
    public partial bool IsLoaded { get; set; }

    /// <summary>Gets or sets a value indicating whether the data differs from the unit.</summary>
    [ObservableProperty]
    public partial bool IsDirty { get; set; }

    /// <summary>Gets the selected node.</summary>
    public ControlTreeNode? Node => SelectedInstance is { } i && i.Value < Nodes.Count ? Nodes[i.Value] : null;

    /// <summary>Reads the selected instance from the unit.</summary>
    [RelayCommand]
    public Task ReadAsync() => _owner.RunAsync($"Reading {Title}…", async (dumps, ct) =>
    {
        if (Node is not { } node)
            return "Nothing to read.";
        Show(await dumps.ReadAsync(node, ct));
        return $"Read {InstanceLabel}.";
    });

    /// <summary>Writes the selected instance to the unit; setups need confirmation (CON-007).</summary>
    [RelayCommand]
    public Task WriteAsync() => _owner.RunAsync($"Writing {Title}…", async (dumps, ct) =>
    {
        if (Node is not { } node || !IsLoaded || !IsWritable)
            return "Nothing to write.";
        if (Kind == SystemDumpKind.Setup
            && !await _owner.Dialogs.ConfirmAsync("Write setup", $"Write {InstanceLabel} to the MPX 1? The unit is reconfigured immediately when the active setup changes.", "Write"))
        {
            return "Write cancelled.";
        }
        await dumps.WriteAsync(Kind, node, _data, ct);
        IsDirty = false;
        return $"Wrote {InstanceLabel}.";
    });

    /// <summary>Saves the selected instance as a <c>.syx</c> file.</summary>
    [RelayCommand]
    public async Task ExportAsync()
    {
        if (Node is not { } node || !IsLoaded)
            return;
        byte[] file = SyxFile.Write([new ParameterData(_owner.Device, _data, node.Address)]);
        string? saved = await _owner.Dialogs.SaveSyxAsync($"Export {Title}", $"{InstanceLabel}.syx", file);
        _owner.Status = saved is null ? "Export cancelled." : $"Saved {saved}.";
    }

    /// <summary>Loads a dump of this kind from a <c>.syx</c> file into the editor (not written until Write).</summary>
    [RelayCommand]
    public async Task ImportAsync()
    {
        PickedFile? file = await _owner.Dialogs.OpenSyxAsync($"Import {Title}");
        if (file is null)
            return;
        SyxContent content = SyxFile.Read(file.Contents);
        ParameterData? match = content.Dumps.FirstOrDefault(d => Node is { } n && d.Address == n.Address && d.Data.Length == Layout.Size)
            ?? content.Dumps.FirstOrDefault(d => d.Data.Length == Layout.Size && Nodes.Any(n => n.Address == d.Address));
        if (match is null)
        {
            _owner.Status = $"{file.Name} contains no {Title} dump.";
            return;
        }
        Show(match.Data.ToArray());
        IsDirty = true;
        _owner.Status = $"Loaded {Title} from {file.Name}; choose Write to send it.";
    }

    /// <summary>Applies an edit from a field.</summary>
    /// <param name="source">The field that changed.</param>
    /// <param name="change">Returns the new bytes.</param>
    public void Edit(DumpFieldViewModel source, Func<byte[], byte[]> change)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(change);
        if (!IsLoaded)
            return;
        try
        {
            _data = change(_data);
            IsDirty = true;
            source.Load(_data);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            _owner.Status = ex.Message;
        }
    }

    /// <summary>Shows dump bytes.</summary>
    /// <param name="data">The bytes.</param>
    public void Show(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        _data = data;
        foreach (DumpFieldViewModel field in Fields)
            field.Load(data);
        IsLoaded = true;
        IsDirty = false;
    }

    partial void OnSelectedInstanceChanged(ChoiceItem? value)
    {
        IsLoaded = false;
        if (value is not null && _owner.IsAttached)
            _ = ReadAsync();
    }

    private string InstanceLabel => SelectedInstance is { } i && HasInstances ? $"{Title} – {i.Label}" : Title;

    private static string InstanceName(SystemDumpKind kind, int index, int count)
        => kind == SystemDumpKind.Setup && count == 6
            ? index == 5 ? "Active setup" : $"Setup {index + 1}"
            : $"#{index + 1}";
}

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
            Status = await work(dumps, CancellationToken.None);
        }
        catch (Exception ex) when (ex is Mpx1TimeoutException or ArgumentException or IOException)
        {
            Status = OperationStatus.Describe(ex);
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
