using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace PeachPDF.Text
{
    /// <summary>
    /// Pattern-based automatic hyphenation, implementing Frank Liang's classic TeX hyphenation
    /// algorithm (as used by <c>hyphens: auto</c> — see <c>CssBox.ParseToWords</c>/
    /// <c>CssLayoutEngine.FlowBox</c>). This is general text-processing logic, not layout-engine-
    /// specific, hence its own <c>PeachPDF.Text</c> namespace rather than living under
    /// <c>Html.Core.Dom</c>.
    ///
    /// Pattern data for ~70 languages (sourced from CTAN's hyph-utf8 package — see
    /// <c>tools/Update-HyphenationPatterns.ps1</c> for provenance/regeneration) is embedded as
    /// Brotli-compressed plain-text resources under <c>Text/Resources/Patterns/</c>, one file per
    /// language, and lazily decompressed/parsed on first use of that language — so a document that
    /// only uses one language never pays to load the rest. A document's declared language (e.g.
    /// <c>&lt;html lang="de-AT"&gt;</c>) is resolved to the closest available pattern set by trying
    /// the tag itself, then progressively shorter subtag prefixes, consulting
    /// <c>Text/Resources/language-tags.txt</c> for languages whose canonical BCP-47 tag doesn't
    /// match the pattern file's own tag one-for-one (see <see cref="ResolveLanguageTag"/>). No
    /// reflection or dynamic codegen is used, so this stays trim/AOT-safe.
    /// </summary>
    internal static class HyphenationEngine
    {
        /// <summary>
        /// Hyphenation minimums used when a pattern file doesn't state its own via a
        /// <c>hyphenmins:</c> comment line — this is plain TeX's own default.
        /// </summary>
        private const int DefaultLeftHyphenMin = 2;
        private const int DefaultRightHyphenMin = 3;

        private static readonly Regex HyphenMinsRegex =
            new(@"hyphenmins:\s*left=(?<left>\d+)\s*right=(?<right>\d+)", RegexOptions.Compiled);

        private static readonly Regex PatternResourceRegex =
            new(@"hyph-(?<tag>[a-z0-9\-]+)\.txt\.br$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly ConcurrentDictionary<string, LanguagePatternSet?> PatternSetCache = new();

        private static HashSet<string>? _availableTags;
        private static Dictionary<string, string>? _languageTagMap;
        private static readonly object InitLock = new();

        private sealed record LanguagePatternSet(
            Dictionary<string, byte[]> Patterns,
            int MaxPatternLength,
            int LeftHyphenMin,
            int RightHyphenMin);

        /// <summary>
        /// Finds candidate hyphenation break points for <paramref name="word"/> — each returned value
        /// <c>i</c> means a hyphen may be inserted between <c>word[i-1]</c> and <c>word[i]</c>. Returns
        /// an empty list if the language isn't supported, the word is too short (per that language's
        /// own hyphenation minimums), or it contains characters that aren't letters (digits,
        /// punctuation, apostrophes are left unhyphenated rather than guessed at).
        /// </summary>
        public static IReadOnlyList<int> FindHyphenationPoints(string word, string? language)
        {
            if (string.IsNullOrEmpty(word))
                return [];

            var patternSet = GetPatternSet(language);
            if (patternSet is null || patternSet.Patterns.Count == 0)
                return [];

            if (word.Length < patternSet.LeftHyphenMin + patternSet.RightHyphenMin)
                return [];

            var lower = word.ToLowerInvariant();
            foreach (var c in lower)
            {
                if (!char.IsLetter(c)) return [];
            }

            var wrapped = "." + lower + ".";
            var gapCount = wrapped.Length + 1;
            var values = new byte[gapCount];
            var maxPatternLength = patternSet.MaxPatternLength;
            var patterns = patternSet.Patterns;

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
            for (var originalGap = patternSet.LeftHyphenMin; originalGap <= word.Length - patternSet.RightHyphenMin; originalGap++)
            {
                // wrapped = "." + word + "." shifts every original-word gap index up by 1.
                if ((values[originalGap + 1] & 1) == 1)
                    candidates.Add(originalGap);
            }

            return candidates;
        }

        /// <summary>
        /// Resolves a BCP-47-ish document language tag to one of the available pattern-set tags.
        /// Tries the normalized tag itself, then progressively shorter subtag prefixes (dropping
        /// one trailing <c>-subtag</c> at a time) — at each step first checking whether that prefix
        /// is itself an available pattern tag (true for the common case, since most pattern tags are
        /// already valid BCP-47 forms, e.g. "fr", "ru", "en-gb"), then consulting the
        /// language-tags.txt alias/default-variant table. Returns <see langword="null"/> if nothing
        /// matches at any prefix length.
        /// </summary>
        private static string? ResolveLanguageTag(string? language)
        {
            if (string.IsNullOrEmpty(language))
                return null;

            var remaining = language.Trim().ToLowerInvariant();
            if (remaining.Length == 0)
                return null;

            var availableTags = GetAvailableTags();
            var languageTagMap = GetLanguageTagMap();

            while (true)
            {
                if (availableTags.Contains(remaining))
                    return remaining;

                if (languageTagMap.TryGetValue(remaining, out var mapped) && availableTags.Contains(mapped))
                    return mapped;

                var lastDash = remaining.LastIndexOf('-');
                if (lastDash < 0)
                    return null;

                remaining = remaining[..lastDash];
            }
        }

        private static LanguagePatternSet? GetPatternSet(string? language)
        {
            var tag = ResolveLanguageTag(language);
            return tag is null ? null : PatternSetCache.GetOrAdd(tag, LoadPatternSet);
        }

        private static HashSet<string> GetAvailableTags()
        {
            if (_availableTags != null)
                return _availableTags;

            lock (InitLock)
            {
                return _availableTags ??= ComputeAvailableTags();
            }
        }

        private static HashSet<string> ComputeAvailableTags()
        {
            var assembly = typeof(HyphenationEngine).Assembly;
            var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var resourceName in assembly.GetManifestResourceNames())
            {
                var match = PatternResourceRegex.Match(resourceName);
                if (match.Success)
                    tags.Add(match.Groups["tag"].Value);
            }

            return tags;
        }

        private static Dictionary<string, string> GetLanguageTagMap()
        {
            if (_languageTagMap != null)
                return _languageTagMap;

            lock (InitLock)
            {
                return _languageTagMap ??= LoadLanguageTagMap();
            }
        }

        /// <summary>
        /// Parses the small "bcp47-tag=pattern-tag" alias/default-variant table — see
        /// Text/Resources/language-tags.txt for the format and the rationale for each entry.
        /// </summary>
        private static Dictionary<string, string> LoadLanguageTagMap()
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var assembly = typeof(HyphenationEngine).Assembly;
            var resourceName = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("language-tags.txt", StringComparison.OrdinalIgnoreCase));

            if (resourceName is null)
                return result;

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream is null)
                return result;

            using var reader = new StreamReader(stream);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                line = line.Trim();
                if (line.Length == 0 || line[0] == '#')
                    continue;

                var parts = line.Split('=', 2);
                if (parts.Length == 2)
                    result[parts[0].Trim()] = parts[1].Trim();
            }

            return result;
        }

        /// <summary>
        /// Loads and decompresses one language's Brotli-compressed pattern resource
        /// (Text/Resources/Patterns/hyph-&lt;tag&gt;.txt.br) into a lookup from letters-only pattern
        /// text (dots included, digits stripped) to the digit values at each gap position within
        /// that pattern — e.g. <c>"hy1ph"</c> becomes key <c>"hyph"</c> with values
        /// <c>[0,0,1,0,0]</c> — plus that pattern set's own hyphenation minimums, parsed from a
        /// <c>hyphenmins:</c> comment line (see <see cref="HyphenMinsRegex"/>), defaulting to
        /// <see cref="DefaultLeftHyphenMin"/>/<see cref="DefaultRightHyphenMin"/> when absent.
        /// </summary>
        private static LanguagePatternSet? LoadPatternSet(string tag)
        {
            var assembly = typeof(HyphenationEngine).Assembly;
            var suffix = $"hyph-{tag}.txt.br";
            var resourceName = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));

            if (resourceName is null)
                return null;

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream is null)
                return null;

            using var brotli = new BrotliStream(stream, CompressionMode.Decompress);
            using var reader = new StreamReader(brotli);

            var patterns = new Dictionary<string, byte[]>();
            var left = DefaultLeftHyphenMin;
            var right = DefaultRightHyphenMin;

            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                line = line.Trim();
                if (line.Length == 0)
                    continue;

                if (line[0] == '#')
                {
                    var minsMatch = HyphenMinsRegex.Match(line);
                    if (minsMatch.Success)
                    {
                        left = int.Parse(minsMatch.Groups["left"].Value, CultureInfo.InvariantCulture);
                        right = int.Parse(minsMatch.Groups["right"].Value, CultureInfo.InvariantCulture);
                    }
                    continue;
                }

                var (letters, values) = ParsePattern(line);
                if (letters.Length > 0)
                    patterns[letters] = values;
            }

            var maxPatternLength = patterns.Count == 0 ? 0 : patterns.Keys.Max(k => k.Length);
            return new LanguagePatternSet(patterns, maxPatternLength, left, right);
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
