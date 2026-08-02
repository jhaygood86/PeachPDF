using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using PeachPDF.Fonts.OpenType;

namespace PeachPDF.Text
{
    /// <summary>
    /// One shaped glyph: <see cref="ClusterStart"/>/<see cref="ClusterLength"/> are the UTF-16
    /// offsets, into the text that was shaped, of the source character(s) this glyph represents -
    /// a single character for an ordinary glyph, or the whole matched span for a ligature.
    /// </summary>
    internal readonly record struct ShapedGlyph(int GlyphIndex, int ClusterStart, int ClusterLength);

    /// <summary>Which GSUB ligature features <see cref="GsubShaper.Shape"/> should apply.</summary>
    [Flags]
    internal enum LigatureFeatures
    {
        None = 0,

        /// <summary>The `liga`/`clig` ("common ligatures") features - what CSS
        /// <c>font-variant-ligatures: common-ligatures</c> (and the initial <c>normal</c>) mean.</summary>
        Common = 1,

        /// <summary>The `rlig` ("required ligatures") feature - applied whenever the font defines
        /// it, independent of <c>font-variant-ligatures</c> (mirrors browser behavior).</summary>
        Required = 2,

        /// <summary>The `dlig` ("discretionary ligatures") feature - CSS
        /// <c>font-variant-ligatures: discretionary-ligatures</c>. Off by default (opt-in), unlike
        /// common ligatures.</summary>
        Discretionary = 4,

        /// <summary>The `hlig` ("historical ligatures") feature - CSS
        /// <c>font-variant-ligatures: historical-ligatures</c>. Off by default (opt-in).</summary>
        Historical = 8,

        Default = Common | Required,
    }

    /// <summary>Which CSS <c>font-variant-caps</c> keyword <see cref="GsubShaper.Shape"/> should apply -
    /// a single-select property, so unlike <see cref="LigatureFeatures"/> this is not [Flags].</summary>
    internal enum FontVariantCapsFeature
    {
        None,
        SmallCaps,
        AllSmallCaps,
        PetiteCaps,
        AllPetiteCaps,
        Unicase,
        TitlingCaps,
    }

    /// <summary>Which GSUB numeric features (CSS <c>font-variant-numeric</c>) <see cref="GsubShaper.Shape"/>
    /// should apply.</summary>
    [Flags]
    internal enum NumericFeatures
    {
        None = 0,
        LiningNums = 1 << 0,
        OldstyleNums = 1 << 1,
        ProportionalNums = 1 << 2,
        TabularNums = 1 << 3,
        DiagonalFractions = 1 << 4,
        StackedFractions = 1 << 5,
        Ordinal = 1 << 6,
        SlashedZero = 1 << 7,
    }

    /// <summary>Which GSUB east-asian features (CSS <c>font-variant-east-asian</c>) <see cref="GsubShaper.Shape"/>
    /// should apply.</summary>
    [Flags]
    internal enum EastAsianFeatures
    {
        None = 0,
        Jis78 = 1 << 0,
        Jis83 = 1 << 1,
        Jis90 = 1 << 2,
        Jis04 = 1 << 3,
        Simplified = 1 << 4,
        Traditional = 1 << 5,
        FullWidth = 1 << 6,
        ProportionalWidth = 1 << 7,
        Ruby = 1 << 8,
    }

    /// <summary>
    /// The combined set of GSUB feature requests for one shaped run - ligatures, caps, numeric,
    /// east-asian, and arbitrary explicit <c>font-feature-settings</c> tags all fold into one request
    /// so <see cref="GsubShaper.Shape"/> can activate every requested lookup in a single pass, ordered
    /// by the font's own <c>LookupList</c> index, instead of several independently-ordered passes.
    /// <see cref="ExplicitFeatures"/> uses default (reference) equality when this struct is used as a
    /// cache key (see <see cref="GsubShaper"/>'s lookup-index cache) - two logically-identical but
    /// distinct list instances cache separately, which only costs a redundant lookup-index computation,
    /// never an incorrect one.
    /// </summary>
    internal readonly record struct TextShapingFeatures(
        LigatureFeatures Ligatures = LigatureFeatures.Default,
        FontVariantCapsFeature Caps = FontVariantCapsFeature.None,
        NumericFeatures Numeric = NumericFeatures.None,
        EastAsianFeatures EastAsian = EastAsianFeatures.None,
        IReadOnlyList<(string Tag, int Value)>? ExplicitFeatures = null)
    {
        // NOT `new()` - for a record struct, a bare `new()` invokes the struct's implicit,
        // zero-initializing parameterless constructor, NOT this primary constructor's own declared
        // defaults (a genuine C# gotcha: unlike a class, `new S()` never routes through a struct's
        // primary constructor when every parameter is optional). Passing the ligatures argument
        // explicitly forces the real primary-constructor overload, so the other three still apply
        // their own declared defaults correctly.
        public static readonly TextShapingFeatures Default = new(LigatureFeatures.Default);
    }

