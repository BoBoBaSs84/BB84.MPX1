using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using BB84.MPX1.App.Services;
using BB84.MPX1.Core.Dumps;
using BB84.MPX1.Midi;
using BB84.MPX1.Midi.Dumps;

namespace BB84.MPX1.App.ViewModels;

/// <summary>One character cell of the 2×16 display; custom characters (codes 0–7) are drawn from their bitmaps.</summary>
public sealed partial class DisplayCellViewModel : ObservableObject
{
    /// <summary>Gets or sets the character as text (empty for custom characters).</summary>
    [ObservableProperty]
    public partial string Text { get; set; } = " ";

    /// <summary>Gets or sets a value indicating whether the cell shows a custom character.</summary>
    [ObservableProperty]
    public partial bool IsCustom { get; set; }

    /// <summary>Gets or sets the 5×8 pixels of a custom character, row by row.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<bool> Pixels { get; set; } = [];
}

/// <summary>A single front-panel LED.</summary>
/// <param name="led">The LED.</param>
public sealed partial class LedViewModel(PanelLed led) : ObservableObject
{
    /// <summary>Gets the LED.</summary>
    public PanelLed Led { get; } = led;

    /// <summary>Gets the label.</summary>
    public string Name { get; } = led switch
    {
        PanelLed.DigitalIn => "Digital in",
        PanelLed.MidiActivity => "MIDI in",
        _ => led.ToString(),
    };

    /// <summary>Gets or sets a value indicating whether the LED is lit.</summary>
    [ObservableProperty]
    public partial bool IsOn { get; set; }
}

/// <summary>A front-panel button: click = PRESS + RELEASE, hold = HOLD + RELEASE (PAR-007).</summary>
public sealed class PanelButtonViewModel
{
    /// <summary>Initializes a new instance of the <see cref="PanelButtonViewModel"/> class.</summary>
    /// <param name="button">The button.</param>
    /// <param name="owner">The front panel.</param>
    public PanelButtonViewModel(PanelButton button, FrontPanelViewModel owner)
    {
        Button = button ?? throw new ArgumentNullException(nameof(button));
        ArgumentNullException.ThrowIfNull(owner);
        ClickCommand = new AsyncRelayCommand(() => owner.ClickAsync(button), () => owner.IsAttached);
        HoldCommand = new AsyncRelayCommand(() => owner.HoldAsync(button), () => owner.IsAttached);
    }

    /// <summary>Gets the button.</summary>
    public PanelButton Button { get; }

    /// <summary>Gets the label.</summary>
    public string Name => Button.Name;

    /// <summary>Gets the click command.</summary>
    public IAsyncRelayCommand ClickCommand { get; }

    /// <summary>Gets the hold command.</summary>
    public IAsyncRelayCommand HoldCommand { get; }
}

