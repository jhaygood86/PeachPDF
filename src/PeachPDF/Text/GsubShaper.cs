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
    /// a single character for an ordinary glyph, or the whole matched span for a ligature (and, for
    /// a Multiple Substitution expansion's non-first output glyph, a zero-length span anchored at
    /// the end of the source span - see <see cref="GsubMultipleSubstitutionSubtable"/>).
    /// <see cref="XAdvanceDelta"/>/<see cref="YAdvanceDelta"/>/<see cref="XOffset"/>/<see cref="YOffset"/>
    /// are GPOS positioning deltas (font design units, same space <c>OpenTypeDescriptor.GlyphIndexToWidth</c>
    /// returns), applied by <see cref="GposPositioner"/> after GSUB substitution - all zero for a
    /// glyph GPOS doesn't touch, so every pre-GPOS call site is unaffected by their mere existence.
    /// </summary>
    internal readonly record struct ShapedGlyph(
        int GlyphIndex, int ClusterStart, int ClusterLength,
        double XAdvanceDelta = 0, double YAdvanceDelta = 0,
        double XOffset = 0, double YOffset = 0);

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

        /// <summary>The `calt` ("contextual alternates") feature, driven by GSUB Lookup Types 5/6 -
        /// CSS <c>font-variant-ligatures: no-contextual</c> turns this off; it's on by default
        /// (like common ligatures) per CSS Fonts Level 3.</summary>
        Contextual = 16,

        Default = Common | Required | Contextual,
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
    /// The combined set of GSUB/GPOS feature requests for one shaped run - ligatures, caps, numeric,
    /// east-asian, kerning, an explicit document language, and arbitrary explicit
    /// <c>font-feature-settings</c> tags all fold into one request so <see cref="GsubShaper.Shape"/>
    /// can activate every requested lookup in a single pass, ordered by the font's own <c>LookupList</c>
    /// index, instead of several independently-ordered passes.
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
        IReadOnlyList<(string Tag, int Value)>? ExplicitFeatures = null,
        bool Kerning = true,
        string? Language = null)
    {
        // NOT `new()` - for a record struct, a bare `new()` invokes the struct's implicit,
        // zero-initializing parameterless constructor, NOT this primary constructor's own declared
        // defaults (a genuine C# gotcha: unlike a class, `new S()` never routes through a struct's
        // primary constructor when every parameter is optional). Passing the ligatures argument
        // explicitly forces the real primary-constructor overload, so the other arguments still apply
        // their own declared defaults correctly.
        public static readonly TextShapingFeatures Default = new(LigatureFeatures.Default);
    }

    /// <summary>
    /// Turns text into a shaped glyph run: a 1:1 codepoint-to-glyph <c>cmap</c> mapping (via
    /// <see cref="OpenTypeDescriptor.CharCodeToGlyphIndex"/>), followed by GSUB substitution -
    /// ligature (Lookup Type 4), single (Type 1), multiple (Type 2), alternate (Type 3), and
    /// contextual/chaining (Types 5/6, formats 1/2/3) substitution - when the font has a <c>GSUB</c>
    /// table and the requested <see cref="TextShapingFeatures"/> call for it. `lookupFlag`-driven
    /// mark filtering (ligature component matching only - see <see cref="GlyphSequenceFilter"/>) and
    /// per-language (non-default `LangSys`) feature selection are both honored via
    /// <see cref="GdefTable"/>/<see cref="OpenTypeLanguageTags"/>.
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
        // A fixed, deliberately narrow (not spec-complete) simplification: contextual/chaining
        // (Lookup Types 5/6) backtrack/input/lookahead matching is done against literal glyph
        // adjacency, without consulting `lookupFlag`/GDEF to skip an intervening mark the way
        // ligature matching does (see TryMatchLigature) - the overwhelming common `calt` case (no
        // mark interspersed inside the matched window) is unaffected; a font whose contextual rule
        // specifically depends on skipping a mark mid-context may under-match. Recorded as an
        // accepted gap rather than attempted here, given the added complexity of position-tracking
        // through a skip-aware backtrack/input/lookahead walk.
        private const int MaxNestedContextDepth = 8;

        // Real per-language script selection (which script, e.g. "latn" vs "DFLT") is out of scope -
        // "latn" covers the common case, "DFLT" the fallback GsubTable itself falls back to the
        // font's first script if neither is present. Per-language *LangSys* selection (which of a
        // script's language systems to use) IS supported - see TextShapingFeatures.Language below.
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
        // CSS Fonts Level 3's "the value selects the Nth glyph alternate" rule. A lookup of any other
        // type at that index ignores the paired value entirely.
        private static readonly ConditionalWeakTable<GsubTable, ConcurrentDictionary<TextShapingFeatures, SortedDictionary<int, int>>> LookupIndexCache = new();

        // Every tag any font-variant-* longhand (ligatures/caps/numeric/east-asian) can itself
        // produce - an explicit font-feature-settings entry for one of these is always superseded by
        // the dedicated longhand's own request (CSS Fonts precedence: font-variant-* always wins over
        // font-feature-settings for a tag it already controls), never treated as an independent
        // request of its own. See GetActiveLookupIndices below.
        private static readonly IReadOnlySet<string> ReservedTags = new HashSet<string>
        {
            "liga", "clig", "rlig", "dlig", "hlig", "calt",
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

        /// <summary>Returns a mutable list so <see cref="GposPositioner.Apply"/> can add its own
        /// positioning deltas in place after GSUB substitution runs - see
        /// <see cref="OpenTypeDescriptor.Shape"/>, the only real caller (returned to its own callers
        /// as <c>IReadOnlyList&lt;ShapedGlyph&gt;</c>).</summary>
        public static List<ShapedGlyph> Shape(OpenTypeDescriptor descriptor, string text, TextShapingFeatures features)
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

            GdefTable? gdef = descriptor.FontFace.gdef?.Table;

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
                    case 2:
                        if (gsub.GetMultipleSubstitutionLookup(lookupIndex) is { } multiSub)
                            ApplyMultipleSubstitutionLookup(multiSub, glyphs);
                        break;
                    case 3:
                        if (gsub.GetAlternateSubstitutionLookup(lookupIndex) is { } altSub)
                            ApplyAlternateSubstitutionLookup(altSub, glyphs, alternateIndex);
                        break;
                    case 4:
                        if (gsub.GetLigatureLookup(lookupIndex) is { } lig)
                            ApplyLigatureLookup(lig, glyphs, gdef);
                        break;
                    case 5:
                        if (gsub.GetContextualLookup(lookupIndex) is { } contextual)
                            ApplySequenceContextLookup(gsub, contextual.Subtables, glyphs, gdef);
                        break;
                    case 6:
                        if (gsub.GetChainingContextLookup(lookupIndex) is { } chaining)
                            ApplySequenceContextLookup(gsub, chaining.Subtables, glyphs, gdef);
                        break;
                    // 7, 8, and any other/unresolved type: silently skipped, matching the pre-existing
                    // behavior for unsupported lookup types (see file-header gap note).
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
                if ((key.Ligatures & LigatureFeatures.Contextual) != 0) defaultTags.Add("calt");

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

                string? languageTag = OpenTypeLanguageTags.Resolve(key.Language);

                var result = new SortedDictionary<int, int>();
                if (defaultTags.Count > 0)
                {
                    foreach (int lookupIndex in gsub.GetActiveLookupIndices(ScriptPreference, languageTag, defaultTags))
                        result[lookupIndex] = 0;
                }

                if (customAltIndexByTag is not null)
                {
                    foreach ((string tag, int altIndex) in customAltIndexByTag)
                    {
                        foreach (int lookupIndex in gsub.GetActiveLookupIndices(ScriptPreference, languageTag, new HashSet<string> { tag }))
                            result[lookupIndex] = altIndex;
                    }
                }

                return result;
            });
        }

        /// <summary>
        /// Applies one ligature lookup as a single left-to-right pass: at each position, the first
        /// matching ligature (in the font's own authored order) replaces the matched glyph span,
        /// and scanning resumes immediately after it (and any glyph skipped over by `lookupFlag`
        /// mark filtering - see <see cref="ApplyLigatureAt"/>) - matching how OpenType Lookup Type 4
        /// is specified to behave.
        /// </summary>
        // internal rather than private - see ApplyMultipleSubstitutionLookup's identical rationale
        // (here, testing the lookupFlag/GDEF mark-skip retrofit directly).
        internal static void ApplyLigatureLookup(GsubLigatureLookup lookup, List<ShapedGlyph> glyphs, GdefTable? gdef)
        {
            int i = 0;
            while (i < glyphs.Count)
            {
                int consumed = ApplyLigatureAt(lookup, glyphs, i, gdef);
                i += consumed > 0 ? consumed : 1;
            }
        }

        /// <summary>
        /// Tries to match and apply one ligature starting at <paramref name="index"/>, returning how
        /// many glyphs now occupy its former position - the merged ligature glyph, plus any glyph
        /// `lookupFlag` mark filtering skipped over while matching components (e.g. a diacritic
        /// between two ligature-forming base glyphs), which stays in the glyph stream rather than
        /// being consumed by the ligature, moved to immediately after it - or 0 if nothing matched.
        /// </summary>
        private static int ApplyLigatureAt(GsubLigatureLookup lookup, List<ShapedGlyph> glyphs, int index, GdefTable? gdef)
        {
            if (!TryMatchLigature(lookup, glyphs, index, gdef, out ShapedGlyph merged, out int spanLength, out List<int> skippedOffsets))
                return 0;

            var skippedGlyphs = new List<ShapedGlyph>(skippedOffsets.Count);
            foreach (int offset in skippedOffsets)
                skippedGlyphs.Add(glyphs[index + offset]);

            glyphs.RemoveRange(index, spanLength);
            glyphs.Insert(index, merged);
            glyphs.InsertRange(index + 1, skippedGlyphs);

            return 1 + skippedGlyphs.Count;
        }

        private static bool TryMatchLigature(GsubLigatureLookup lookup, List<ShapedGlyph> glyphs, int index, GdefTable? gdef,
            out ShapedGlyph merged, out int spanLength, out List<int> skippedOffsets)
        {
            merged = default;
            spanLength = 0;
            skippedOffsets = [];
            var firstGlyph = (ushort)glyphs[index].GlyphIndex;
            CoverageTable? markFilteringSet = lookup.MarkFilteringSetIndex is { } mfsIndex ? gdef?.GetMarkGlyphSet(mfsIndex) : null;

            foreach (GsubLigatureSubtable subtable in lookup.Subtables)
            {
                int coverageIndex = subtable.Coverage.IndexOfGlyph(firstGlyph);
                if (coverageIndex < 0 || coverageIndex >= subtable.LigatureSets.Length)
                    continue;

                foreach (GsubLigature ligature in subtable.LigatureSets[coverageIndex])
                {
                    var matched = new List<int>(ligature.ComponentGlyphIds.Length);
                    var skipped = new List<int>();
                    int pos = index + 1;
                    int compIdx = 0;

                    while (compIdx < ligature.ComponentGlyphIds.Length && pos < glyphs.Count)
                    {
                        if (!GlyphSequenceFilter.Participates((ushort)glyphs[pos].GlyphIndex, lookup.LookupFlag, gdef, markFilteringSet))
                        {
                            skipped.Add(pos - index);
                            pos++;
                            continue;
                        }

                        if (glyphs[pos].GlyphIndex != ligature.ComponentGlyphIds[compIdx])
                            break;

                        matched.Add(pos);
                        compIdx++;
                        pos++;
                    }

                    if (compIdx != ligature.ComponentGlyphIds.Length)
                        continue;

                    spanLength = pos - index;
                    ShapedGlyph first = glyphs[index];
                    ShapedGlyph last = glyphs[matched.Count > 0 ? matched[^1] : index];
                    merged = new ShapedGlyph(ligature.LigatureGlyph, first.ClusterStart, last.ClusterStart + last.ClusterLength - first.ClusterStart);
                    skippedOffsets = skipped;
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
                ApplySingleSubstitutionAt(lookup, glyphs, i);
        }

        private static void ApplySingleSubstitutionAt(GsubSingleSubstitutionLookup lookup, List<ShapedGlyph> glyphs, int i)
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

        /// <summary>
        /// Applies one alternate-substitution lookup as a plain 1:1 glyph swap, always picking
        /// <paramref name="alternateIndex"/> (0 = the first/default alternate) out of each covered
        /// glyph's alternate set - like single substitution, this never changes <paramref name="glyphs"/>'s
        /// count.
        /// </summary>
        private static void ApplyAlternateSubstitutionLookup(GsubAlternateSubstitutionLookup lookup, List<ShapedGlyph> glyphs, int alternateIndex)
        {
            for (int i = 0; i < glyphs.Count; i++)
                ApplyAlternateSubstitutionAt(lookup, glyphs, i, alternateIndex);
        }

        private static void ApplyAlternateSubstitutionAt(GsubAlternateSubstitutionLookup lookup, List<ShapedGlyph> glyphs, int i, int alternateIndex)
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

        /// <summary>
        /// Applies one multiple-substitution lookup as a single left-to-right pass: each covered
        /// glyph expands into its Sequence's glyphs in place, and scanning resumes immediately after
        /// every inserted glyph (never re-matching one of them).
        /// </summary>
        // internal rather than private: lets tests exercise the matching/application algorithm
        // directly against a synthetic GsubTable + hand-built glyph list, without also needing to
        // control a real font's cmap (see GsubMultipleAndContextualSyntheticTests).
        internal static void ApplyMultipleSubstitutionLookup(GsubMultipleSubstitutionLookup lookup, List<ShapedGlyph> glyphs)
        {
            int i = 0;
            while (i < glyphs.Count)
            {
                int inserted = ApplyMultipleSubstitutionAt(lookup, glyphs, i);
                i += inserted > 0 ? inserted : 1;
            }
        }

        /// <summary>Expands the glyph at <paramref name="i"/> in place if a subtable covers it,
        /// returning how many glyphs now occupy its former position, or 0 if nothing matched.</summary>
        private static int ApplyMultipleSubstitutionAt(GsubMultipleSubstitutionLookup lookup, List<ShapedGlyph> glyphs, int i)
        {
            ushort glyphId = (ushort)glyphs[i].GlyphIndex;
            foreach (GsubMultipleSubstitutionSubtable subtable in lookup.Subtables)
            {
                int coverageIndex = subtable.Coverage.IndexOfGlyph(glyphId);
                if (coverageIndex < 0 || coverageIndex >= subtable.Sequences.Length)
                    continue;

                ushort[] sequence = subtable.Sequences[coverageIndex];
                if (sequence.Length == 0)
                    continue;

                ShapedGlyph original = glyphs[i];
                var expanded = new ShapedGlyph[sequence.Length];

                // The first output glyph keeps the original source-text span; every subsequent one
                // gets a zero-length span anchored at its end - so CMapInfo.AddShapedText's
                // glyph-index-keyed ToUnicode map doesn't have every one of the N output glyphs
                // independently claim the whole original span (which would make PDF text extraction
                // over-copy the source character N times). Substring(x, 0) already resolves such a
                // span to "" with no special-casing needed downstream.
                expanded[0] = new ShapedGlyph(sequence[0], original.ClusterStart, original.ClusterLength);
                for (int k = 1; k < sequence.Length; k++)
                    expanded[k] = new ShapedGlyph(sequence[k], original.ClusterStart + original.ClusterLength, 0);

                glyphs.RemoveAt(i);
                glyphs.InsertRange(i, expanded);
                return expanded.Length;
            }

            return 0;
        }

        /// <summary>
        /// Applies one contextual (Lookup Type 5) or chaining-context (Lookup Type 6) lookup's
        /// subtables as a single left-to-right pass over every glyph position: at each position, the
        /// first subtable (in lookup order) whose backtrack/input/lookahead pattern matches wins,
        /// its <c>SequenceLookupRecord</c>s are applied in the order given (see
        /// <see cref="ApplyMatchedLookups"/>), and scanning resumes past the matched input span
        /// (adjusted for any glyph count change a nested substitution made). Lookup Types 5 and 6
        /// share this one implementation since <see cref="GsubSequenceContextSubtable"/> already
        /// represents a non-chaining rule as one with empty backtrack/lookahead.
        /// </summary>
        // internal rather than private - see ApplyMultipleSubstitutionLookup's identical rationale.
        internal static void ApplySequenceContextLookup(GsubTable gsub, IReadOnlyList<GsubSequenceContextSubtable> subtables, List<ShapedGlyph> glyphs, GdefTable? gdef)
        {
            int i = 0;
            while (i < glyphs.Count)
            {
                int consumed = TryApplySequenceContextAt(gsub, subtables, glyphs, i, gdef);
                i += consumed > 0 ? consumed : 1;
            }
        }

        private static int TryApplySequenceContextAt(GsubTable gsub, IReadOnlyList<GsubSequenceContextSubtable> subtables, List<ShapedGlyph> glyphs, int pos, GdefTable? gdef)
        {
            foreach (GsubSequenceContextSubtable subtable in subtables)
            {
                if (TryMatchSequenceContext(subtable, glyphs, pos) is not (int inputLength, GsubSequenceLookupRecord[] records))
                    continue;

                int countBefore = glyphs.Count;
                ApplyMatchedLookups(gsub, glyphs, pos, inputLength, records, depth: 0, gdef);
                int delta = glyphs.Count - countBefore;
                return Math.Max(1, inputLength + delta);
            }

            return 0;
        }

        private static (int InputLength, GsubSequenceLookupRecord[] Records)? TryMatchSequenceContext(
            GsubSequenceContextSubtable subtable, List<ShapedGlyph> glyphs, int pos)
        {
            switch (subtable.Format)
            {
                case GsubSequenceContextFormat.Glyph:
                {
                    if (subtable.Coverage is not { } coverage || subtable.RuleSets is not { } ruleSets)
                        return null;
                    int coverageIndex = coverage.IndexOfGlyph((ushort)glyphs[pos].GlyphIndex);
                    if (coverageIndex < 0 || coverageIndex >= ruleSets.Length)
                        return null;
                    foreach (GsubSequenceRule rule in ruleSets[coverageIndex])
                    {
                        if (TryMatchRule(rule, glyphs, pos, matchGlyph: true, null, null, null))
                            return (rule.Input.Length + 1, rule.SeqLookupRecords);
                    }
                    return null;
                }

                case GsubSequenceContextFormat.Class:
                {
                    if (subtable.Coverage is not { } coverage2 || subtable.InputClassDef is not { } inputClassDef
                        || subtable.RuleSets is not { } classRuleSets)
                        return null;
                    // Format 2 still requires the first glyph to pass the subtable's own Coverage
                    // test - only its class value (via InputClassDef) selects which RuleSets entry
                    // to try.
                    if (coverage2.IndexOfGlyph((ushort)glyphs[pos].GlyphIndex) < 0)
                        return null;
                    int classValue = inputClassDef.GetClass((ushort)glyphs[pos].GlyphIndex);
                    if (classValue < 0 || classValue >= classRuleSets.Length)
                        return null;
                    foreach (GsubSequenceRule rule in classRuleSets[classValue])
                    {
                        if (TryMatchRule(rule, glyphs, pos, matchGlyph: false, inputClassDef, subtable.BacktrackClassDef, subtable.LookaheadClassDef))
                            return (rule.Input.Length + 1, rule.SeqLookupRecords);
                    }
                    return null;
                }

                case GsubSequenceContextFormat.Coverage:
                {
                    if (subtable.InputCoverages is not { } inputCoverages || subtable.SeqLookupRecords is not { } records)
                        return null;
                    if (!TryMatchCoverageSequence(subtable.BacktrackCoverages, inputCoverages, subtable.LookaheadCoverages, glyphs, pos))
                        return null;
                    return (inputCoverages.Length, records);
                }

                default:
                    return null;
            }
        }

        private static bool TryMatchRule(GsubSequenceRule rule, List<ShapedGlyph> glyphs, int pos, bool matchGlyph,
            ClassDefTable? inputClassDef, ClassDefTable? backtrackClassDef, ClassDefTable? lookaheadClassDef)
        {
            // Backtrack is stored in reverse logical order: rule.Backtrack[0] is the glyph
            // immediately before `pos`, [1] is the one before that, and so on.
            for (int k = 0; k < rule.Backtrack.Length; k++)
            {
                int idx = pos - 1 - k;
                if (idx < 0 || !MatchesRulePosition(glyphs[idx].GlyphIndex, rule.Backtrack[k], matchGlyph, backtrackClassDef))
                    return false;
            }

            // Input positions after the first (which the caller already matched via Coverage/ClassDef).
            for (int k = 0; k < rule.Input.Length; k++)
            {
                int idx = pos + 1 + k;
                if (idx >= glyphs.Count || !MatchesRulePosition(glyphs[idx].GlyphIndex, rule.Input[k], matchGlyph, inputClassDef))
                    return false;
            }

            // Lookahead begins immediately after the input sequence, in forward logical order.
            int lookaheadStart = pos + 1 + rule.Input.Length;
            for (int k = 0; k < rule.Lookahead.Length; k++)
            {
                int idx = lookaheadStart + k;
                if (idx >= glyphs.Count || !MatchesRulePosition(glyphs[idx].GlyphIndex, rule.Lookahead[k], matchGlyph, lookaheadClassDef))
                    return false;
            }

            return true;
        }

        private static bool MatchesRulePosition(int glyphIndex, ushort expected, bool matchGlyph, ClassDefTable? classDef)
            => matchGlyph ? glyphIndex == expected : classDef is not null && classDef.GetClass((ushort)glyphIndex) == expected;

        private static bool TryMatchCoverageSequence(
            CoverageTable[]? backtrack, CoverageTable[] input, CoverageTable[]? lookahead, List<ShapedGlyph> glyphs, int pos)
        {
            backtrack ??= [];
            lookahead ??= [];

            for (int k = 0; k < backtrack.Length; k++)
            {
                int idx = pos - 1 - k;
                if (idx < 0 || backtrack[k].IndexOfGlyph((ushort)glyphs[idx].GlyphIndex) < 0)
                    return false;
            }

            for (int k = 0; k < input.Length; k++)
            {
                int idx = pos + k;
                if (idx >= glyphs.Count || input[k].IndexOfGlyph((ushort)glyphs[idx].GlyphIndex) < 0)
                    return false;
            }

            int lookaheadStart = pos + input.Length;
            for (int k = 0; k < lookahead.Length; k++)
            {
                int idx = lookaheadStart + k;
                if (idx >= glyphs.Count || lookahead[k].IndexOfGlyph((ushort)glyphs[idx].GlyphIndex) < 0)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Applies <paramref name="records"/> (in the order given, per spec) to the glyph sequence
        /// matched at <paramref name="matchStart"/>/<paramref name="inputLength"/> - each record's
        /// <c>SequenceIndex</c> is resolved against the *current* real glyph-list index of that
        /// original input position, re-derived after every earlier record's own application, since a
        /// nested Ligature/Multiple Substitution changes the glyph count and shifts every later
        /// position. Only nested lookup types 1/2/3/4 are supported (matching real fonts' near-universal
        /// practice for `calt`-style rules and this file's existing "unsupported type is silently
        /// skipped" convention) - a nested contextual/chaining lookup is skipped, guarded further by
        /// <paramref name="depth"/> against a pathological/adversarial font nesting indefinitely.
        /// </summary>
        private static void ApplyMatchedLookups(GsubTable gsub, List<ShapedGlyph> glyphs, int matchStart, int inputLength,
            GsubSequenceLookupRecord[] records, int depth, GdefTable? gdef)
        {
            if (depth >= MaxNestedContextDepth || inputLength <= 0)
                return;

            var slotIndex = new int[inputLength];
            for (int s = 0; s < inputLength; s++)
                slotIndex[s] = matchStart + s;

            foreach (GsubSequenceLookupRecord record in records)
            {
                if (record.SequenceIndex < 0 || record.SequenceIndex >= inputLength)
                    continue;

                int realIndex = slotIndex[record.SequenceIndex];
                if (realIndex < 0 || realIndex >= glyphs.Count)
                    continue;

                int countBefore = glyphs.Count;
                ApplyNestedLookup(gsub, glyphs, realIndex, record.LookupListIndex, gdef);
                int delta = glyphs.Count - countBefore;

                if (delta != 0)
                {
                    for (int s = 0; s < inputLength; s++)
                    {
                        if (slotIndex[s] > realIndex)
                            slotIndex[s] += delta;
                    }
                }
            }
        }

        private static void ApplyNestedLookup(GsubTable gsub, List<ShapedGlyph> glyphs, int position, int lookupListIndex, GdefTable? gdef)
        {
            switch (gsub.GetResolvedLookupType(lookupListIndex))
            {
                case 1:
                    if (gsub.GetSingleSubstitutionLookup(lookupListIndex) is { } single)
                        ApplySingleSubstitutionAt(single, glyphs, position);
                    break;
                case 2:
                    if (gsub.GetMultipleSubstitutionLookup(lookupListIndex) is { } multi)
                        ApplyMultipleSubstitutionAt(multi, glyphs, position);
                    break;
                case 3:
                    if (gsub.GetAlternateSubstitutionLookup(lookupListIndex) is { } alt)
                        ApplyAlternateSubstitutionAt(alt, glyphs, position, alternateIndex: 0);
                    break;
                case 4:
                    if (gsub.GetLigatureLookup(lookupListIndex) is { } lig)
                        ApplyLigatureAt(lig, glyphs, position, gdef);
                    break;
                // 5, 6, 7-wrapping-5-or-6, 8, unresolved: a nested contextual/chaining lookup is not
                // supported (see ApplyMatchedLookups' own doc comment) - left unmodified.
            }
        }
    }
}
