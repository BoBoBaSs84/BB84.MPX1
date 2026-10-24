using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using BB84.MPX1.App.Services;
using BB84.MPX1.App.ViewModels.Parameters;
using BB84.MPX1.Core.Database;
using BB84.MPX1.Core.Dumps;
using BB84.MPX1.Core.Files;
using BB84.MPX1.Core.Protocol;

namespace BB84.MPX1.App.ViewModels;

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
