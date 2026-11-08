using PeachBrackets.Html.Dom;
using PeachDrawing.Core;
using PeachPDF.Tests.TestSupport;

namespace PeachBrackets.Engine.Tests.Integration;

/// <summary>
/// A nested inline box's margin, border and padding inside vertical line flow: the sides along the inline axis (physical top and bottom) reserve
/// room once, at the box's true start and end, and the sides along the block axis (physical left and right) are painted on every column the box
/// spans without taking room from the column (CSS 2.1 section 8.1 and 10.8.1, css-writing-modes-4 section 4.1 and 7).
/// </summary>
public class VerticalInlineBoxSpacingTests
{
    private const double PointsPerPx = 0.75;

    private static async Task<(CssBox Block, CssBox Span)> LayoutAsync(string blockStyle, string spanStyle, string content = "aaa <span id='sp' style='{0}'>bbb</span> ccc")
    {
        var html = LayoutHarness.Wrap(
            $"<div id='el' style='font-size:16px;{blockStyle}'>{string.Format(content, spanStyle)}</div>");
        var (root, _) = await LayoutHarness.LayoutAsync(html);

        return (LayoutHarness.FindById(root, "el")!, LayoutHarness.FindById(root, "sp")!);
    }

    private static CssRect Word(CssBox block, string text) =>
        block.LineBoxes.SelectMany(l => l.Words).First(w => w.Text == text);

    [Theory]
    [InlineData("vertical-rl")]
    [InlineData("vertical-lr")]
    public async Task TheInlineStartAndEndSpacing_PushesTheBoxContentAndWhatFollowsAlongTheColumn(string writingMode)
    {
        var style = $"writing-mode:{writingMode};width:300px;height:400px";
        var (plain, _) = await LayoutAsync(style, "");
        var (padded, _) = await LayoutAsync(style, "border-top:2px solid red;padding-top:5px;border-bottom:3px solid red;padding-bottom:4px;margin-top:6px;margin-bottom:1px");

        // The word the box opens at moves down by the leading side's margin, border and padding...
        Assert.Equal(Word(plain, "bbb").Top + (6 + 2 + 5) * PointsPerPx, Word(padded, "bbb").Top, 2);
        // ...and what comes after the box by both sides.
        Assert.Equal(Word(plain, "ccc").Top + (6 + 2 + 5 + 4 + 3 + 1) * PointsPerPx, Word(padded, "ccc").Top, 2);
        // Text before the box is untouched.
        Assert.Equal(Word(plain, "aaa").Top, Word(padded, "aaa").Top, 3);
    }

    [Theory]
    [InlineData("vertical-rl")]
    [InlineData("vertical-lr")]
    public async Task TheBoxRectangle_CoversTheInlineAxisPaddingAndBorderOnce_AndTheBlockAxisOnTheColumn(string writingMode)
    {
        var (block, span) = await LayoutAsync($"writing-mode:{writingMode};width:300px;height:400px",
            "border:2px solid red;padding:5px");
        var word = Word(block, "bbb");
        var rect = Assert.Single(span.Rectangles).Value;

        Assert.Equal(word.Top - 7 * PointsPerPx, rect.Top, 2);
        Assert.Equal(word.Bottom + 7 * PointsPerPx, rect.Bottom, 2);
        Assert.Equal(word.Left - 7 * PointsPerPx, rect.Left, 2);
        Assert.Equal(word.Right + 7 * PointsPerPx, rect.Right, 2);
    }

    [Fact]
    public async Task ASpanWrappedOverColumns_GetsItsInlineEdgesOnceAndItsBlockEdgesOnEveryColumn()
    {
        // 90px of column height holds a few words, so the span's words fill more than one column.
        var (block, span) = await LayoutAsync("writing-mode:vertical-rl;width:400px;height:90px",
            "border:2px solid red;padding:5px", "<span id='sp' style='{0}'>bbb bbb bbb bbb bbb bbb bbb bbb</span>");

        Assert.True(block.LineBoxes.Count >= 2, "the span should wrap onto more than one column");
        Assert.Equal(block.LineBoxes.Count, span.Rectangles.Count);

        var first = block.LineBoxes[0];
        var last = block.LineBoxes[^1];
        var firstRect = span.Rectangles[first];
        var lastRect = span.Rectangles[last];

        // Inline axis (physical top/bottom): the leading edge only on the first column, the trailing edge only on the last.
        var firstTop = first.Words.Min(w => w.Top);
        var firstBottom = first.Words.Max(w => w.Bottom);
        var lastTop = last.Words.Min(w => w.Top);
        var lastBottom = last.Words.Max(w => w.Bottom);

        Assert.Equal(firstTop - 7 * PointsPerPx, firstRect.Top, 2);
        Assert.Equal(firstBottom, firstRect.Bottom, 2);
        Assert.Equal(lastTop, lastRect.Top, 2);
        Assert.Equal(lastBottom + 7 * PointsPerPx, lastRect.Bottom, 2);

        // Block axis (physical left/right): both edges on every column.
        foreach (var column in block.LineBoxes)
        {
            var rect = span.Rectangles[column];
            Assert.Equal(column.Words.Min(w => w.Left) - 7 * PointsPerPx, rect.Left, 2);
            Assert.Equal(column.Words.Max(w => w.Right) + 7 * PointsPerPx, rect.Right, 2);
        }
    }

