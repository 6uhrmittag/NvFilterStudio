using System.Text.Json.Nodes;

namespace NvFilterStudio.Core.Model;

/// <summary>One slider within a filter.</summary>
/// <remarks>
/// A control's stable identity is (shader file name, <see cref="Id"/>).
/// <see cref="LocalizedName"/> follows the NVIDIA App's UI language and cannot
/// be relied on — on a German install <c>Adjustments.fx</c> control 2 is
/// labelled <c>Hoogtepunten</c>, which is Dutch. Never key on it.
/// </remarks>
public sealed class ControlEntry(JsonObject node)
{
    private readonly JsonObject _node = node ?? throw new ArgumentNullException(nameof(node));

    /// <summary>Control id, unique within its filter.</summary>
    public int Id => _node["id"]?.GetValue<int>() ?? 0;

    /// <summary>Localised label as written by the overlay.</summary>
    public string LocalizedName => _node["displayName"]?.GetValue<string>() ?? $"control {Id}";

    /// <summary>Lowest value the UI offers.</summary>
    public double UiMinimum => _node["uiMinValue"]?.GetValue<double>() ?? 0;

    /// <summary>Highest value the UI offers.</summary>
    public double UiMaximum => _node["uiMaxValue"]?.GetValue<double>() ?? 100;

    /// <summary>Increment the UI steps by.</summary>
    public double UiStep => _node["uiStepSize"]?.GetValue<double>() ?? 1;

    /// <summary>Default value, in UI units.</summary>
    public double UiDefault => _node["defaultValue"]?.GetValue<double>() ?? 0;

    /// <summary>Lowest raw value the shader accepts.</summary>
    public double RawMinimum => _node["minValue"]?.GetValue<double>() ?? 0;

    /// <summary>Highest raw value the shader accepts.</summary>
    public double RawMaximum => _node["maxValue"]?.GetValue<double>() ?? 1;

    /// <summary>
    /// The value shown in the NVIDIA App.
    /// </summary>
    /// <remarks>
    /// Setting this also rewrites <c>currentValue</c> and
    /// <c>currentValueArray</c>, which are the normalised form the shader
    /// actually receives. Writing one without the others leaves the record
    /// internally inconsistent.
    /// </remarks>
    public double UiValue
    {
        get => _node["currentUIValue"]?.GetValue<double>() ?? 0;
        set
        {
            double clamped = Math.Clamp(value, UiMinimum, UiMaximum);
            double raw = ToRaw(clamped);

            _node["currentUIValue"] = clamped;
            _node["currentValue"] = raw;

            if (_node["currentValueArray"] is JsonArray)
            {
                _node["currentValueArray"] = new JsonArray(raw);
            }
        }
    }

    /// <summary>The normalised value handed to the shader.</summary>
    public double RawValue => _node["currentValue"]?.GetValue<double>() ?? 0;

    /// <summary>Restores <see cref="UiValue"/> to <see cref="UiDefault"/>.</summary>
    public void ResetToDefault() => UiValue = UiDefault;

    /// <summary>
    /// Converts a UI value to the shader's raw scale.
    /// </summary>
    /// <remarks>
    /// Every control observed so far is a plain <c>ui / 100</c>, but the
    /// mapping is computed from the control's own bounds rather than assumed,
    /// because those bounds vary: 0..100 maps to 0..1 while -100..100 maps to
    /// -1..1, and a future filter need not follow either.
    /// </remarks>
    public double ToRaw(double uiValue)
    {
        double uiSpan = UiMaximum - UiMinimum;

        // Guarding a division, so a near-zero span matters as much as an exact
        // zero: a span of 1e-300 would not equal 0 but would still produce
        // infinity. Comparing doubles with == would miss that.
        if (Math.Abs(uiSpan) < 1e-9)
        {
            return RawMinimum;
        }

        double ratio = (uiValue - UiMinimum) / uiSpan;
        return (ratio * (RawMaximum - RawMinimum)) + RawMinimum;
    }
}