    /// <summary>
    /// Turns text into a shaped glyph run: a 1:1 codepoint-to-glyph <c>cmap</c> mapping (via
    /// <see cref="OpenTypeDescriptor.CharCodeToGlyphIndex"/>), followed by GSUB substitution -
    /// ligature (Lookup Type 4: <c>liga</c>/<c>clig</c>/<c>rlig</c>/<c>dlig</c>/<c>hlig</c>), single
    /// (Lookup Type 1), and alternate (Lookup Type 3) substitution for caps/numeric/east-asian/
    /// explicit <c>font-feature-settings</c> tags - when the font has a <c>GSUB</c> table and the
    /// requested <see cref="TextShapingFeatures"/> call for it.
    /// This is general text-processing logic built from <c>PeachPDF.Fonts.OpenType</c>'s table data,
    /// hence its own <c>PeachPDF.Text</c> namespace - see <see cref="HyphenationEngine"/> for the same
    /// reasoning applied to hyphenation.
    ///
    /// This is the single glyph-walk every text-drawing/measuring call site shares
    /// (<c>FontHelper.MeasureString</c>, <c>XGraphicsPdfRenderer.DrawString</c>,
    /// <c>ColorGlyphPainter.Paint</c>, <c>GraphicsAdapter.GetTextOutline</c>, <c>CMapInfo</c>) -
    /// previously each re-derived "codepoint to glyph" independently.
    /// </summary>
    internal static class GsubShaper
    {
        // Real per-language script/language-system selection is out of scope (see
        // .claude/accepted-gaps/no-text-shaping.md) - "latn" covers the common case, "DFLT" the
        // fallback GsubTable itself falls back to the font's first script if neither is present.
        // Internal (not private) so OpenTypeDescriptor.SupportsFeatureTags can check support against
        // the exact same script preference Shape itself resolves against - otherwise "supported" and
        // "actually applied" could disagree.
        internal static readonly IReadOnlyList<string> ScriptPreference = ["latn", "DFLT"];

        // GsubTable instances are cached and shared process-wide (owned by the cached
        // OpenTypeFontface), so the lookup-index sets computed from them are cached the same way -
        // otherwise every shaped word would re-walk ScriptList/FeatureList from scratch. The value
        // maps each active lookup index to the alternate-glyph index an Alternate Substitution (Type
        // 3) lookup at that index should use - 0 (the first/default alternate) for every boolean
        // feature request (caps/numeric/east-asian/ligatures, and a bare font-feature-settings tag),
        // or <value> - 1 for an explicit font-feature-settings tag with an integer greater than 1, per
        // CSS Fonts Level 3's "the value selects the Nth glyph alternate" rule. A Type 1 or Type 4
        // lookup at that index ignores the paired value entirely.
        private static readonly ConditionalWeakTable<GsubTable, ConcurrentDictionary<TextShapingFeatures, SortedDictionary<int, int>>> LookupIndexCache = new();

        // Every tag any font-variant-* longhand (ligatures/caps/numeric/east-asian) can itself
        // produce - an explicit font-feature-settings entry for one of these is always superseded by
        // the dedicated longhand's own request (CSS Fonts precedence: font-variant-* always wins over
        // font-feature-settings for a tag it already controls), never treated as an independent
        // request of its own. See GetActiveLookupIndices below.
        private static readonly IReadOnlySet<string> ReservedTags = new HashSet<string>
        {
            "liga", "clig", "rlig", "dlig", "hlig",
            "smcp", "c2sc", "pcap", "c2pc", "unic", "titl",
            "lnum", "onum", "pnum", "tnum", "frac", "afrc", "ordn", "zero",
            "jp78", "jp83", "jp90", "jp04", "smpl", "trad", "fwid", "pwid", "ruby",
        };

