using BB84.SourceGenerators.Attributes;

using BB84.MPX1.App.Services;
using BB84.MPX1.App.ViewModels.Parameters;
using BB84.MPX1.Core.Database;
using BB84.MPX1.Core.Dumps;
using BB84.MPX1.Core.Files;
using BB84.MPX1.Core.Protocol;

namespace BB84.MPX1.App.ViewModels;

/// <summary>One System tab section: a dump kind, its instances (for example the six setups) and its fields.</summary>
[GenerateNotifications(propertyChanging: false, partialMethods: true, fields: false)]
public sealed partial class DumpSectionViewModel
{
    private readonly SystemViewModel _owner;
    private readonly ControlTreeNode? _allSetups;
    private byte[] _data = [];

    /// <summary>Initializes a new instance of the <see cref="DumpSectionViewModel"/> class.</summary>
    /// <param name="owner">The System tab.</param>
    /// <param name="kind">The dump kind.</param>
    /// <param name="nodes">The dump's nodes in the learned tree.</param>
    public DumpSectionViewModel(SystemViewModel owner, SystemDumpKind kind, IReadOnlyList<ControlTreeNode> nodes)
        : this(owner, kind, nodes, allSetups: null)
    { }

    private DumpSectionViewModel(SystemViewModel owner, SystemDumpKind kind, IReadOnlyList<ControlTreeNode> nodes, ControlTreeNode? allSetups)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        ArgumentNullException.ThrowIfNull(nodes);
        Kind = kind;
        _allSetups = allSetups;
        if (allSetups is not null)
        {
            Layout = SystemDumps.Layout(SystemDumpKind.Setup, allSetups.Description.Size / SystemDumps.SetupCount)!;
            Instances = [.. Enumerable.Range(0, SystemDumps.SetupCount).Select(i => new ChoiceItem(i, i == 0 ? "Active setup" : $"Setup {i}"))];
            Nodes = [allSetups];
        }
        else
        {
            Layout = (nodes.Count > 0 ? SystemDumps.Layout(kind, nodes[0].Description.Size) : null) ?? SystemDumps.Layout(kind)!;
            Instances = [.. nodes.Select((n, i) => new ChoiceItem(i, InstanceName(kind, i, nodes.Count)))];
            Nodes = nodes;
        }
        Fields = [.. Layout.Fields.Select(f => new DumpFieldViewModel(this, f))];
        SelectedInstance = Instances.Count > 0 ? Instances[0] : null;
    }

    /// <summary>
    /// Creates the Setups section over the all-setups dump: the active setup and stored setups 1–5 (DMP-004). Used when
    /// the firmware has it, because a V1.00 unit answers every single-setup address with stored setup 1 (spec 9, E13).
    /// </summary>
    /// <param name="owner">The System tab.</param>
    /// <param name="allSetups">The all-setups dump node.</param>
    public static DumpSectionViewModel ForAllSetups(SystemViewModel owner, ControlTreeNode allSetups)
        => new(owner, SystemDumpKind.Setup, [], allSetups ?? throw new ArgumentNullException(nameof(allSetups)));

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
    public partial ChoiceItem? SelectedInstance { get; set; }

    /// <summary>Gets or sets a value indicating whether the data was read.</summary>
    public partial bool IsLoaded { get; set; }

    /// <summary>Gets or sets a value indicating whether the data differs from the unit.</summary>
    public partial bool IsDirty { get; set; }

    /// <summary>Gets the selected node.</summary>
    public ControlTreeNode? Node => _allSetups ?? (SelectedInstance is { } i && i.Value < Nodes.Count ? Nodes[i.Value] : null);

    private int Setup => SelectedInstance?.Value ?? 0;

    /// <summary>Reads the selected instance from the unit.</summary>
    [ActionCommand]
    public Task ReadAsync() => _owner.RunAsync($"Reading {Title}…", async (dumps, ct) =>
    {
        if (Node is not { } node)
            return "Nothing to read.";
        byte[] data = await dumps.ReadAsync(node, ct);
        Show(_allSetups is null ? data : SystemDumps.SplitSetups(data)[Setup].ToArray());
        return $"Read {InstanceLabel}.";
    });

    /// <summary>Writes the selected instance to the unit; setups need confirmation (CON-007).</summary>
    [ActionCommand]
    public Task WriteAsync() => _owner.RunAsync($"Writing {Title}…", async (dumps, ct) =>
    {
        if (Node is not { } node || !IsLoaded || !IsWritable)
            return "Nothing to write.";
        if (Kind == SystemDumpKind.Setup
            && !await _owner.Dialogs.ConfirmAsync("Write setup", $"Write {InstanceLabel} to the MPX 1? The unit is reconfigured immediately when the active setup changes.", "Write"))
        {
            return "Write cancelled.";
        }
        if (_allSetups is null)
        {
            await dumps.WriteAsync(Kind, node, _data, ct);
        }
        else
        {
            // Read the other five setups fresh, so only the edited one changes.
            byte[] all = await dumps.ReadAsync(node, ct);
            await dumps.WriteAsync(SystemDumpKind.AllSetups, node, WithSetup(all, _data), ct);
        }
        IsDirty = false;
        return $"Wrote {InstanceLabel}.";
    });

    /// <summary>Saves the selected instance as a <c>.syx</c> file.</summary>
    [ActionCommand]
    public async Task ExportAsync()
    {
        if (Node is not { } node || !IsLoaded)
            return;
        // The all-setups dump is exported whole, from the unit, with the edited setup in place.
        byte[] data = _allSetups is null ? _data : WithSetup(await _owner.ReadAllSetupsAsync(node) ?? [], _data);
        if (data.Length == 0)
            return;
        byte[] file = SyxFile.Write([new ParameterData(_owner.Device, data, node.Address)]);
        string? saved = await _owner.Dialogs.SaveSyxAsync($"Export {Title}", $"{InstanceLabel}.syx", file);
        _owner.Status = saved is null ? "Export cancelled." : $"Saved {saved}.";
    }

    /// <summary>Loads a dump of this kind from a <c>.syx</c> file into the editor (not written until Write).</summary>
    [ActionCommand]
    public async Task ImportAsync()
    {
        PickedFile? file = await _owner.Dialogs.OpenSyxAsync($"Import {Title}");
        if (file is null)
            return;
        SyxContent content = SyxFile.Read(file.Contents);
        byte[]? imported = _allSetups is { } all
            ? content.Dumps.FirstOrDefault(d => d.Address == all.Address && d.Data.Length == Layout.Size * SystemDumps.SetupCount) is { } combined
                ? SystemDumps.SplitSetups(combined.Data)[Setup].ToArray()
                : null
            : (content.Dumps.FirstOrDefault(d => Node is { } n && d.Address == n.Address && d.Data.Length == Layout.Size)
                ?? content.Dumps.FirstOrDefault(d => d.Data.Length == Layout.Size && Nodes.Any(n => n.Address == d.Address)))?.Data.ToArray();
        if (imported is null)
        {
            _owner.Status = $"{file.Name} contains no {Title} dump.";
            return;
        }
        Show(imported);
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

    private byte[] WithSetup(byte[] all, byte[] setup)
    {
        byte[] copy = [.. all];
        setup.CopyTo(copy, Setup * setup.Length);
        return copy;
    }

    private string InstanceLabel => SelectedInstance is { } i && HasInstances ? $"{Title} – {i.Label}" : Title;

    private static string InstanceName(SystemDumpKind kind, int index, int count)
        => kind == SystemDumpKind.Setup && count is 5 or 6
            ? index == 5 ? "Active setup" : $"Setup {index + 1}"
            : $"#{index + 1}";
}
