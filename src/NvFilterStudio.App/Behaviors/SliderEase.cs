using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Animation;

namespace NvFilterStudio.App.Behaviors;

/// <summary>
/// Eases a slider's thumb into place when its value changes from somewhere
/// other than the slider itself — undo, reset, import, a pasted share code.
/// </summary>
/// <remarks>
/// Purely cosmetic, and deliberately so. Undo used to teleport every slider in
/// the stack at once, which made it hard to see what had actually been undone;
/// a short glide answers "what just changed" without a status message.
/// <para>
/// The awkward part is that <see cref="Slider.Value"/> is bound two-way to the
/// view model, and the view model writes each set through to the store record.
/// Animating the bound property naively would therefore perform one store-bound
/// edit per animation frame. This works because a WPF animation supplies the
/// property's *effective* value without ever writing back to the binding source:
/// the source is set once, normally, and the animation only replays the journey.
/// <see cref="FillBehavior.Stop"/> then hands control back to the binding, which
/// already holds the destination.
/// </para>
/// </remarks>
public static class SliderEase
{
    /// <summary>How long the glide takes. Zero or unset disables it.</summary>
    public static readonly DependencyProperty DurationProperty =
        DependencyProperty.RegisterAttached(
            "Duration",
            typeof(Duration),
            typeof(SliderEase),
            new PropertyMetadata(new Duration(TimeSpan.Zero), OnDurationChanged));

    /// <summary>Tracks the value we last saw, so the animation knows where to start from.</summary>
    private static readonly DependencyProperty PreviousValueProperty =
        DependencyProperty.RegisterAttached(
            "PreviousValue", typeof(double), typeof(SliderEase), new PropertyMetadata(double.NaN));

    /// <summary>True between thumb drag start and end.</summary>
    private static readonly DependencyProperty IsDraggingProperty =
        DependencyProperty.RegisterAttached(
            "IsDragging", typeof(bool), typeof(SliderEase), new PropertyMetadata(false));

    /// <summary>Gets the glide duration.</summary>
    public static Duration GetDuration(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (Duration)element.GetValue(DurationProperty);
    }

    /// <summary>Sets the glide duration.</summary>
    public static void SetDuration(DependencyObject element, Duration value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(DurationProperty, value);
    }

    private static void OnDurationChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is not Slider slider)
        {
            return;
        }

        // A template reload can re-apply the attached property, and a duplicate
        // handler would animate twice.
        slider.ValueChanged -= OnValueChanged;
        slider.RemoveHandler(Thumb.DragStartedEvent, new DragStartedEventHandler(OnDragStarted));
        slider.RemoveHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler(OnDragCompleted));

        if (e.NewValue is Duration { HasTimeSpan: true, TimeSpan.Ticks: > 0 })
        {
            slider.SetValue(PreviousValueProperty, slider.Value);
            slider.ValueChanged += OnValueChanged;
            slider.AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler(OnDragStarted));
            slider.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler(OnDragCompleted));
        }
    }

    private static void OnDragStarted(object sender, DragStartedEventArgs e) =>
        ((DependencyObject)sender).SetValue(IsDraggingProperty, true);

    private static void OnDragCompleted(object sender, DragCompletedEventArgs e)
    {
        var slider = (Slider)sender;
        slider.SetValue(IsDraggingProperty, false);
        slider.SetValue(PreviousValueProperty, slider.Value);
    }

    private static void OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        var slider = (Slider)sender;

        // The base value is what the binding holds, ignoring any animation. It
        // is the only way to tell a real change from one of our own animation's
        // frames, which raise this event identically. Comparing e.NewValue
        // instead would either chase every frame or - if guarded by a flag -
        // ignore a change that arrives mid-flight, leaving a stale animation to
        // finish travelling to a target nobody wants any more.
        double target = (double)slider.GetAnimationBaseValue(RangeBase.ValueProperty);
        double previous = (double)slider.GetValue(PreviousValueProperty);

        if (Math.Abs(target - previous) < 1e-9)
        {
            return;
        }

        slider.SetValue(PreviousValueProperty, target);

        // Dragging must stay exactly 1:1 with the pointer. Easing it would make
        // the thumb lag the cursor, which reads as the app being slow.
        if ((bool)slider.GetValue(IsDraggingProperty))
        {
            slider.BeginAnimation(RangeBase.ValueProperty, null);
            return;
        }

        // e.OldValue, not slider.Value: by the time this fires the property
        // already holds the new value, so reading it would animate from the
        // destination to itself and do nothing. OldValue is also correct when a
        // glide is interrupted, because it is then the last animated frame -
        // exactly where the thumb is on screen.
        double from = e.OldValue;

        if (Math.Abs(from - target) < 1e-9)
        {
            return;
        }

        var animation = new DoubleAnimation
        {
            From = from,
            To = target,
            Duration = GetDuration(slider),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },

            // Hands the property back to the binding, which already holds the
            // destination value. Without this the animation would keep owning
            // the property and later binding updates would be ignored.
            FillBehavior = FillBehavior.Stop,
        };

        slider.BeginAnimation(RangeBase.ValueProperty, animation);
    }
}
