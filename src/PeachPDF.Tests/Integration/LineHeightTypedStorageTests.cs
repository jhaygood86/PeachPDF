using PeachPDF.CSS;
using PeachPDF.Tests.TestSupport;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Direct typed-storage assertions for <c>line-height</c>, now backed by
    /// <c>CssProperty&lt;CssKeywordOrValue&lt;NormalKeyword, LengthOrUnitless&gt;&gt;</c> - the tenth and
    /// final keyword-or-value batch. Complements <c>FontShorthandIntegrationTests</c> (which exercises
    /// <c>line-height</c> indirectly through the <c>font</c> shorthand's slash-separated syntax) and
    /// <c>Acid2FeatureVerificationTests</c>' <c>FontShorthand_SlashSeparatedLineHeight_...</c> case.
    /// </summary>
    public class LineHeightTypedStorageTests
    {
        [Fact]
        public async Task NormalKeyword_StoresTheKeyword_CaseInsensitively()
        {
            var box = await GetLineHeightBoxAsync("NORMAL");
            Assert.True(box.LineHeight.Value is { IsKeyword: true, Keyword: NormalKeyword.Normal });
        }

        [Fact]
        public async Task UnitlessNumber_StoresAsUnitlessMultiplier()
        {
            var box = await GetLineHeightBoxAsync("1.5");
            Assert.True(box.LineHeight.Value is
            {
                IsValue: true,
                Value: { IsUnitless: true, Unitless: 1.5d }
            });
        }

        [Fact]
        public async Task AbsoluteLength_StoresParsedLength()
        {
            var box = await GetLineHeightBoxAsync("24px");
            Assert.True(box.LineHeight.Value is
            {
                IsValue: true,
                Value: { IsUnitless: false, LengthOrCalc: { IsCalc: false, Length: { Type: Length.Unit.Px, Value: 24f } } }
            });
        }

        [Fact]
        public async Task Percentage_StoresParsedLength()
        {
            // line-height accepts <length-percentage>, not just <length> - LengthOrCalc (reused as-is from
            // word-spacing/letter-spacing/row-gap/column-gap/margin/inset/font-size) already covers this.
            var box = await GetLineHeightBoxAsync("150%");
            Assert.True(box.LineHeight.Value is
            {
                IsValue: true,
                Value: { LengthOrCalc: { IsCalc: false, Length: { Type: Length.Unit.Percent, Value: 150f } } }
            });
        }

        [Fact]
        public async Task RelativeUnitCalc_StaysDeferred_NotEagerlyResolved()
        {
            var box = await GetLineHeightBoxAsync("calc(1em + 4px)");
            Assert.True(box.LineHeight.Value is
            {
                IsValue: true,
                Value: { IsUnitless: false, LengthOrCalc: { IsCalc: true } }
            });
        }

        [Fact]
        public async Task BareNumberCalc_FoldsToALiteralUnitlessMultiplier()
        {
            // CalcCategory.Number always folds at cascade time (Layer A's CalcSerializer), unlike
            // CalcCategory.LengthPercentage - so calc(1 + 0.5) is indistinguishable, by the time it
            // reaches this union, from a literal "1.5". No deferred-calc slot is needed on the unitless
            // side because of this (see LengthOrUnitless's own doc comment).
            var box = await GetLineHeightBoxAsync("calc(1 + 0.5)");
            Assert.True(box.LineHeight.Value is
            {
                IsValue: true,
                Value: { IsUnitless: true, Unitless: 1.5d }
            });
        }

        // ─── ActualLineHeight resolution (not just storage) ────────────────────────

        [Fact]
        public async Task NormalKeyword_ActualLineHeight_ResolvesToOnePointTwoTimesFontSize()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='t' style='font-size:20pt;line-height:normal'>x</div>"));

            var box = LayoutHarness.FindById(root, "t");

            Assert.NotNull(box);
            Assert.Equal(1.2 * 20, box!.ActualLineHeight, 2);
        }

        [Fact]
        public async Task UnitlessNumber_ActualLineHeight_ResolvesToTheMultiplierTimesTheBoxsOwnFontSize()
        {
            // Regression-guard: before this conversion, a bare unitless line-height (e.g. "1.5") validated
            // but silently collapsed ActualLineHeight to 0 - the old string-based ParseLength had no case
            // for a number with no unit token (Length.Unit.None resolves to 0 in Length.ToPixels).
            // LengthOrUnitless's Unitless side fixes this (CSS2.1 §10.8.1: the number times the element's
            // own font size).
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='t' style='font-size:20pt;line-height:1.5'>x</div>"));

            var box = LayoutHarness.FindById(root, "t");

            Assert.NotNull(box);
            Assert.Equal(30d, box!.ActualLineHeight, 2);
        }

        [Fact]
        public async Task AbsoluteLength_ActualLineHeight_ResolvesToTheLengthItself()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='t' style='font-size:20pt;line-height:36pt'>x</div>"));

            var box = LayoutHarness.FindById(root, "t");

            Assert.NotNull(box);
            Assert.Equal(36d, box!.ActualLineHeight, 2);
        }

        [Fact]
        public async Task RelativeUnitCalc_ActualLineHeight_EvaluatesLazilyAgainstTheBoxsOwnContext()
        {
            // calc(1em + 4px) on a 20pt font-size box: 1em -> 20pt (the box's own font size, the same
            // emFactor GetEmHeight() would supply), 4px -> 3pt (PointsPerPx), for 23pt total.
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='t' style='font-size:20pt;line-height:calc(1em + 4px)'>x</div>"));

            var box = LayoutHarness.FindById(root, "t");

            Assert.NotNull(box);
            Assert.Equal(23d, box!.ActualLineHeight, 2);
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────

        private static async Task<PeachPDF.Html.Core.Dom.CssBox> GetLineHeightBoxAsync(string authored)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                $"<div id='t' style='line-height:{authored}'></div>"));

            var box = LayoutHarness.FindById(root, "t");
            Assert.NotNull(box);
            return box!;
        }
    }
}