/// <summary>
/// The Front panel tab (UI-007): mirrors the display (with custom characters) and LEDs and drives the buttons.
/// While the tab is visible it polls the Display and LED dumps every <see cref="PollInterval"/> and refreshes right
/// after each button press.
/// </summary>
public sealed partial class FrontPanelViewModel : ObservableObject, IDisposable
{
    /// <summary>Time between mirror refreshes while the tab is visible.</summary>
    public static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    private readonly IUiDispatcher _dispatcher;
    private readonly TimeProvider _time;
    private FrontPanelController? _panel;
    private ITimer? _timer;
    private byte[] _customCharacters = new byte[CustomCharacters.Size];
    private int _refreshing;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="FrontPanelViewModel"/> class.</summary>
    /// <param name="dispatcher">UI dispatcher.</param>
    /// <param name="time">Time source for polling.</param>
    public FrontPanelViewModel(IUiDispatcher dispatcher, TimeProvider time)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        for (int i = 0; i < DisplayText.Size; i++)
            (i < DisplayText.LineLength ? TopLine : BottomLine).Add(new DisplayCellViewModel());
        foreach (PanelLed led in Enum.GetValues<PanelLed>())
            Leds.Add(new LedViewModel(led));
        foreach (PanelButton button in PanelButtons.All)
            Buttons.Add(new PanelButtonViewModel(button, this));
    }

    /// <summary>Gets the top display line.</summary>
    public ObservableCollection<DisplayCellViewModel> TopLine { get; } = [];

    /// <summary>Gets the bottom display line.</summary>
    public ObservableCollection<DisplayCellViewModel> BottomLine { get; } = [];

    /// <summary>Gets the single LEDs.</summary>
    public ObservableCollection<LedViewModel> Leds { get; } = [];

    /// <summary>Gets the buttons.</summary>
    public ObservableCollection<PanelButtonViewModel> Buttons { get; } = [];

    /// <summary>Gets or sets the lit LEDs of the left headroom bar (0–6).</summary>
    [ObservableProperty]
    public partial int LeftHeadroom { get; set; }

    /// <summary>Gets or sets the lit LEDs of the right headroom bar (0–6).</summary>
    [ObservableProperty]
    public partial int RightHeadroom { get; set; }

    /// <summary>Gets or sets the 7-segment digits.</summary>
    [ObservableProperty]
    public partial string Digits { get; set; } = "   ";

    /// <summary>Gets or sets a value indicating whether a device with a front panel is attached.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand), nameof(TurnClockwiseCommand), nameof(TurnCounterClockwiseCommand), nameof(TapCommand), nameof(ToggleABCommand), nameof(ShowTextCommand))]
    public partial bool IsAttached { get; set; }

    /// <summary>Gets or sets a value indicating whether the V1.10 single-message Tap and A/B toggle exist.</summary>
    [ObservableProperty]
    public partial bool HasV110Codes { get; set; }

    /// <summary>Gets or sets a value indicating whether the tab is visible (polling runs only then).</summary>
    [ObservableProperty]
    public partial bool IsActive { get; set; }

    /// <summary>Gets or sets the top line to show on the unit.</summary>
    [ObservableProperty]
    public partial string MessageTop { get; set; } = "";

    /// <summary>Gets or sets the bottom line to show on the unit.</summary>
    [ObservableProperty]
    public partial string MessageBottom { get; set; } = "";

    /// <summary>Gets or sets the status line.</summary>
    [ObservableProperty]
    public partial string Status { get; set; } = "Connect and learn a device to use the front panel.";

    partial void OnIsAttachedChanged(bool value)
    {
        foreach (PanelButtonViewModel button in Buttons)
        {
            button.ClickCommand.NotifyCanExecuteChanged();
            button.HoldCommand.NotifyCanExecuteChanged();
        }
    }

    /// <summary>Connects the panel.</summary>
    /// <param name="panel">The controller, or <see langword="null"/> when the firmware has no Panel Button parameter.</param>
    public void Attach(FrontPanelController? panel)
    {
        _panel = panel;
        IsAttached = panel is not null;
        HasV110Codes = panel?.HasV110Codes ?? false;
        Status = panel is null ? "The learned database has no Panel Button parameter." : "Click a button; right-click or press and hold to hold it.";
        _customCharacters = new byte[CustomCharacters.Size];
        if (IsAttached && IsActive)
            _ = StartAsync();
    }

    /// <summary>Disconnects the panel and stops polling.</summary>
    public void Detach()
    {
        StopPolling();
        _panel = null;
        IsAttached = false;
        Status = "Connect and learn a device to use the front panel.";
    }

    /// <summary>Clicks a button and refreshes the mirror (PAR-007).</summary>
    /// <param name="button">The button.</param>
    public Task ClickAsync(PanelButton button) => RunAsync(p => p.ClickAsync(button), $"{button.Name}");

    /// <summary>Holds a button and refreshes the mirror (PAR-007).</summary>
    /// <param name="button">The button.</param>
    public Task HoldAsync(PanelButton button) => RunAsync(p => p.HoldAsync(button), $"{button.Name} (hold)");

    /// <summary>Turns the encoder one step clockwise.</summary>
    [RelayCommand(CanExecute = nameof(IsAttached))]
    public Task TurnClockwiseAsync() => RunAsync(p => p.TurnAsync(1), "Encoder +");

    /// <summary>Turns the encoder one step counter-clockwise.</summary>
    [RelayCommand(CanExecute = nameof(IsAttached))]
    public Task TurnCounterClockwiseAsync() => RunAsync(p => p.TurnAsync(-1), "Encoder −");

    /// <summary>Sends the V1.10 single-message Tap.</summary>
    [RelayCommand(CanExecute = nameof(IsAttached))]
    public Task TapAsync() => RunAsync(p => p.SendAsync(PanelButtons.TapV110), "Tap");

    /// <summary>Sends the V1.10 single-message A/B toggle.</summary>
    [RelayCommand(CanExecute = nameof(IsAttached))]
    public Task ToggleABAsync() => RunAsync(p => p.SendAsync(PanelButtons.ToggleABV110), "A/B");

    /// <summary>Shows <see cref="MessageTop"/> and <see cref="MessageBottom"/> on the unit's display (DMP-002).</summary>
    [RelayCommand(CanExecute = nameof(IsAttached))]
    public Task ShowTextAsync() => RunAsync(p => p.WriteDisplayAsync(MessageTop, MessageBottom), "Message sent");

    /// <summary>Reads the display and LEDs once.</summary>
    [RelayCommand(CanExecute = nameof(IsAttached))]
    public async Task RefreshAsync()
    {
        if (_panel is not { } panel || Interlocked.Exchange(ref _refreshing, 1) == 1)
            return;
        try
        {
            PanelSnapshot snapshot = await panel.ReadAsync();
            Show(snapshot);
        }
        catch (Mpx1TimeoutException)
        {
            Status = "The device did not answer the display or LED request.";
        }
        catch (ObjectDisposedException)
        {
            // The connection closed while the request was in flight.
        }
        finally
        {
            Interlocked.Exchange(ref _refreshing, 0);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _disposed = true;
        StopPolling();
    }

    partial void OnIsActiveChanged(bool value)
    {
        if (value && IsAttached)
            _ = StartAsync();
        else
            StopPolling();
    }

    private async Task StartAsync()
    {
        if (_panel is { } panel)
        {
            try
            {
                _customCharacters = await panel.ReadCustomCharactersAsync() ?? new byte[CustomCharacters.Size];
            }
            catch (Mpx1TimeoutException)
            {
                // The display still works; custom characters show as blanks.
            }
            catch (ObjectDisposedException)
            {
                // The connection closed while the request was in flight.
                return;
            }
        }
        await RefreshAsync();
        StopPolling();
        if (IsActive && IsAttached && !_disposed)
            _timer = _time.CreateTimer(_ => _dispatcher.Post(() => _ = RefreshAsync()), null, PollInterval, PollInterval);
    }

    private void StopPolling()
    {
        _timer?.Dispose();
        _timer = null;
    }

    private async Task RunAsync(Func<FrontPanelController, Task> action, string what)
    {
        if (_panel is not { } panel)
            return;
        try
        {
            await action(panel);
            Status = what;
            await RefreshAsync();
        }
        catch (Exception ex) when (ex is Mpx1TimeoutException or ArgumentException)
        {
            Status = OperationStatus.Describe(ex);
        }
        catch (ObjectDisposedException)
        {
            // The connection closed while the request was in flight.
        }
    }

    private void Show(PanelSnapshot snapshot)
    {
        for (int i = 0; i < DisplayText.Size; i++)
        {
            DisplayCellViewModel cell = i < DisplayText.LineLength ? TopLine[i] : BottomLine[i - DisplayText.LineLength];
            byte value = i < snapshot.Display.Length ? snapshot.Display[i] : (byte)' ';
            bool custom = DisplayText.IsCustom(value);
            cell.IsCustom = custom;
            cell.Text = custom ? "" : ((char)value).ToString();
            if (custom)
            {
                cell.Pixels = [.. Enumerable.Range(0, CustomCharacters.Rows * CustomCharacters.Columns)
                    .Select(p => CustomCharacters.IsSet(_customCharacters, value, p / CustomCharacters.Columns, p % CustomCharacters.Columns))];
            }
        }

        if (snapshot.Leds is { } leds)
        {
            foreach (LedViewModel led in Leds)
                led.IsOn = leds.IsOn(led.Led);
            LeftHeadroom = leds.LeftHeadroom;
            RightHeadroom = leds.RightHeadroom;
            Digits = leds.Digits3;
        }
    }
}
