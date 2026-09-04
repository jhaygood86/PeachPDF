using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace PeachPDF.Text
{
    /// <summary>
    /// Resolves a document's BCP-47 language tag (e.g. <c>en-US</c>, from <c>CssBox.Language</c>) to
    /// an OpenType 4-character language-system tag (e.g. <c>"ENG "</c>) for <see cref="GsubShaper"/>'s
    /// per-language GSUB feature selection (<c>GsubTable.GetActiveLookupIndices</c>'s `LangSys`
    /// overload). The OpenType language-tag registry
    /// (https://learn.microsoft.com/en-us/typography/opentype/spec/languagetags) is large and its
    /// mapping from BCP-47 is not a mechanical derivation (irregular abbreviations, script/region
    /// disambiguation BCP-47 encodes differently) - this is a curated subset, not full coverage. A
    /// language absent from it simply isn't recognized here (the caller falls back to the font's
    /// `DefaultLangSys`, exactly as if no language were declared at all - never worse than before).
    /// Structurally mirrors <see cref="HyphenationEngine"/>'s own BCP-47 tag-resolution shape (progressive
    /// subtag-prefix fallback against an embedded `tag=value` resource).
    /// </summary>
    internal static class OpenTypeLanguageTags
    {
        private static readonly Lazy<IReadOnlyDictionary<string, string>> Tags = new(LoadTags);

        /// <summary>Resolves <paramref name="bcp47"/> (e.g. <c>"en-US"</c>, <c>"fr"</c>) to an OpenType
        /// language-system tag, trying progressively shorter subtag prefixes (e.g. <c>"en-US"</c> then
        /// <c>"en"</c>) before giving up. Returns null for a null/empty/unrecognized input.</summary>
        public static string? Resolve(string? bcp47)
        {
            if (string.IsNullOrEmpty(bcp47))
                return null;

            IReadOnlyDictionary<string, string> tags = Tags.Value;
            string candidate = bcp47;
            while (true)
            {
                if (tags.TryGetValue(candidate.ToLowerInvariant(), out string? otTag))
                    return otTag;

                int dash = candidate.LastIndexOf('-');
                if (dash <= 0)
                    return null;
                candidate = candidate[..dash];
            }
        }

        private static IReadOnlyDictionary<string, string> LoadTags()
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            Assembly assembly = typeof(OpenTypeLanguageTags).Assembly;
            string? resourceName = Array.Find(assembly.GetManifestResourceNames(), n => n.EndsWith("opentype-language-tags.txt", StringComparison.Ordinal));
            if (resourceName is null)
                return result;

            using Stream? stream = assembly.GetManifestResourceStream(resourceName);
            if (stream is null)
                return result;

            using var reader = new StreamReader(stream);
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                if (line.Length == 0 || line[0] == '#')
                    continue;

                int eq = line.IndexOf('=');
                if (eq <= 0 || eq >= line.Length - 1)
                    continue;

                string bcp47 = line[..eq].Trim();
                string otTag = line[(eq + 1)..].Trim();
                if (bcp47.Length > 0 && otTag.Length > 0)
                    result[bcp47] = otTag;
            }

            return result;
        }
    }
}
