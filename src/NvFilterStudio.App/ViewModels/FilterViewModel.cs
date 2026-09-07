using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using NvFilterStudio.Core.Model;
using NvFilterStudio.Core.Share;

namespace NvFilterStudio.App.ViewModels;

/// <summary>One filter in a slot's stack.</summary>
public sealed partial class FilterViewModel : ObservableObject
{
    public FilterViewModel(FilterEntry filter, Action onChanged)
    {
        ArgumentNullException.ThrowIfNull(filter);

        Entry = filter;
        Shader = filter.Shader;
        Name = FilterNames.ForShader(filter.Shader);
        LocalizedName = filter.LocalizedName;
        Controls = [.. filter.Controls.Select(c => new ControlViewModel(filter.Shader, c, onChanged))];
    }

    /// <summary>The underlying entry, for stack operations.</summary>
    public FilterEntry Entry { get; }

    /// <summary>Shader file name.</summary>
    public string Shader { get; }

    /// <summary>Friendly name, or the shader file name when unmapped.</summary>
    public string Name { get; }

    /// <summary>Name as NVIDIA wrote it, in the App's UI language.</summary>
    public string LocalizedName { get; }

    /// <summary>Sliders belonging to this filter.</summary>
    public ObservableCollection<ControlViewModel> Controls { get; }

    /// <summary>What a screen reader announces for this filter's card.</summary>
    /// <remarks>
    /// Without an explicit name WPF falls back to <c>ToString()</c> on the bound
    /// item, so the container announces the view-model's type name before every
    /// filter in the stack. Position is included because stack order changes the
    /// image, and it is otherwise conveyed only by a number in a coloured circle.
    /// </remarks>
    public string AccessibleName =>
        string.Create(CultureInfo.CurrentCulture, $"{Name} ({Shader}), position {Order + 1}");

    /// <summary>Position in the stack, shown to make the ordering explicit.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AccessibleName))]
    private int _order;

    /// <summary>Whether this is the first filter, so "move up" can be disabled.</summary>
    [ObservableProperty]
    private bool _isFirst;

    /// <summary>Whether this is the last filter.</summary>
    [ObservableProperty]
    private bool _isLast;

    /// <summary>
    /// True when this filter's controls are not fully mapped to friendly names.
    /// </summary>
    /// <remarks>
    /// Surfaced in the UI so a generic "control 0" reads as a known gap rather
    /// than a bug. The values are still correct — only the labels are missing.
    /// </remarks>
    public bool HasUnmappedControls =>
        !FilterNames.ControlsByShader.ContainsKey(Shader);

    /// <summary>Restores every control to its NVIDIA default.</summary>
    public void ResetAll()
    {
        foreach (ControlViewModel control in Controls)
        {
            control.Reset();
        }
    }
}
