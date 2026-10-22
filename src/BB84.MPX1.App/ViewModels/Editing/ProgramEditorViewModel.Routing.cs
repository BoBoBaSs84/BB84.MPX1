using System.Collections.ObjectModel;

using BB84.MPX1.Core.Programs;
using BB84.MPX1.Core.Protocol;

namespace BB84.MPX1.App.ViewModels.Editing;

/// <summary>Audio routing table.</summary>
public sealed partial class ProgramEditorViewModel
{
    /// <summary>Gets the routing table rows.</summary>
    public ObservableCollection<RoutingRowViewModel> RoutingRows { get; } = [];

    /// <summary>Gets the routing map: input, six effects, output on upper/lower lanes.</summary>
    public IReadOnlyList<RoutingSlot> RoutingMap => AudioRouting.Layout(Program.Routing);

    private void RebuildRouting()
    {
        RoutingRows.Clear();
        IReadOnlyList<RoutingSlot> slots = AudioRouting.Layout(Program.Routing);
        foreach (RoutingSlot slot in slots)
        {
            IReadOnlyList<RoutingMode> modes = slot.Position < AudioRouting.BlockCount - 1
                ? AudioRouting.AllowedModes(Program.Routing, slot.Position, _maxMixingBlocks)
                : [(RoutingMode)slot.Block.Routing];
            RoutingRows.Add(new RoutingRowViewModel(slot, modes, OnRoutingRowChanged));
        }
        OnPropertyChanged(nameof(RoutingMap));
    }

    private void OnRoutingRowChanged(RoutingRowViewModel row)
    {
        switch (row.LastChange)
        {
            case nameof(RoutingRowViewModel.SelectedEffect) when row.SelectedEffect is { } effect:
                Update(p => p with { Routing = AudioRouting.PlaceEffect(p.Routing, row.Position, (RoutingBlockType)effect.Value) });
                break;
            case nameof(RoutingRowViewModel.SelectedMode) when row.SelectedMode is { } mode:
                if (!AudioRouting.AllowedModes(Program.Routing, row.Position, _maxMixingBlocks).Contains((RoutingMode)mode.Value))
                    return;
                Update(p => p with { Routing = AudioRouting.SetMode(p.Routing, row.Position, (RoutingMode)mode.Value, _maxMixingBlocks) });
                RoutingWarning = null;
                break;
            case nameof(RoutingRowViewModel.SelectedUpperInput) or nameof(RoutingRowViewModel.SelectedLowerInput):
                Update(p => p with
                {
                    Routing = AudioRouting.SetInputs(p.Routing, row.Position,
                        (InputConnection)(row.SelectedUpperInput?.Value ?? 0), (InputConnection)(row.SelectedLowerInput?.Value ?? 0)),
                });
                return; // Lanes do not change.
            default:
                return;
        }
        RebuildRouting();
    }
}
