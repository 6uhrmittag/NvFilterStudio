using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace NvFilterStudio.App.ViewModels;

/// <summary>Shows an element only when a collection is empty.</summary>
public sealed class ZeroToVisibleConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int count && count == 0 ? Visibility.Visible : Visibility.Collapsed;

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Shows an element only when a string has content.</summary>
public sealed class TextToVisibleConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Shows an element when a flag is <see langword="false"/>.</summary>
public sealed class InverseBoolToVisibleConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool flag && flag ? Visibility.Collapsed : Visibility.Visible;

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Places a marker along a track at a value's position within a range.
/// </summary>
/// <remarks>
/// Takes (track width, value, minimum, maximum) and returns a left margin. Used
/// for the slider's default-value tick, which has no layout of its own to sit
/// in — the track is a single cell, so the tick has to be offset by hand.
/// </remarks>
public sealed class RatioToMarginConverter : IMultiValueConverter
{
    /// <summary>Width of the marker, so it centres on the value rather than starting at it.</summary>
    public double MarkerWidth { get; set; } = 2;

    /// <inheritdoc />
    public object Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(values);

        if (values.Length < 4 ||
            values[0] is not double width ||
            values[1] is not double value ||
            values[2] is not double minimum ||
            values[3] is not double maximum)
        {
            return new Thickness(0);
        }

        double span = maximum - minimum;

        // Same guard as ControlEntry.ToRaw: a near-zero span is as dangerous as
        // an exact zero, and == would miss it.
        if (Math.Abs(span) < 1e-9 || !double.IsFinite(width) || width <= 0)
        {
            return new Thickness(0);
        }

        double ratio = Math.Clamp((value - minimum) / span, 0, 1);
        double offset = (ratio * width) - (MarkerWidth / 2);

        return new Thickness(Math.Clamp(offset, 0, Math.Max(0, width - MarkerWidth)), 0, 0, 0);
    }

    /// <inheritdoc />
    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Hides a range marker sitting on either end of its own range.
/// </summary>
/// <remarks>
/// A default of 0 on a 0..100 control would draw the tick on top of the track's
/// own edge, which reads as a rendering artefact rather than as information.
/// </remarks>
public sealed class RangeEndpointToVisibilityConverter : IMultiValueConverter
{
    /// <inheritdoc />
    public object Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(values);

        if (values.Length < 3 ||
            values[0] is not double value ||
            values[1] is not double minimum ||
            values[2] is not double maximum)
        {
            return Visibility.Collapsed;
        }

        bool onAnEnd = Math.Abs(value - minimum) < 1e-9 || Math.Abs(value - maximum) < 1e-9;
        return onAnEnd ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <inheritdoc />
    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
