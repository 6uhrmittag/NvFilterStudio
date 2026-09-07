using System.Text.Json.Nodes;
using NvFilterStudio.Core.Model;

namespace NvFilterStudio.Core.Tests.Model;

/// <summary>
/// Covers snapping a UI value onto NVIDIA's step grid.
/// </summary>
/// <remarks>
/// Every mapped filter uses a step of 1, which makes every bug in here
/// invisible. These use awkward steps on purpose.
/// </remarks>
public class ControlSnapTests
{
    private static ControlEntry Control(
        double uiMin = -50, double uiMax = 250, double uiStep = 7,
        double rawMin = 0, double rawMax = 5)
    {
        // Mirrors the NightMode.fx probe that established the behaviour: a real
        // filter never has bounds like these, which is why it was chosen.
        var node = new JsonObject
        {
            ["controlType"] = "slider",
            ["displayName"] = "probe",
            ["id"] = 0,
            ["dataType"] = "float",
            ["currentValue"] = 0.0,
            ["currentValueArray"] = new JsonArray(0.0),
            ["minValue"] = rawMin,
            ["maxValue"] = rawMax,
            ["stepSize"] = 0.01,
            ["uiMinValue"] = uiMin,
            ["uiMaxValue"] = uiMax,
            ["uiStepSize"] = uiStep,
            ["currentUIValue"] = 0.0,
            ["defaultValue"] = 0.0,
        };

        return new ControlEntry(node);
    }

    [Theory]
    // The grid is anchored at uiMinValue, not at zero.
    [InlineData(48, 48)]     // already on the grid: -50 + 7*14
    [InlineData(50, 48)]     // the value that could not be restored by hand
    [InlineData(-50, -50)]   // the minimum is always a grid point
    [InlineData(-46, -43)]   // rounds to nearest, not down
    public void SnapToStep_RoundsOntoTheGrid(double input, double expected)
    {
        Assert.Equal(expected, Control().SnapToStep(input), 6);
    }

    [Fact]
    public void SnapToStep_NeverExceedsTheStatedMaximum()
    {
        // -50 + 7*42 = 244 is the last grid point at or below 250. The overlay
        // never offers 250 itself, so neither may we.
        Assert.Equal(244, Control().SnapToStep(250), 6);
        Assert.Equal(244, Control().SnapToStep(9999), 6);
    }

    [Fact]
    public void SnapToStep_ClampsBelowTheMinimum()
    {
        Assert.Equal(-50, Control().SnapToStep(-9999), 6);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    public void SnapToStep_UnusableStep_ClampsOnly(double step)
    {
        // A zero step would divide by zero, a negative one would run the grid
        // backwards. Neither justifies inventing a step, so the value is only
        // clamped.
        ControlEntry control = Control(uiStep: step);

        Assert.Equal(37, control.SnapToStep(37), 6);
        Assert.Equal(250, control.SnapToStep(9999), 6);
    }

    [Fact]
    public void SnapToStep_StepOfOne_LeavesIntegersAlone()
    {
        // The case every mapped filter is in. Snapping must not introduce
        // rounding noise into values that were already fine.
        ControlEntry control = Control(uiMin: -100, uiMax: 100, uiStep: 1, rawMin: -1, rawMax: 1);

        foreach (int value in (int[])[-100, -18, 0, 20, 37, 100])
        {
            Assert.Equal(value, control.SnapToStep(value), 9);
        }
    }

    [Fact]
    public void UiValue_SetterSnapsAndKeepsRawConsistent()
    {
        ControlEntry control = Control();

        control.UiValue = 50;

        // Snapped to 48, and the raw value must follow it rather than the 50
        // that was asked for - a record where the two disagree is what the
        // overlay would render.
        Assert.Equal(48, control.UiValue, 6);
        Assert.Equal(5.0 * (48.0 - -50.0) / (250.0 - -50.0), control.RawValue, 9);
    }
}
