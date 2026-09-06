namespace NvFilterStudio.Core.Store;

/// <summary>
/// Extracts a complete JSON object from a larger buffer by brace matching.
/// </summary>
/// <remarks>
/// Needed because records are recovered from raw LevelDB bytes where the JSON
/// is not length-delimited in any way the scanner can see — particularly inside
/// <c>.ldb</c> tables, whose block structure is not parsed. String and escape
/// state must be tracked or a brace inside a Windows path (or any quoted text)
/// ends the object early.
/// </remarks>
internal static class JsonBraceScanner
{
    /// <summary>
    /// Returns the JSON object beginning at <paramref name="start"/>, or
    /// <see langword="null"/> if it never closes within <paramref name="text"/>.
    /// </summary>
    public static string? ExtractObject(string text, int start)
    {
        int depth = 0;
        bool inString = false;
        bool escaped = false;

        for (int i = start; i < text.Length; i++)
        {
            char c = text[i];

            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (c == '\\')
            {
                if (inString)
                {
                    escaped = true;
                }

                continue;
            }

            if (c == '"')
            {
                inString = !inString;
                continue;
            }

            if (inString)
            {
                continue;
            }

            if (c == '{')
            {
                depth++;
            }
            else if (c == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return text[start..(i + 1)];
                }
            }
        }

        return null;
    }

    /// <summary>Offsets of every occurrence of <paramref name="marker"/>, ascending.</summary>
    public static IReadOnlyList<int> FindAll(string text, string marker)
    {
        var offsets = new List<int>();
        int pos = 0;

        while (true)
        {
            int found = text.IndexOf(marker, pos, StringComparison.Ordinal);
            if (found < 0)
            {
                break;
            }

            offsets.Add(found);
            pos = found + 1;
        }

        return offsets;
    }
}
