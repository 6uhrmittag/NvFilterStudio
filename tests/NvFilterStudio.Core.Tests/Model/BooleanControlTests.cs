using System.Text.Json;
using System.Text.Json.Nodes;
using NvFilterStudio.Core.Model;

namespace NvFilterStudio.Core.Tests.Model;

/// <summary>
/// Covers on/off controls, which carry none of the numeric fields.
/// </summary>
/// <remarks>
/// Shape taken from <c>BeautifyDOF.fx</c> control 2 in a real store. Note what
/// is absent: no <c>minValue</c>, <c>maxValue</c>, <c>stepSize</c>,
/// <c>uiMinValue</c>, <c>uiMaxValue</c>, <c>uiStepSize</c>, <c>currentUIValue</c>
/// or <c>defaultValue</c>. Every one of those is assumed by the slider path.
/// </remarks>
public class BooleanControlTests
{
    private static JsonObject BooleanNode(bool value = true) => new()
    {
        ["controlType"] = "boolean",
        ["displayName"] = "Z-Achse umkehren",
        ["currentValueArray"] = new JsonArray(value),
        ["id"] = 2,
        ["dataType"] = "bool",
        ["dimension"] = 0,
        ["currentValue"] = value,
    };

    private static JsonObject SliderNode() => new()
    {
        ["controlType"] = "slider",
        ["displayName"] = "Intensität",
        ["currentValueArray"] = new JsonArray(0.5),
        ["id"] = 1,
        ["dataType"] = "float",
        ["minValue"] = 0.0,
        ["maxValue"] = 1.0,
        ["stepSize"] = 0.01,
        ["currentValue"] = 0.5,
        ["uiMinValue"] = 0.0,
        ["uiMaxValue"] = 100.0,
        ["uiStepSize"] = 1.0,
        ["currentUIValue"] = 50.0,
        ["defaultValue"] = 50.0,
    };

    [Fact]
    public void Kind_DistinguishesBooleanFromSlider()
    {
        Assert.Equal(ControlKind.Boolean, new ControlEntry(BooleanNode()).Kind);
        Assert.Equal(ControlKind.Slider, new ControlEntry(SliderNode()).Kind);
    }

    [Fact]
    public void RawValue_DoesNotThrowOnABoolean()
    {
        // Reading currentValue as a double threw outright, which took down the
        // whole document because one filter in it had a toggle.
        Assert.Equal(1, new ControlEntry(BooleanNode(true)).RawValue);
        Assert.Equal(0, new ControlEntry(BooleanNode(false)).RawValue);
    }

    [Fact]
    public void UiValue_ReportsOneOrZero()
    {
        Assert.Equal(1, new ControlEntry(BooleanNode(true)).UiValue);
        Assert.Equal(0, new ControlEntry(BooleanNode(false)).UiValue);
    }

    [Fact]
    public void UiValue_SetterKeepsItAJsonBoolean()
    {
        // The corruption case. Going through the slider path would write a
        // float here, and the overlay would then be reading a number where its
        // shader expects a bool.
        var node = BooleanNode(true);
        var control = new ControlEntry(node);

        control.UiValue = 0;

        Assert.Equal(JsonValueKind.False, node["currentValue"]!.GetValueKind());
        Assert.Equal(JsonValueKind.False, node["currentValueArray"]![0]!.GetValueKind());
        Assert.False(control.BoolValue);
    }

    [Fact]
    public void UiValue_SetterInventsNoNumericFields()
    {
        // Bolting uiMinValue and friends onto a boolean would be writing fields
        // NVIDIA never puts there, against the "preserve, do not invent" rule.
        var node = BooleanNode();
        var control = new ControlEntry(node);

        control.UiValue = 1;

        foreach (string absent in (string[])
                 ["minValue", "maxValue", "stepSize", "uiMinValue",
                  "uiMaxValue", "uiStepSize", "currentUIValue", "defaultValue"])
        {
            Assert.False(node.ContainsKey(absent), $"{absent} was invented");
        }
    }

    [Fact]
    public void ResetToDefault_LeavesABooleanAlone()
    {
        // There is no defaultValue to restore. Picking one would silently
        // change a setting the user chose.
        var node = BooleanNode(true);
        var control = new ControlEntry(node);

        control.ResetToDefault();

        Assert.True(control.BoolValue);
        Assert.Equal(JsonValueKind.True, node["currentValue"]!.GetValueKind());
    }

    [Fact]
    public void BoolValue_SetterIgnoresASlider()
    {
        // The mirror of the corruption guard: a bool must never land in a
        // numeric control either.
        var node = SliderNode();
        var control = new ControlEntry(node);

        control.BoolValue = true;

        Assert.Equal(JsonValueKind.Number, node["currentValue"]!.GetValueKind());
        Assert.Equal(0.5, control.RawValue);
    }

    [Fact]
    public void IntSlider_RoundTripsThroughTheGenericMapping()
    {
        // Letterbox.fx control 0: dataType "int", bounds 1..30 with the raw and
        // UI scales identical, so the mapping must come out as an identity.
        var node = new JsonObject
        {
            ["controlType"] = "slider",
            ["displayName"] = "Horizontale Skala",
            ["currentValueArray"] = new JsonArray(21),
            ["id"] = 0,
            ["dataType"] = "int",
            ["minValue"] = 1.0,
            ["maxValue"] = 30.0,
            ["stepSize"] = 1.0,
            ["currentValue"] = 21,
            ["uiMinValue"] = 1.0,
            ["uiMaxValue"] = 30.0,
            ["uiStepSize"] = 1.0,
            ["currentUIValue"] = 21,
            ["defaultValue"] = 21,
        };

        var control = new ControlEntry(node);
        Assert.Equal(ControlKind.Slider, control.Kind);

        control.UiValue = 14;

        Assert.Equal(14, control.UiValue);
        Assert.Equal(14, control.RawValue);
    }
}
