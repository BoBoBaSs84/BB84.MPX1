using CommunityToolkit.Mvvm.Input;

using BB84.MPX1.App.Services;
using BB84.MPX1.Core.Programs;
using BB84.MPX1.Core.Protocol;
using BB84.MPX1.Midi;
using BB84.MPX1.Midi.Library;

namespace BB84.MPX1.App.ViewModels.Editing;

/// <summary>Device side: connection, audition and slot transfers.</summary>
public sealed partial class ProgramEditorViewModel
{
    /// <summary>Minimum time between audition dumps; a Program Dump takes about 270 ms on a MIDI cable.</summary>
    public static readonly TimeSpan AuditionInterval = TimeSpan.FromMilliseconds(300);

    /// <summary>Gets a value indicating whether auditioning is possible.</summary>
    public bool CanAudition => IsConnected;

    /// <summary>Connects or disconnects the device used for audition, receive and store.</summary>
    /// <param name="librarian">The librarian, or <see langword="null"/> when offline.</param>
    /// <param name="device">The device ID.</param>
    public void SetConnection(ProgramLibrarian? librarian, DeviceId device)
    {
        _audition?.Dispose();
        _audition = null;
        _librarian = librarian;
        _device = device;
        IsConnected = librarian is not null;
        if (librarian is not null)
        {
            _audition = new CoalescingWriter<ProgramData>(
                (program, ct) => librarian.SendToActiveAsync(program, ct),
                _ => Task.CompletedTask,
                ex => Status = ex is Mpx1TimeoutException ? "The device did not answer the audition dump." : ex.Message,
                _time, AuditionInterval, TimeSpan.Zero);
        }
    }

    void IDeviceTab.Attach(DeviceConnection connection, TimeProvider time)
    {
        SetDatabase(connection.Database);
        SetConnection(connection.Librarian, connection.Session.Device);
    }

    // The database stays after disconnecting so files can still be edited offline (UI-010).
    void IDeviceTab.Detach() => SetConnection(null, default);

    /// <summary>Gets the audition writer, for tests.</summary>
    internal CoalescingWriter<ProgramData>? Audition => _audition;

    /// <summary>Reads a slot from the device and opens it.</summary>
    /// <param name="number">Program number 0–249.</param>
    public Task OpenSlotAsync(ushort number) => RunAsync($"Reading program {number:D3}…", async (librarian, ct) =>
    {
        ProgramData program = await librarian.ReceiveAsync(number, ct);
        Load(program, $"Slot {number:D3} \"{program.Name.TrimEnd()}\"", number);
        return $"Opened {number:D3}.";
    });

    /// <summary>Opens the active program of the device.</summary>
    [RelayCommand(CanExecute = nameof(CanTransfer))]
    public Task ReceiveActiveAsync() => RunAsync("Reading the active program…", async (librarian, ct) =>
    {
        ProgramData program = await librarian.ReceiveAsync(Request.ActiveProgram, ct);
        Load(program, $"Active program \"{program.Name.TrimEnd()}\"", Request.ActiveProgram);
        return "Opened the active program.";
    });

    /// <summary>Stores the program in <see cref="StoreTarget"/>; presets need confirmation (PRG-004).</summary>
    [RelayCommand(CanExecute = nameof(CanTransfer))]
    public Task StoreAsync() => RunAsync("Storing…", async (librarian, ct) =>
    {
        ushort target = (ushort)Math.Clamp(StoreTarget, 0, ProgramSlots.Count - 1);
        if (ProgramSlots.IsPreset(target)
            && !await _dialogs.ConfirmAsync("Overwrite factory preset", $"Store \"{Program.Name.TrimEnd()}\" in preset {target:D3}? Factory presets can only be restored from a backup.", "Overwrite"))
        {
            return "Store cancelled.";
        }
        await librarian.StoreAsync(target, Program, presetConfirmed: true, ct);
        _original = Program;
        _sourceNumber = target;
        Source = $"Slot {target:D3} \"{Program.Name.TrimEnd()}\"";
        IsDirty = false;
        Stored?.Invoke(this, (target, Program));
        return $"Stored in {target:D3}.";
    });

    /// <summary>Sends the program to the active program without storing it.</summary>
    [RelayCommand(CanExecute = nameof(CanTransfer))]
    public Task SendToActiveAsync() => RunAsync("Sending…", async (librarian, ct) =>
    {
        await librarian.SendToActiveAsync(Program, ct);
        return "Sent to the active program.";
    });

    partial void OnIsAuditionEnabledChanged(bool value)
    {
        if (value && IsDirty)
            _audition?.Submit(Program);
    }

    private bool CanTransfer() => IsConnected && !IsBusy;

    private async Task RunAsync(string busy, Func<ProgramLibrarian, CancellationToken, Task<string>> work)
    {
        if (_librarian is not { } librarian)
            return;
        IsBusy = true;
        Status = busy;
        try
        {
            Status = await work(librarian, CancellationToken.None);
        }
        catch (Exception ex) when (ex is Mpx1TimeoutException or PresetOverwriteException or ArgumentException)
        {
            Status = OperationStatus.Describe(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
