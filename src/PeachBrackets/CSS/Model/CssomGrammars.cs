#nullable disable

using System;
using System.Collections.Frozen;
using System.Collections.Generic;

namespace PeachBrackets.Css
{
    using static Converters;

    /// <summary>
    /// The grammars of the longhands whose <c>cssom.converter</c> in css-properties.json is more than one
    /// expression over <see cref="Converters"/>: hoisted here, by name, so the JSON stays a single line per property and
    /// the generated <c>Property</c> class has nothing bespoke left in it. Each is the property's grammar before the
    /// CSS-wide keywords are added (the generated class's <c>ValueGrammar</c>).
    /// </summary>
    internal static class CssomGrammars
    {
        /// <summary>
        /// <c>container-name: none | &lt;custom-ident&gt;+</c> (CSS Containment 3 §2) - a space-separated
        /// list of bare identifiers, NOT a quoted CSS &lt;string&gt;. <see cref="Converters.LiteralsConverter"/>
        /// (the same converter <c>font-family</c>'s unquoted form uses to accept consecutive Ident tokens
        /// joined by whitespace) already accepts exactly this shape, including the single-token "none" case
        /// - but its shared grammar doesn't know <c>none</c> is only valid alone here (unlike font-family's
        /// own identifier list, which has no such restriction), so that's enforced with a thin wrapper
        /// instead of forking a second copy of the identifier-list walk.
        /// </summary>
        internal static readonly IValueConverter ContainerName = new IdentifierValueConverter(ToContainerNameList);

        private static string ToContainerNameList(IReadOnlyList<Token> value)
        {
            var literals = ValueExtensions.ToLiterals(value);
            if (literals is null) return null;
            var parts = literals.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length > 1 && Array.IndexOf(parts, Keywords.None) >= 0 ? null : literals;
        }

        private abstract class ContentMode
        {
        }

        private sealed class NormalContentMode : ContentMode
        {
        }

        private sealed class OpenQuoteContentMode : ContentMode
        {
        }

        private sealed class CloseQuoteContentMode : ContentMode
        {
        }

        private sealed class NoOpenQuoteContentMode : ContentMode
        {
        }

        private sealed class NoCloseQuoteContentMode : ContentMode
        {
        }

