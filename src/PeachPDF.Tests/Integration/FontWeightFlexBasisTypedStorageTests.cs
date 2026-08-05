using PeachPDF.CSS;
using PeachPDF.Html.Core.Parse;
using PeachPDF.Tests.TestSupport;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Direct typed-storage assertions for <c>font-weight</c> (now
    /// <c>CssProperty&lt;CssKeywordOrValue&lt;FontWeightKeyword, int&gt;&gt;</c>) and <c>flex-basis</c> (now
    /// <c>CssProperty&lt;CssKeywordOrValue&lt;FlexBasisKeyword, LengthOrCalc&gt;&gt;</c>) - the first two
    /// keyword-or-value properties needing a dedicated keyword enum instead of reusing
    /// <c>AutoKeyword</c>/<c>NormalKeyword</c>. Complements <c>FontShorthandIntegrationTests</c>
    /// (<c>ActualNumericWeight</c> resolution) and <c>FlexboxIntegrationTests</c> (layout geometry), which
    /// exercise these properties indirectly rather than asserting the stored <c>.Value</c> directly.
    /// </summary>
    public class FontWeightFlexBasisTypedStorageTests
    {
        [Fact]
        public async Task FontWeight_Normal_StoresTheKeyword_CaseInsensitively()
        {
            var box = await GetFontWeightBoxAsync("Normal");
            Assert.True(box.FontWeight.Value is { IsKeyword: true, Keyword: FontWeightKeyword.Normal });
        }

        [Fact]
        public async Task FontWeight_Bold_StoresTheKeyword_CaseInsensitively()
        {
            var box = await GetFontWeightBoxAsync("BOLD");
            Assert.True(box.FontWeight.Value is { IsKeyword: true, Keyword: FontWeightKeyword.Bold });
        }

        [Fact]
        public async Task FontWeight_Bolder_StoresTheKeyword_CaseInsensitively()
        {
            var box = await GetFontWeightBoxAsync("Bolder");
            Assert.True(box.FontWeight.Value is { IsKeyword: true, Keyword: FontWeightKeyword.Bolder });
        }

        [Fact]
        public async Task FontWeight_Lighter_StoresTheKeyword_CaseInsensitively()
        {
            var box = await GetFontWeightBoxAsync("lighter");
            Assert.True(box.FontWeight.Value is { IsKeyword: true, Keyword: FontWeightKeyword.Lighter });
        }

        private static async Task<PeachPDF.Html.Core.Dom.CssBox> GetFontWeightBoxAsync(string authored)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                $"<div id='t' style='font-weight:{authored}'></div>"));

            var box = LayoutHarness.FindById(root, "t");
            Assert.NotNull(box);
            return box!;
        }

        [Fact]
        public async Task FontWeight_NumericValue_StoresParsedInteger()
        {
            // A multiple of 100: the Layer A CSS-OM's own FontWeightProperty/WeightIntegerConverter
            // (ValueExtensions.IsWeight) restricts inline-style parsing to the legacy 100-900-by-100 set
            // before the value ever reaches CssUtils.SetPropertyValue - a pre-existing, unrelated
            // asymmetry with the generated registry's own deliberately-unconstrained integer clause (see
            // the font-weight JSON entry's comment). Using a value both paths accept keeps this test about
            // the typed-storage conversion, not that separate gap.
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='t' style='font-weight:600'></div>"));

            var box = LayoutHarness.FindById(root, "t");

            Assert.NotNull(box);
            Assert.True(box!.FontWeight.Value is { IsValue: true, Value: 600 });
        }

        [Fact]
        public async Task FlexBasis_Auto_StoresTheAutoKeyword_CaseInsensitively()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div style='display:flex'><div id='t' style='flex-basis:AUTO'></div></div>"));

            var box = LayoutHarness.FindById(root, "t");

            Assert.NotNull(box);
            Assert.True(box!.FlexBasis.Value is { IsKeyword: true, Keyword: FlexBasisKeyword.Auto });
        }

        [Fact]
        public async Task FlexBasis_Content_StoresTheContentKeyword()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div style='display:flex'><div id='t' style='flex-basis:content'></div></div>"));

            var box = LayoutHarness.FindById(root, "t");

            Assert.NotNull(box);
            Assert.True(box!.FlexBasis.Value is { IsKeyword: true, Keyword: FlexBasisKeyword.Content });
        }

        [Fact]
        public async Task FlexBasis_LengthValue_StoresParsedLength()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div style='display:flex'><div id='t' style='flex-basis:120px'></div></div>"));

            var box = LayoutHarness.FindById(root, "t");

            Assert.NotNull(box);
            Assert.True(box!.FlexBasis.Value is { IsValue: true, Value: { IsCalc: false, Length: { Value: 120f } } });
        }

        [Fact]
        public async Task FlexBasis_RelativeUnitCalc_EvaluatesLazily()
        {
            // font-size:20px resolves to an actual font size of 15pt (1px = 0.75pt), so
            // calc(2em + 10px) = 2*15pt + 10px(=7.5pt) = 37.5pt.
            var (calcRoot, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div style='display:flex'><div id='t' style='font-size:20px; flex-basis:calc(2em + 10px)'></div></div>"));

            var box = LayoutHarness.FindById(calcRoot, "t");

            Assert.NotNull(box);
            Assert.True(box!.FlexBasis.Value is { IsValue: true, Value: { IsCalc: true } });

            var resolved = CssValueParser.ParseLength(box.FlexBasis.Value.Value!.Value, 0, box);
            Assert.Equal(37.5, resolved, 2);
        }
    }
}