    [Fact]
    public async Task WithDirectionRtl_TheInlineStartSpacingIsOnThePhysicalBottom()
    {
        var style = "writing-mode:vertical-rl;direction:rtl;width:300px;height:400px";
        var (plain, _) = await LayoutAsync(style, "");
        var (padded, span) = await LayoutAsync(style, "padding-top:5px;padding-bottom:9px");
        var word = Word(padded, "bbb");

        // The box opens at the bottom and the text runs up: the word sits 9px higher than without the padding (its start side is the bottom),
        // and the rectangle reaches 9px below the word and 5px above it.
        Assert.Equal(Word(plain, "bbb").Top - 9 * PointsPerPx, word.Top, 2);
        var rect = Assert.Single(span.Rectangles).Value;
        Assert.Equal(word.Bottom + 9 * PointsPerPx, rect.Bottom, 2);
        Assert.Equal(word.Top - 5 * PointsPerPx, rect.Top, 2);
    }

    [Theory]
    [InlineData("vertical-rl")]
    [InlineData("vertical-lr")]
    public async Task AWrappedSpansSlices_KeepTheirBlockAxisEdgesOnEveryColumn_AndTheInlineEdgesOnlyAtTheEnds(string writingMode)
    {
        var html = LayoutHarness.Wrap(
            $"<div id='el' style='font-size:16px;writing-mode:{writingMode};width:400px;height:90px'>"
            + "<span id='sp' style='border:2px solid red;padding:5px'>bbb bbb bbb bbb bbb bbb bbb bbb</span></div>");
        var (root, container) = await LayoutHarness.LayoutAsync(html);
        var block = LayoutHarness.FindById(root, "el")!;
        var span = LayoutHarness.FindById(root, "sp")!;

        var lines = FragmentPaintHarness.FragmentOf(container, span).Lines;
        Assert.True(lines.Count >= 2);

        // In reading order: vertical-rl reads right to left, vertical-lr left to right.
        var columns = (writingMode == "vertical-rl" ? lines.OrderByDescending(l => l.Rect.X) : lines.OrderBy(l => l.Rect.X)).ToList();

        Assert.All(columns, l => Assert.True(l.Slice.HasLeftEdge && l.Slice.HasRightEdge, "the block-axis edges are the box's own on every column"));
        Assert.True(columns[0].Slice.HasTopEdge);
        Assert.True(columns[^1].Slice.HasBottomEdge);
        Assert.All(columns.Skip(1), l => Assert.False(l.Slice.HasTopEdge, "only the first column opens the box"));
        Assert.All(columns.Take(columns.Count - 1), l => Assert.False(l.Slice.HasBottomEdge, "only the last column closes the box"));
    }

    [Fact]
    public async Task ANestedInline_AddsItsSpacingInsideItsParentsRectangle()
    {
        var (block, outer) = await LayoutAsync("writing-mode:vertical-lr;width:300px;height:400px",
            "padding:4px",
            "<span id='sp' style='{0}'><b id='in' style='padding:3px'>bbb</b></span>");
        var inner = LayoutHarness.FindById(block, "in")!;
        var word = Word(block, "bbb");

        var innerRect = Assert.Single(inner.Rectangles).Value;
        var outerRect = Assert.Single(outer.Rectangles).Value;

        Assert.Equal(word.Top - 3 * PointsPerPx, innerRect.Top, 2);
        Assert.Equal(word.Top - 7 * PointsPerPx, outerRect.Top, 2);
        Assert.Equal(word.Bottom + 7 * PointsPerPx, outerRect.Bottom, 2);
    }
}