        private static readonly FrozenDictionary<string, ContentMode> ContentModes =
            new Dictionary<string, ContentMode>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.OpenQuote, new OpenQuoteContentMode()},
                {Keywords.NoOpenQuote, new NoOpenQuoteContentMode()},
                {Keywords.CloseQuote, new CloseQuoteContentMode()},
                {Keywords.NoCloseQuote, new NoCloseQuoteContentMode()}
            }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

        private static readonly ContentMode[] DefaultContent = [new NormalContentMode()];

        /// <summary><c>content</c>: <c>normal | none | [ &lt;quote&gt; | &lt;url&gt; | &lt;gradient&gt; | &lt;content-list item&gt; | element() ]+</c>.</summary>
        internal static readonly IValueConverter Content = Assign(Keywords.Normal, DefaultContent).OrNone().Or(
            ContentModes.ToConverter().Or(
                UrlConverter).Or(
                Converters.GradientConverter).Or(
                Converters.ContentListItemConverter).Or(
                new GcpmElementFunctionConverter()).Many());

        private static readonly IValueConverter FeatureTagValueConverter =
            WithOrder(StringConverter.Required(), FontFeatureTagListGrammar.FeatureValueConverter.Option());

        /// <summary>
        /// <c>font-feature-settings: normal | &lt;feature-tag-value&gt;#</c>, where <c>&lt;feature-tag-value&gt; = &lt;string&gt;
        /// [ &lt;integer&gt; | on | off ]?</c> - the standard CSS Fonts Level 3 escape hatch for activating
        /// an arbitrary OpenType feature by tag. Real GSUB activation (not just parsing/cascading) happens
        /// via <c>DerivedStyle.ActualFontFeatureSettings</c>.
        /// </summary>
        internal static readonly IValueConverter FontFeatureSettings =
            FeatureTagValueConverter.FromList().Or(Keywords.Normal);

        private static readonly IValueConverter AxisValueConverter =
            WithOrder(StringConverter.Required(), NumberConverter.Required());

        /// <summary><c>font-variation-settings: normal | [ &lt;string&gt; &lt;number&gt; ]#</c> (CSS Fonts 4 §6.10), applied where the font is created, see <c>FontVariationSettingsResolver</c>.</summary>
        internal static readonly IValueConverter FontVariationSettings =
            AxisValueConverter.FromList().Or(Keywords.Normal);

        // perspective-origin and transform-origin are a <position> (CSS Transforms 1). The two-value
        // <length-percentage> form is ORDERED (horizontal then vertical); only the keyword-only form may be
        // reordered ("center left" == "left center"). So "2px left" and "top 2px" are invalid: a length in one
        // axis forces the ordered form, and the remaining token is a keyword for the wrong axis.
        private static readonly IValueConverter Horizontal =
            LengthOrPercentConverter
                .Or(Keywords.Left, Length.Zero)
                .Or(Keywords.Right, Length.Full)
                .Or(Keywords.Center, Length.Half);

        private static readonly IValueConverter Vertical =
            LengthOrPercentConverter
                .Or(Keywords.Top, Length.Zero)
                .Or(Keywords.Bottom, Length.Full)
                .Or(Keywords.Center, Length.Half);

        private static readonly IValueConverter HorizontalKeyword =
            Assign(Keywords.Left, Length.Zero)
                .Or(Keywords.Right, Length.Full)
                .Or(Keywords.Center, Length.Half);

        private static readonly IValueConverter VerticalKeyword =
            Assign(Keywords.Top, Length.Zero)
                .Or(Keywords.Bottom, Length.Full)
                .Or(Keywords.Center, Length.Half);

        /// <summary><c>perspective-origin</c>: a <c>&lt;position&gt;</c> (CSS Transforms 1).</summary>
        internal static readonly IValueConverter PerspectiveOrigin = LengthOrPercentConverter.Or(
            Keywords.Left, new Point(Length.Zero, Length.Half)).Or(
            Keywords.Center, new Point(Length.Half, Length.Half)).Or(
            Keywords.Right, new Point(Length.Full, Length.Half)).Or(
            Keywords.Top, new Point(Length.Half, Length.Zero)).Or(
            Keywords.Bottom, new Point(Length.Half, Length.Full)).Or(
            WithOrder(Horizontal.Option(Length.Half), Vertical.Option(Length.Half))).Or(
            WithAnyOrderIndependent(HorizontalKeyword.Option(Length.Half), VerticalKeyword.Option(Length.Half)));

        // CSS Transforms 1 (https://www.w3.org/TR/css-transforms-1/#transform-origin-property):
        //   [ left | center | right | top | bottom | <length-percentage> ]
        // | [ left | center | right | <length-percentage> ] [ top | center | bottom | <length-percentage> ] <length>?
        // | [ [ center | left | right ] && [ center | top | bottom ] ] <length>?
        //
        // A one- or two-value <position> with no z-offset (this is exactly the perspective-origin grammar).
        private static readonly IValueConverter Position =
            LengthOrPercentConverter.Or(Keywords.Center, Point.Center)
                // Ordered horizontal-then-vertical (rejects a keyword in the wrong axis position).
                .Or(WithOrder(Horizontal.Option(Length.Half), Vertical.Option(Length.Half)))
                // Keyword-only form, any order ("center left"): a token accepted by both axes must not be
                // claimed positionally, so this needs order-independent matching.
                .Or(WithAnyOrderIndependent(HorizontalKeyword.Option(Length.Half), VerticalKeyword.Option(Length.Half)));

        // A genuine two-value position: BOTH a horizontal and a vertical component must be present. The axis
        // converters are bare (not .Option), so each must actually consume a token. The <length> z-offset only
        // exists in this two-value production, so gating the z-offset behind this (rather than the shared outer
        // WithOrder) is what makes a single-value position + length such as "top 2px" invalid.
        private static readonly IValueConverter TwoValuePosition =
            WithOrder(Horizontal, Vertical)
                .Or(WithAnyOrderIndependent(HorizontalKeyword, VerticalKeyword));

        /// <summary><c>transform-origin</c>: a <c>&lt;position&gt;</c> optionally followed by a <c>&lt;length&gt;</c> z-offset (CSS Transforms 1).</summary>
        internal static readonly IValueConverter TransformOrigin =
            // Two-value position followed by a (required) <length> z-offset - this branch only matches when a
            // third length token is actually present...
            WithOrder(TwoValuePosition, LengthConverter)
                // ...otherwise a plain one- or two-value <position> with no z-offset.
                .Or(Position);
    }
}
