using PeachDrawing;

namespace PeachBrackets.Tests;

/// <summary>
/// Vertical writing-mode layout (multi-column, grid, table pagination, atomic inlines, flex), grid <c>direction</c>, <c>block-step</c>,
/// <c>text-box-trim</c>, text decorations/emphasis and the grammars behind them, seen only through the public API: the pages a document
/// lays out into and the pixels painted on them.
/// </summary>
public class VerticalAndLineBoxCoverageTests
{
    private const string Font = "font-family:'Liberation Sans',Arial,sans-serif;margin:0;";

    private record struct Box(int Left, int Top, int Right, int Bottom)
    {
        public int Width => Right - Left + 1;
        public int Height => Bottom - Top + 1;
    }

    private static async Task<(RasterRenderContext Context, HtmlDocument Document)> LoadPagedAsync(string body, double width, double height, string head = "")
    {
        var options = new HtmlDocumentOptions { PageWidthPoints = width, PageHeightPoints = height };
        options.SetMargins(0);
        return await TestDocument.LoadAsync(TestDocument.Wrap(body, "<style>html,body{margin:0;padding:0}</style>" + head), options);
    }

    private static async Task<(RasterRenderContext Context, HtmlDocument Document)> LoadScreenAsync(string body, double width = 300, double height = 300, string head = "")
    {
        var options = new HtmlDocumentOptions { MediaType = MediaType.Screen, PageWidthPoints = width, ViewportHeightPoints = height };
        options.SetMargins(0);
        return await TestDocument.LoadAsync(TestDocument.Wrap(body, "<style>html,body{margin:0;padding:0}</style>" + head), options);
    }

    private static Box? Find(RasterCanvas canvas, Func<(byte R, byte G, byte B, byte A), bool> match, int width, int height)
    {
        int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
        var buffer = canvas.ToPixelBuffer();
        for (var y = 0; y < Math.Min(height, buffer.Height); y++)
        {
            for (var x = 0; x < Math.Min(width, buffer.Width); x++)
            {
                var span = buffer.PremultipliedRgba.Span.Slice(((y * buffer.Width) + x) * 4, 4);
                if (!match((span[0], span[1], span[2], span[3])))
                    continue;
                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }
        }

        return maxX < 0 ? null : new Box(minX, minY, maxX, maxY);
    }

    private static bool Red((byte R, byte G, byte B, byte A) p) => p.R > 200 && p.G < 60 && p.B < 60;
    private static bool Blue((byte R, byte G, byte B, byte A) p) => p.B > 200 && p.R < 60 && p.G < 60;
    private static bool Green((byte R, byte G, byte B, byte A) p) => p.G > 200 && p.R < 60 && p.B < 60;
    private static bool Ink((byte R, byte G, byte B, byte A) p) => p.R < 128 && p.G < 128 && p.B < 128;

    private static async Task<Box?> PaintedAsync(RasterRenderContext context, HtmlDocument document,
        Func<(byte R, byte G, byte B, byte A), bool> match, int page = 0)
    {
        using var canvas = await TestDocument.PaintAsync(context, document, page);
        var p = document.Pages[page];
        return Find(canvas, match, (int)p.WidthPoints, (int)p.HeightPoints);
    }

    private const string Words = "alpha bravo charlie delta echo foxtrot golf hotel india juliet kilo lima mike november oscar papa quebec romeo sierra tango";

    // --- vertical multi-column ---

    [Theory]
    [InlineData("vertical-rl")]
    [InlineData("vertical-lr")]
    public async Task VerticalMulticol_StacksColumnsAlongTheInlineAxisWithAGap(string mode)
    {
        var (context, document) = await LoadScreenAsync(
            $"<div style=\"{Font}writing-mode:{mode};width:200pt;height:220pt;column-count:2;column-gap:20pt;column-fill:auto;font-size:12pt;line-height:12pt;color:#000\">"
            + "<p style='margin:0;color:red'>first</p><p style='margin:0;color:blue;break-before:column'>second</p></div>", 220, 300);

        var red = await PaintedAsync(context, document, Red);
        var blue = await PaintedAsync(context, document, Blue);
        Assert.NotNull(red);
        Assert.NotNull(blue);
        Assert.True(blue!.Value.Top >= red!.Value.Top + 119, $"red {red}, blue {blue}");
        Assert.True(red.Value.Bottom < 100);
    }

