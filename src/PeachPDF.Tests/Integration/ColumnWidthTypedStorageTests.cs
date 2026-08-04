using PeachPDF.CSS;
using PeachPDF.Html.Core.Parse;
using PeachPDF.Tests.TestSupport;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Direct typed-storage assertions for <c>column-width</c>, now backed by
    /// <c>CssProperty&lt;CssKeywordOrValue&lt;AutoKeyword, LengthOrCalc&gt;&gt;</c> - reusing the
    /// <c>LengthOrCalc</c> value side <c>word-spacing</c>/<c>letter-spacing</c>/<c>row-gap</c>/
    /// <c>column-gap</c> introduced, so a relative-unit <c>calc()</c> still evaluates lazily rather than
    /// being rejected. Complements <c>MulticolLayoutIntegrationTests</c>, which exercises
    /// <c>column-width</c> indirectly through its effect on column geometry rather than asserting the
    /// stored <c>.Value</c> directly.
    /// </summary>
    public class ColumnWidthTypedStorageTests
    {
        [Fact]
        public async Task Auto_StoresTheAutoKeyword_CaseInsensitively()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='t' style='column-width:AUTO'></div>"));

            var box = LayoutHarness.FindById(root, "t");

            Assert.NotNull(box);
            Assert.True(box!.ColumnWidth.Value is { IsKeyword: true, Keyword: AutoKeyword.Auto });
        }

        [Fact]
        public async Task LengthValue_StoresParsedLength()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='t' style='column-width:120px'></div>"));

            var box = LayoutHarness.FindById(root, "t");

            Assert.NotNull(box);
            Assert.True(box!.ColumnWidth.Value is { IsValue: true, Value: { IsCalc: false, Length: { Value: 120f } } });
        }

        [Fact]
        public async Task AbsoluteUnitCalc_FoldsToALiteralLength()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='t' style='column-width:calc(100px + 20px)'></div>"));

            var box = LayoutHarness.FindById(root, "t");

            Assert.NotNull(box);
            Assert.True(box!.ColumnWidth.Value is { IsValue: true, Value: { IsCalc: false } });
        }

        [Fact]
        public async Task RelativeUnitCalc_EvaluatesAgainstTheBoxsOwnFontSize()
        {
            // font-size:20px resolves to an actual font size of 15pt (1px = 0.75pt), so
            // calc(5em + 20px) = 5*15pt + 20px(=15pt) = 90pt.
            var (calcRoot, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='t' style='font-size:20px; column-width:calc(5em + 20px)'></div>"));
            var (absoluteRoot, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='t' style='font-size:20px; column-width:90pt'></div>"));

            var calcBox = LayoutHarness.FindById(calcRoot, "t");
            var absoluteBox = LayoutHarness.FindById(absoluteRoot, "t");

            Assert.NotNull(calcBox);
            Assert.NotNull(absoluteBox);
            Assert.True(calcBox!.ColumnWidth.Value is { IsValue: true, Value: { IsCalc: true } });

            var calcWidth = CssValueParser.ParseLength(calcBox.ColumnWidth.Value.Value!.Value, 0, calcBox);
            var absoluteWidth = CssValueParser.ParseLength(absoluteBox!.ColumnWidth.Value.Value!.Value, 0, absoluteBox);
            Assert.Equal(absoluteWidth, calcWidth, 2);
        }
    }
}
