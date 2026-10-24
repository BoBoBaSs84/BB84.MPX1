using System.Buffers.Binary;

using CommunityToolkit.Mvvm.ComponentModel;

using BB84.MPX1.App.Services;
using BB84.MPX1.Core.Database;
using BB84.MPX1.Core.Values;
using BB84.MPX1.Midi.Editing;

namespace BB84.MPX1.App.ViewModels.Parameters;

/// <summary>Rate editor with Hz and ratio modes (PAR-004, UI-004).</summary>
public sealed partial class RateParameterViewModel : ParameterViewModel
{
    private readonly CoalescingWriter<RateValue> _writer;

    /// <summary>Initializes a new instance of the <see cref="RateParameterViewModel"/> class.</summary>
    /// <param name="node">The tree node.</param>
    /// <param name="context">The connection context.</param>
    /// <param name="readOnlyReason">Why the parameter is read-only, or <see langword="null"/>.</param>
    public RateParameterViewModel(ControlTreeNode node, ParameterContext context, string? readOnlyReason)
        : base(node, context, readOnlyReason)
        => _writer = CreateWriter<RateValue>((v, ct) => Context.Editor.WriteAsync(Node.Address, Encode(v), ct));

    /// <summary>Gets the ratio term choices (1–24).</summary>
    public IReadOnlyList<int> RatioTerms { get; } = [.. Enumerable.Range(1, RateValue.MaxRatioTerm)];

    /// <summary>Gets or sets a value indicating whether the rate is a tempo ratio instead of a frequency.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsHertz))]
    public partial bool IsRatio { get; set; }

    /// <summary>Gets a value indicating whether the rate is a frequency.</summary>
    public bool IsHertz => !IsRatio;

    /// <summary>Gets or sets the frequency in Hz (0.00–50.00).</summary>
    [ObservableProperty]
    public partial double Hertz { get; set; }

    /// <summary>Gets or sets the ratio numerator (1–24).</summary>
    [ObservableProperty]
    public partial int Numerator { get; set; } = 1;

    /// <summary>Gets or sets the ratio denominator (1–24).</summary>
    [ObservableProperty]
    public partial int Denominator { get; set; } = 1;

    /// <summary>Gets the current value.</summary>
    public RateValue Current => IsRatio
        ? RateValue.FromRatio(Math.Clamp(Numerator, 1, RateValue.MaxRatioTerm), Math.Clamp(Denominator, 1, RateValue.MaxRatioTerm))
        : RateValue.FromCentihertz((int)Math.Round(Math.Clamp(Hertz, 0, RateValue.MaxCentihertz / 100.0) * 100));

    /// <inheritdoc/>
    public override void CancelPendingWrites() => _writer.Dispose();

    /// <inheritdoc/>
    protected override void Apply(ParameterReading reading)
    {
        if (reading.Data.Length < 2)
            return;
        RateValue rate = RateValue.FromRaw(BinaryPrimitives.ReadUInt16LittleEndian(reading.Data.Span));
        IsRatio = rate.IsRatio;
        if (rate.IsRatio)
        {
            Numerator = rate.Numerator;
            Denominator = rate.Denominator;
        }
        else
        {
            Hertz = rate.Centihertz / 100.0;
        }
    }

    partial void OnIsRatioChanged(bool value) => Submit();

    partial void OnHertzChanged(double value) => Submit();

    partial void OnNumeratorChanged(int value) => Submit();

    partial void OnDenominatorChanged(int value) => Submit();

    private void Submit()
    {
        if (IsApplying || !IsEditable || !IsLoaded)
            return;
        RateValue rate = Current;
        DisplayText = rate.ToString();
        _writer.Submit(rate);
    }

    private byte[] Encode(RateValue rate)
    {
        // The ratio flag lives in the MSB (PAR-004/005), so the value is written raw rather than range-checked.
        byte[] data = new byte[Math.Max(2, Context.Editor.Database.ValueSize(Node.Type))];
        BinaryPrimitives.WriteUInt16LittleEndian(data, rate.ToRaw());
        return data;
    }
}