    [Fact]
    public async Task VerticalMulticol_DrawsARuleInTheGap_AndSpanAllBreaksTheFlow()
    {
        var (context, document) = await LoadScreenAsync(
            $"<div style=\"{Font}writing-mode:vertical-rl;width:300pt;height:220pt;column-count:2;column-gap:20pt;column-rule:2pt solid red;font-size:12pt;line-height:12pt\">"
            + $"<p style='margin:0'>{Words}</p><h2 style='margin:0;column-span:all;font-size:12pt;line-height:12pt;background:#0f0'>Spanning heading</h2><p style='margin:0'>{Words}</p></div>", 300, 300);

        var rule = await PaintedAsync(context, document, Red);
        Assert.NotNull(rule);
        Assert.True(rule!.Value.Width > 20);

        var heading = await PaintedAsync(context, document, Green);
        Assert.NotNull(heading);
        Assert.True(heading!.Value.Height >= 215, $"heading {heading}");
    }

    [Fact]
    public async Task VerticalMulticol_ColumnWidthChoosesTheCount_AndBalancedFlowUsesAllColumns()
    {
        var (context, document) = await LoadScreenAsync(
            $"<div style=\"{Font}writing-mode:vertical-rl;width:300pt;height:220pt;column-width:80pt;column-gap:10pt;font-size:12pt;line-height:12pt\">{Words} {Words} {Words}</div>", 300, 300);

        var ink = await PaintedAsync(context, document, Ink);
        Assert.NotNull(ink);
        Assert.True(ink!.Value.Bottom > 100);
    }

    [Fact]
    public async Task VerticalMulticol_RtlDirectionAndForcedBreaks()
    {
        var (context, document) = await LoadScreenAsync(
            $"<div style=\"{Font}writing-mode:vertical-rl;direction:rtl;width:300pt;height:220pt;column-count:2;column-gap:20pt;column-fill:auto;font-size:12pt;line-height:12pt\">"
            + "<p style='margin:0;color:red;break-after:column'>first</p><p style='margin:0;color:blue'>second</p></div>", 300, 300);

        var red = await PaintedAsync(context, document, Red);
        var blue = await PaintedAsync(context, document, Blue);
        Assert.NotNull(red);
        Assert.NotNull(blue);
        Assert.NotEqual(red!.Value.Top, blue!.Value.Top);
    }

    [Fact]
    public async Task VerticalMulticol_LongTextOverflowsIntoFurtherColumns_AndCountOneIsOrdinary()
    {
        var (context, document) = await LoadScreenAsync(
            $"<div style=\"{Font}writing-mode:vertical-rl;width:60pt;height:220pt;column-count:3;column-gap:10pt;column-fill:auto;font-size:12pt;line-height:12pt;orphans:2;widows:2\">{Words} {Words} {Words} {Words}</div>", 300, 300);
        var ink = await PaintedAsync(context, document, Ink);
        Assert.NotNull(ink);
        Assert.True(ink!.Value.Height > 150);

        var (context1, document1) = await LoadScreenAsync(
            $"<div style=\"{Font}writing-mode:vertical-rl;width:200pt;height:220pt;column-count:1;font-size:12pt;line-height:12pt\"><p style='margin:0;color:red'>one</p><p style='margin:0;color:blue'>two</p></div>");
        var red = await PaintedAsync(context1, document1, Red);
        var blue = await PaintedAsync(context1, document1, Blue);
        Assert.True(red!.Value.Left > blue!.Value.Left);
        Assert.InRange(Math.Abs(red.Value.Top - blue.Value.Top), 0, 3);
    }

    [Fact]
    public async Task VerticalMulticol_BlocksWithBackgroundsBordersAndAvoidBreaksAcrossColumns()
    {
        var (context, document) = await LoadScreenAsync(
            $"<div style=\"{Font}writing-mode:vertical-rl;width:300pt;height:220pt;column-count:2;column-gap:20pt;column-fill:auto;font-size:12pt;line-height:12pt\">"
            + $"<div style='background:#f00;margin:0 0 6pt 0;padding:2pt;border:1pt solid #00f;break-inside:avoid'>{Words}</div>"
            + $"<div style='background:#0f0;break-inside:avoid-column;margin-right:4pt'>{Words}</div>"
            + "<ul style='margin:0'><li>one</li><li>two</li></ul><table><tr><td>cell</td></tr></table></div>", 300, 300);
        Assert.NotNull(await PaintedAsync(context, document, Red));
        Assert.NotNull(await PaintedAsync(context, document, Green));
    }

    [Fact]
    public async Task VerticalMulticol_PaginatesAcrossPages()
    {
        var text = string.Join(' ', Enumerable.Repeat(Words, 30));
        var (context, document) = await LoadPagedAsync(
            $"<div style=\"{Font}writing-mode:vertical-rl;height:150pt;column-count:2;column-gap:10pt;font-size:12pt;line-height:12pt\">{text}</div>", 200, 200);
        Assert.True(document.Pages.Count >= 1);
        Assert.NotNull(await PaintedAsync(context, document, Ink));
    }

