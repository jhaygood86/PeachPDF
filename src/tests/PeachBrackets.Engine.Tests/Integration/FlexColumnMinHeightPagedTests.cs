using PeachPDF;
using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Utils;
using PeachPDF.Tests.TestSupport;
using PeachBrackets.Engine.Tests.TestSupport;

namespace PeachBrackets.Engine.Tests.Integration;

/// <summary>
/// A column flex container taller than its content only because of <c>min-height</c> hands that free space to its items
/// (auto margins, <c>justify-content</c>, <c>flex-grow</c>), in a real paged document.
/// </summary>
public class FlexColumnMinHeightPagedTests
{
    [Theory]
    [InlineData("grid-template-rows: auto 1fr auto")]
    [InlineData("grid-template-rows: auto minmax(0, 1fr) auto")]
    public async Task AGridWithAMinHeightGivesItsFlexibleRowTheRoom(string rows)
    {
        var html = "<!DOCTYPE html><html><head><style>@page { size: A4; margin: 10mm } body { margin:0; font: 10pt sans-serif } " +
                   ".box { display:grid; min-height:200mm; " + rows + " }</style></head><body>" +
                   "<div class='box' id='col'><div>TOP</div><div></div><div id='bottom'>BOTTOM</div></div><div>AFTER</div></body></html>";

        var (root, _) = await PdfGeneratorLayoutHarness.LayoutAsync(html, new PdfGenerateConfig(), BundledFonts.PinSansSerifAsync);
        var col = DomUtils.GetBoxById(root, "col")!;
        var bottom = DomUtils.GetBoxById(root, "bottom")!;
        Assert.Equal(col.ActualBottom, bottom.ActualBottom, 0.5);
    }

    [Fact]
    public async Task AGridWithAShortMinHeightIsLeftAlone()
    {
        var html = "<!DOCTYPE html><html><head><style>@page { size: A4; margin: 10mm } body { margin:0; font: 10pt sans-serif } " +
                   ".box { display:grid; min-height:1mm; grid-template-rows: auto 1fr auto }</style></head><body>" +
                   "<div class='box' id='col'><div>TOP</div><div></div><div id='bottom'>BOTTOM</div></div></body></html>";

        var (root, _) = await PdfGeneratorLayoutHarness.LayoutAsync(html, new PdfGenerateConfig(), BundledFonts.PinSansSerifAsync);
        var col = DomUtils.GetBoxById(root, "col")!;
        Assert.True(col.ActualBottom - col.Location.Y < 40);
    }

    [Theory]
    [InlineData(".bottom { margin-top: auto }", "")]
    [InlineData("", "justify-content: space-between;")]
    [InlineData(".mid { flex: 1 1 auto }", "")]
    public async Task TheBottomItemSitsAtTheFootOfTheContainer(string extraCss, string containerStyle)
    {
        var html = "<!DOCTYPE html><html><head><style>@page { size: A4; margin: 10mm } " +
                   "body { margin:0; font: 10pt sans-serif } " +
                   ".box { display:flex; flex-direction:column; min-height:200mm; " + containerStyle + " } " + extraCss +
                   "</style></head><body><div class='box' id='col'><div id='top'>TOP</div>" +
                   (extraCss.Contains(".mid") ? "<div class='mid'></div>" : "") +
                   "<div class='bottom' id='bottom'>BOTTOM</div></div><div>AFTER</div></body></html>";

        var (root, _) = await PdfGeneratorLayoutHarness.LayoutAsync(html, new PdfGenerateConfig(), BundledFonts.PinSansSerifAsync);
        var col = DomUtils.GetBoxById(root, "col")!;
        var bottom = DomUtils.GetBoxById(root, "bottom")!;
        Assert.Equal(col.ActualBottom, bottom.ActualBottom, 0.5);
    }
}
