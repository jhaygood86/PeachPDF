using PeachDrawing;
using PeachDrawing.Core;

namespace PeachBrackets.Tests;

/// <summary>Fixed and sticky boxes and fixed backgrounds follow the viewport of a continuous document.</summary>
public class ViewportTests
{
    private static HtmlDocumentOptions Screen(double viewportHeight = 300)
    {
        var options = new HtmlDocumentOptions { MediaType = MediaType.Screen, PageWidthPoints = 400, ViewportHeightPoints = viewportHeight, RecordTextRuns = true };
        options.SetMargins(0);
        return options;
    }

    // The bounding box of the red pixels of the whole surface, as left, top, right, bottom, or null when there are none.
    private static async Task<(int X0, int Y0, int X1, int Y1)?> RedAsync(RasterRenderContext context, HtmlDocument document)
    {
        var canvas = await TestDocument.PaintAsync(context, document);
        int x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1;
        var height = (int)document.Pages[0].HeightPoints;
        var width = (int)document.Pages[0].WidthPoints;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var p = TestDocument.Pixel(canvas, x, y);
                if (p.R > 200 && p.G < 60 && p.B < 60)
                {
                    x0 = Math.Min(x0, x);
                    y0 = Math.Min(y0, y);
                    x1 = Math.Max(x1, x);
                    y1 = Math.Max(y1, y);
                }
            }
        }

        return x1 < 0 ? null : (x0, y0, x1, y1);
    }

    private const string Fixed = "<div style='height:2000pt'></div><div id=f style='position:fixed;right:10pt;bottom:10pt;width:50pt;height:20pt;background:red'></div>";

    [Fact]
    public async Task AFixedBox_IsAtTheViewportCornerItsOffsetsNameWhateverTheScroll()
    {
        var (context, document) = await TestDocument.LoadAsync(TestDocument.Wrap(Fixed), Screen());

        Assert.Equal((340, 270, 389, 289), await RedAsync(context, document));

        document.SetViewport(new Rect(0, 500, 400, 300));
        Assert.Equal((340, 770, 389, 789), await RedAsync(context, document));

        document.SetViewport(new Rect(0, 0, 400, 300));
        Assert.Equal((340, 270, 389, 289), await RedAsync(context, document));
    }

    [Fact]
    public async Task MovingTheViewport_ReportsWhereThingsWereAndAre()
    {
        var (_, document) = await TestDocument.LoadAsync(TestDocument.Wrap(Fixed), Screen());

        var regions = document.SetViewport(new Rect(0, 500, 400, 300));

        Assert.Contains(regions, r => r.Bounds.Top <= 270 && r.Bounds.Bottom >= 290);
        Assert.Contains(regions, r => r.Bounds.Top <= 770 && r.Bounds.Bottom >= 790);
        Assert.Empty(document.SetViewport(new Rect(0, 500, 400, 300)));
    }

    private const string Sticky = "<div style='height:100pt'></div><div style='height:1000pt'><div id=s style='position:sticky;top:0;height:30pt;background:red'></div></div>";

    [Theory]
    [InlineData(0, 100)]     // above it: where it is in the flow
    [InlineData(50, 100)]
    [InlineData(400, 400)]   // scrolled past: held at the top of the viewport
    [InlineData(1200, 1070)] // scrolled beyond its containing block: pushed up by the end of it
    public async Task AStickyBox_SticksToTheTopOfTheViewport_ButNotPastItsContainingBlock(double scroll, int expectedTop)
    {
        var (context, document) = await TestDocument.LoadAsync(TestDocument.Wrap(Sticky), Screen());
        document.SetViewport(new Rect(0, scroll, 400, 300));

        var red = await RedAsync(context, document);

        Assert.NotNull(red);
        Assert.Equal(expectedTop, red!.Value.Y0);
        Assert.Equal(expectedTop + 29, red.Value.Y1);
    }

    [Fact]
    public async Task ABoxChangesLayer_OnlyWhenAStickyBoxBeginsToStickOrIsReleased()
    {
        // A host paints the content layer under its scroller and the attached layer over it: a box that moves while it is held changes only the
        // attached one, but one that begins to stick (or is released) has to be painted again in the content layer where it was.
        var (_, document) = await TestDocument.LoadAsync(TestDocument.Wrap(Sticky), Screen());

        document.SetViewport(new Rect(0, 0, 400, 300));
        Assert.False(document.ViewportLayersChanged);

        document.SetViewport(new Rect(0, 400, 400, 300));
        Assert.True(document.ViewportLayersChanged);

        document.SetViewport(new Rect(0, 410, 400, 300));
        Assert.False(document.ViewportLayersChanged);

        document.SetViewport(new Rect(0, 0, 400, 300));
        Assert.True(document.ViewportLayersChanged);

        var (_, fixedDocument) = await TestDocument.LoadAsync(TestDocument.Wrap(Fixed), Screen());
        fixedDocument.SetViewport(new Rect(0, 0, 400, 300));
        Assert.True(fixedDocument.ViewportLayersChanged); // a fixed box is attached from the first viewport on
        fixedDocument.SetViewport(new Rect(0, 500, 400, 300));
        Assert.False(fixedDocument.ViewportLayersChanged);
    }

    [Fact]
    public async Task AStickyBoxWithABottomInset_StaysAtTheBottomOfTheViewportUntilItsPlaceComes()
    {
        const string html = "<div style='height:1000pt'></div><div id=s style='position:sticky;bottom:0;height:30pt;background:red'></div><div style='height:500pt'></div>";
        var (context, document) = await TestDocument.LoadAsync(TestDocument.Wrap(html), Screen());

        document.SetViewport(new Rect(0, 0, 400, 300));
        Assert.Equal(270, (await RedAsync(context, document))!.Value.Y0);

        document.SetViewport(new Rect(0, 900, 400, 300));
        Assert.Equal(1000, (await RedAsync(context, document))!.Value.Y0);
    }

    [Fact]
    public async Task TheTextOfAFixedBox_IsWhereItIsDrawn()
    {
        const string html = "<div style='height:2000pt'></div><div style='position:fixed;left:10pt;top:20pt'>pinned</div>";
        var (_, document) = await TestDocument.LoadAsync(TestDocument.Wrap(html), Screen());

        var before = document.TextRuns.Single(r => r.Text.Contains("pinned")).Bounds;
        document.SetViewport(new Rect(0, 700, 400, 300));
        var after = document.TextRuns.Single(r => r.Text.Contains("pinned")).Bounds;

        Assert.Equal(before.Y + 700, after.Y, 1);
        Assert.Equal(before.X, after.X, 1);
    }

    [Fact]
    public async Task ALinkInAStickyBox_IsWhereItIsDrawn()
    {
        const string html = "<div style='height:100pt'></div><div style='height:1000pt'><div style='position:sticky;top:0'><a id=l href='https://example.com/'>go there</a></div></div>";
        var (_, document) = await TestDocument.LoadAsync(TestDocument.Wrap(html), new HtmlDocumentOptions { MediaType = MediaType.Screen, PageWidthPoints = 400, ViewportHeightPoints = 300, CollectFormFields = true });

        document.SetViewport(new Rect(0, 400, 400, 300));

        var link = Assert.Single(document.Annotations.Links);
        Assert.InRange(link.Bounds.Top, 399, 420);
    }

    [Fact]
    public async Task APagedDocument_HasNoViewportToMove()
    {
        var options = new HtmlDocumentOptions { PageWidthPoints = 400, PageHeightPoints = 300 };
        var (_, document) = await TestDocument.LoadAsync(TestDocument.Wrap("<div style='position:fixed;top:0'>x</div>"), options);

        Assert.Empty(document.SetViewport(new Rect(0, 500, 400, 300)));
    }

    [Fact]
    public async Task AFixedBackground_IsPositionedAgainstTheViewport()
    {
        // A 20pt red square (as a gradient) that sits at the top-left of the viewport, however far the document is scrolled.
        const string html = "<div style=\"height:2000pt;background:linear-gradient(red,red) no-repeat fixed 0 0 / 20pt 20pt\"></div>";
        var (context, document) = await TestDocument.LoadAsync(TestDocument.Wrap(html), Screen());

        Assert.Equal(0, (await RedAsync(context, document))!.Value.Y0);

        document.SetViewport(new Rect(0, 600, 400, 300));
        var red = await RedAsync(context, document);
        Assert.Equal(600, red!.Value.Y0);
        Assert.Equal(619, red.Value.Y1);
    }

    [Theory]
    [InlineData("<div style=\"height:2000pt;background:linear-gradient(red,red) no-repeat fixed 0 0 / 20pt 20pt\"></div>", true)]
    [InlineData("<div style=\"height:2000pt;background:red url(x.png) no-repeat fixed\"></div>", true)]
    [InlineData("<div style=\"height:2000pt;background:linear-gradient(red,red) no-repeat 0 0 / 20pt 20pt\"></div>", false)]
    [InlineData("<div style='height:2000pt'></div><div style='position:fixed;top:0;width:10pt;height:10pt;background:red'></div>", false)]
    public async Task HasViewportBackgrounds_IsTrueOnlyForAFixedAttachmentBackground(string html, bool expected)
    {
        var (_, document) = await TestDocument.LoadAsync(TestDocument.Wrap(html), Screen());

        Assert.Equal(expected, document.HasViewportBackgrounds);
    }

    [Fact]
    public async Task HasViewportBackgrounds_IsFalseForAPagedDocument()
    {
        var options = new HtmlDocumentOptions { MediaType = MediaType.Print, PageWidthPoints = 400, PageHeightPoints = 300 };
        var (_, document) = await TestDocument.LoadAsync(
            TestDocument.Wrap("<div style=\"height:100pt;background:linear-gradient(red,red) no-repeat fixed 0 0 / 20pt 20pt\"></div>"), options);

        Assert.False(document.HasViewportBackgrounds);
    }

    [Fact]
    public async Task AFixedBackground_IsPaintedByTheContentLayerAlone_SoAHostMustRepaintItOnScroll()
    {
        // The image is part of the content (it sits under its box's text), so it is not in the viewport-attached layer a host could keep still.
        const string html = "<div style=\"height:2000pt;background:linear-gradient(red,red) no-repeat fixed 0 0 / 20pt 20pt\"></div>";
        var (context, document) = await TestDocument.LoadAsync(TestDocument.Wrap(html), Screen());
        document.SetViewport(new Rect(0, 600, 400, 300));

        var height = (int)document.Pages[0].HeightPoints;
        Assert.NotNull(RedOf(await LayerAsync(context, document, DocumentPaintLayer.Content), 400, height));
        Assert.Null(RedOf(await LayerAsync(context, document, DocumentPaintLayer.ViewportAttached), 400, height));
    }

    // Paints the layers a host that scrolls natively draws separately, onto one white bitmap each.
    private static RasterCanvas NewCanvas(RasterRenderContext context, HtmlDocument document)
    {
        var page = document.Pages[0];
        var canvas = context.CreateCanvas((int)Math.Ceiling(page.WidthPoints), (int)Math.Ceiling(page.HeightPoints));
        canvas.DrawRectangle(canvas.GetSolidBrush(PaintColor.FromArgb(255, 255, 255, 255)), 0, 0, page.WidthPoints, page.HeightPoints);
        return canvas;
    }

    private static (int X0, int Y0, int X1, int Y1)? RedOf(RasterCanvas canvas, int width, int height)
    {
        int x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var p = TestDocument.Pixel(canvas, x, y);
                if (p.R > 200 && p.G < 60 && p.B < 60)
                {
                    x0 = Math.Min(x0, x);
                    y0 = Math.Min(y0, y);
                    x1 = Math.Max(x1, x);
                    y1 = Math.Max(y1, y);
                }
            }
        }

        return x1 < 0 ? null : (x0, y0, x1, y1);
    }

    private static async Task<RasterCanvas> LayerAsync(RasterRenderContext context, HtmlDocument document, DocumentPaintLayer layer)
    {
        var canvas = NewCanvas(context, document);
        await document.PaintPageAsync(0, canvas, layer);
        return canvas;
    }

    [Fact]
    public async Task APinnedStickyBox_IsOnlyInTheAttachedLayer()
    {
        var (context, document) = await TestDocument.LoadAsync(TestDocument.Wrap(Sticky), Screen());
        document.SetViewport(new Rect(0, 400, 400, 300));
        var height = (int)document.Pages[0].HeightPoints;

        Assert.Null(RedOf(await LayerAsync(context, document, DocumentPaintLayer.Content), 400, height));
        Assert.Equal((0, 400, 399, 429), RedOf(await LayerAsync(context, document, DocumentPaintLayer.ViewportAttached), 400, height));
    }

    [Fact]
    public async Task AStickyBoxBeforeItsThreshold_StaysInTheContentLayer()
    {
        var (context, document) = await TestDocument.LoadAsync(TestDocument.Wrap(Sticky), Screen());
        document.SetViewport(new Rect(0, 50, 400, 300));
        var height = (int)document.Pages[0].HeightPoints;

        Assert.Equal((0, 100, 399, 129), RedOf(await LayerAsync(context, document, DocumentPaintLayer.Content), 400, height));
        Assert.Null(RedOf(await LayerAsync(context, document, DocumentPaintLayer.ViewportAttached), 400, height));
    }

    [Fact]
    public async Task AFixedBox_IsAttachedEvenAtTheTopOfTheDocument()
    {
        var (context, document) = await TestDocument.LoadAsync(TestDocument.Wrap(Fixed), Screen());
        document.SetViewport(new Rect(0, 0, 400, 300));
        var height = (int)document.Pages[0].HeightPoints;

        Assert.Null(RedOf(await LayerAsync(context, document, DocumentPaintLayer.Content), 400, height));
        Assert.Equal((340, 270, 389, 289), RedOf(await LayerAsync(context, document, DocumentPaintLayer.ViewportAttached), 400, height));
    }

    [Fact]
    public async Task TheTwoLayersTogether_AreWhatOnePassDraws()
    {
        const string html = "<div style='height:100pt'></div><div style='height:1000pt;background:#cde'><div style='position:sticky;top:0;height:30pt;background:red'>head</div>text</div><div style='position:fixed;right:10pt;bottom:10pt;width:50pt;height:20pt;background:red'></div>";
        var (context, document) = await TestDocument.LoadAsync(TestDocument.Wrap(html), Screen());
        document.SetViewport(new Rect(0, 400, 400, 300));

        var one = await LayerAsync(context, document, DocumentPaintLayer.All);
        var two = NewCanvas(context, document);
        await document.PaintPageAsync(0, two, DocumentPaintLayer.Content);
        await document.PaintPageAsync(0, two, DocumentPaintLayer.ViewportAttached);

        Assert.True(one.ToPixelBuffer().PremultipliedRgba.Span.SequenceEqual(two.ToPixelBuffer().PremultipliedRgba.Span));
    }

    [Fact]
    public async Task APagedDocument_HasNothingInTheAttachedLayer()
    {
        var options = new HtmlDocumentOptions { PageWidthPoints = 400, PageHeightPoints = 300 };
        var (context, document) = await TestDocument.LoadAsync(TestDocument.Wrap("<div style='position:fixed;top:0;width:50pt;height:20pt;background:red'></div>"), options);
        var canvas = NewCanvas(context, document);

        await document.PaintPageAsync(0, canvas, DocumentPaintLayer.ViewportAttached);

        Assert.Null(RedOf(canvas, 400, 300));
    }

    [Fact]
    public async Task AStickyTableHeaderCell_FollowsTheTopOfALongTable()
    {
        var rows = string.Concat(Enumerable.Range(0, 60).Select(i => $"<tr><td style='height:20pt;padding:0'>row {i}</td></tr>"));
        var html = $"<table style='border-collapse:collapse;width:100%'><thead><tr><th style='position:sticky;top:0;height:20pt;padding:0;background:red'>head</th></tr></thead><tbody>{rows}</tbody></table>";
        var (context, document) = await TestDocument.LoadAsync(TestDocument.Wrap(html), Screen());

        document.SetViewport(new Rect(0, 300, 400, 300));
        var height = (int)document.Pages[0].HeightPoints;

        Assert.Equal(300, RedOf(await LayerAsync(context, document, DocumentPaintLayer.ViewportAttached), 400, height)!.Value.Y0);
        Assert.Null(RedOf(await LayerAsync(context, document, DocumentPaintLayer.Content), 400, height));
    }

    [Fact]
    public async Task AFixedBoxWithAutoWidth_ShrinksToFitItsContent()
    {
        var (context, document) = await TestDocument.LoadAsync(
            TestDocument.Wrap("<div style='height:2000pt'></div><div style='position:fixed;right:10pt;bottom:10pt;background:red'>badge</div>"), Screen());

        var red = await RedAsync(context, document);

        Assert.NotNull(red);
        Assert.Equal(389, red!.Value.X1);
        Assert.InRange(red.Value.X0, 300, 380);
    }

    // A link in a sticky bar at the top of a long page: the in-page links of a page with a bar that stays put.
    private const string StickyLink = "<div style='position:sticky;top:0;height:40pt;width:200pt;background:#eee'><a id=l href='#t' style='display:block;height:40pt;width:100pt'>Jump</a></div><div style='height:3000pt'></div><div id=t>target</div>";

    [Fact]
    public async Task ALinkInAStickyBar_ShowsThePointerWhereverTheBarIs()
    {
        var (_, document) = await TestDocument.LoadAsync(TestDocument.Wrap(StickyLink), Screen());

        Assert.True(document.GetCursorAt(0, 10, 10).Shape == CursorShape.Pointer, "at the top of the document");

        // Scrolled down, the bar is held at the top of the viewport: the pointer is there, and not where the bar would have been.
        document.SetViewport(new Rect(0, 500, 400, 300));
        Assert.True(document.GetCursorAt(0, 10, 510).Shape == CursorShape.Pointer, "where the bar is pinned");
        Assert.Equal(CursorShape.Default, document.GetCursorAt(0, 10, 10).Shape);
    }

    [Fact]
    public async Task TheBoundsOfAPinnedBox_AreWhereItIsDrawn()
    {
        var (_, document) = await TestDocument.LoadAsync(TestDocument.Wrap(StickyLink), new HtmlDocumentOptions { MediaType = MediaType.Screen, PageWidthPoints = 400, ViewportHeightPoints = 300, RecordTextRuns = true });
        document.SetViewport(new Rect(0, 500, 400, 300));

        Assert.Equal(500, document.GetElementBounds("l")!.Value.Y, 1);
        var link = document.GetAccessibilityTree().Nodes.Single(n => n.Role == PeachBrackets.Accessibility.AccessibilityRole.Link);
        Assert.Equal(500, link.Bounds[0].Bounds.Y, 1);
    }

    [Fact]
    public async Task AFixedLink_ShowsThePointerAtTheViewport()
    {
        const string html = "<div style='height:3000pt'></div><a href='#x' style='position:fixed;top:20pt;left:10pt;display:block;width:100pt;height:30pt'>pinned</a>";
        var (_, document) = await TestDocument.LoadAsync(TestDocument.Wrap(html), Screen());
        document.SetViewport(new Rect(0, 700, 400, 300));

        Assert.Equal(CursorShape.Pointer, document.GetCursorAt(0, 20, 730).Shape);
        Assert.Equal(CursorShape.Default, document.GetCursorAt(0, 20, 30).Shape);
    }

    [Fact]
    public async Task ALinkInAStickyBar_FollowsTheViewportThroughRepeatedPaintsAndScrolls()
    {
        var (context, document) = await TestDocument.LoadAsync(TestDocument.Wrap(StickyLink), Screen());

        foreach (var scroll in new[] { 300.0, 900.0, 200.0 })
        {
            document.SetViewport(new Rect(0, scroll, 400, 300));
            await document.PaintPageAsync(0, NewCanvas(context, document), DocumentPaintLayer.ViewportAttached);

            var link = Assert.Single(document.Annotations.Links);
            Assert.InRange(link.Bounds.Top, scroll - 1, scroll + 1);
        }
    }

    [Fact]
    public async Task AnInPageTarget_IsScrolledToLessItsScrollMargin_AndTheRootsScrollPadding()
    {
        const string html = "<div style='height:300pt'></div><div id=a style='height:50pt'></div><div id=b style='height:50pt;scroll-margin-top:48px'></div><div id=c style='height:50pt;scroll-margin-top:30pt'></div>";
        var (_, plain) = await TestDocument.LoadAsync(TestDocument.Wrap(html), Screen());

        Assert.Equal(300, plain.GetScrollOffset("a")!.Value, 1);
        Assert.Equal(350 - 36, plain.GetScrollOffset("b")!.Value, 1);   // 48px is 36pt
        Assert.Equal(400 - 30, plain.GetScrollOffset("c")!.Value, 1);
        Assert.Null(plain.GetScrollOffset("missing"));

        var (_, padded) = await TestDocument.LoadAsync(TestDocument.Wrap(html, "<style>html{scroll-padding-top:20pt}</style>"), Screen());
        Assert.Equal(300 - 20, padded.GetScrollOffset("a")!.Value, 1);
        Assert.Equal(350 - 36 - 20, padded.GetScrollOffset("b")!.Value, 1);
    }

    [Fact]
    public async Task ATargetThatIsNeverNegative_AndWhatTheViewportMovedIsStillWhereItIsInTheFlow()
    {
        const string html = "<div id=t style='position:sticky;top:0;height:40pt'></div><div style='height:3000pt'></div>";
        var (_, document) = await TestDocument.LoadAsync(TestDocument.Wrap(html), Screen());
        document.SetViewport(new Rect(0, 500, 400, 300));

        Assert.Equal(0, document.GetScrollOffset("t")!.Value, 1);
    }
}