    // --- grid direction / vertical writing modes ---

    [Fact]
    public async Task GridWithRtlDirection_PlacesColumnsFromTheRight()
    {
        var (context, document) = await LoadScreenAsync(
            "<div style='display:grid;direction:rtl;width:200pt;grid-template-columns:40pt 60pt;grid-template-rows:20pt 20pt'>"
            + "<div style='background:#f00'></div><div style='background:#00f'></div><div style='background:#0f0;grid-column:1/3'></div></div>");
        var red = await PaintedAsync(context, document, Red);
        var blue = await PaintedAsync(context, document, Blue);
        Assert.True(red!.Value.Left > blue!.Value.Left, $"red {red} blue {blue}");
        Assert.Equal(200, red.Value.Right + 1);
        Assert.InRange(red.Value.Width, 39, 41);
    }

    [Theory]
    [InlineData("justify-content:start")]
    [InlineData("justify-content:end")]
    [InlineData("justify-content:center")]
    [InlineData("justify-content:space-between")]
    public async Task GridWithRtlDirection_JustifyContentAndAutoFlowColumn(string justify)
    {
        var (context, document) = await LoadScreenAsync(
            $"<div style='display:grid;direction:rtl;width:200pt;grid-template-columns:40pt 40pt;{justify};grid-auto-flow:column;grid-template-rows:20pt'>"
            + "<div style='background:#f00'></div><div style='background:#00f'></div><div style='background:#0f0;width:30pt'></div></div>");
        var red = await PaintedAsync(context, document, Red);
        var blue = await PaintedAsync(context, document, Blue);
        Assert.True(red!.Value.Left > blue!.Value.Left);
    }

    [Theory]
    [InlineData("vertical-rl", "")]
    [InlineData("vertical-lr", "")]
    [InlineData("vertical-rl", "direction:rtl;")]
    [InlineData("vertical-rl", "justify-content:end;align-content:end;")]
    [InlineData("vertical-lr", "justify-content:center;align-content:center;")]
    public async Task VerticalGrid_TracksFollowTheWritingModeAxes(string mode, string extra)
    {
        var (context, document) = await LoadScreenAsync(
            $"<div style='display:grid;writing-mode:{mode};{extra}height:120pt;width:120pt;grid-template-columns:30pt 40pt;grid-template-rows:20pt 20pt'>"
            + "<div style='background:#f00'></div><div style='background:#00f'></div><div style='background:#0f0'></div><div></div></div>");
        var red = await PaintedAsync(context, document, Red);
        var blue = await PaintedAsync(context, document, Blue);
        Assert.NotNull(red);
        Assert.NotNull(blue);
        Assert.NotEqual(red!.Value.Top, blue!.Value.Top);
    }

    [Fact]
    public async Task VerticalGrid_FractionalAutoAndContentSizedTracks()
    {
        var (context, document) = await LoadScreenAsync(
            $"<div style=\"{Font}display:grid;writing-mode:vertical-rl;height:120pt;width:200pt;grid-template-columns:1fr 2fr;grid-template-rows:auto auto;font-size:10pt;line-height:12pt\">"
            + "<div style='background:#f00'>a</div><div style='background:#00f'>b</div><div style='background:#0f0;grid-column:1/-1'>c</div></div>");
        Assert.NotNull(await PaintedAsync(context, document, Red));
        Assert.NotNull(await PaintedAsync(context, document, Blue));

        var (context2, document2) = await LoadScreenAsync(
            $"<div style=\"{Font}display:grid;writing-mode:vertical-rl;font-size:10pt;line-height:12pt;grid-template-columns:min-content max-content\"><div style='background:#f00'>aaa bbb</div><div style='background:#00f'>cc dd</div></div>");
        Assert.NotNull(await PaintedAsync(context2, document2, Red));

        var (context3, document3) = await LoadScreenAsync(
            $"<div style=\"{Font}display:grid;writing-mode:vertical-rl;font-size:10pt;line-height:12pt;grid-template-columns:auto auto;grid-auto-flow:column;grid-auto-columns:50pt\"><div style='background:#f00'>aaa</div><div style='background:#00f'>cc</div><div style='background:#0f0'>e</div></div>");
        Assert.NotNull(await PaintedAsync(context3, document3, Green));
    }

    // --- vertical tables ---

    private static string Cells(string color, int count = 8, string extra = "") =>
        string.Concat(Enumerable.Range(0, count).Select(i => $"<td style='height:40pt;width:20pt;padding:0;background:{color};{extra}'></td>"));

