namespace PeachBrackets.Tests;

/// <summary>
/// <c>pointer-events</c> on SVG content (SVG 1.1 section 16.4) decides whether the link an <c>&lt;a&gt;</c> inside an inline
/// <c>&lt;svg&gt;</c> writes can be clicked at all.
/// </summary>
public class SvgPointerEventsLinkTests
{
    private static async Task<int> LinkCountAsync(string svgAttributes, string anchorAttributes, string shapeAttributes = "", string hostStyle = "")
    {
        var (_, document) = await TestDocument.LoadAsync(TestDocument.Wrap(
            $"<svg width='100' height='50' style='{hostStyle}' {svgAttributes}><a href='https://example.com/x' {anchorAttributes}><rect width='100' height='50' {shapeAttributes}/></a></svg>"));
        return document.Annotations.Links.Count(l => l.Uri == "https://example.com/x");
    }

    [Fact]
    public async Task ALinkIsWrittenByDefault() =>
        Assert.Equal(1, await LinkCountAsync("", ""));

    [Fact]
    public async Task NoneOnTheAnchor_LeavesNoLink() =>
        Assert.Equal(0, await LinkCountAsync("", "pointer-events='none'"));

    [Fact]
    public async Task NoneOnTheSvgRoot_IsInheritedByItsLinks() =>
        Assert.Equal(0, await LinkCountAsync("pointer-events='none'", ""));

    [Fact]
    public async Task NoneOnTheHostBoxInCss_IsInheritedByItsLinks() =>
        Assert.Equal(0, await LinkCountAsync("", "", hostStyle: "pointer-events:none"));

    [Fact]
    public async Task AShapeThatTurnsItBackOn_KeepsTheLink() =>
        Assert.Equal(1, await LinkCountAsync("", "pointer-events='none'", "pointer-events='all'"));

    [Fact]
    public async Task VisiblePaintedNeedsPaint_SoAnUnpaintedShapeCannotBeHit() =>
        Assert.Equal(0, await LinkCountAsync("", "pointer-events='visiblePainted'", "fill='none'"));

    [Fact]
    public async Task VisibleFillDoesNotNeedPaint() =>
        Assert.Equal(1, await LinkCountAsync("", "pointer-events='visibleFill'", "fill='none'"));

    [Fact]
    public async Task VisibleKeywordsNeedAVisibleShape()
    {
        Assert.Equal(0, await LinkCountAsync("", "pointer-events='visible'", "visibility='hidden'"));
        Assert.Equal(1, await LinkCountAsync("", "pointer-events='all'", "visibility='hidden'"));
        Assert.Equal(1, await LinkCountAsync("", "pointer-events='painted'", "visibility='hidden'"));
    }

    [Fact]
    public async Task PaintedNeedsAFillOrAStroke()
    {
        Assert.Equal(0, await LinkCountAsync("", "pointer-events='painted'", "fill='none'"));
        Assert.Equal(1, await LinkCountAsync("", "pointer-events='painted'", "fill='none' stroke='black'"));
    }

    [Fact]
    public async Task APointerEventsDeclarationInAStyleAttributeCounts() =>
        Assert.Equal(0, await LinkCountAsync("", "style='pointer-events:none'"));

    [Fact]
    public async Task AnInvalidValueIsIgnored() =>
        Assert.Equal(1, await LinkCountAsync("", "pointer-events='bogus'"));

    [Fact]
    public async Task AUseOfAShape_TakesThePointerEventsOfTheUse()
    {
        var (_, document) = await TestDocument.LoadAsync(TestDocument.Wrap(
            "<svg width='100' height='50'><defs><rect id='r' width='100' height='50'/></defs><a href='https://example.com/x'><use href='#r' pointer-events='none'/></a></svg>"));
        Assert.DoesNotContain(document.Annotations.Links, l => l.Uri == "https://example.com/x");
    }
}
