using PeachDrawing.Text.Unicode;
using System;
using System.Collections.Generic;

namespace PeachDrawing.Text.Shaping
{
    /// <summary>The ligature features to apply when shaping (CSS <c>font-variant-ligatures</c>).</summary>
    [Flags]
    public enum LigatureSet
    {
        /// <summary>No ligature feature at all.</summary>
        None = 0,

        /// <summary>Common ligatures (<c>liga</c> and <c>clig</c>): what <c>font-variant-ligatures: common-ligatures</c> asks for, and what the initial value <c>normal</c> means.</summary>
        Common = 1,

        /// <summary>Required ligatures (<c>rlig</c>), which are applied whenever the font defines them, whatever the caller asked for, as browsers do.</summary>
        Required = 2,

        /// <summary>Discretionary ligatures (<c>dlig</c>), which are off unless asked for.</summary>
        Discretionary = 4,

        /// <summary>Historical ligatures (<c>hlig</c>), which are off unless asked for.</summary>
        Historical = 8,

        /// <summary>Contextual alternates (<c>calt</c>): on by default, and what <c>font-variant-ligatures: no-contextual</c> turns off.</summary>
        Contextual = 16,

        /// <summary>What CSS applies with no declaration: <see cref="Common"/>, <see cref="Required"/> and <see cref="Contextual"/>.</summary>
        Default = Common | Required | Contextual,
    }

    /// <summary>The caps feature to apply when shaping (CSS <c>font-variant-caps</c>), of which at most one applies.</summary>
    public enum CapsMode
    {
        /// <summary>No caps feature.</summary>
        None,

        /// <summary>Small capitals for lower-case letters (<c>smcp</c>).</summary>
        SmallCaps,

        /// <summary>Small capitals for upper-case letters too (<c>smcp</c> and <c>c2sc</c>).</summary>
        AllSmallCaps,

        /// <summary>Petite capitals for lower-case letters (<c>pcap</c>).</summary>
        PetiteCaps,

        /// <summary>Petite capitals for upper-case letters too (<c>pcap</c> and <c>c2pc</c>).</summary>
        AllPetiteCaps,

        /// <summary>Unicase, a mix of small capitals and capitals (<c>unic</c>).</summary>
        Unicase,

        /// <summary>Titling capitals (<c>titl</c>).</summary>
        TitlingCaps,
    }

    /// <summary>The subscript or superscript feature to apply when shaping (CSS <c>font-variant-position</c>), of which at most one applies.</summary>
    public enum SubSuperMode
    {
        /// <summary>Neither.</summary>
        None,

        /// <summary>Subscript glyphs (<c>subs</c>).</summary>
        Sub,

        /// <summary>Superscript glyphs (<c>sups</c>).</summary>
        Super,
    }

    /// <summary>The numeral features to apply when shaping (CSS <c>font-variant-numeric</c>).</summary>
    [Flags]
    public enum NumeralSet
    {
        /// <summary>No numeral feature.</summary>
        None = 0,

        /// <summary>Lining figures (<c>lnum</c>).</summary>
        LiningNums = 1 << 0,

        /// <summary>Old-style figures (<c>onum</c>).</summary>
        OldstyleNums = 1 << 1,

        /// <summary>Proportional figures (<c>pnum</c>).</summary>
        ProportionalNums = 1 << 2,

        /// <summary>Tabular figures (<c>tnum</c>).</summary>
        TabularNums = 1 << 3,

        /// <summary>Diagonal fractions (<c>frac</c>).</summary>
        DiagonalFractions = 1 << 4,

        /// <summary>Stacked fractions (<c>afrc</c>).</summary>
        StackedFractions = 1 << 5,

        /// <summary>Ordinal markers (<c>ordn</c>).</summary>
        Ordinal = 1 << 6,

        /// <summary>A slashed zero (<c>zero</c>).</summary>
        SlashedZero = 1 << 7,
    }

    /// <summary>The East Asian features to apply when shaping (CSS <c>font-variant-east-asian</c>).</summary>
    [Flags]
    public enum EastAsianSet
    {
        /// <summary>No East Asian feature.</summary>
        None = 0,

        /// <summary>JIS78 forms (<c>jp78</c>).</summary>
        Jis78 = 1 << 0,

        /// <summary>JIS83 forms (<c>jp83</c>).</summary>
        Jis83 = 1 << 1,

        /// <summary>JIS90 forms (<c>jp90</c>).</summary>
        Jis90 = 1 << 2,

        /// <summary>JIS04 forms (<c>jp04</c>).</summary>
        Jis04 = 1 << 3,

        /// <summary>Simplified forms (<c>smpl</c>).</summary>
        Simplified = 1 << 4,

        /// <summary>Traditional forms (<c>trad</c>).</summary>
        Traditional = 1 << 5,

        /// <summary>Full-width forms (<c>fwid</c>).</summary>
        FullWidth = 1 << 6,

        /// <summary>Proportional-width forms (<c>pwid</c>).</summary>
        ProportionalWidth = 1 << 7,