        private static readonly IReadOnlySet<string> EmptyTags = new HashSet<string>();
        private static readonly IReadOnlySet<string> SmallCapsTags = new HashSet<string> { "smcp" };
        private static readonly IReadOnlySet<string> AllSmallCapsTags = new HashSet<string> { "smcp", "c2sc" };
        private static readonly IReadOnlySet<string> PetiteCapsTags = new HashSet<string> { "pcap" };
        private static readonly IReadOnlySet<string> AllPetiteCapsTags = new HashSet<string> { "pcap", "c2pc" };
        private static readonly IReadOnlySet<string> UnicaseTags = new HashSet<string> { "unic" };
        private static readonly IReadOnlySet<string> TitlingCapsTags = new HashSet<string> { "titl" };

        /// <summary>
        /// The GSUB feature tag(s) that implement <paramref name="capsFeature"/> - the single source
        /// of truth for the CSS <c>font-variant-caps</c> keyword -&gt; OpenType tag mapping, used both
        /// by <see cref="Shape"/>'s own tag computation and by <see cref="OpenTypeDescriptor.SupportsFeatureTags"/>
        /// callers building a capability-query tag set for a given keyword.
        /// </summary>
        public static IReadOnlySet<string> GetFeatureTags(FontVariantCapsFeature capsFeature) => capsFeature switch
        {
            FontVariantCapsFeature.SmallCaps => SmallCapsTags,
            FontVariantCapsFeature.AllSmallCaps => AllSmallCapsTags,
            FontVariantCapsFeature.PetiteCaps => PetiteCapsTags,
            FontVariantCapsFeature.AllPetiteCaps => AllPetiteCapsTags,
            FontVariantCapsFeature.Unicase => UnicaseTags,
            FontVariantCapsFeature.TitlingCaps => TitlingCapsTags,
            _ => EmptyTags,
        };

        public static IReadOnlyList<ShapedGlyph> Shape(OpenTypeDescriptor descriptor, string text, TextShapingFeatures features)
        {
            List<ShapedGlyph> glyphs = MapToGlyphs(descriptor, text);
            if (glyphs.Count == 0 || IsEmpty(features))
                return glyphs;

            // GSUB substitution isn't a realistic case for symbol-encoded fonts (Wingdings et al.),
            // and their remapped codepoints would need separate coverage-matching logic - skip rather
            // than risk mismatched substitutions.
            if (descriptor.FontFace.cmap.symbol)
                return glyphs;

            GsubTable? gsub = descriptor.FontFace.gsub?.Table;
            if (gsub is null)
                return glyphs;

            SortedDictionary<int, int> lookupIndices = GetActiveLookupIndices(gsub, features);
            if (lookupIndices.Count == 0)
                return glyphs;

            // One pass over the combined, font-LookupList-index-ordered set, dispatching each lookup
            // by its real type - this is what makes cross-feature lookup ordering (e.g. a caps
            // substitution feeding into a later ligature match, or vice versa) match real OpenType
            // application order, rather than an arbitrary code-imposed order from separate passes.
            foreach ((int lookupIndex, int alternateIndex) in lookupIndices)
            {
                switch (gsub.GetResolvedLookupType(lookupIndex))
                {
                    case 1:
                        if (gsub.GetSingleSubstitutionLookup(lookupIndex) is { } singleSub)
                            ApplySingleSubstitutionLookup(singleSub, glyphs);
                        break;
                    case 3:
                        if (gsub.GetAlternateSubstitutionLookup(lookupIndex) is { } altSub)
                            ApplyAlternateSubstitutionLookup(altSub, glyphs, alternateIndex);
                        break;
                    case 4:
                        if (gsub.GetLigatureLookup(lookupIndex) is { } lig)
                            ApplyLigatureLookup(lig, glyphs);
                        break;
                    // 2, 5, 6, 7, 8, and any other/unresolved type: silently skipped, matching the
                    // pre-existing behavior for unsupported lookup types (see file-header gap note).
                }
            }

            return glyphs;
        }

        private static bool IsEmpty(TextShapingFeatures features) =>
            features.Ligatures == LigatureFeatures.None
            && features.Caps == FontVariantCapsFeature.None
            && features.Numeric == NumericFeatures.None
            && features.EastAsian == EastAsianFeatures.None
            && (features.ExplicitFeatures is null || features.ExplicitFeatures.Count == 0);

