using PeachDrawing.Text.Internal.Text;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PeachDrawing.Text.Unicode
{
    /// <summary>
    /// Unicode's default case conversion (section 3.13 of the Unicode Standard): text to upper, lower or title case, with the full
    /// mappings of <c>SpecialCasing.txt</c> (the German sharp s becomes <c>SS</c>, or <c>Ss</c> in title case, the <c>fi</c> ligature
    /// <c>FI</c>), the context a Greek capital sigma needs to become a final or a medial small one, and the tailorings of Turkish,
    /// Azerbaijani and Lithuanian.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A full mapping can change the length of the text, so each method has a form that also returns, for each UTF-16 index of
    /// the result, the index of the character of the text it came from: a host that keeps other data per character of the
    /// original (bidi levels, scripts) reads it through that map. The language is a BCP 47 tag; only its primary subtag
    /// and a script subtag matter, and an unknown or <see langword="null"/> language gets the language-independent mappings. The Lithuanian
    /// tailoring, which adds and removes combining dots above, is not implemented.
    /// </para>
    /// </remarks>
    public static class CaseMapper
    {
        private static readonly Lazy<Dictionary<int, string>> FullUpper = new(() => Build(CaseMappingData.FullUpper));
        private static readonly Lazy<Dictionary<int, string>> FullLower = new(() => Build(CaseMappingData.FullLower));
        private static readonly Lazy<Dictionary<int, string>> TitleMappings = new(() => Build(CaseMappingData.Title));
        private static readonly Lazy<HashSet<int>> SoftDotted = new(() => [.. CaseMappingData.SoftDotted]);

        private static Dictionary<int, string> Build((int CodePoint, string Mapping)[] data)
        {
            var map = new Dictionary<int, string>(data.Length);
            foreach (var (codePoint, mapping) in data) map[codePoint] = mapping;
            return map;
        }

        /// <summary>The value paired with <paramref name="codePoint"/> in a sorted flat array of (code point, value) pairs, or the code point itself.</summary>
        private static int SimpleMapping(int[] pairs, int codePoint)
        {
            var low = 0;
            var high = pairs.Length / 2 - 1;
            while (low <= high)
            {
                var mid = (low + high) / 2;
                var key = pairs[mid * 2];
                if (codePoint < key) high = mid - 1;
                else if (codePoint > key) low = mid + 1;
                else return pairs[mid * 2 + 1];
            }

            return codePoint;
        }

        private static string SimpleUpperOf(Rune rune) => new Rune(SimpleMapping(CaseMappingData.SimpleUpper, rune.Value)).ToString();

        private static string SimpleLowerOf(Rune rune) => new Rune(SimpleMapping(CaseMappingData.SimpleLower, rune.Value)).ToString();

        private static bool InRanges((int First, int Last)[] ranges, int codePoint)
        {
            var low = 0;
            var high = ranges.Length - 1;
            while (low <= high)
            {
                var mid = (low + high) / 2;
                if (codePoint < ranges[mid].First) high = mid - 1;
                else if (codePoint > ranges[mid].Last) low = mid + 1;
                else return true;
            }

            return false;
        }

        /// <summary>Maps <paramref name="text"/> to upper case.</summary>
        /// <param name="text">The text.</param>
        /// <param name="language">The BCP 47 language of the text, or <see langword="null"/> when it has none.</param>
        /// <returns>The upper case text, which can be longer than <paramref name="text"/>.</returns>
        public static string ToUpper(string text, string? language = null) => ToUpper(text, language, out _);

        /// <summary>Maps <paramref name="text"/> to upper case and says where each character of the result came from.</summary>
        /// <param name="text">The text.</param>
        /// <param name="language">The BCP 47 language of the text, or <see langword="null"/> when it has none.</param>
        /// <param name="sourceIndices">
        /// For each UTF-16 index of the result, the index in <paramref name="text"/> of the character it was mapped from, and for the
        /// end of the result the length of <paramref name="text"/>; <see langword="null"/> when the result has the same length and each
        /// character stands where it came from.
        /// </param>
        /// <returns>The upper case text.</returns>
        public static string ToUpper(string text, string? language, out int[]? sourceIndices)
        {
            ArgumentNullException.ThrowIfNull(text);
            var (turkic, lithuanian) = Tailoring(language);
            return Map(text, out sourceIndices, (t, index, rune) => UpperOf(t, index, rune, turkic, lithuanian));
        }

        private static string UpperOf(string text, int index, Rune rune, bool turkic, bool lithuanian)
        {
            if (turkic && rune.Value == 'i') return "\u0130";
            if (lithuanian && rune.Value == 0x0307 && IsAfterSoftDotted(text, index)) return string.Empty;
            return FullUpper.Value.TryGetValue(rune.Value, out var full) ? full : SimpleUpperOf(rune);
        }

        /// <summary>Maps each character of <paramref name="text"/> to title case: the form a letter takes at the start of a word.</summary>
        /// <param name="text">The text.</param>
        /// <param name="language">The BCP 47 language of the text, or <see langword="null"/> when it has none.</param>
        /// <returns>The title case text, which can be longer than <paramref name="text"/>.</returns>
        public static string ToTitle(string text, string? language = null) => ToTitle(text, language, out _);

        /// <summary>Maps each character of <paramref name="text"/> to title case and says where each character of the result came from.</summary>
        /// <param name="text">The text.</param>
        /// <param name="language">The BCP 47 language of the text, or <see langword="null"/> when it has none.</param>
        /// <param name="sourceIndices">See <see cref="ToUpper(string, string, out int[])"/>.</param>
        /// <returns>The title case text.</returns>
        public static string ToTitle(string text, string? language, out int[]? sourceIndices)
        {
            ArgumentNullException.ThrowIfNull(text);
            var (turkic, lithuanian) = Tailoring(language);
            return Map(text, out sourceIndices, (t, index, rune) =>
                TitleMappings.Value.TryGetValue(rune.Value, out var title) && !(turkic && rune.Value == 'i') ? title : UpperOf(t, index, rune, turkic, lithuanian));
        }

        /// <summary>Maps <paramref name="text"/> to lower case.</summary>
        /// <param name="text">The text.</param>
        /// <param name="language">The BCP 47 language of the text, or <see langword="null"/> when it has none.</param>
        /// <returns>The lower case text, which can be longer than <paramref name="text"/>.</returns>
        public static string ToLower(string text, string? language = null) => ToLower(text, language, out _);

        /// <summary>Maps <paramref name="text"/> to lower case and says where each character of the result came from.</summary>
        /// <param name="text">The text.</param>
        /// <param name="language">The BCP 47 language of the text, or <see langword="null"/> when it has none.</param>
        /// <param name="sourceIndices">See <see cref="ToUpper(string, string, out int[])"/>.</param>
        /// <returns>The lower case text.</returns>
        public static string ToLower(string text, string? language, out int[]? sourceIndices)
        {
            ArgumentNullException.ThrowIfNull(text);
            var (turkic, lithuanian) = Tailoring(language);
            return Map(text, out sourceIndices, (t, index, rune) =>
            {
                if (rune.Value == 0x03A3) return IsFinalSigma(t, index) ? "\u03C2" : "\u03C3";
                if (turkic)
                {
                    if (rune.Value == 0x0130) return "i";
                    if (rune.Value == 0x0307 && IsAfterI(t, index)) return string.Empty;     // the dot an I carried is absorbed by the i
                    if (rune.Value == 'I') return IsBeforeDot(t, index) ? "i" : "\u0131";
                }

                if (lithuanian)
                {
                    switch (rune.Value)
                    {
                        case 0x00CC: return "i\u0307\u0300";
                        case 0x00CD: return "i\u0307\u0301";
                        case 0x0128: return "i\u0307\u0303";
                        case 'I' or 'J' or 0x012E when IsMoreAbove(t, index):
                            return SimpleLowerOf(rune) + "\u0307";
                    }
                }

                return FullLower.Value.TryGetValue(rune.Value, out var full) ? full : SimpleLowerOf(rune);
            });
        }

        private static string Map(string text, out int[]? sourceIndices, Func<string, int, Rune, string> mapRune)
        {
            var result = new StringBuilder(text.Length);
            List<int>? sources = null;

            for (var i = 0; i < text.Length;)
            {
                Rune.DecodeFromUtf16(text.AsSpan(i), out var rune, out var length);
                if (rune == Rune.ReplacementChar && length == 1 && text[i] != '�')
                {
                    // A lone surrogate is not a scalar value: it maps to itself.
                    result.Append(text[i]);
                    sources?.Add(i);
                    i++;
                    continue;
                }

                var mapped = mapRune(text, i, rune);
                if (mapped.Length != length && sources is null)
                {
                    // The first change of length: everything before it stood where it came from.
                    sources = new List<int>(text.Length + 4);
                    for (var before = 0; before < result.Length; before++) sources.Add(before);
                }

                result.Append(mapped);
                if (sources is not null)
                    for (var c = 0; c < mapped.Length; c++) sources.Add(i);

                i += length;
            }

            if (sources is null)
            {
                sourceIndices = null;
                return result.ToString();
            }

            sources.Add(text.Length);
            sourceIndices = [.. sources];
            return result.ToString();
        }

        /// <summary>
        /// Which tailoring a language asks for: Turkish and Azerbaijani (written in Latin script: a script subtag other than <c>Latn</c>, as
        /// in <c>tr-Cyrl</c>, overrides the language, since the tailoring is about the Latin letter i) or Lithuanian.
        /// </summary>
        private static (bool Turkic, bool Lithuanian) Tailoring(string? language)
        {
            if (string.IsNullOrEmpty(language)) return (false, false);

            var subtags = language.Split('-', '_');
            var latin = subtags.Length < 2 || subtags[1].Length != 4 || subtags[1].Equals("Latn", StringComparison.OrdinalIgnoreCase);
            var primary = subtags[0];
            return (latin && (primary.Equals("tr", StringComparison.OrdinalIgnoreCase) || primary.Equals("az", StringComparison.OrdinalIgnoreCase)),
                latin && primary.Equals("lt", StringComparison.OrdinalIgnoreCase));
        }

        private static int CombiningClassOf(int codePoint)
        {
            var ranges = CaseMappingData.CombiningClasses;
            var low = 0;
            var high = ranges.Length - 1;
            while (low <= high)
            {
                var mid = (low + high) / 2;
                if (codePoint < ranges[mid].First) high = mid - 1;
                else if (codePoint > ranges[mid].Last) low = mid + 1;
                else return ranges[mid].Class;
            }

            return 0;
        }

        /// <summary>The After_Soft_Dotted context: a soft-dotted letter before the character, with nothing of class 0 or 230 (above) between.</summary>
        private static bool IsAfterSoftDotted(string text, int index)
        {
            for (var i = index; i > 0;)
            {
                var rune = PreviousRune(text, ref i);
                var combiningClass = CombiningClassOf(rune.Value);
                if (combiningClass is 0 or 230) return SoftDotted.Value.Contains(rune.Value);
            }

            return false;
        }

        /// <summary>The More_Above context: the next character that is not a mark of some other class is a mark above.</summary>
        private static bool IsMoreAbove(string text, int index)
        {
            Rune.DecodeFromUtf16(text.AsSpan(index), out _, out var own);
            for (var i = index + own; i < text.Length;)
            {
                Rune.DecodeFromUtf16(text.AsSpan(i), out var rune, out var length);
                i += length;
                var combiningClass = CombiningClassOf(rune.Value);
                if (combiningClass is 0 or 230) return combiningClass == 230;
            }

            return false;
        }

        /// <summary>The After_I context: a capital I before the character, with no mark of class 0 or 230 (above) between.</summary>
        private static bool IsAfterI(string text, int index)
        {
            for (var i = index; i > 0;)
            {
                var rune = PreviousRune(text, ref i);
                var combiningClass = CombiningClassOf(rune.Value);
                if (combiningClass is 0 or 230) return rune.Value == 'I';
            }

            return false;
        }

        /// <summary>The negation of Not_Before_Dot: the next character that is not a mark of another class is a combining dot above.</summary>
        private static bool IsBeforeDot(string text, int index)
        {
            for (var i = index + 1; i < text.Length;)
            {
                Rune.DecodeFromUtf16(text.AsSpan(i), out var rune, out var length);
                i += length;
                var combiningClass = CombiningClassOf(rune.Value);
                if (rune.Value == 0x0307) return true;
                if (combiningClass is 0 or 230) return false;
            }

            return false;
        }

        /// <summary>
        /// The Final_Sigma context of SpecialCasing.txt: the capital sigma is preceded by a cased letter (with case-ignorable
        /// characters between) and not followed by one.
        /// </summary>
        private static bool IsFinalSigma(string text, int index)
        {
            var before = false;
            for (var i = index; i > 0;)
            {
                var rune = PreviousRune(text, ref i);
                if (IsCaseIgnorable(rune)) continue;
                before = IsCased(rune);
                break;
            }

            if (!before) return false;

            for (var i = index + 1; i < text.Length;)
            {
                Rune.DecodeFromUtf16(text.AsSpan(i), out var rune, out var length);
                i += length;
                if (IsCaseIgnorable(rune)) continue;
                return !IsCased(rune);
            }

            return true;
        }

        private static Rune PreviousRune(string text, ref int index)
        {
            index--;
            if (index > 0 && char.IsLowSurrogate(text[index]) && char.IsHighSurrogate(text[index - 1]))
            {
                index--;
                return new Rune(text[index], text[index + 1]);
            }

            return Rune.TryCreate(text[index], out var rune) ? rune : Rune.ReplacementChar;
        }

        private static bool IsCased(Rune rune) => InRanges(CaseMappingData.CasedRanges, rune.Value);

        private static bool IsCaseIgnorable(Rune rune) => InRanges(CaseMappingData.CaseIgnorableRanges, rune.Value);
    }
}