        /// <summary>Ruby forms (<c>ruby</c>).</summary>
        Ruby = 1 << 8,
    }

    /// <summary>
    /// One OpenType feature a caller asks for by its tag, in the way CSS <c>font-feature-settings</c> does.
    /// </summary>
    /// <param name="Tag">The four-letter feature tag, such as <c>ss01</c>.</param>
    /// <param name="Value">0 leaves the feature unrequested, 1 asks for it, and a larger number selects that alternate of a feature that has several. It cannot switch off a feature that shaping applies on its own, such as <c>ccmp</c> and <c>locl</c>.</param>
    public readonly record struct FeatureSetting(string Tag, int Value);

    /// <summary>
    /// Everything a caller can ask of the shaper for one run of text.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The requests fold into one, so the shaper can activate every lookup in a single pass in the order the font's own lookup
    /// list gives. The typed groups (<see cref="Ligatures"/>, <see cref="Caps"/>, <see cref="Numeric"/>,
    /// <see cref="EastAsian"/>, <see cref="Position"/>) are kept apart from <see cref="ExplicitFeatures"/> because they carry
    /// precedence and alternate-selection rules that a list of tags cannot: a tag a typed group controls is always decided by that
    /// group, never by an explicit setting, which is CSS's rule for <c>font-variant-*</c> over <c>font-feature-settings</c>.
    /// </para>
    /// <para>
    /// <see cref="JoiningForms"/> and <see cref="UseCategories"/> are for a run of a joining script or an Indic script, and only
    /// one of the two is ever set for a run. The lists are compared by reference when settings are used as a cache key, so two
    /// equal lists that are separate objects cache separately, which costs a repeated lookup and never a wrong answer.
    /// </para>
    /// <para>
    /// Write <c>new ShapeSettings()</c> or <see cref="Default"/> for the defaults; <c>default(ShapeSettings)</c> is all zeros and
    /// means no ligatures and no kerning.
    /// </para>
    /// </remarks>
    /// <param name="Ligatures">The ligature features to apply.</param>
    /// <param name="Caps">The caps feature to apply.</param>
    /// <param name="Numeric">The numeral features to apply.</param>
    /// <param name="EastAsian">The East Asian features to apply.</param>
    /// <param name="ExplicitFeatures">Substitution (<c>GSUB</c>) features asked for by tag, or <see langword="null"/> for none. Positioning is not controlled this way: kerning is the <paramref name="Kerning"/> setting.</param>
    /// <param name="Kerning">Whether to apply the font's kerning.</param>
    /// <param name="Language">A BCP 47 language tag that selects language-specific behaviour in the font, or <see langword="null"/> for none.</param>
    /// <param name="ScriptTag">The OpenType script tag the run is in (see <see cref="OpenTypeTags.ForScript"/>), or <see langword="null"/> to let the font's default script apply.</param>
    /// <param name="JoiningForms">For a run of a joining script, the positional form of each character, one for each code point, as <see cref="ArabicJoining.Resolve"/> returns them; otherwise <see langword="null"/>.</param>
    /// <param name="UseCategories">For a run of an Indic script, the Universal Shaping Engine category of each code point, as <see cref="UniversalShaping.Classify"/> gives them; otherwise <see langword="null"/>.</param>
    /// <param name="ReverseForDisplay">Whether to reverse the shaped glyphs into visual order at the end, and replace mirrorable glyphs by their mirror images, for a run that is laid out right to left. The text the lookups ran over is never reversed. Reversing rewrites each glyph's <see cref="PlacedGlyph.XOffset"/> so that it stays in place, and leaves <see cref="PlacedGlyph.AttachedToIndex"/> empty on every glyph of the reversed run.</param>
    /// <param name="Position">The subscript or superscript feature to apply.</param>
    /// <param name="EmojiMode">Which presentation of a character with both a text and an emoji form to choose a glyph for, which is a <c>cmap</c> format 14 lookup and not a feature.</param>
    public readonly record struct ShapeSettings(
        LigatureSet Ligatures = LigatureSet.Default,
        CapsMode Caps = CapsMode.None,
        NumeralSet Numeric = NumeralSet.None,
        EastAsianSet EastAsian = EastAsianSet.None,
        IReadOnlyList<FeatureSetting>? ExplicitFeatures = null,
        bool Kerning = true,
        string? Language = null,
        string? ScriptTag = null,
        IReadOnlyList<ArabicJoiningForm>? JoiningForms = null,
        IReadOnlyList<UseCategory>? UseCategories = null,
        bool ReverseForDisplay = false,
        // Appended last so that callers that construct a value with positional arguments keep their meaning.
        SubSuperMode Position = SubSuperMode.None,
        EmojiMode EmojiMode = EmojiMode.Normal)
    {
        /// <summary>Creates the default settings: common and required ligatures, contextual alternates and kerning, and nothing else.</summary>
        public ShapeSettings()
            : this(LigatureSet.Default)
        {
        }

        /// <summary>The default settings, which is what <c>new ShapeSettings()</c> gives.</summary>
        public static readonly ShapeSettings Default = new();
    }
}