        private static List<ShapedGlyph> MapToGlyphs(OpenTypeDescriptor descriptor, string text)
        {
            var result = new List<ShapedGlyph>(text.Length);
            bool symbol = descriptor.FontFace.cmap.symbol;
            int clusterStart = 0;

            foreach (Rune rune in text.EnumerateRunes())
            {
                int utf16Length = rune.Utf16SequenceLength;
                Rune lookup = rune;
                if (symbol && rune.Value <= 0xFFFF)
                    lookup = new Rune(rune.Value | (descriptor.FontFace.os2.usFirstCharIndex & 0xFF00));

                int glyphIndex = descriptor.CharCodeToGlyphIndex(lookup);
                result.Add(new ShapedGlyph(glyphIndex, clusterStart, utf16Length));
                clusterStart += utf16Length;
            }

            return result;
        }

        private static SortedDictionary<int, int> GetActiveLookupIndices(GsubTable gsub, TextShapingFeatures features)
        {
            var perTableCache = LookupIndexCache.GetOrCreateValue(gsub);
            return perTableCache.GetOrAdd(features, key =>
            {
                // Every tag here wants the default (first) glyph alternate if its lookup turns out to
                // be Type 3 - only an explicit font-feature-settings value greater than 1 asks for a
                // later one (collected into customAltIndexByTag below instead).
                var defaultTags = new HashSet<string>();

                if ((key.Ligatures & LigatureFeatures.Common) != 0) { defaultTags.Add("liga"); defaultTags.Add("clig"); }
                if ((key.Ligatures & LigatureFeatures.Required) != 0) defaultTags.Add("rlig");
                if ((key.Ligatures & LigatureFeatures.Discretionary) != 0) defaultTags.Add("dlig");
                if ((key.Ligatures & LigatureFeatures.Historical) != 0) defaultTags.Add("hlig");

                foreach (string tag in GetFeatureTags(key.Caps)) defaultTags.Add(tag);

                if ((key.Numeric & NumericFeatures.LiningNums) != 0) defaultTags.Add("lnum");
                if ((key.Numeric & NumericFeatures.OldstyleNums) != 0) defaultTags.Add("onum");
                if ((key.Numeric & NumericFeatures.ProportionalNums) != 0) defaultTags.Add("pnum");
                if ((key.Numeric & NumericFeatures.TabularNums) != 0) defaultTags.Add("tnum");
                if ((key.Numeric & NumericFeatures.DiagonalFractions) != 0) defaultTags.Add("frac");
                if ((key.Numeric & NumericFeatures.StackedFractions) != 0) defaultTags.Add("afrc");
                if ((key.Numeric & NumericFeatures.Ordinal) != 0) defaultTags.Add("ordn");
                if ((key.Numeric & NumericFeatures.SlashedZero) != 0) defaultTags.Add("zero");

                if ((key.EastAsian & EastAsianFeatures.Jis78) != 0) defaultTags.Add("jp78");
                if ((key.EastAsian & EastAsianFeatures.Jis83) != 0) defaultTags.Add("jp83");
                if ((key.EastAsian & EastAsianFeatures.Jis90) != 0) defaultTags.Add("jp90");
                if ((key.EastAsian & EastAsianFeatures.Jis04) != 0) defaultTags.Add("jp04");
                if ((key.EastAsian & EastAsianFeatures.Simplified) != 0) defaultTags.Add("smpl");
                if ((key.EastAsian & EastAsianFeatures.Traditional) != 0) defaultTags.Add("trad");
                if ((key.EastAsian & EastAsianFeatures.FullWidth) != 0) defaultTags.Add("fwid");
                if ((key.EastAsian & EastAsianFeatures.ProportionalWidth) != 0) defaultTags.Add("pwid");
                if ((key.EastAsian & EastAsianFeatures.Ruby) != 0) defaultTags.Add("ruby");

                Dictionary<string, int>? customAltIndexByTag = null;
                if (key.ExplicitFeatures is not null)
                {
                    foreach ((string tag, int value) in key.ExplicitFeatures)
                    {
                        if (value == 0 || ReservedTags.Contains(tag))
                            continue;

                        if (value >= 2)
                            (customAltIndexByTag ??= new Dictionary<string, int>())[tag] = value - 1;
                        else
                            defaultTags.Add(tag);
                    }
                }

                var result = new SortedDictionary<int, int>();
                if (defaultTags.Count > 0)
                {
                    foreach (int lookupIndex in gsub.GetActiveLookupIndices(ScriptPreference, defaultTags))
                        result[lookupIndex] = 0;
                }

                if (customAltIndexByTag is not null)
                {
                    foreach ((string tag, int altIndex) in customAltIndexByTag)
                    {
                        foreach (int lookupIndex in gsub.GetActiveLookupIndices(ScriptPreference, new HashSet<string> { tag }))
                            result[lookupIndex] = altIndex;
                    }
                }

                return result;
            });
        }

