using System.Text.Json.Nodes;

namespace NvFilterStudio.Core.Model;

/// <summary>How a control is presented and stored.</summary>
public enum ControlKind
{
    /// <summary>A numeric slider with bounds, a step and a raw mapping.</summary>
    Slider,

    /// <summary>An on/off toggle, stored as a JSON boolean with no bounds.</summary>
    Boolean,
}

/// <summary>One control within a filter — a slider or an on/off toggle.</summary>
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

    /// <summary>Whether this is a slider or an on/off toggle.</summary>
    /// <remarks>
    /// Boolean controls store <c>currentValue</c> as a JSON <c>true</c>/<c>false</c>
    /// and carry none of the numeric fields — no <c>minValue</c>, no
    /// <c>uiMinValue</c>, no <c>currentUIValue</c>, no <c>defaultValue</c>.
    /// Treating one as a slider throws on read and, worse, replaces the boolean
    /// with a float on write, destroying the control.
    /// </remarks>
    public ControlKind Kind =>
        Text("controlType") is "boolean" || Text("dataType") is "bool"
            ? ControlKind.Boolean
            : ControlKind.Slider;

    /// <summary>On/off state of a <see cref="ControlKind.Boolean"/> control.</summary>
    /// <remarks>Setting this on a slider would corrupt it, so it is ignored there.</remarks>
    public bool BoolValue
    {
        get => _node["currentValue"] is JsonValue value && value.TryGetValue(out bool flag) && flag;
        set
        {
            if (Kind != ControlKind.Boolean)
            {
                return;
            }

            _node["currentValue"] = value;

            if (_node["currentValueArray"] is JsonArray)
            {
                _node["currentValueArray"] = new JsonArray(value);
            }
        }
    }

    /// <summary>Reads a numeric field, or null when it is absent or not a number.</summary>
    /// <remarks>
    /// <c>GetValue&lt;double&gt;()</c> throws on a JSON boolean rather than
    /// returning a default, which is how a single toggle in one filter took the
    /// whole document down.
    /// </remarks>
    private double? Number(string field) =>
        _node[field] is JsonValue value && value.TryGetValue(out double number) ? number : null;

    private string? Text(string field) =>
        _node[field] is JsonValue value && value.TryGetValue(out string? text) ? text?.ToLowerInvariant() : null;

    /// <summary>Lowest value the UI offers.</summary>
    public double UiMinimum => Number("uiMinValue") ?? 0;

    /// <summary>Highest value the UI offers.</summary>
    public double UiMaximum => Number("uiMaxValue") ?? (Kind == ControlKind.Boolean ? 1 : 100);

    /// <summary>Increment the UI steps by.</summary>
    public double UiStep => Number("uiStepSize") ?? 1;

    /// <summary>Default value, in UI units.</summary>
    public double UiDefault => Number("defaultValue") ?? 0;

    /// <summary>Lowest raw value the shader accepts.</summary>
    public double RawMinimum => Number("minValue") ?? 0;

    /// <summary>Highest raw value the shader accepts.</summary>
    public double RawMaximum => Number("maxValue") ?? 1;

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
        get => Kind == ControlKind.Boolean
            ? (BoolValue ? 1 : 0)
            : Number("currentUIValue") ?? 0;
        set
        {
            // A boolean control has no UI scale to snap to and no numeric
            // fields to write. Going through the slider path here would replace
            // its JSON true with a float and bolt on bounds it never had.
            if (Kind == ControlKind.Boolean)
            {
                BoolValue = value != 0;
                return;
            }

            double snapped = SnapToStep(value);
            double raw = ToRaw(snapped);

            _node["currentUIValue"] = snapped;
            _node["currentValue"] = raw;

            if (_node["currentValueArray"] is JsonArray)
            {
                _node["currentValueArray"] = new JsonArray(raw);
            }
        }
    }

    /// <summary>Rounds a UI value onto the grid NVIDIA's own slider uses.</summary>
    /// <remarks>
    /// The overlay snaps a slider to <c>uiMinValue + k * uiStepSize</c>, and the
    /// reachable maximum is the last grid point at or below <c>uiMaxValue</c> — a
    /// control declaring -50..250 step 7 topped out at 244. A value off that grid
    /// displays correctly but jumps to the nearest grid point the moment the user
    /// touches the slider, and cannot then be restored from NVIDIA's own UI. So
    /// writing one is a quiet, one-way way to lose the value.
    /// <para>
    /// Every mapped filter uses a step of 1, which makes this invisible for them.
    /// It matters for imports, pasted share codes and typed values, and for any
    /// filter whose real step is not 1.
    /// </para>
    /// </remarks>
    public double SnapToStep(double uiValue)
    {
        double clamped = Math.Clamp(uiValue, UiMinimum, UiMaximum);
        double step = UiStep;

        // A non-positive or non-finite step would divide by zero or run the grid
        // backwards. Treat that as "no grid" rather than inventing a step.
        if (!double.IsFinite(step) || step <= 0)
        {
            return clamped;
        }

        double snapped = UiMinimum + (Math.Round((clamped - UiMinimum) / step) * step);

        // Rounding can land one step above the maximum, which the overlay never
        // offers as a grid point.
        return snapped > UiMaximum ? snapped - step : snapped;
    }

    /// <summary>The normalised value handed to the shader.</summary>
    /// <remarks>
    /// A boolean control reports 1 or 0, so callers that only deal in numbers
    /// still get something meaningful. Read <see cref="BoolValue"/> for the
    /// faithful value. Reading the field as a double used to throw outright on
    /// any store containing a boolean control.
    /// </remarks>
    public double RawValue => Kind == ControlKind.Boolean
        ? (BoolValue ? 1 : 0)
        : Number("currentValue") ?? 0;

    /// <summary>Restores <see cref="UiValue"/> to <see cref="UiDefault"/>.</summary>
    /// <remarks>
    /// Boolean controls are left alone: the store records no <c>defaultValue</c>
    /// for them, and picking one would silently change a setting the user chose.
    /// </remarks>
    public void ResetToDefault()
    {
        if (Kind != ControlKind.Boolean)
        {
            UiValue = UiDefault;
        }
    }

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
