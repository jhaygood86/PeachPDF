using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace PeachPDF.Text
{
    /// <summary>
    /// Pattern-based automatic hyphenation, implementing Frank Liang's classic TeX hyphenation
    /// algorithm (as used by <c>hyphens: auto</c> — see <c>CssBox.ParseToWords</c>/
    /// <c>CssLayoutEngine.FlowBox</c>). This is general text-processing logic, not layout-engine-
    /// specific, hence its own <c>PeachPDF.Text</c> namespace rather than living under
    /// <c>Html.Core.Dom</c>.
    ///
    /// Pattern data is embedded as a plain-text resource (one pattern per line) and lazily parsed on
    /// first use — see <c>Text/Resources/hyph-en-us.txt</c> for provenance/license. No reflection or
    /// dynamic codegen is used, so this stays trim/AOT-safe.
    /// </summary>
    internal static class HyphenationEngine
    {
        /// <summary>
        /// Minimum number of characters that must remain before/after any hyphenation point, per this
        /// pattern set's own documented recommendation (hyph-en-us.txt: left=2, right=3).
        /// </summary>
        private const int LeftHyphenMin = 2;
        private const int RightHyphenMin = 3;

        private static Dictionary<string, byte[]>? _englishPatterns;
        private static int _englishMaxPatternLength;
        private static readonly object Lock = new();

        /// <summary>
        /// Finds candidate hyphenation break points for <paramref name="word"/> — each returned value
        /// <c>i</c> means a hyphen may be inserted between <c>word[i-1]</c> and <c>word[i]</c>. Returns
        /// an empty list if the language isn't supported, the word is too short, or it contains
        /// characters outside the pattern set's alphabet (this pattern set is ASCII-letters-only, so a
        /// word with digits, apostrophes, or non-ASCII letters is simply left unhyphenated rather than
        /// guessed at).
        /// </summary>
        public static IReadOnlyList<int> FindHyphenationPoints(string word, string? language)
        {
            if (string.IsNullOrEmpty(word) || word.Length < LeftHyphenMin + RightHyphenMin)
                return [];

            var patterns = GetPatterns(language);
            if (patterns is null || patterns.Count == 0)
                return [];

            var lower = word.ToLowerInvariant();
            foreach (var c in lower)
            {
                if (c < 'a' || c > 'z') return [];
            }

            var wrapped = "." + lower + ".";
            var gapCount = wrapped.Length + 1;
            var values = new byte[gapCount];
            var maxPatternLength = _englishMaxPatternLength;

            for (var start = 0; start < wrapped.Length; start++)
            {
                var maxLen = Math.Min(maxPatternLength, wrapped.Length - start);
                for (var len = 1; len <= maxLen; len++)
                {
                    if (!patterns.TryGetValue(wrapped.Substring(start, len), out var digits))
                        continue;

                    for (var k = 0; k < digits.Length; k++)
                    {
                        var pos = start + k;
                        if (digits[k] > values[pos])
                            values[pos] = digits[k];
                    }
                }
            }

            var candidates = new List<int>();
            for (var originalGap = LeftHyphenMin; originalGap <= word.Length - RightHyphenMin; originalGap++)
            {
                // wrapped = "." + word + "." shifts every original-word gap index up by 1.
                if ((values[originalGap + 1] & 1) == 1)
                    candidates.Add(originalGap);
            }

            return candidates;
        }

        private static Dictionary<string, byte[]>? GetPatterns(string? language)
        {
            if (string.IsNullOrEmpty(language))
                return null;

            // Only American English is supported today (see the plan this shipped under — adding more
            // languages is a pure data addition, not a code change, once a suitably-licensed pattern
            // file is sourced for them).
            if (!language.StartsWith("en", StringComparison.OrdinalIgnoreCase))
                return null;

            if (_englishPatterns != null)
                return _englishPatterns;

            lock (Lock)
            {
                _englishPatterns ??= LoadPatterns("hyph-en-us.txt");
                _englishMaxPatternLength = _englishPatterns.Count == 0
                    ? 0
                    : _englishPatterns.Keys.Max(k => k.Length);
                return _englishPatterns;
            }
        }

        /// <summary>
        /// Parses an embedded pattern resource into a lookup from letters-only pattern text (dots
        /// included, digits stripped) to the digit values at each gap position within that pattern —
        /// e.g. <c>"hy1ph"</c> becomes key <c>"hyph"</c> with values <c>[0,0,1,0,0]</c> (one entry per
        /// gap: before the first letter, between each pair of letters, and after the last letter).
        /// </summary>
        private static Dictionary<string, byte[]> LoadPatterns(string resourceFileName)
        {
            var assembly = typeof(HyphenationEngine).Assembly;
            var resourceName = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith(resourceFileName, StringComparison.OrdinalIgnoreCase));

            if (resourceName is null)
                return new Dictionary<string, byte[]>();

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream is null)
                return new Dictionary<string, byte[]>();

            using var reader = new StreamReader(stream);
            var result = new Dictionary<string, byte[]>();

            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                line = line.Trim();
                if (line.Length == 0 || line[0] == '#')
                    continue;

                var (letters, values) = ParsePattern(line);
                if (letters.Length > 0)
                    result[letters] = values;
            }

            return result;
        }

        /// <summary>
        /// Parses one digit-annotated pattern line (e.g. <c>".ad4der"</c>) into its letters-only key
        /// (<c>".adder"</c>) and a parallel <c>letters.Length + 1</c>-element gap-value array. A digit
        /// in the source sets the value for the gap immediately before the next letter; gaps with no
        /// explicit digit default to 0. This is the standard Liang-pattern parse used by every TeX-
        /// derived hyphenation implementation.
        /// </summary>
        private static (string Letters, byte[] Values) ParsePattern(string pattern)
        {
            var letters = new System.Text.StringBuilder(pattern.Length);
            var values = new byte[pattern.Length + 1];

            foreach (var c in pattern)
            {
                if (c is >= '0' and <= '9')
                {
                    values[letters.Length] = (byte)(c - '0');
                }
                else
                {
                    letters.Append(c);
                }
            }

            var trimmedValues = values.Length == letters.Length + 1
                ? values
                : values[..(letters.Length + 1)];

            return (letters.ToString(), trimmedValues);
        }
    }
}