        /// <summary>
        /// Applies one ligature lookup as a single left-to-right pass: at each position, the first
        /// matching ligature (in the font's own authored order) replaces the matched glyph span,
        /// and scanning resumes immediately after the substituted glyph - matching how OpenType
        /// Lookup Type 4 is specified to behave.
        /// </summary>
        private static void ApplyLigatureLookup(GsubLigatureLookup lookup, List<ShapedGlyph> glyphs)
        {
            int i = 0;
            while (i < glyphs.Count)
            {
                if (TryMatchLigature(lookup, glyphs, i, out ShapedGlyph merged, out int consumed))
                {
                    glyphs.RemoveRange(i, consumed);
                    glyphs.Insert(i, merged);
                }
                i++;
            }
        }

        private static bool TryMatchLigature(GsubLigatureLookup lookup, List<ShapedGlyph> glyphs, int index, out ShapedGlyph merged, out int consumed)
        {
            merged = default;
            consumed = 0;
            var firstGlyph = (ushort)glyphs[index].GlyphIndex;

            foreach (GsubLigatureSubtable subtable in lookup.Subtables)
            {
                int coverageIndex = subtable.Coverage.IndexOfGlyph(firstGlyph);
                if (coverageIndex < 0 || coverageIndex >= subtable.LigatureSets.Length)
                    continue;

                foreach (GsubLigature ligature in subtable.LigatureSets[coverageIndex])
                {
                    int componentsNeeded = ligature.ComponentGlyphIds.Length;
                    if (index + 1 + componentsNeeded > glyphs.Count)
                        continue;

                    bool allMatch = true;
                    for (int j = 0; j < componentsNeeded; j++)
                    {
                        if (glyphs[index + 1 + j].GlyphIndex != ligature.ComponentGlyphIds[j])
                        {
                            allMatch = false;
                            break;
                        }
                    }
                    if (!allMatch)
                        continue;

                    consumed = componentsNeeded + 1;
                    ShapedGlyph first = glyphs[index];
                    ShapedGlyph last = glyphs[index + consumed - 1];
                    merged = new ShapedGlyph(ligature.LigatureGlyph, first.ClusterStart, last.ClusterStart + last.ClusterLength - first.ClusterStart);
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Applies one single-substitution lookup as a plain 1:1 glyph swap - no glyph is ever merged
        /// or split, so unlike ligature substitution this never changes <paramref name="glyphs"/>'s
        /// count (and so never affects a shaped-glyph count taken before vs. after, e.g. letter-spacing).
        /// </summary>
        private static void ApplySingleSubstitutionLookup(GsubSingleSubstitutionLookup lookup, List<ShapedGlyph> glyphs)
        {
            for (int i = 0; i < glyphs.Count; i++)
            {
                ushort glyphId = (ushort)glyphs[i].GlyphIndex;
                foreach (GsubSingleSubstitutionSubtable subtable in lookup.Subtables)
                {
                    if (subtable.TryGetSubstitute(glyphId, out ushort substitute))
                    {
                        glyphs[i] = glyphs[i] with { GlyphIndex = substitute };
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Applies one alternate-substitution lookup as a plain 1:1 glyph swap, always picking
        /// <paramref name="alternateIndex"/> (0 = the first/default alternate) out of each covered
        /// glyph's alternate set - like single substitution, this never changes <paramref name="glyphs"/>'s
        /// count.
        /// </summary>
        private static void ApplyAlternateSubstitutionLookup(GsubAlternateSubstitutionLookup lookup, List<ShapedGlyph> glyphs, int alternateIndex)
        {
            for (int i = 0; i < glyphs.Count; i++)
            {
                ushort glyphId = (ushort)glyphs[i].GlyphIndex;
                foreach (GsubAlternateSubstitutionSubtable subtable in lookup.Subtables)
                {
                    if (subtable.TryGetAlternate(glyphId, alternateIndex, out ushort substitute))
                    {
                        glyphs[i] = glyphs[i] with { GlyphIndex = substitute };
                        break;
                    }
                }
            }
        }
    }
}
