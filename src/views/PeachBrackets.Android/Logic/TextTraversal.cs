using System;
using System.Globalization;

namespace PeachBrackets.Android.Logic;

/// <summary>The sizes of text step an accessibility service moves by, as the <c>MOVEMENT_GRANULARITY_*</c> bits of Android's accessibility API.</summary>
[Flags]
internal enum TextGranularity
{
    None = 0,
    Character = 1,
    Word = 2,
    Line = 4,
    Paragraph = 8,
    Page = 16,
}

/// <summary>
/// Moving through the text of a node by a character, a word or a paragraph, the way TalkBack's reading controls do: from where it last stopped, to
/// the end (or start) of the next (or previous) piece of that size. Pure functions of the text, so they are tested without a device.
/// </summary>
internal static class TextTraversal
{
    /// <summary>The sizes this can move by: lines and pages depend on layout the text alone does not carry.</summary>
    public const TextGranularity Supported = TextGranularity.Character | TextGranularity.Word | TextGranularity.Paragraph;

    /// <summary>
    /// The piece of text after <paramref name="from"/>.
    /// </summary>
    /// <param name="text">the text</param>
    /// <param name="from">where the last move stopped, between two characters (0 to the length)</param>
    /// <param name="granularity">the size of step</param>
    /// <returns>the start and end of the next piece, or null at the end of the text or for a size this does not support</returns>
    public static (int Start, int End)? Next(string text, int from, TextGranularity granularity)
    {
        ArgumentNullException.ThrowIfNull(text);
        from = Math.Clamp(from, 0, text.Length);
        if (from >= text.Length)
            return null;

        switch (granularity)
        {
            case TextGranularity.Character:
                return (from, from + StringInfo.GetNextTextElementLength(text, from));

            case TextGranularity.Word:
            {
                var start = from;
                while (start < text.Length && !IsWordCharacter(text[start]))
                    start++;

                if (start >= text.Length)
                    return null;

                var end = start;
                while (end < text.Length && IsWordCharacter(text[end]))
                    end++;

                return (start, end);
            }

            case TextGranularity.Paragraph:
            {
                var start = from;
                while (start < text.Length && text[start] == '\n')
                    start++;

                if (start >= text.Length)
                    return null;

                var end = text.IndexOf('\n', start);
                return (start, end < 0 ? text.Length : end);
            }

            default:
                return null;
        }
    }

    /// <summary>The piece of text before <paramref name="from"/>; see <see cref="Next"/>.</summary>
    /// <param name="text">the text</param>
    /// <param name="from">where the last move stopped, between two characters (0 to the length)</param>
    /// <param name="granularity">the size of step</param>
    /// <returns>the start and end of the previous piece, or null at the start of the text or for a size this does not support</returns>
    public static (int Start, int End)? Previous(string text, int from, TextGranularity granularity)
    {
        ArgumentNullException.ThrowIfNull(text);
        from = Math.Clamp(from, 0, text.Length);
        if (from <= 0)
            return null;

        switch (granularity)
        {
            case TextGranularity.Character:
            {
                var start = from - 1;
                // A pair of UTF-16 units and a combining sequence are one text element: step back to where the element starts.
                var elementStarts = StringInfo.ParseCombiningCharacters(text);
                for (var i = elementStarts.Length - 1; i >= 0; i--)
                {
                    if (elementStarts[i] < from)
                    {
                        start = elementStarts[i];
                        break;
                    }
                }

                return (start, from);
            }

            case TextGranularity.Word:
            {
                var end = from;
                while (end > 0 && !IsWordCharacter(text[end - 1]))
                    end--;

                if (end <= 0)
                    return null;

                var start = end;
                while (start > 0 && IsWordCharacter(text[start - 1]))
                    start--;

                return (start, end);
            }

            case TextGranularity.Paragraph:
            {
                var end = from;
                while (end > 0 && text[end - 1] == '\n')
                    end--;

                if (end <= 0)
                    return null;

                var start = text.LastIndexOf('\n', end - 1) + 1;
                return (start, end);
            }

            default:
                return null;
        }
    }

    private static bool IsWordCharacter(char c) => char.IsLetterOrDigit(c) || c is '_' or '\'' or '’';
}
