using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using NvFilterStudio.Core.Model;
using NvFilterStudio.Core.Share;

namespace NvFilterStudio.App.ViewModels;

/// <summary>One slider, bound to a control in the underlying document.</summary>
public sealed partial class ControlViewModel : ObservableObject
{
    private readonly ControlEntry _control;
    private readonly Action _onChanged;

    public ControlViewModel(string shader, ControlEntry control, Action onChanged)
    {
        ArgumentNullException.ThrowIfNull(control);

        _control = control;
        _onChanged = onChanged;
        _value = control.UiValue;

        Name = FilterNames.ForControl(shader, control.Id);
        LocalizedName = control.LocalizedName;
        Minimum = control.UiMinimum;
        Maximum = control.UiMaximum;
        Step = control.UiStep <= 0 ? 1 : control.UiStep;
        Default = control.UiDefault;
    }

    /// <summary>English name where the control is mapped, else "control N".</summary>
    public string Name { get; }

    /// <summary>The label NVIDIA wrote, in the App's UI language.</summary>
    public string LocalizedName { get; }

    /// <summary>
    /// Shown beside the name when the two differ, so a German or Dutch label is
    /// still recognisable next to the English one.
    /// </summary>
    public string? SecondaryLabel =>
        string.Equals(Name, LocalizedName, StringComparison.OrdinalIgnoreCase) ? null : LocalizedName;

    /// <summary>What a screen reader announces for this slider's row.</summary>
    /// <remarks>
    /// The slider itself reports its own value and range; this names the row it
    /// sits in, which would otherwise fall back to the view-model's type name.
    /// </remarks>
    public string AccessibleName =>
        SecondaryLabel is { Length: > 0 } secondary ? $"{Name}, {secondary}" : Name;

    public double Minimum { get; }

    public double Maximum { get; }

    public double Step { get; }

    public double Default { get; }

    [ObservableProperty]
    private double _value;

    /// <summary>The value as text, so it can be typed exactly rather than dragged towards.</summary>
    public string ValueText
    {
        get => Value.ToString("0.##", CultureInfo.InvariantCulture);
        set
        {
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
            {
                Value = Math.Clamp(parsed, Minimum, Maximum);
            }

            // A rejected entry falls back to the current value on the next
            // notification rather than clearing what the user typed over.
            OnPropertyChanged();
        }
    }

    /// <summary>Whether this differs from the filter's default.</summary>
    public bool IsModified => Math.Abs(Value - Default) > 0.0001;

    partial void OnValueChanged(double value)
    {
        // Writing through the model keeps currentValue and currentValueArray in
        // step with currentUIValue; setting one alone leaves the record
        // internally inconsistent.
        _control.UiValue = value;

        OnPropertyChanged(nameof(ValueText));
        OnPropertyChanged(nameof(IsModified));
        _onChanged();
    }

    /// <summary>Restores the NVIDIA default for this control.</summary>
    public void Reset() => Value = Default;
}
