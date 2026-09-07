using System.Globalization;
using System.Text;
using NvFilterStudio.Core.Model;

namespace NvFilterStudio.Core.Share;

/// <summary>What one filter would gain, lose or change.</summary>
/// <param name="Shader">Shader file name.</param>
/// <param name="Name">Friendly name, for display.</param>
/// <param name="Change">Added, removed, moved or edited.</param>
/// <param name="Detail">Human-readable specifics, empty when there are none.</param>
public sealed record FilterChange(string Shader, string Name, FilterChangeKind Change, string Detail);

/// <summary>Kind of change to a filter in a slot.</summary>
public enum FilterChangeKind
{
    /// <summary>Not in the slot before.</summary>
    Added,

    /// <summary>In the slot before, not after.</summary>
    Removed,

    /// <summary>Present both times, at a different stack position.</summary>
    Moved,

    /// <summary>Present both times, with different values.</summary>
    Changed,

    /// <summary>Present both times and identical.</summary>
    Unchanged,
}

/// <summary>
/// Compares two versions of a slot so a change can be reviewed before it is made.
/// </summary>
/// <remarks>
/// Written for imports and pasted share codes, which replace a whole stack in
/// one step. The stack being replaced may have taken a long time to tune, and
/// "apply and see" is a poor way to find out what a stranger's code does.
/// </remarks>
public static class SlotDiff
{
    /// <summary>Compares <paramref name="before"/> against <paramref name="after"/>.</summary>
    public static IReadOnlyList<FilterChange> Compare(Slot before, Slot after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        IReadOnlyList<FilterEntry> old = before.Filters;
        IReadOnlyList<FilterEntry> current = after.Filters;

        var changes = new List<FilterChange>();

        foreach (FilterEntry entry in old)
        {
            FilterEntry? match = current.FirstOrDefault(
                f => string.Equals(f.Shader, entry.Shader, StringComparison.OrdinalIgnoreCase));

            if (match is null)
            {
                changes.Add(new FilterChange(
                    entry.Shader, FilterNames.ForShader(entry.Shader), FilterChangeKind.Removed, string.Empty));
                continue;
            }

            string values = DescribeValueChanges(entry, match);

            if (values.Length > 0)
            {
                changes.Add(new FilterChange(
                    entry.Shader, FilterNames.ForShader(entry.Shader), FilterChangeKind.Changed, values));
            }
            else if (entry.StackIndex != match.StackIndex)
            {
                changes.Add(new FilterChange(
                    entry.Shader,
                    FilterNames.ForShader(entry.Shader),
                    FilterChangeKind.Moved,
                    string.Create(CultureInfo.InvariantCulture,
                        $"position {entry.StackIndex} → {match.StackIndex}")));
            }
            else
            {
                changes.Add(new FilterChange(
                    entry.Shader, FilterNames.ForShader(entry.Shader), FilterChangeKind.Unchanged, string.Empty));
            }
        }

        foreach (FilterEntry entry in current)
        {
            if (!old.Any(f => string.Equals(f.Shader, entry.Shader, StringComparison.OrdinalIgnoreCase)))
            {
                changes.Add(new FilterChange(
                    entry.Shader, FilterNames.ForShader(entry.Shader), FilterChangeKind.Added, string.Empty));
            }
        }

        return changes;
    }

    /// <summary>
    /// Renders a comparison for a confirmation prompt, or an empty string when
    /// nothing would change.
    /// </summary>
    public static string Describe(IReadOnlyList<FilterChange> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);

        List<FilterChange> notable = [.. changes.Where(c => c.Change != FilterChangeKind.Unchanged)];
        if (notable.Count == 0)
        {
            return string.Empty;
        }

        var text = new StringBuilder();

        foreach (FilterChange change in notable)
        {
            string verb = change.Change switch
            {
                FilterChangeKind.Added => "add",
                FilterChangeKind.Removed => "REMOVE",
                FilterChangeKind.Moved => "move",
                _ => "change",
            };

            text.Append(CultureInfo.InvariantCulture, $"  {verb} {change.Name}");

            if (change.Detail.Length > 0)
            {
                text.Append(CultureInfo.InvariantCulture, $" — {change.Detail}");
            }

            text.AppendLine();
        }

        int unchanged = changes.Count - notable.Count;
        if (unchanged > 0)
        {
            text.AppendLine(CultureInfo.InvariantCulture,
                $"  ({unchanged} filter{(unchanged == 1 ? string.Empty : "s")} unchanged)");
        }

        return text.ToString();
    }

    private static string DescribeValueChanges(FilterEntry before, FilterEntry after)
    {
        var parts = new List<string>();

        foreach (ControlEntry control in before.Controls)
        {
            if (after.GetControl(control.Id) is not { } updated)
            {
                continue;
            }

            // UI values are whole numbers in practice; compare on those rather
            // than the raw floats, which carry representation noise like
            // 0.19999999999999996 and would report a change that is not one.
            if (Math.Abs(control.UiValue - updated.UiValue) < 0.001)
            {
                continue;
            }

            string name = FilterNames.ForControl(before.Shader, control.Id);
            parts.Add(string.Create(CultureInfo.InvariantCulture,
                $"{name} {control.UiValue:0.##}→{updated.UiValue:0.##}"));
        }

        return string.Join(", ", parts);
    }
}
