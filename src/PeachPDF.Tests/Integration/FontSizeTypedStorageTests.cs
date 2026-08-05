using PeachPDF.CSS;
using PeachPDF.Tests.TestSupport;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Direct typed-storage assertions for <c>font-size</c>, now backed by
    /// <c>CssProperty&lt;CssKeywordOrValue&lt;FontSizeKeyword, LengthOrCalc&gt;&gt;</c> - the ninth
    /// keyword-or-value batch, and the first with a bespoke eager-resolution helper
    /// (<c>CssBox.ResolveFontSizeValueComputation</c>) instead of a purely mechanical parse. Complements
    /// <c>FontSizeInheritanceIntegrationTests</c> (the multi-generation compounding regression coverage
    /// from the original eager-resolution fix, unaffected by this storage-shape conversion) and
    /// <c>FontShorthandIntegrationTests</c>, which exercise <c>font-size</c> indirectly through
    /// <c>ActualFontSize</c> rather than asserting the stored <c>.Value</c> directly.
    /// </summary>
    public class FontSizeTypedStorageTests
    {
        [Fact]
        public async Task AbsoluteKeyword_StoresTheKeyword_CaseInsensitively()
        {
            var box = await GetFontSizeBoxAsync("MEDIUM");
            Assert.True(box.FontSize.Value is { IsKeyword: true, Keyword: FontSizeKeyword.Medium });
        }

        [Fact]
        public async Task LargeKeyword_StoresTheKeyword()
        {
            var box = await GetFontSizeBoxAsync("Large");
            Assert.True(box.FontSize.Value is { IsKeyword: true, Keyword: FontSizeKeyword.Large });
        }

        [Fact]
        public async Task AbsoluteLength_StoresParsedLength()
        {
            var box = await GetFontSizeBoxAsync("18pt");
            Assert.True(box.FontSize.Value is { IsValue: true, Value: { IsCalc: false, Length: { Value: 18f } } });
        }

        [Fact]
        public async Task PlainEmValue_IsEagerlyResolvedToAbsolutePoints()
        {
            // The eager em-to-pt resolution must still run at set-time (not deferred) - a stored "1.5em"
            // would silently compound across generations since InheritStyle adopts the Font area by
            // reference (see CssBox.ResolveFontSizeValueComputation's own doc comment, and the original
            // compounding bug this guards against). Asserting the resolved Type/Value directly (not just
            // IsCalc: false) proves the transform actually ran - an unconverted "1.5em" Length would also
            // report IsCalc: false.
            // font-size:20px resolves to an actual parent font size of 15pt (1px = 0.75pt), so
            // 1.5em = 1.5 * 15pt = 22.5pt.
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div style='font-size:20px'><span id='t' style='font-size:1.5em'>x</span></div>"));

            var box = LayoutHarness.FindById(root, "t");

            Assert.NotNull(box);
            Assert.True(box!.FontSize.Value is
            {
                IsValue: true,
                Value: { IsCalc: false, Length: { Type: Length.Unit.Pt, Value: 22.5f } }
            });
        }

        [Fact]
        public async Task SmallerKeyword_IsEagerlyResolvedToAbsolutePoints()
        {
            // "smaller"/"larger" are transformed away into an absolute Length at set-time too (per the
            // FontSizeKeyword enum's own doc comment) - they never actually reach FontSizeKeyword.Smaller
            // on the stored union when a real parent is in scope.
            // font-size:20px resolves to an actual parent font size of 15pt, so smaller = 15pt - 2 = 13pt
            // (FontSizeResolver.Resolve's Smaller case).
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div style='font-size:20px'><span id='t' style='font-size:smaller'>x</span></div>"));

            var box = LayoutHarness.FindById(root, "t");

            Assert.NotNull(box);
            Assert.True(box!.FontSize.Value is
            {
                IsValue: true,
                Value: { IsCalc: false, Length: { Type: Length.Unit.Pt, Value: 13f } }
            });
        }

        [Fact]
        public async Task AbsoluteUnitCalc_FoldsToALiteralLength()
        {
            // An absolute-only calc() already folds to a literal length string upstream (Layer A's
            // CalcSerializer), before this property's own eager-resolution helper ever sees it - matching
            // every other LengthOrCalc-backed property's behavior.
            var box = await GetFontSizeBoxAsync("calc(12pt + 2px)");
            Assert.True(box.FontSize.Value is { IsValue: true, Value: { IsCalc: false } });
        }

        [Fact]
        public async Task RelativeUnitCalc_StaysDeferred_NotEagerlyResolved()
        {
            // Unlike a bare em/ex/%/smaller/larger value, a calc() mixing relative units is deliberately
            // left lazy at set-time - CssBox.ResolveFontSizeValueComputation's own IsCalcFunction guard
            // excludes it from eager resolution entirely, and it stays deferred calc text (LengthOrCalc's
            // mechanism), evaluated later against the box's own font-size-resolution basis.
            var box = await GetFontSizeBoxAsync("calc(1em + 2px)");
            Assert.True(box.FontSize.Value is { IsValue: true, Value: { IsCalc: true } });
        }

        [Fact]
        public async Task RemValue_StaysUnresolvedText_NotEagerlyFoldedToASpecificPointValue()
        {
            // rem resolves against a single, non-chained root reference, so - unlike em/%/smaller/larger -
            // it doesn't need eager resolution to avoid compounding; it stays a plain Length (Type: Rem),
            // resolved lazily by FontSizeResolver/DerivedStyle.ActualFont.
            var box = await GetFontSizeBoxAsync("2rem");
            Assert.True(box.FontSize.Value is
            {
                IsValue: true,
                Value: { IsCalc: false, Length: { Type: Length.Unit.Rem, Value: 2f } }
            });
        }

        // ─── FontSizeResolver's absolute-keyword resolution (not just storage) ─────

        [Theory]
        [InlineData("xx-small", 11d - 4)]
        [InlineData("x-small", 11d - 3)]
        [InlineData("small", 11d - 2)]
        [InlineData("x-large", 11d + 3)]
        [InlineData("xx-large", 11d + 4)]
        public async Task AbsoluteKeyword_ActualFontSize_ResolvesToTheSpecFixedOffset(string keyword, double expected)
        {
            // Every keyword offsets the CSS initial font-size (CssConstants.FontSize, 11) by a fixed
            // amount - not parent-relative, so this must hold regardless of what a parent's own font-size
            // is. Reads ActualFontSize (not just the stored .Value) to prove FontSizeResolver.Resolve's
            // typed keyword-side switch, not just parsing, is correct.
            var box = await GetFontSizeBoxAsync(keyword);
            Assert.Equal(expected, box.ActualFont.Size, 2);
        }

        [Fact]
        public async Task LegacyFontSizeAttribute_RoutesThroughTheTypedParser()
        {
            // <font size="..."> (HTML4 presentational attribute) previously assigned the raw string
            // directly to CssBox.FontSize; now must route through the same CssKeywordOrValueParser every
            // other assignment site uses.
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<font id='t' size='large'>x</font>"));

            var box = LayoutHarness.FindById(root, "t");

            Assert.NotNull(box);
            Assert.True(box!.FontSize.Value is { IsKeyword: true, Keyword: FontSizeKeyword.Large });
        }

        [Fact]
        public async Task LegacyFontSizeAttribute_UnrecognizedNumericScale_LeavesTheInheritedValueInPlace()
        {
            // HTML's legacy <font size> is really a "1"-"7"/relative scale, never a CSS keyword or length
            // - there's no translation table for it, so a real-world numeric value never validates. The
            // attribute must be treated as absent (leaving the cascade's own value, "large" here, in
            // place) rather than forcing every such element to "medium" regardless of the requested size.
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div style='font-size:large'><font id='t' size='3'>x</font></div>"));

            var box = LayoutHarness.FindById(root, "t");

            Assert.NotNull(box);
            Assert.True(box!.FontSize.Value is { IsKeyword: true, Keyword: FontSizeKeyword.Large });
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────

        private static async Task<PeachPDF.Html.Core.Dom.CssBox> GetFontSizeBoxAsync(string authored)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                $"<div id='t' style='font-size:{authored}'></div>"));

            var box = LayoutHarness.FindById(root, "t");
            Assert.NotNull(box);
            return box!;
        }
    }
}
