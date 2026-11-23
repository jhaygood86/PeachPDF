using PeachDrawing.Text.Internal.Text;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PeachDrawing.Text.Unicode
{
    /// <summary>
    /// Unicode's default case conversion (section 3.13 of the Unicode Standard): text to upper or lower case, with the full mappings
    /// of <c>SpecialCasing.txt</c> (the German sharp s becomes <c>SS</c>, the <c>fi</c> ligature <c>FI</c>), the context a Greek
    /// capital sigma needs to become a final or a medial small one, and the tailorings of Turkish and Azerbaijani.
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
        private static readonly Lazy<Dictionary<int, string>> UpperMappings = new(() => Build(CaseMappingData.Upper));
        private static readonly Lazy<Dictionary<int, string>> LowerMappings = new(() => Build(CaseMappingData.Lower));

        private static Dictionary<int, string> Build((int CodePoint, string Mapping)[] data)
        {
            var map = new Dictionary<int, string>(data.Length);
            foreach (var (codePoint, mapping) in data) map[codePoint] = mapping;
            return map;
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
            var turkic = IsTurkic(language);
            return Map(text, sourceIndices: out sourceIndices, (string t, int index, Rune rune) =>
            {
                if (turkic && rune.Value == 'i') return "İ";
                if (rune.Value == 0x0131) return "I";
                return UpperMappings.Value.TryGetValue(rune.Value, out var full) ? full : Rune.ToUpperInvariant(rune).ToString();
            });
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
            var turkic = IsTurkic(language);
            return Map(text, sourceIndices: out sourceIndices, (string t, int index, Rune rune) =>
            {
                if (rune.Value == 0x03A3) return IsFinalSigma(t, index) ? "ς" : "σ";
                if (turkic)
                {
                    if (rune.Value == 'I') return "ı";
                    if (rune.Value == 0x0130) return "i";
                }

                return LowerMappings.Value.TryGetValue(rune.Value, out var full) ? full : Rune.ToLowerInvariant(rune).ToString();
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
        /// Whether the language is Turkish or Azerbaijani written in Latin script: a script subtag other than <c>Latn</c> (as in
        /// <c>tr-Cyrl</c>) overrides the language, since the tailoring is about the Latin letter i.
        /// </summary>
        private static bool IsTurkic(string? language)
        {
            if (string.IsNullOrEmpty(language)) return false;

            var subtags = language.Split('-', '_');
            if (!subtags[0].Equals("tr", StringComparison.OrdinalIgnoreCase) && !subtags[0].Equals("az", StringComparison.OrdinalIgnoreCase))
                return false;

            return subtags.Length < 2 || subtags[1].Length != 4 || subtags[1].Equals("Latn", StringComparison.OrdinalIgnoreCase);
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

        private static bool IsCased(Rune rune) =>
            Rune.GetUnicodeCategory(rune) is UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter or UnicodeCategory.TitlecaseLetter;

        private static bool IsCaseIgnorable(Rune rune)
        {
            if (rune.Value is '\'' or '.' or ':' or '·' or '’' or '‘' or '․' or '‧' or '︓' or '﹒' or '﹕' or '＇' or '．' or '：')
                return true;

            return Rune.GetUnicodeCategory(rune) is UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark or UnicodeCategory.Format
                or UnicodeCategory.ModifierLetter or UnicodeCategory.ModifierSymbol;
        }
    }
}
