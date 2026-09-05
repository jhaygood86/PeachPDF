using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using PeachPDF.Fonts.OpenType;
using PeachPDF.Text.Shaping.Arabic;
using PeachPDF.Text.Shaping.Use;

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
    /// <see cref="LigatureComponentClusterStarts"/> is null for every glyph except one produced by a
    /// GSUB ligature merge (see <see cref="GsubShaper.TryMatchLigature"/>), where it records each
    /// original component's own <see cref="ClusterStart"/> (component 0 = the coverage-matched first
    /// glyph) - the bookkeeping <see cref="GposPositioner.ApplyMarkToLigature"/> (GPOS Lookup Type 5)
    /// needs to identify which ligature component a later-attaching mark belongs to.
    /// <see cref="AttachedToIndex"/> is null for every glyph except a mark <see cref="GposPositioner.ApplyMarkAnchor"/>
    /// just positioned via mark-to-base/mark-to-ligature/mark-to-mark attachment (GPOS Types 4/5/6),
    /// where it records the glyph-list index (stable for the lifetime of one
    /// <see cref="OpenTypeDescriptor.Shape"/> call - GPOS never inserts/removes glyphs) of whatever it
    /// anchored to. <see cref="XOffset"/> alone can't reconstruct that relationship once the list is
    /// reordered (see <see cref="OpenTypeDescriptor.Shape"/>'s remarks on <c>ReverseForDisplay</c>): the
    /// offset bakes in the pen-distance to the base under the walk order GPOS actually ran in, so
    /// reordering without this back-reference would silently mis-position the mark. Cursive attachment
    /// (GPOS Type 3, <see cref="GposPositioner.ApplyCursiveAttachment"/>) needs no equivalent
    /// back-reference - its own correction is self-contained per glyph (depends only on that glyph's own
    /// anchor, never on the other glyph's position), so it survives reversal via a plain interval-mirror
    /// with no special-casing - see <see cref="GposPositioner.TryApplyCursivePair"/>'s own remarks.
    /// </summary>
    internal readonly record struct ShapedGlyph(
        int GlyphIndex, int ClusterStart, int ClusterLength,
        double XAdvanceDelta = 0, double YAdvanceDelta = 0,
        double XOffset = 0, double YOffset = 0,
        int[]? LigatureComponentClusterStarts = null,
        int? AttachedToIndex = null);

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
    /// east-asian, kerning, an explicit document language, an explicit OpenType script tag, per-character
    /// Arabic-family joining forms, per-character Universal-Shaping-Engine categories (Devanagari/
    /// Bengali/Gujarati/Tamil), and arbitrary explicit <c>font-feature-settings</c> tags all fold into
    /// one request so
    /// <see cref="GsubShaper.Shape"/> can activate every requested lookup in a single pass, ordered by
    /// the font's own <c>LookupList</c> index, instead of several independently-ordered passes.
    /// <see cref="JoiningForms"/> and <see cref="UseCategories"/> are the exceptions to "single pass" -
    /// see <see cref="GsubShaper.Shape"/>'s own remarks on why each must run in its own dedicated
    /// stage(s) before the ordered pass, not folded into it (never both at once for one run - a word
    /// resolves to exactly one script, so only one of the two is ever non-null). <see cref="ExplicitFeatures"/>/
    /// <see cref="JoiningForms"/>/<see cref="UseCategories"/> use default (reference) equality when this
    /// struct is used as a cache key (see <see cref="GsubShaper"/>'s lookup-index cache) - two
    /// logically-identical but distinct list instances cache separately, which only costs a redundant
    /// lookup-index computation, never an incorrect one (for <see cref="JoiningForms"/>/<see cref="UseCategories"/>
    /// specifically, this means the cache essentially never hits across two different complex-script
    /// runs, since each has its own distinct per-character sequence - still correct, just without the
    /// caching benefit ordinary Latin text gets).
    /// <see cref="ReverseForDisplay"/> requests <see cref="OpenTypeDescriptor.Shape"/>'s own final step:
    /// reverse the shaped <c>ShapedGlyph</c> list (never the source text GSUB/GPOS themselves ran
    /// against) and remap any mirrorable glyph via <c>BidiMirroring</c> - see
    /// <see cref="OpenTypeDescriptor.Shape"/>'s remarks for why Arabic-family joining words shape this
    /// way instead of shaping already-visually-reversed text the way a plain RTL word (Hebrew, etc.)
    /// still does.
    /// </summary>
    internal readonly record struct TextShapingFeatures(
        LigatureFeatures Ligatures = LigatureFeatures.Default,
        FontVariantCapsFeature Caps = FontVariantCapsFeature.None,
        NumericFeatures Numeric = NumericFeatures.None,
        EastAsianFeatures EastAsian = EastAsianFeatures.None,
        IReadOnlyList<(string Tag, int Value)>? ExplicitFeatures = null,
        bool Kerning = true,
        string? Language = null,
        string? ScriptTag = null,
        IReadOnlyList<ArabicJoiningForm>? JoiningForms = null,
        IReadOnlyList<UseCategory>? UseCategories = null,
        bool ReverseForDisplay = false)
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
        // Guards ApplyMatchedLookups against a pathological/adversarial font nesting indefinitely -
        // real fonts never chain nested contextual lookups anywhere near this deep.
        private const int MaxNestedContextDepth = 8;

        // The fallback script preference used when a run carries no TextShapingFeatures.ScriptTag (or
        // that tag isn't in the font's own ScriptList) - "latn" covers the common case, "DFLT" the
        // fallback GsubTable itself falls back to the font's first script if neither is present.
        // Internal (not private) so OpenTypeDescriptor.SupportsFeatureTags can check general capability
        // against this same default chain.
        internal static readonly IReadOnlyList<string> ScriptPreference = ["latn", "DFLT"];

        /// <summary>
        /// Builds the ordered script-tag preference <c>GsubTable.GetActiveLookupIndices</c> tries: the
        /// run's own resolved OpenType script tag first (when the caller supplied one - see
        /// <see cref="TextShapingFeatures.ScriptTag"/>, typically <see cref="OpenTypeScriptTags.Resolve"/>
        /// applied to a run's <see cref="ScriptRunResolver"/>-resolved script), falling back to
        /// <see cref="ScriptPreference"/>'s existing <c>"latn"</c>/<c>"DFLT"</c> chain - never worse than
        /// before this parameter existed for a run that doesn't supply one, or whose script isn't in the
        /// font's own <c>ScriptList</c> (<c>GetActiveLookupIndices</c> already tries each preference in
        /// order and falls through).
        /// </summary>
        private static IReadOnlyList<string> ResolveScriptPreference(string? scriptTag) =>
            scriptTag is null ? ScriptPreference : [scriptTag, .. ScriptPreference];

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

            if (features.JoiningForms is { Count: > 0 } joiningForms)
            {
                var languageTag = OpenTypeLanguageTags.Resolve(features.Language);
                var scriptPreference = ResolveScriptPreference(features.ScriptTag);

                // Captured by ClusterStart BEFORE the ccmp/locl pre-stage runs just below - that stage
                // can change glyphs' count (e.g. decomposing one codepoint into a base + mark glyph, see
                // its own remarks), so "joiningForms[i] describes glyphs[i]" stops being true the moment
                // it does. ClusterStart (the source codepoint's own UTF-16 offset) survives a Type 2
                // expansion untouched on the resulting first/primary glyph (ApplyMultipleSubstitutionAt's
                // own convention), so keying on it instead of raw position is what keeps every later
                // lookup pointed at the right glyph regardless of how many glyphs an earlier stage
                // inserted or removed.
                Dictionary<int, ArabicJoiningForm>? formsByClusterStart = null;
                for (var i = 0; i < glyphs.Count && i < joiningForms.Count; i++)
                {
                    if (joiningForms[i] == ArabicJoiningForm.None)
                        continue;
                    (formsByClusterStart ??= new Dictionary<int, ArabicJoiningForm>())[glyphs[i].ClusterStart] = joiningForms[i];
                }

                if (formsByClusterStart is not null)
                {
                    // ccmp/locl run in their own stage BEFORE positional joining-form substitution,
                    // matching HarfBuzz's own collect_features_arabic staging (both are enabled ahead of
                    // its own isol/init/medi/fina loop). This is not a stylistic nicety: some real fonts
                    // (confirmed directly against Noto Sans Arabic) define ccmp rules that decompose a
                    // precomposed dotted letter into a base glyph + separate combining-mark glyph, and
                    // the base glyph - not the original precomposed one - is what the font's own
                    // init/medi/fina coverage tables are actually keyed on. Skipping this stage means
                    // positional substitution silently no-ops for exactly the letters that need
                    // decomposing first, on a real font that happens to use this technique - found by
                    // rasterizing real output during development, not by reading the spec alone (see
                    // this fix's own recent-fixes entry).
                    foreach (int lookupIndex in gsub.GetActiveLookupIndices(scriptPreference, languageTag, CcmpLoclTags))
                        ApplyLookup(gsub, lookupIndex, alternateIndex: 0, glyphs, gdef);

                    // Positional joining-form substitution (isol/fina/fin2/fin3/medi/med2/init) runs in
                    // its own dedicated stage BEFORE the general feature pass below, matching HarfBuzz's
                    // own staged application order (see ArabicJoiningShaper's own remarks): rlig/calt/liga
                    // must see the already-joining-form-selected glyphs (e.g. a lam-alef ligature rule is
                    // keyed on the specific joining-form glyphs, not the nominal isolated ones), never the
                    // reverse. This runs independent of whether any other feature is requested, so it
                    // can't be skipped by the "no active lookups at all" early-return just below.
                    ApplyArabicJoiningFeatures(gsub, glyphs, formsByClusterStart, languageTag, scriptPreference);
                }
            }

            if (features.UseCategories is { Count: > 0 } useCategories)
            {
                var languageTag = OpenTypeLanguageTags.Resolve(features.Language);
                var scriptPreference = ResolveScriptPreference(features.ScriptTag);
                ApplyUseShaping(gsub, glyphs, useCategories, languageTag, scriptPreference, gdef);
            }

            SortedDictionary<int, int> lookupIndices = GetActiveLookupIndices(gsub, features);
            if (lookupIndices.Count == 0)
                return glyphs;

            // One pass over the combined, font-LookupList-index-ordered set, dispatching each lookup
            // by its real type - this is what makes cross-feature lookup ordering (e.g. a caps
            // substitution feeding into a later ligature match, or vice versa) match real OpenType
            // application order, rather than an arbitrary code-imposed order from separate passes.
            foreach ((int lookupIndex, int alternateIndex) in lookupIndices)
                ApplyLookup(gsub, lookupIndex, alternateIndex, glyphs, gdef);

            return glyphs;
        }

        /// <summary>The <c>ccmp</c>/<c>locl</c> feature tags - see <see cref="Shape"/>'s own remarks on
        /// why these need their own pre-stage ahead of positional joining-form substitution.</summary>
        private static readonly IReadOnlySet<string> CcmpLoclTags = new HashSet<string> { "ccmp", "locl" };

        /// <summary>
        /// Dispatches one lookup by its real (post-Extension-unwrapping) type - the single switch every
        /// stage of <see cref="Shape"/> shares (the main per-feature pass, and the <c>ccmp</c>/<c>locl</c>
        /// pre-stage <see cref="TextShapingFeatures.JoiningForms"/> needs), so a lookup type gains
        /// support in exactly one place regardless of which stage ends up needing it.
        /// </summary>
        private static void ApplyLookup(GsubTable gsub, int lookupIndex, int alternateIndex, List<ShapedGlyph> glyphs, GdefTable? gdef)
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
                    {
                        CoverageTable? contextualMfs = contextual.MarkFilteringSetIndex is { } cMfs ? gdef?.GetMarkGlyphSet(cMfs) : null;
                        ApplySequenceContextLookup(gsub, contextual.Subtables, glyphs, gdef, contextual.LookupFlag, contextualMfs);
                    }
                    break;
                case 6:
                    if (gsub.GetChainingContextLookup(lookupIndex) is { } chaining)
                    {
                        CoverageTable? chainingMfs = chaining.MarkFilteringSetIndex is { } chMfs ? gdef?.GetMarkGlyphSet(chMfs) : null;
                        ApplySequenceContextLookup(gsub, chaining.Subtables, glyphs, gdef, chaining.LookupFlag, chainingMfs);
                    }
                    break;
                case 8:
                    if (gsub.GetReverseChainSingleSubstLookup(lookupIndex) is { } reverseChain)
                    {
                        CoverageTable? reverseChainMfs = reverseChain.MarkFilteringSetIndex is { } rcMfs ? gdef?.GetMarkGlyphSet(rcMfs) : null;
                        ApplyReverseChainSingleSubstitutionLookup(reverseChain, glyphs, gdef, reverseChainMfs);
                    }
                    break;
                // Any other/unresolved type: silently skipped, matching the pre-existing behavior for
                // unsupported lookup types (see file-header gap note).
            }
        }

        // internal rather than private: lets a test prove JoiningForms alone (with every other field at
        // its "empty" value) does NOT trip this early-return - see ApplyArabicJoiningFeatures's own
        // synthetic tests.
        internal static bool IsEmpty(TextShapingFeatures features) =>
            features.Ligatures == LigatureFeatures.None
            && features.Caps == FontVariantCapsFeature.None
            && features.Numeric == NumericFeatures.None
            && features.EastAsian == EastAsianFeatures.None
            && (features.ExplicitFeatures is null || features.ExplicitFeatures.Count == 0)
            && (features.JoiningForms is null || features.JoiningForms.Count == 0)
            && (features.UseCategories is null || features.UseCategories.Count == 0);

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

                // The Universal Shaping Engine's own "standard typographic presentation" group -
                // applied AFTER reordering (see ApplyUseShaping's own staging), so it belongs in this
                // general, font-LookupList-ordered pass rather than ApplyUseShaping's own dedicated
                // pre-reorder stages.
                if (key.UseCategories is { Count: > 0 })
                {
                    defaultTags.Add("abvs"); defaultTags.Add("blws"); defaultTags.Add("haln");
                    defaultTags.Add("pres"); defaultTags.Add("psts");
                }

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
                IReadOnlyList<string> scriptPreference = ResolveScriptPreference(key.ScriptTag);

                var result = new SortedDictionary<int, int>();
                if (defaultTags.Count > 0)
                {
                    foreach (int lookupIndex in gsub.GetActiveLookupIndices(scriptPreference, languageTag, defaultTags))
                        result[lookupIndex] = 0;
                }

                if (customAltIndexByTag is not null)
                {
                    foreach ((string tag, int altIndex) in customAltIndexByTag)
                    {
                        foreach (int lookupIndex in gsub.GetActiveLookupIndices(scriptPreference, languageTag, new HashSet<string> { tag }))
                            result[lookupIndex] = altIndex;
                    }
                }

                return result;
            });
        }

        private static readonly IReadOnlyDictionary<ArabicJoiningForm, string> JoiningFormTags = new Dictionary<ArabicJoiningForm, string>
        {
            [ArabicJoiningForm.Isol] = "isol",
            [ArabicJoiningForm.Fina] = "fina",
            [ArabicJoiningForm.Fin2] = "fin2",
            [ArabicJoiningForm.Fin3] = "fin3",
            [ArabicJoiningForm.Medi] = "medi",
            [ArabicJoiningForm.Med2] = "med2",
            [ArabicJoiningForm.Init] = "init",
        };

        /// <summary>
        /// Applies each participating glyph's own resolved <see cref="ArabicJoiningForm"/> (see
        /// <see cref="ArabicJoiningShaper"/>) as a GSUB substitution - <c>isol</c>/<c>fina</c>/<c>fin2</c>/
        /// <c>fin3</c>/<c>medi</c>/<c>med2</c>/<c>init</c> are conventionally Lookup Type 1 (a 1:1 glyph
        /// swap), but a real font can equally implement one as Lookup Type 2 (Multiple Substitution)
        /// with a single-glyph output sequence - confirmed directly against Noto Sans Arabic, whose own
        /// <c>init</c>/<c>medi</c>/<c>fina</c> lookups are Type 2 despite being semantically 1:1 - so
        /// both are handled here. <paramref name="formsByClusterStart"/> is keyed by each source
        /// codepoint's own UTF-16 offset (<see cref="ShapedGlyph.ClusterStart"/>) rather than by glyph
        /// position, since <paramref name="glyphs"/> may already have been expanded by an earlier stage
        /// (the <c>ccmp</c>/<c>locl</c> pre-stage <see cref="Shape"/> runs immediately before this) - a
        /// glyph with <see cref="ShapedGlyph.ClusterLength"/> 0 is one of that expansion's own trailing
        /// output glyphs (e.g. a mark split off a decomposed base letter), never itself a joining
        /// position, so it's skipped rather than mis-keyed against the wrong source codepoint entirely.
        /// A font whose positional forms are (unusually) implemented as a contextual/chaining lookup
        /// instead of a plain Type 1/2 swap silently gets no substitution here - a documented,
        /// narrower-than-ideal v1 scope (see <c>.claude/accepted-gaps</c>), not a crash.
        /// </summary>
        // internal rather than private: lets tests exercise this directly against a synthetic GsubTable
        // + hand-built glyph list, bypassing cmap/real-text shaping - same rationale as
        // ApplySequenceContextLookup's own internal visibility.
        internal static void ApplyArabicJoiningFeatures(GsubTable gsub, List<ShapedGlyph> glyphs, IReadOnlyDictionary<int, ArabicJoiningForm> formsByClusterStart, string? languageTag, IReadOnlyList<string> scriptPreference)
        {
            // Resolve each requested tag's active lookups once, not once per position.
            Dictionary<string, IReadOnlyList<int>>? lookupsByTag = null;

            for (var pos = 0; pos < glyphs.Count; pos++)
            {
                var glyph = glyphs[pos];
                if (glyph.ClusterLength == 0)
                    continue;
                if (!formsByClusterStart.TryGetValue(glyph.ClusterStart, out var form) || form == ArabicJoiningForm.None)
                    continue;
                if (!JoiningFormTags.TryGetValue(form, out var tag))
                    continue;

                lookupsByTag ??= new Dictionary<string, IReadOnlyList<int>>();
                if (!lookupsByTag.TryGetValue(tag, out var lookupIndices))
                {
                    var resolved = new List<int>(gsub.GetActiveLookupIndices(scriptPreference, languageTag, new HashSet<string> { tag }));
                    lookupsByTag[tag] = lookupIndices = resolved;
                }

                foreach (var lookupIndex in lookupIndices)
                {
                    switch (gsub.GetResolvedLookupType(lookupIndex))
                    {
                        case 1:
                            if (gsub.GetSingleSubstitutionLookup(lookupIndex) is { } singleSub)
                                ApplySingleSubstitutionAt(singleSub, glyphs, pos);
                            break;
                        case 2:
                            // A further expansion here (unusual, but not spec-forbidden) inserts its own
                            // trailing zero-length-cluster glyphs right after `pos`, exactly like the
                            // ccmp pre-stage's own decomposition does - this loop's own ClusterLength==0
                            // skip above handles them the same way on a later iteration, so no offset
                            // bookkeeping is needed here the way a purely positional design would need.
                            if (gsub.GetMultipleSubstitutionLookup(lookupIndex) is { } multiSub)
                                ApplyMultipleSubstitutionAt(multiSub, glyphs, pos);
                            break;
                    }
                }
            }
        }

        /// <summary>The Universal Shaping Engine's own "default glyph pre-processing" feature
        /// tags - applied globally rather than per-syllable-masked (see <see cref="ApplyUseShaping"/>'s
        /// own remarks on why that's an acceptable v1 simplification for well-formed text).</summary>
        private static readonly IReadOnlySet<string> UseNuktaCcmpLoclAkhnTags = new HashSet<string> { "nukt", "ccmp", "locl", "akhn" };

        /// <summary>The `rphf` (Reph Form) feature tag - see <see cref="TryApplyRphf"/>.</summary>
        private static readonly IReadOnlySet<string> RphfTags = new HashSet<string> { "rphf" };

        /// <summary>The Universal Shaping Engine's own "orthographic unit shaping" feature tags
        /// (conjunct/half-form/subjoined-form formation) - applied globally, before
        /// <see cref="UseReorderer"/> runs, matching HarfBuzz's own <c>collect_features_use</c>
        /// staging.</summary>
        private static readonly IReadOnlySet<string> UseBasicFeatureTags = new HashSet<string>
        {
            "rkrf", "abvf", "blwf", "half", "pstf", "vatu", "cjct",
        };

        /// <summary>
        /// Applies the Universal Shaping Engine's own pre-reorder GSUB stages, then reorders the
        /// resulting glyphs - Devanagari/Bengali/Gujarati/Tamil's own use of HarfBuzz's
        /// <c>collect_features_use</c>/<c>reorder_use</c> pipeline (ported per
        /// <see cref="UseCategoryClassifier"/>/<see cref="UseSyllableScanner"/>/
        /// <see cref="UseReorderer"/>'s own remarks), reduced to the stages this four-script-scoped port
        /// actually needs: default glyph pre-processing (<c>locl</c>/<c>ccmp</c>/<c>nukt</c>/<c>akhn</c>),
        /// reph formation (<c>rphf</c>, tried once at each syllable's own start - see
        /// <see cref="TryApplyRphf"/> for why this needs no general OpenType-mask mechanism), the 7
        /// "orthographic unit shaping" features (conjunct/half-form formation), and finally the glyph
        /// reorder itself. The remaining two HarfBuzz stages - <c>pref</c> (pre-base-reordering
        /// consonants) and the topographical (<c>isol</c>/<c>init</c>/<c>medi</c>/<c>fina</c>) features
        /// - are both skipped: none of these four scripts has a codepoint that classifies as a
        /// pre-base-reordering consonant at all (see <see cref="UseCategoryClassifier"/>'s own
        /// remarks), and the topographical features exist for scripts that share Arabic's own
        /// joining-form model, which none of these four use. The
        /// final "standard typographic presentation" group (<c>abvs</c>/<c>blws</c>/<c>haln</c>/
        /// <c>pres</c>/<c>psts</c>) runs afterward, folded into <see cref="Shape"/>'s own ordered
        /// general feature pass (see <see cref="GetActiveLookupIndices(GsubTable, TextShapingFeatures)"/>'s
        /// own <c>UseCategories</c> check) rather than applied here directly - unlike <c>rphf</c>/the
        /// basic features, HarfBuzz runs this group after clearing per-syllable state entirely, so it
        /// has no per-syllable masking concern this method's own stages need to replicate.
        ///
        /// Every stage here keys a glyph's own semantic content by <see cref="ShapedGlyph.ClusterStart"/>,
        /// never by raw glyph-list position - the same technique <see cref="ApplyArabicJoiningFeatures"/>
        /// uses and for the identical reason: an earlier stage (nukt/ccmp composing or decomposing a
        /// glyph, rphf/the basic features merging a conjunct into one ligature glyph) can change
        /// <paramref name="glyphs"/>'s own count before a later stage runs, and ClusterStart is what
        /// keeps every later stage pointed at the right semantic content regardless.
        /// </summary>
        private static void ApplyUseShaping(GsubTable gsub, List<ShapedGlyph> glyphs, IReadOnlyList<UseCategory> useCategories,
            string? languageTag, IReadOnlyList<string> scriptPreference, GdefTable? gdef)
        {
            // Snapshot: ClusterStart -> initial category, computed before any substitution in this
            // stage runs (mirrors ApplyArabicJoiningFeatures' own formsByClusterStart exactly).
            var categoryByClusterStart = new Dictionary<int, UseCategory>();
            for (var i = 0; i < glyphs.Count && i < useCategories.Count; i++)
                categoryByClusterStart[glyphs[i].ClusterStart] = useCategories[i];

            // Syllable boundaries are computed once, over the initial (pre-substitution) category
            // sequence - matching HarfBuzz's own setup_syllables_use, which runs before any lookup at
            // all (add_gsub_pause(setup_syllables_use) is the very first thing collect_features_use
            // does). Recorded as ClusterStart ranges (not raw indices), so they too survive later
            // glyph-count changes exactly like categoryByClusterStart does.
            List<UseSyllable> initialSyllables = UseSyllableScanner.Scan(useCategories);
            var syllableRanges = new (int ClusterStart, int ClusterEnd, UseSyllableType Type)[initialSyllables.Count];
            for (var s = 0; s < initialSyllables.Count; s++)
            {
                UseSyllable syllable = initialSyllables[s];
                int clusterStart = glyphs[syllable.Start].ClusterStart;
                int clusterEnd = syllable.Start + syllable.Length < glyphs.Count
                    ? glyphs[syllable.Start + syllable.Length].ClusterStart
                    : int.MaxValue;
                syllableRanges[s] = (clusterStart, clusterEnd, syllable.Type);
            }

            // Stage: default glyph pre-processing - applied globally rather than per-syllable-masked.
            // HarfBuzz masks this (and the basic features below) to each syllable's own span mainly so
            // one syllable's substitution can never reach into its neighbor's glyphs; for well-formed
            // text a font's own coverage/context tables already only match the sequences they're
            // authored for, so applying globally produces the same result in practice - a documented,
            // narrower-than-ideal v1 simplification rather than building a general OpenType-mask
            // mechanism this codebase has no other use for yet.
            foreach (int lookupIndex in gsub.GetActiveLookupIndices(scriptPreference, languageTag, UseNuktaCcmpLoclAkhnTags))
                ApplyLookup(gsub, lookupIndex, alternateIndex: 0, glyphs, gdef);

            // Stage: rphf, tried once at each syllable's own current start position - retagging that
            // position's category to R on success, mirroring record_rphf_use exactly (the first
            // masked glyph the GSUB engine actually substituted becomes the reph).
            foreach ((int clusterStart, _, _) in syllableRanges)
            {
                int startIndex = FindGlyphIndexByClusterStart(glyphs, clusterStart);
                if (startIndex < 0)
                    continue;
                if (TryApplyRphf(gsub, glyphs, startIndex, gdef, languageTag, scriptPreference))
                    categoryByClusterStart[glyphs[startIndex].ClusterStart] = UseCategory.R;
            }

            // Stage: the 7 "orthographic unit shaping" features - global, same rationale as the
            // pre-processing stage above.
            foreach (int lookupIndex in gsub.GetActiveLookupIndices(scriptPreference, languageTag, UseBasicFeatureTags))
                ApplyLookup(gsub, lookupIndex, alternateIndex: 0, glyphs, gdef);

            // Stage: reorder. Re-derive each CURRENT glyph's category from its own ClusterStart (never
            // stale positional data - see this method's own remarks), and each syllable's CURRENT
            // [start, length) span from its own ClusterStart range, then run the two-pass reorder over
            // exactly that. A glyph with ClusterLength 0 (a Multiple Substitution's own trailing output
            // glyph - see ApplyMultipleSubstitutionAt's convention) always resolves to O rather than
            // whatever categoryByClusterStart happens to hold for its ClusterStart: that ClusterStart is
            // by construction the very next source character's own offset (original.ClusterStart +
            // original.ClusterLength), so looking it up here would silently borrow a neighboring
            // syllable's real category for an unrelated expansion artifact - the same ClusterLength == 0
            // skip ApplyArabicJoiningFeatures already applies for the identical hazard (see its own
            // remarks), applied here to the category re-derivation instead of a substitution guard.
            var currentCategories = new UseCategory[glyphs.Count];
            for (var i = 0; i < glyphs.Count; i++)
                currentCategories[i] = glyphs[i].ClusterLength > 0 && categoryByClusterStart.TryGetValue(glyphs[i].ClusterStart, out var category)
                    ? category
                    : UseCategory.O;

            var currentSyllables = new List<UseSyllable>(syllableRanges.Length);
            foreach ((int clusterStart, int clusterEnd, UseSyllableType type) in syllableRanges)
            {
                int start = FindGlyphIndexByClusterStart(glyphs, clusterStart);
                if (start < 0)
                    continue;
                int end = clusterEnd == int.MaxValue ? glyphs.Count : FindGlyphIndexByClusterStart(glyphs, clusterEnd);
                if (end < 0)
                    end = glyphs.Count;
                if (end > start)
                    currentSyllables.Add(new UseSyllable(start, end - start, type));
            }

            UseReorderer.ReorderAll(glyphs, currentCategories, currentSyllables);
        }

        /// <summary>
        /// Applies the font's own `rphf` feature at exactly <paramref name="start"/> - never scanning
        /// further into the run - mirroring HarfBuzz's own <c>setup_rphf_mask</c> (which masks `rphf`
        /// to a syllable's own leading up-to-3 glyphs) without needing a general OpenType-mask
        /// mechanism: since a real font's `rphf` rule only ever needs to try matching starting at one
        /// specific position (a syllable's own start), restricting the search itself to that position
        /// achieves the identical observable result with no masking infrastructure at all. Returns
        /// true if a substitution actually fired (mirroring <c>record_rphf_use</c>'s own check via
        /// HarfBuzz's <c>_hb_glyph_info_substituted</c> bit), in which case the caller retags that
        /// position's category to <see cref="UseCategory.R"/>.
        ///
        /// Only Lookup Type 1 (single) and Type 4 (ligature) are tried, matching
        /// <see cref="ApplyArabicJoiningFeatures"/>'s own identical, documented v1 scope limit - a
        /// font whose `rphf` is (unusually) implemented as a contextual/chaining lookup instead
        /// silently produces no reph.
        /// </summary>
        private static bool TryApplyRphf(GsubTable gsub, List<ShapedGlyph> glyphs, int start, GdefTable? gdef,
            string? languageTag, IReadOnlyList<string> scriptPreference)
        {
            foreach (int lookupIndex in gsub.GetActiveLookupIndices(scriptPreference, languageTag, RphfTags))
            {
                switch (gsub.GetResolvedLookupType(lookupIndex))
                {
                    case 1:
                        if (gsub.GetSingleSubstitutionLookup(lookupIndex) is { } single)
                        {
                            int before = glyphs[start].GlyphIndex;
                            ApplySingleSubstitutionAt(single, glyphs, start);
                            if (glyphs[start].GlyphIndex != before)
                                return true;
                        }
                        break;
                    case 4:
                        if (gsub.GetLigatureLookup(lookupIndex) is { } ligature && ApplyLigatureAt(ligature, glyphs, start, gdef) > 0)
                            return true;
                        break;
                }
            }
            return false;
        }

        /// <summary>
        /// Finds the glyph that genuinely represents source position <paramref name="clusterStart"/> -
        /// skipping any glyph with <see cref="ShapedGlyph.ClusterLength"/> 0, since a Multiple
        /// Substitution's own trailing output glyph carries the *next* source character's own
        /// ClusterStart (see <c>ApplyMultipleSubstitutionAt</c>'s convention), not its own; without this
        /// skip, such a glyph could be returned instead of the real next-syllable-start glyph whenever
        /// the two happen to share that value, corrupting the caller's own syllable-boundary resolution.
        /// </summary>
        private static int FindGlyphIndexByClusterStart(List<ShapedGlyph> glyphs, int clusterStart)
        {
            for (var i = 0; i < glyphs.Count; i++)
                if (glyphs[i].ClusterStart == clusterStart && glyphs[i].ClusterLength > 0)
                    return i;
            return -1;
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

                    // Component 0 is the coverage-matched first glyph; components 1..N are each
                    // matched component's own real glyph-list position, in the same order
                    // TryMatchLigature just matched them - see ShapedGlyph.LigatureComponentClusterStarts.
                    var componentClusterStarts = new int[matched.Count + 1];
                    componentClusterStarts[0] = first.ClusterStart;
                    for (int c = 0; c < matched.Count; c++)
                        componentClusterStarts[c + 1] = glyphs[matched[c]].ClusterStart;

                    merged = new ShapedGlyph(ligature.LigatureGlyph, first.ClusterStart, last.ClusterStart + last.ClusterLength - first.ClusterStart,
                        LigatureComponentClusterStarts: componentClusterStarts);
                    skippedOffsets = skipped;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Applies one Lookup Type 8 (Reverse Chaining Context Single Substitution) lookup - the one
        /// GSUB lookup type specified to process its input glyphs end-to-start rather than
        /// start-to-end (per spec: "the input glyph sequence is processed from the end of the string
        /// to the start"), substituting each coverage-matched position whose backtrack/lookahead
        /// context also matches directly (no nested <c>SequenceLookupRecord</c>s - the substitute
        /// glyph id is read straight off <see cref="GsubReverseChainSingleSubstSubtable.SubstituteGlyphIds"/>,
        /// parallel to the matched position's own Coverage index). Reuses the same skip-aware
        /// backtrack/lookahead walk Types 5/6 use (<see cref="FindParticipatingIndices"/>), so an
        /// intervening non-participating glyph (per <paramref name="lookup"/>'s own `lookupFlag`/GDEF
        /// filtering) is skipped the same way. Never changes
        /// <paramref name="glyphs"/>'s count, so the end-to-start walk needs no index reconciliation
        /// as earlier (higher-index) positions are substituted.
        /// </summary>
        internal static void ApplyReverseChainSingleSubstitutionLookup(GsubReverseChainSingleSubstLookup lookup, List<ShapedGlyph> glyphs, GdefTable? gdef, CoverageTable? markFilteringSet)
        {
            for (int i = glyphs.Count - 1; i >= 0; i--)
            {
                ushort glyphId = (ushort)glyphs[i].GlyphIndex;

                foreach (GsubReverseChainSingleSubstSubtable subtable in lookup.Subtables)
                {
                    int coverageIndex = subtable.Coverage.IndexOfGlyph(glyphId);
                    if (coverageIndex < 0 || coverageIndex >= subtable.SubstituteGlyphIds.Length)
                        continue;

                    if (FindParticipatingIndices(glyphs, i - 1, -1, subtable.BacktrackCoverages.Length, lookup.LookupFlag, gdef, markFilteringSet) is not { } backtrackIndices)
                        continue;
                    bool backtrackMatches = true;
                    for (int k = 0; k < subtable.BacktrackCoverages.Length && backtrackMatches; k++)
                        backtrackMatches = subtable.BacktrackCoverages[k].IndexOfGlyph((ushort)glyphs[backtrackIndices[k]].GlyphIndex) >= 0;
                    if (!backtrackMatches)
                        continue;

                    if (FindParticipatingIndices(glyphs, i + 1, +1, subtable.LookaheadCoverages.Length, lookup.LookupFlag, gdef, markFilteringSet) is not { } lookaheadIndices)
                        continue;
                    bool lookaheadMatches = true;
                    for (int k = 0; k < subtable.LookaheadCoverages.Length && lookaheadMatches; k++)
                        lookaheadMatches = subtable.LookaheadCoverages[k].IndexOfGlyph((ushort)glyphs[lookaheadIndices[k]].GlyphIndex) >= 0;
                    if (!lookaheadMatches)
                        continue;

                    // GlyphIndex=null clears any stale LigatureComponentClusterStarts a `with` would
                    // otherwise carry forward from whatever this position held before - the substitute
                    // glyph is no longer the ligature merge (if any) that bookkeeping described.
                    glyphs[i] = glyphs[i] with { GlyphIndex = subtable.SubstituteGlyphIds[coverageIndex], LigatureComponentClusterStarts = null };
                    break;
                }
            }
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
                    // See ApplyReverseChainSingleSubstitutionLookup's identical comment on why
                    // LigatureComponentClusterStarts must be cleared, not merely left as-is, by `with`.
                    glyphs[i] = glyphs[i] with { GlyphIndex = substitute, LigatureComponentClusterStarts = null };
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
                    // See ApplyReverseChainSingleSubstitutionLookup's identical comment on why
                    // LigatureComponentClusterStarts must be cleared, not merely left as-is, by `with`.
                    glyphs[i] = glyphs[i] with { GlyphIndex = substitute, LigatureComponentClusterStarts = null };
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
        /// (adjusted for any glyph count change a nested substitution made, and for any
        /// non-participating glyph - per <paramref name="lookupFlag"/>/GDEF - skipped over while
        /// matching). Lookup Types 5 and 6 share this one implementation since
        /// <see cref="GsubSequenceContextSubtable"/> already represents a non-chaining rule as one
        /// with empty backtrack/lookahead. The *outer* per-position walk below (which positions are
        /// even tried as a match anchor) is not itself skip-aware - only the inner backtrack/input/
        /// lookahead walk is - mirroring <see cref="ApplyLigatureLookup"/>'s own precedent.
        /// </summary>
        // internal rather than private - see ApplyMultipleSubstitutionLookup's identical rationale.
        internal static void ApplySequenceContextLookup(GsubTable gsub, IReadOnlyList<GsubSequenceContextSubtable> subtables,
            List<ShapedGlyph> glyphs, GdefTable? gdef, ushort lookupFlag, CoverageTable? markFilteringSet)
        {
            int i = 0;
            while (i < glyphs.Count)
            {
                int consumed = TryApplySequenceContextAt(gsub, subtables, glyphs, i, gdef, lookupFlag, markFilteringSet, depth: 0);
                i += consumed > 0 ? consumed : 1;
            }
        }

        /// <summary>
        /// Tries every subtable in <paramref name="subtables"/> (in lookup order) against
        /// <paramref name="pos"/>, applying the first one that matches - the single implementation
        /// shared by a lookup's own top-level per-position walk (<see cref="ApplySequenceContextLookup"/>,
        /// always <paramref name="depth"/> 0) and a <c>SequenceLookupRecord</c> that targets another
        /// contextual/chaining-context lookup (<see cref="ApplyNestedLookup"/>'s own case 5/6, one
        /// <paramref name="depth"/> deeper) - real fonts commonly resolve a matra/consonant's final
        /// presentation form through exactly this kind of lookup-referencing-another-contextual-lookup
        /// chain (e.g. a font's own class-based `abvs` rule narrowing to a specific glyph variant only
        /// after a second, independently-classed contextual lookup narrows further - see this
        /// feature's own recent-fixes entry for a real Gujarati font that needs two levels of this to
        /// pick the correct pre-base-matra glyph), which <see cref="ApplyMatchedLookups"/>'s own
        /// <paramref name="depth"/> guard against runaway recursion already accounts for.
        /// </summary>
        private static int TryApplySequenceContextAt(GsubTable gsub, IReadOnlyList<GsubSequenceContextSubtable> subtables,
            List<ShapedGlyph> glyphs, int pos, GdefTable? gdef, ushort lookupFlag, CoverageTable? markFilteringSet, int depth)
        {
            foreach (GsubSequenceContextSubtable subtable in subtables)
            {
                if (TryMatchSequenceContext(subtable, glyphs, pos, lookupFlag, gdef, markFilteringSet) is not
                    (int[] inputIndices, GsubSequenceLookupRecord[] records))
                    continue;

                int[] finalIndices = ApplyMatchedLookups(gsub, glyphs, inputIndices, records, depth, gdef);
                int lastIndex = finalIndices.Length > 0 ? finalIndices[^1] : pos;
                return Math.Max(1, lastIndex - pos + 1);
            }

            return 0;
        }

        /// <summary>Real glyph-list indices of every matched input position (index 0 is always
        /// <c>pos</c> itself - the outer walk's own anchor is never skip-adjusted), or null if no
        /// rule in <paramref name="subtable"/> matches at <paramref name="pos"/>.</summary>
        private static (int[] InputIndices, GsubSequenceLookupRecord[] Records)? TryMatchSequenceContext(
            GsubSequenceContextSubtable subtable, List<ShapedGlyph> glyphs, int pos, ushort lookupFlag, GdefTable? gdef, CoverageTable? markFilteringSet)
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
                        if (TryMatchRule(rule, glyphs, pos, matchGlyph: true, null, null, null, lookupFlag, gdef, markFilteringSet) is { } indices)
                            return (indices, rule.SeqLookupRecords);
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
                        if (TryMatchRule(rule, glyphs, pos, matchGlyph: false, inputClassDef, subtable.BacktrackClassDef, subtable.LookaheadClassDef,
                                lookupFlag, gdef, markFilteringSet) is { } indices)
                            return (indices, rule.SeqLookupRecords);
                    }
                    return null;
                }

                case GsubSequenceContextFormat.Coverage:
                {
                    if (subtable.InputCoverages is not { } inputCoverages || subtable.SeqLookupRecords is not { } records)
                        return null;
                    if (TryMatchCoverageSequence(subtable.BacktrackCoverages, inputCoverages, subtable.LookaheadCoverages, glyphs, pos,
                            lookupFlag, gdef, markFilteringSet) is not { } coverageIndices)
                        return null;
                    return (coverageIndices, records);
                }

                default:
                    return null;
            }
        }

        /// <summary>
        /// Matches <paramref name="rule"/> against <paramref name="glyphs"/> starting at
        /// <paramref name="pos"/> (already known to satisfy the rule's first input position via the
        /// owning subtable's own Coverage/ClassDef test), walking backtrack/input/lookahead through
        /// <see cref="FindParticipatingIndices"/> so an intervening glyph that doesn't participate
        /// under <paramref name="lookupFlag"/>/GDEF (e.g. a mark) is skipped rather than counted as
        /// its own position - mirroring <see cref="TryMatchLigature"/>'s own mark-skipping. Returns
        /// the real glyph-list index of every input position (index 0 is <paramref name="pos"/>
        /// itself), or null if the rule doesn't match.
        /// </summary>
        private static int[]? TryMatchRule(GsubSequenceRule rule, List<ShapedGlyph> glyphs, int pos, bool matchGlyph,
            ClassDefTable? inputClassDef, ClassDefTable? backtrackClassDef, ClassDefTable? lookaheadClassDef,
            ushort lookupFlag, GdefTable? gdef, CoverageTable? markFilteringSet)
        {
            // Backtrack is stored in reverse logical order: rule.Backtrack[0] is the participating
            // glyph immediately before `pos`, [1] is the one before that, and so on.
            if (FindParticipatingIndices(glyphs, pos - 1, -1, rule.Backtrack.Length, lookupFlag, gdef, markFilteringSet) is not { } backtrackIndices)
                return null;
            for (int k = 0; k < rule.Backtrack.Length; k++)
            {
                if (!MatchesRulePosition(glyphs[backtrackIndices[k]].GlyphIndex, rule.Backtrack[k], matchGlyph, backtrackClassDef))
                    return null;
            }

            // Input positions after the first (which the caller already matched via Coverage/ClassDef).
            var inputIndices = new int[rule.Input.Length + 1];
            inputIndices[0] = pos;
            if (FindParticipatingIndices(glyphs, pos + 1, +1, rule.Input.Length, lookupFlag, gdef, markFilteringSet) is not { } restInput)
                return null;
            for (int k = 0; k < rule.Input.Length; k++)
            {
                if (!MatchesRulePosition(glyphs[restInput[k]].GlyphIndex, rule.Input[k], matchGlyph, inputClassDef))
                    return null;
                inputIndices[k + 1] = restInput[k];
            }

            // Lookahead begins immediately after the last matched input position, in forward logical order.
            int lookaheadStart = inputIndices[^1] + 1;
            if (FindParticipatingIndices(glyphs, lookaheadStart, +1, rule.Lookahead.Length, lookupFlag, gdef, markFilteringSet) is not { } lookaheadIndices)
                return null;
            for (int k = 0; k < rule.Lookahead.Length; k++)
            {
                if (!MatchesRulePosition(glyphs[lookaheadIndices[k]].GlyphIndex, rule.Lookahead[k], matchGlyph, lookaheadClassDef))
                    return null;
            }

            return inputIndices;
        }

        private static bool MatchesRulePosition(int glyphIndex, ushort expected, bool matchGlyph, ClassDefTable? classDef)
            => matchGlyph ? glyphIndex == expected : classDef is not null && classDef.GetClass((ushort)glyphIndex) == expected;

        /// <summary>Walks from <paramref name="start"/> in <paramref name="direction"/> (+1/-1) over
        /// <paramref name="glyphs"/>, skipping any glyph that doesn't participate under
        /// <paramref name="lookupFlag"/>/GDEF (see <see cref="GlyphSequenceFilter.Participates"/>),
        /// collecting the real glyph-list index of each of the next <paramref name="count"/>
        /// participating glyphs found. Returns null if the walk runs off either end of
        /// <paramref name="glyphs"/> before finding all <paramref name="count"/> positions (or
        /// immediately, if <paramref name="count"/> is 0). Internal (not private) so
        /// <see cref="GposPositioner"/>'s own Type 7/8 contextual-positioning matcher can reuse this
        /// same skip-aware walk - it operates purely on <see cref="ShapedGlyph"/>/`lookupFlag`/GDEF,
        /// with nothing GSUB-specific about it.</summary>
        internal static int[]? FindParticipatingIndices(List<ShapedGlyph> glyphs, int start, int direction, int count,
            ushort lookupFlag, GdefTable? gdef, CoverageTable? markFilteringSet)
        {
            if (count == 0)
                return [];

            var result = new int[count];
            int pos = start;
            int found = 0;
            while (found < count)
            {
                if (pos < 0 || pos >= glyphs.Count)
                    return null;

                if (GlyphSequenceFilter.Participates((ushort)glyphs[pos].GlyphIndex, lookupFlag, gdef, markFilteringSet))
                {
                    result[found] = pos;
                    found++;
                }

                pos += direction;
            }

            return result;
        }

        /// <summary>Format 3's per-position Coverage matching, walked the same skip-aware way as
        /// <see cref="TryMatchRule"/> (format 3 has no rule-set indirection - every position,
        /// including the first, is its own <see cref="CoverageTable"/>). Returns the real glyph-list
        /// index of every input position, or null if the sequence doesn't match.</summary>
        private static int[]? TryMatchCoverageSequence(
            CoverageTable[]? backtrack, CoverageTable[] input, CoverageTable[]? lookahead, List<ShapedGlyph> glyphs, int pos,
            ushort lookupFlag, GdefTable? gdef, CoverageTable? markFilteringSet)
        {
            backtrack ??= [];
            lookahead ??= [];

            if (FindParticipatingIndices(glyphs, pos - 1, -1, backtrack.Length, lookupFlag, gdef, markFilteringSet) is not { } backtrackIndices)
                return null;
            for (int k = 0; k < backtrack.Length; k++)
            {
                if (backtrack[k].IndexOfGlyph((ushort)glyphs[backtrackIndices[k]].GlyphIndex) < 0)
                    return null;
            }

            if (input.Length == 0 || pos >= glyphs.Count || input[0].IndexOfGlyph((ushort)glyphs[pos].GlyphIndex) < 0)
                return null;

            var inputIndices = new int[input.Length];
            inputIndices[0] = pos;
            if (input.Length > 1)
            {
                if (FindParticipatingIndices(glyphs, pos + 1, +1, input.Length - 1, lookupFlag, gdef, markFilteringSet) is not { } restInput)
                    return null;
                for (int k = 1; k < input.Length; k++)
                {
                    if (input[k].IndexOfGlyph((ushort)glyphs[restInput[k - 1]].GlyphIndex) < 0)
                        return null;
                    inputIndices[k] = restInput[k - 1];
                }
            }

            int lookaheadStart = inputIndices[^1] + 1;
            if (FindParticipatingIndices(glyphs, lookaheadStart, +1, lookahead.Length, lookupFlag, gdef, markFilteringSet) is not { } lookaheadIndices)
                return null;
            for (int k = 0; k < lookahead.Length; k++)
            {
                if (lookahead[k].IndexOfGlyph((ushort)glyphs[lookaheadIndices[k]].GlyphIndex) < 0)
                    return null;
            }

            return inputIndices;
        }

        /// <summary>
        /// Applies <paramref name="records"/> (in the order given, per spec) to the glyph sequence
        /// whose matched input positions' real glyph-list indices are <paramref name="inputIndices"/>
        /// (not necessarily contiguous - a non-participating glyph, e.g. a mark, may sit between two
        /// matched input positions) - each record's <c>SequenceIndex</c> is resolved against the
        /// *current* real glyph-list index of that original input position, re-derived after every
        /// earlier record's own application, since a nested Ligature/Multiple Substitution (or a
        /// nested contextual lookup that itself nests one) changes the glyph count and shifts every
        /// later position. Returns the (possibly further-shifted) real indices, so the caller can
        /// resume scanning immediately after the last one. Nested lookup types 1/2/3/4/5/6 are all
        /// supported (see <see cref="ApplyNestedLookup"/>) - real fonts routinely resolve a matra's
        /// final presentation form through a chain of contextual lookups each narrowing by a
        /// differently-classed context (see that method's own remarks) - guarded by
        /// <paramref name="depth"/> against a pathological/adversarial font nesting indefinitely.
        /// </summary>
        private static int[] ApplyMatchedLookups(GsubTable gsub, List<ShapedGlyph> glyphs, int[] inputIndices,
            GsubSequenceLookupRecord[] records, int depth, GdefTable? gdef)
        {
            if (depth >= MaxNestedContextDepth || inputIndices.Length == 0)
                return inputIndices;

            var slotIndex = (int[])inputIndices.Clone();

            foreach (GsubSequenceLookupRecord record in records)
            {
                if (record.SequenceIndex < 0 || record.SequenceIndex >= slotIndex.Length)
                    continue;

                int realIndex = slotIndex[record.SequenceIndex];
                if (realIndex < 0 || realIndex >= glyphs.Count)
                    continue;

                int countBefore = glyphs.Count;
                ApplyNestedLookup(gsub, glyphs, realIndex, record.LookupListIndex, depth, gdef);
                int delta = glyphs.Count - countBefore;

                if (delta != 0)
                {
                    for (int s = 0; s < slotIndex.Length; s++)
                    {
                        if (slotIndex[s] > realIndex)
                            slotIndex[s] += delta;
                    }
                }
            }

            return slotIndex;
        }

        /// <summary>
        /// Applies one <c>SequenceLookupRecord</c>'s own target lookup at the single matched
        /// <paramref name="position"/> it names. Lookup types 1/2/3/4 (single/multiple/alternate/
        /// ligature substitution) apply directly; types 5/6 (contextual/chaining-context
        /// substitution) recurse into <see cref="TryApplySequenceContextAt"/> at that exact position
        /// with <paramref name="depth"/> incremented - a real, spec-legal, and real-font-exercised
        /// pattern: a font can resolve one glyph's final presentation form through a *chain* of
        /// contextual lookups, each with its own independent <c>ClassDef</c> narrowing the same
        /// coverage glyph by a progressively more specific context (found via a real Noto Sans
        /// Gujarati font whose `abvs` feature needs exactly two such nested levels to pick a
        /// pre-base matra's correct contextual glyph variant - see this feature's own recent-fixes
        /// entry). Type 8 (reverse chaining) and an unresolved lookup type are not supported as a
        /// nested target - left unmodified, matching this file's existing "unsupported type is
        /// silently skipped" convention.
        /// </summary>
        private static void ApplyNestedLookup(GsubTable gsub, List<ShapedGlyph> glyphs, int position, int lookupListIndex, int depth, GdefTable? gdef)
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
                case 5:
                    if (gsub.GetContextualLookup(lookupListIndex) is { } nestedContextual)
                    {
                        CoverageTable? mfs = nestedContextual.MarkFilteringSetIndex is { } m ? gdef?.GetMarkGlyphSet(m) : null;
                        TryApplySequenceContextAt(gsub, nestedContextual.Subtables, glyphs, position, gdef, nestedContextual.LookupFlag, mfs, depth + 1);
                    }
                    break;
                case 6:
                    if (gsub.GetChainingContextLookup(lookupListIndex) is { } nestedChaining)
                    {
                        CoverageTable? mfs = nestedChaining.MarkFilteringSetIndex is { } m ? gdef?.GetMarkGlyphSet(m) : null;
                        TryApplySequenceContextAt(gsub, nestedChaining.Subtables, glyphs, position, gdef, nestedChaining.LookupFlag, mfs, depth + 1);
                    }
                    break;
                // 8, unresolved: not supported as a nested target - left unmodified.
            }
        }
    }
}
