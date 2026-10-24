using CommunityToolkit.Mvvm.ComponentModel;

using BB84.MPX1.App.ViewModels.Parameters;
using BB84.MPX1.Core.Dumps;
using BB84.MPX1.Core.Programs;

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