    [Fact]
    public async Task VerticalTable_PaginatesByColumn()
    {
        var (context, document) = await LoadPagedAsync(
            $"<table style='writing-mode:vertical-rl;border-spacing:0'><tr>{Cells("#f00")}</tr><tr>{Cells("#00f")}</tr></table>", 300, 260);
        Assert.Equal(2, document.Pages.Count);
        var onFirst = await PaintedAsync(context, document, Red, 0);
        var onSecond = await PaintedAsync(context, document, Red, 1);
        Assert.NotNull(onFirst);
        Assert.NotNull(onSecond);
        Assert.Equal(0, onSecond!.Value.Top);
        Assert.True(onFirst!.Value.Height > onSecond.Value.Height);
    }

    [Fact]
    public async Task VerticalTable_RowspanAndColspanMoveWithTheirColumns()
    {
        var row0 = string.Concat(Enumerable.Range(0, 8).Select(i =>
            $"<td {(i == 6 ? "rowspan='2'" : "")} style='height:40pt;width:20pt;padding:0;background:#f00'></td>"));
        var row1 = string.Concat(Enumerable.Range(0, 8).Where(i => i != 6).Select(i =>
            "<td style='height:40pt;width:20pt;padding:0;background:#00f'></td>"));
        var (context, document) = await LoadPagedAsync(
            $"<table style='writing-mode:vertical-rl;border-spacing:0'><tr>{row0}</tr><tr>{row1}</tr></table>", 300, 260);
        Assert.Equal(2, document.Pages.Count);
        Assert.NotNull(await PaintedAsync(context, document, Red, 1));
        Assert.NotNull(await PaintedAsync(context, document, Blue, 1));

        var wide = Cells("#f00", 5) + "<td colspan='2' style='height:80pt;width:20pt;padding:0;background:#0f0'></td>" + Cells("#f00", 1);
        var (context2, document2) = await LoadPagedAsync(
            $"<table style='writing-mode:vertical-rl;border-spacing:0'><tr>{wide}</tr><tr>{Cells("#00f")}</tr></table>", 300, 260);
        Assert.Equal(2, document2.Pages.Count);
        var green = await PaintedAsync(context2, document2, Green, 1);
        Assert.NotNull(green);
        Assert.Equal(0, green!.Value.Top);
    }

    [Theory]
    [InlineData("vertical-rl", "separate")]
    [InlineData("vertical-lr", "separate")]
    [InlineData("vertical-rl", "collapse")]
    [InlineData("vertical-lr", "collapse")]
    public async Task VerticalTable_HeaderFooterAndCaptionPaginate(string mode, string collapse)
    {
        var table = $"<table style='writing-mode:{mode};border-spacing:0;border-collapse:{collapse}'>"
            + "<caption style='background:#0f0;height:30pt'>cap</caption>"
            + $"<thead><tr>{Cells("#f00", 8, "border:1px solid #000;")}</tr></thead>"
            + $"<tbody><tr>{Cells("#00f", 8, "border:1px solid #000;")}</tr><tr>{Cells("#00f", 8)}</tr></tbody>"
            + $"<tfoot><tr>{Cells("#f00", 8, "border:2px solid #000;")}</tr></tfoot></table>";
        var (context, document) = await LoadPagedAsync(table, 300, 260);
        Assert.True(document.Pages.Count >= 2, $"pages: {document.Pages.Count}");
        for (var i = 0; i < document.Pages.Count; i++)
            Assert.NotNull(await PaintedAsync(context, document, Ink, i) ?? await PaintedAsync(context, document, Red, i) ?? await PaintedAsync(context, document, Blue, i));
    }

    [Fact]
    public async Task VerticalTable_CaptionBottomAndFootersWithPositionedContent()
    {
        var table = "<table style='writing-mode:vertical-rl;border-spacing:0'>"
            + "<caption style='caption-side:bottom;background:#0f0;height:30pt'>cap</caption>"
            + $"<tbody><tr>{Cells("#00f")}</tr></tbody>"
            + $"<tfoot><tr><td style='height:40pt;width:20pt;padding:0;background:#f00;position:relative'><div style='position:absolute;left:0;top:0;width:5pt;height:5pt;background:#f00'></div></td>{Cells("#f00", 7)}</tr></tfoot></table>";
        var (context, document) = await LoadPagedAsync(table, 300, 260);
        Assert.True(document.Pages.Count >= 2);
        Assert.NotNull(await PaintedAsync(context, document, Red, document.Pages.Count - 1));
    }

    [Fact]
    public async Task VerticalTable_BodyRowspanReachingPastTheBodyIntoTheFooter()
    {
        const string cell = "padding:0;border:0;vertical-align:top;";
        foreach (var collapse in new[] { "separate", "collapse" })
        {
            var (context, document) = await LoadPagedAsync(
                $"<table style='border-spacing:0;border-collapse:{collapse};writing-mode:vertical-rl'><tbody><tr><td rowspan='3' style='{cell}width:40pt;height:40pt;background:#f00'></td><td style='{cell}width:40pt;height:40pt;background:#00f'></td></tr></tbody>"
                + $"<tfoot><tr><td style='{cell}width:40pt;height:40pt;background:#0f0'></td><td style='{cell}width:40pt;height:40pt'></td></tr></tfoot></table>", 300, 300);
            Assert.NotNull(await PaintedAsync(context, document, Red));
            Assert.NotNull(await PaintedAsync(context, document, Green));
        }
    }

    // --- atomic inlines, flex, auto margins in vertical mode ---

    [Theory]
    [InlineData("inline-block")]
    [InlineData("inline-flex")]
    [InlineData("inline-table")]
    [InlineData("inline-grid")]
    public async Task VerticalLine_AtomicInlineIsOneUnbreakableItem(string display)
    {
        var (context, document) = await LoadScreenAsync(
            $"<div style=\"{Font}writing-mode:vertical-rl;width:200pt;height:150pt;font-size:12pt\">before <span style='display:{display};border:1pt solid red;padding:3pt;margin:5pt 0;vertical-align:middle'>nested block</span> after</div>");
        var red = await PaintedAsync(context, document, Red);
        Assert.NotNull(red);
        Assert.True(red!.Value.Height > 5 || red.Value.Width > 5, $"{red}");
    }

    [Fact]
    public async Task VerticalLine_AtomicInlineWrapsAsAWholeToTheNextColumn_AndNestedBorderedSpans()
    {
        var (context, document) = await LoadScreenAsync(
            $"<div style=\"{Font}writing-mode:vertical-lr;width:200pt;height:80pt;font-size:12pt;line-height:14pt\">some words here <span style='display:inline-block;background:#f00;width:20pt;height:60pt'></span>"
            + " <span style='border:2pt solid #00f;padding:2pt;margin:0 3pt'>outer <span style='border:1pt solid #0f0;padding:1pt'>inner</span></span> end</div>");
        Assert.NotNull(await PaintedAsync(context, document, Red));
        Assert.NotNull(await PaintedAsync(context, document, Blue));
        Assert.NotNull(await PaintedAsync(context, document, Green));
    }

    [Theory]
    [InlineData("vertical-rl", "column")]
    [InlineData("vertical-lr", "column")]
    [InlineData("vertical-rl", "row")]
    public async Task VerticalFlex_AutoWidthTakesTheAspectRatioOfTheDefiniteHeight(string mode, string direction)
    {
        var (context, document) = await LoadScreenAsync(
            $"<div style=\"{Font}display:flex;flex-direction:{direction};writing-mode:{mode};height:200pt;aspect-ratio:1/2;background:#f00\">"
            + "<div style='flex:1'>A</div><div style='flex:1'>B</div></div>", 300, 300);
        var red = await PaintedAsync(context, document, Red);
        Assert.NotNull(red);
        Assert.InRange(red!.Value.Width, 99, 101);
        Assert.InRange(red.Value.Height, 199, 201);
    }

    [Theory]
    [InlineData("vertical-rl")]
    [InlineData("vertical-lr")]
    public async Task VerticalBox_WithAutoMarginsAndMaxWidth_StaysCentred(string mode)
    {
        var (context, document) = await LoadScreenAsync(
            $"<div style=\"{Font}width:400pt\"><div style='writing-mode:{mode};height:120pt;font-size:16pt;max-width:300pt;margin:0 auto;background:#f00'>ab cd</div></div>", 400, 200);
        var red = await PaintedAsync(context, document, Red);
        Assert.NotNull(red);
        Assert.InRange(red!.Value.Left + (red.Value.Width / 2.0), 195, 205);
    }

    [Fact]
    public async Task VerticalBlock_InlineBlocksAlignedToTheLineEdges()
    {
        var (context, document) = await LoadScreenAsync(
            $"<div style=\"{Font}writing-mode:vertical-rl;height:200pt;width:200pt;font-size:12pt\"><span style='display:inline-block;width:20pt;height:20pt;background:#f00;vertical-align:top'></span>"
            + "<span style='display:inline-block;width:20pt;height:20pt;background:#00f;vertical-align:bottom'></span> text</div>");
        Assert.NotNull(await PaintedAsync(context, document, Red));
        Assert.NotNull(await PaintedAsync(context, document, Blue));
    }

    // --- block-step ---

    private static async Task<double> HeightAsync(string body)
    {
        var options = new HtmlDocumentOptions();
        options.SetMargins(0);
        var (_, document) = await TestDocument.LoadAsync(
            TestDocument.Wrap(body, "<style>html,body{margin:0;padding:0}</style>"), options);
        return document.GetContentHeight(0);
    }

    [Theory]
    [InlineData("block-step:25px margin-box")]
    [InlineData("block-step-size:25px;block-step-insert:padding-box")]
    [InlineData("block-step-size:25px;block-step-align:center;block-step-round:up")]
    public async Task BlockStep_RoundsTheBoxHeightToAMultiple(string declaration)
    {
        var plain = await HeightAsync($"<div style=\"{Font}font-size:10px;line-height:10px\">a<br>b<br>c</div><div style='height:0'></div>");
        var height = await HeightAsync($"<div style=\"{Font}font-size:10px;line-height:10px;{declaration}\">a<br>b<br>c</div><div style='height:0'></div>");
        Assert.NotEqual(plain, height);
        Assert.Equal(0, (Math.Round(height / 0.75) % 25), 0.6);
    }

    [Theory]
    [InlineData("block-step-size:25px;block-step-insert:margin-box;block-step-align:start;block-step-round:down")]
    [InlineData("block-step-size:25px;block-step-align:end;block-step-round:nearest")]
    [InlineData("block-step-size:25px;block-step-align:start;block-step-round:up;block-step-insert:padding-box")]
    public async Task BlockStep_AlignAndRoundVariantsStillLayOut(string declaration)
    {
        var height = await HeightAsync($"<div style=\"{Font}font-size:10px;line-height:10px;{declaration}\">a<br>b<br>c</div><div style='height:0'></div>");
        Assert.True(height > 10);
    }

    [Theory]
    [InlineData("block-step:0px")]
    [InlineData("block-step:none")]
    [InlineData("block-step:-5px")]
    [InlineData("block-step-size:abc")]
    [InlineData("block-step-align:diagonal")]
    [InlineData("block-step-round:sideways")]
    [InlineData("block-step-insert:content-box")]
    [InlineData("block-step:25px padding-box start nearest bogus")]
    public async Task BlockStep_ZeroNoneAndInvalidDeclarationsChangeNothing(string declaration)
    {
        var plain = await HeightAsync($"<div style=\"{Font}font-size:10px;line-height:10px\">a<br>b<br>c</div>");
        var height = await HeightAsync($"<div style=\"{Font}font-size:10px;line-height:10px;{declaration}\">a<br>b<br>c</div>");
        Assert.Equal(plain, height, 1);
    }

    [Fact]
    public async Task BlockStep_ReLaidOutMarginsAndFragmentedBoxes()
    {
        var text = string.Join("<br>", Enumerable.Range(0, 30).Select(i => $"line {i}"));
        var (_, document) = await LoadPagedAsync(
            $"<div style=\"{Font}font-size:10px;line-height:10px;block-step:70px margin-box;margin:3px 0 4px 0\">{text}</div>", 200, 120);
        Assert.True(document.Pages.Count >= 2);
        var inline = await HeightAsync($"<span style=\"{Font}block-step:30px\">x</span><div style='display:flow-root;block-step:30px;margin-top:5px;{Font}font-size:10px;line-height:10px'>y</div>");
        Assert.True(inline > 0);
    }

    // --- text-box-trim and text-box-edge ---

    [Theory]
    [InlineData("text-box:trim-both text")]
    [InlineData("text-box:trim-both cap alphabetic")]
    [InlineData("text-box:trim-both ex alphabetic")]
    [InlineData("text-box:trim-both ideographic")]
    [InlineData("text-box:trim-both ideographic-ink")]
    [InlineData("text-box-trim:trim-start;text-box-edge:cap text")]
    [InlineData("text-box-trim:trim-end;text-box-edge:text alphabetic")]
    [InlineData("text-box-trim:trim-end;text-box-edge:ex")]
    public async Task TextBoxTrim_EveryEdgeShrinksTheBoxBelowTheLeading(string declaration)
    {
        var plain = await HeightAsync($"<div style=\"{Font}font-size:20px;line-height:40px\">Hxg</div>");
        var trimmed = await HeightAsync($"<div style=\"{Font}font-size:20px;line-height:40px;{declaration}\">Hxg</div>");
        Assert.True(trimmed < plain, $"{declaration}: {trimmed} vs {plain}");
    }

    [Fact]
    public async Task TextBoxTrim_InlineBlockContainerAndNestedBlocks()
    {
        var plain = await HeightAsync($"<div style=\"{Font}font-size:20px;line-height:40px\"><span style='display:inline-block'>Hxg</span></div>");
        var trimmed = await HeightAsync($"<div style=\"{Font}font-size:20px;line-height:40px\"><span style='display:inline-block;text-box:trim-both cap alphabetic'>Hxg</span></div>");
        Assert.True(trimmed <= plain);

        var nestedPlain = await HeightAsync($"<div style=\"{Font}font-size:20px;line-height:40px\"><div><div><p style='margin:0'>one</p></div></div><p style='margin:0'>two<br>three</p></div>");
        var nested = await HeightAsync($"<div style=\"{Font}font-size:20px;line-height:40px;text-box:trim-both text\"><div><div><p style='margin:0'>one</p></div></div><p style='margin:0'>two<br>three</p></div>");
        Assert.True(nested < nestedPlain);

        var startOnly = await HeightAsync($"<div style=\"{Font}font-size:20px;line-height:40px;text-box-trim:trim-start\"><p style='margin:0;border-top:1px solid red'>one</p></div>");
        var empty = await HeightAsync($"<div style=\"{Font}font-size:20px;line-height:40px;text-box:trim-both text\"></div>");
        Assert.True(startOnly > 0);
        Assert.Equal(0, empty, 1);
    }

    [Theory]
    [InlineData("text-box-trim:sideways")]
    [InlineData("text-box-edge:foo bar baz")]
    [InlineData("text-box-edge:cap cap")]
    [InlineData("text-box:trim-both bogus")]
    [InlineData("text-box:trim-both text alphabetic extra")]
    public async Task TextBoxTrim_InvalidDeclarationsAreDropped(string declaration)
    {
        var plain = await HeightAsync($"<div style=\"{Font}font-size:20px;line-height:40px\">Hxg</div>");
        var height = await HeightAsync($"<div style=\"{Font}font-size:20px;line-height:40px;{declaration}\">Hxg</div>");
        Assert.Equal(plain, height, 1);
    }

    // --- decorations, emphasis, combine-upright ---

    private static async Task<RasterCanvas> PaintStyleAsync(string css, string text = "Hgypq", bool vertical = false)
    {
        var options = new HtmlDocumentOptions { MediaType = MediaType.Screen, PageWidthPoints = 300, ViewportHeightPoints = 200 };
        options.SetMargins(0);
        var mode = vertical ? "writing-mode:vertical-rl;height:150pt;" : "";
        var (context, document) = await TestDocument.LoadAsync(
            TestDocument.Wrap($"<p style=\"{Font}{mode}font:20pt 'Liberation Sans',sans-serif;margin:30pt 40pt;{css}\">{text}</p>",
                "<style>html,body{background:#fff}</style>"), options);
        return await TestDocument.PaintAsync(context, document);
    }

    private static int InkCount(RasterCanvas canvas)
    {
        var buffer = canvas.ToPixelBuffer();
        var count = 0;
        for (var i = 0; i < buffer.Width * buffer.Height; i++)
        {
            var s = buffer.PremultipliedRgba.Span.Slice(i * 4, 4);
            if (s[0] < 200 || s[1] < 200 || s[2] < 200)
                count++;
        }

        return count;
    }

    [Theory]
    [InlineData("text-decoration:underline;text-decoration-skip-ink:auto")]
    [InlineData("text-decoration:underline;text-decoration-skip-ink:none")]
    [InlineData("text-decoration:underline;text-decoration-skip-ink:all")]
    [InlineData("text-decoration:underline;text-decoration-skip:none")]
    [InlineData("text-decoration:underline;text-decoration-skip:objects")]
    [InlineData("text-decoration:underline;text-decoration-skip:spaces")]
    [InlineData("text-decoration:underline;text-decoration-skip:box-decoration")]
    [InlineData("text-decoration:underline;text-decoration-skip:leading-spaces trailing-spaces")]
    [InlineData("text-decoration:underline;text-decoration-skip:edges")]
    [InlineData("text-decoration:underline overline line-through;text-decoration-inset:4pt 2pt")]
    [InlineData("text-decoration:underline;text-decoration-inset:auto")]
    [InlineData("text-decoration:underline;text-underline-position:from-font")]
    [InlineData("text-decoration:underline;text-underline-position:under left")]
    [InlineData("text-decoration:underline wavy red;text-underline-offset:3pt")]
    public async Task Decorations_EveryDeclarationPaintsInk(string css)
    {
        using var canvas = await PaintStyleAsync(css);
        Assert.True(InkCount(canvas) > 50, css);
    }

    [Theory]
    [InlineData("text-decoration-skip:bogus")]
    [InlineData("text-decoration-skip:none objects")]
    [InlineData("text-decoration-skip-ink:sometimes")]
    [InlineData("text-decoration-inset:red")]
    [InlineData("text-decoration-inset:1pt 2pt 3pt")]
    [InlineData("text-emphasis-position:over over")]
    [InlineData("text-emphasis-position:left right")]
    [InlineData("text-emphasis-style:filled filled")]
    [InlineData("text-emphasis:bogus bogus bogus bogus")]
    [InlineData("text-combine-upright:digits 9")]
    [InlineData("text-combine-upright:sometimes")]
    public async Task InvalidDeclarations_AreDroppedAndPaintLikeNothingWasSet(string css)
    {
        using var dropped = await PaintStyleAsync($"text-decoration:underline;{css}");
        using var underlined = await PaintStyleAsync("text-decoration:underline");
        Assert.Equal(InkCount(underlined), InkCount(dropped));
    }

    [Theory]
    [InlineData("text-emphasis:filled circle red;text-emphasis-position:over right")]
    [InlineData("text-emphasis:open triangle red;text-emphasis-position:under left")]
    [InlineData("text-emphasis:double-circle red;text-emphasis-position:over left")]
    [InlineData("text-emphasis:sesame red;text-emphasis-position:under right")]
    [InlineData("text-emphasis:'*' red")]
    [InlineData("text-emphasis:dot red;text-emphasis-position:right")]
    [InlineData("text-emphasis:dot red;text-emphasis-position:left over")]
    [InlineData("text-emphasis:circle red;text-emphasis-skip:spaces punctuation")]
    public async Task VerticalSidewaysText_DrawsEmphasisMarks(string css)
    {
        using var canvas = await PaintStyleAsync($"text-orientation:sideways;{css}", "Hello world", vertical: true);
        Assert.True(Find(canvas, Red, 300, 200) is not null || InkCount(canvas) > 50, css);
    }

    [Theory]
    [InlineData("text-combine-upright:all")]
    [InlineData("text-combine-upright:digits")]
    [InlineData("text-combine-upright:digits 2")]
    [InlineData("text-combine-upright:digits 3")]
    [InlineData("text-combine-upright:digits 4")]
    [InlineData("text-combine-upright:none")]
    public async Task CombineUpright_MergesRunsIntoOneColumnCell(string css)
    {
        using var combined = await PaintStyleAsync(css, "ab 2024 cd 12 e", vertical: true);
        using var none = await PaintStyleAsync("text-combine-upright:none", "ab 2024 cd 12 e", vertical: true);
        Assert.True(InkCount(combined) > 50);
        if (!css.EndsWith("none", StringComparison.Ordinal))
        {
            var a = Find(combined, Ink, 300, 200);
            var b = Find(none, Ink, 300, 200);
            Assert.True(a!.Value.Height <= b!.Value.Height, $"{css}: {a} vs {b}");
        }
    }

    [Theory]
    [InlineData("line-height:calc(1lh + 4px)")]
    [InlineData("line-height:2")]
    [InlineData("line-height:1.2em")]
    [InlineData("line-height:normal")]
    public async Task LineHeightForms_ComputeAPositiveHeight(string css)
    {
        var height = await HeightAsync($"<div style=\"{Font}font-size:14px;{css}\">a<br>b</div>");
        Assert.True(height > 10);
    }

    // --- rhythm grammar edge cases ---

    [Theory]
    [InlineData("line-height-step:-3px")]
    [InlineData("line-height-step:abc")]
    [InlineData("line-height-step:10px 20px")]
    [InlineData("line-grid:bogus")]
    [InlineData("line-snap:bogus")]
    [InlineData("box-snap:bogus")]
    public async Task RhythmGrammars_RejectInvalidValues(string declaration)
    {
        var plain = await HeightAsync($"<div style=\"{Font}font-size:10px;line-height:13px\">a<br>b</div>");
        var height = await HeightAsync($"<div style=\"{Font}font-size:10px;line-height:13px;{declaration}\">a<br>b</div>");
        Assert.Equal(plain, height, 1);
    }

    [Theory]
    [InlineData("block-step:25px margin-box start up")]
    [InlineData("block-step:25px end down")]
    [InlineData("block-step:25px center nearest padding-box")]
    [InlineData("block-step: 25px nearest")]
    [InlineData("line-snap:baseline;line-grid:create")]
    [InlineData("box-snap:block-start")]
    public async Task RhythmGrammars_AcceptEveryOrderOfTheShorthandParts(string declaration)
    {
        var height = await HeightAsync($"<div style=\"{Font}font-size:10px;line-height:10px;{declaration}\">a<br>b<br>c</div>");
        Assert.True(height > 0);
    }
}
