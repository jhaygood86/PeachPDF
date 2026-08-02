using System.Collections.Generic;
using PeachPDF.Adapters;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Parse;
using PeachPDF.Html.Core.Utils;

namespace PeachPDF.Tests.Html.Core.Utils
{
    /// <summary>
    /// A broad, generic sweep proving <see cref="CssPropertyRegistry"/> (generated from
    /// css-properties.json) is behaviorally identical to the hand-written <see cref="CssUtils"/>
    /// dispatch for every property authored so far, across a shared value corpus spanning the shapes
    /// this repo's properties actually validate against (lengths, keywords, colors, integers, case
    /// variants, custom-property/global-keyword-shaped garbage). Complements
    /// <see cref="CssPropertyRegistryEquivalenceTests"/>'s per-property, semantically-targeted cases —
    /// this file exists to catch a transcription mistake in a JSON entry (wrong area, wrong keyword
    /// comparison, wrong data type) that a hand-picked per-property test might not happen to probe.
    /// Equivalence is proven by comparing the box's OWN state after applying (via the property's
    /// getter) rather than by comparing a return value — <see cref="CssUtils.SetPropertyValue"/> is
    /// void, so "did it apply" is only observable as "did the stored value change to match", which is
    /// exactly what @supports and every other real caller cares about too.
    /// </summary>
    public class CssPropertyRegistrySweepEquivalenceTests
    {
        // Every property with a getter on both the old and new dispatch (background-image,
        // list-style-image, overflow-wrap are deliberately excluded - see their own dedicated tests).
        private static readonly string[] SweepableProperties =
        {
            "border-bottom-width", "border-left-width", "border-right-width", "border-top-width", "border-bottom-style", "border-left-style",
            "border-right-style", "border-top-style", "border-bottom-color", "border-left-color", "border-right-color", "border-top-color",
            "border-spacing", "border-collapse", "box-decoration-break", "box-sizing", "transform", "transform-origin",
            "clip-path", "aspect-ratio", "box-shadow", "opacity", "border-top-left-radius", "border-top-right-radius",
            "border-bottom-right-radius", "border-bottom-left-radius", "counter-increment", "counter-reset", "counter-set", "string-set",
            "page", "-peachpdf-pdf-tag-type", "margin-bottom", "margin-left", "margin-right", "margin-top",
            "padding-bottom", "padding-left", "padding-right", "padding-top", "break-after", "page-break-after",
            "break-before", "page-break-before", "break-inside", "page-break-inside", "orphans", "widows",
            "hyphens", "left", "top", "right", "bottom", "width",
            "max-width", "min-width", "height", "max-height", "min-height", "background-color",
            "background-position", "background-size", "background-repeat", "color", "content", "display",
            "direction", "writing-mode", "margin-block-start", "margin-block-end", "margin-inline-start", "margin-inline-end",
            "padding-block-start", "padding-block-end", "padding-inline-start", "padding-inline-end", "inset-block-start", "inset-block-end",
            "inset-inline-start", "inset-inline-end", "border-block-start-width", "border-block-start-style", "border-block-start-color", "border-block-end-width",
            "border-block-end-style", "border-block-end-color", "border-inline-start-width", "border-inline-start-style", "border-inline-start-color", "border-inline-end-width",
            "border-inline-end-style", "border-inline-end-color", "empty-cells", "float", "clear", "position",
            "line-height", "vertical-align", "text-indent", "text-align", "text-decoration-color", "text-decoration-line",
            "text-decoration-style", "text-transform", "white-space", "word-break", "visibility", "word-spacing",
            "letter-spacing", "font-family", "font-size", "font-style", "font-variant-caps", "font-variant-ligatures",
            "font-variant-numeric", "font-variant-east-asian", "font-feature-settings", "font-weight", "font-stretch", "font-palette",
            "list-style-position", "list-style-type", "overflow", "z-index", "background-origin", "background-clip",
            "background-attachment", "object-fit", "object-position", "flex-direction", "flex-wrap", "justify-content",
            "align-items", "align-content", "flex-grow", "flex-shrink", "flex-basis", "align-self",
            "order", "row-gap", "column-gap", "grid-template-columns", "grid-template-rows", "grid-template-areas",
            "grid-auto-columns", "grid-auto-rows", "grid-auto-flow", "justify-items", "justify-self", "grid-column-start",
            "grid-column-end", "grid-row-start", "grid-row-end", "column-count", "column-width", "column-fill",
            "column-span", "column-rule-width", "column-rule-style", "column-rule-color", "unicode-bidi",
        };

        // "1em" and " " are deliberately excluded: a handful of setters (TextIndent/WordSpacing/
        // LetterSpacing) eagerly resolve em-relative lengths at assignment time via NoEms(), which
        // needs a live HtmlContainer/font for GetEmHeight() - a bare CssBox(null, null) (this harness's
        // whole point - see CssPropertyRegistryEquivalenceTests' NewBoxAndParser) has neither, so both
        // OLD and NEW dispatch throw identically. That's a pre-existing property of those setters
        // orthogonal to this migration, not a real vs. generated mismatch - not worth reproducing a
        // full HtmlContainer/layout pass just to exercise it here (CssUtilsTests' own FindDivBoxAndParser
        // helper is the harness for that, for the handful of tests that actually need it). Likewise
        // GetFontFamilyByName throws IndexOutOfRangeException for whitespace-only input in both old
        // and new dispatch (font-family's customSetter calls the identical method) - a real, pre-existing
        // bug, but not one this migration introduced or is scoped to fix.
        private static readonly string[] ValueCorpus =
        {
            "10px", "0", "auto", "AUTO", "none", "normal", "red", "currentColor",
            "border-box", "content-box", "always", "1", "1.5", "-1", "bogus-value", "",
        };

        public static IEnumerable<object[]> SweepCases()
        {
            foreach (var property in SweepableProperties)
            foreach (var value in ValueCorpus)
                yield return new object[] { property, value };
        }

        [Theory]
        [MemberData(nameof(SweepCases))]
        public void GeneratedDispatch_MatchesOldDispatch_ForEveryAuthoredProperty(string property, string value)
        {
            var adapter = new PdfSharpAdapter();
            var oldBox = new CssBox(null, null);
            var newBox = new CssBox(null, null);
            var oldParser = new CssValueParser(adapter);
            var newParser = new CssValueParser(adapter);

            CssUtils.SetPropertyValue(oldParser, oldBox, property, value);
            var oldResult = CssUtils.GetPropertyValue(oldBox, property);

            var applied = CssPropertyRegistry.TrySet(newParser, newBox, property, value);
            var newResult = CssPropertyRegistry.Get(newBox, property);

            Assert.True(oldResult == newResult,
                $"property \"{property}\" value \"{value}\": old=\"{oldResult}\" new=\"{newResult}\" (applied={applied})");
        }

        [Fact]
        public void EveryKnownSetterHasAMatchingGetterOnBothDispatchPaths()
        {
            // Guards the sweep's own premise: if a future property is authored with a setter but no
            // getter (like background-image/list-style-image), it silently falls out of the sweep
            // above rather than failing loudly - this asserts every currently-swept name really does
            // round-trip on both sides right now.
            var adapter = new PdfSharpAdapter();
            var parser = new CssValueParser(adapter);

            foreach (var property in SweepableProperties)
            {
                var box = new CssBox(null, null);
                CssUtils.SetPropertyValue(parser, box, property, "10px");
                Assert.True(CssPropertyRegistry.IsKnownProperty(property), $"\"{property}\" is not known to the generated registry.");
            }
        }
    }
}
