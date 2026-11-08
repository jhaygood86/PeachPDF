using PeachDrawing;

namespace PeachBrackets.Tests;

/// <summary>
/// <c>text-emphasis-*</c>, <c>text-decoration-inset</c>, <c>text-decoration-skip</c> and <c>text-combine-upright</c>,
/// seen only through the public API: load a page, paint it, read pixels.
/// </summary>
public class TextEmphasisTests
{
    private static async Task<RasterCanvas> PaintAsync(string css, string text = "HHHH")
    {
        var options = new HtmlDocumentOptions { MediaType = MediaType.Screen, PageWidthPoints = 300, ViewportHeightPoints = 120 };
        options.SetMargins(0);
        var body = $"<p id='p' style='margin:30pt 0 0 0; font:20pt sans-serif; color:#fff; {css}'>{text}</p>";
        var (context, document) = await TestDocument.LoadAsync(
            TestDocument.Wrap(body, "<style>html,body{background:#fff;margin:0}</style>"), options);
        return await TestDocument.PaintAsync(context, document);
    }

    /// <summary>Counts pixels near the given colour in a band of rows.</summary>
    private static int Count(RasterCanvas canvas, int top, int bottom, Func<(byte R, byte G, byte B, byte A), bool> match)
    {
        var count = 0;
        for (var y = top; y < bottom; y++)
        {
            for (var x = 0; x < 300; x++)
            {
                if (match(TestDocument.Pixel(canvas, x, y)))
                    count++;
            }
        }

        return count;
    }

    private static bool IsTinted((byte R, byte G, byte B, byte A) p) => p.R < 250 || p.G < 250 || p.B < 250;

    private static bool IsRed((byte R, byte G, byte B, byte A) p) => p.R > 180 && p.G < 90 && p.B < 90;

    [Theory]
    [InlineData("text-emphasis: filled circle red")]
    [InlineData("text-emphasis: red open triangle")]
    [InlineData("text-emphasis: dot red")]
    [InlineData("text-emphasis: double-circle red")]
    [InlineData("text-emphasis: filled sesame red")]
    [InlineData("text-emphasis: &quot;*&quot; red")]
    [InlineData("text-emphasis-style: circle; text-emphasis-color: red")]
    public async Task EveryStyleSpelling_DrawsMarksAboveTheText(string declaration)
    {
        var canvas = await PaintAsync(declaration);

        // Text is white on white, so any tinted pixel above it is a mark; this does not depend on how
        // heavily a given platform's fallback font draws a thin glyph (a triangle, an asterisk).
        Assert.True(Count(canvas, 30, 44, IsTinted) > 0, "marks should be drawn over the text");
    }

    [Fact]
    public async Task UnderPosition_DrawsTheMarksBelowTheText()
    {
        var over = await PaintAsync("text-emphasis: circle red");
        var under = await PaintAsync("text-emphasis: circle red; text-emphasis-position: under left");

        Assert.True(Count(over, 30, 44, IsRed) > 0);
        Assert.Equal(0, Count(under, 30, 44, IsRed));
        Assert.True(Count(under, 60, 120, IsRed) > 0, "an under mark sits below the line");
    }

    [Fact]
    public async Task NoEmphasis_DrawsNoMarks()
    {
        var canvas = await PaintAsync("");

        Assert.Equal(0, Count(canvas, 0, 120, IsRed));
    }

    [Fact]
    public async Task SpacesGetNoMark_AndMarksFollowEachCharacter()
    {
        var spaced = await PaintAsync("text-emphasis: circle red", "H H");
        var solid = await PaintAsync("text-emphasis: circle red", "HHH");

        Assert.True(Count(spaced, 30, 44, IsRed) < Count(solid, 30, 44, IsRed));
    }

    [Theory]
    [InlineData("text-emphasis: circle circle red")]
    [InlineData("text-emphasis: red red")]
    [InlineData("text-emphasis: bogus")]
    [InlineData("text-emphasis: red;text-emphasis-style: filled filled")]
    [InlineData("text-emphasis-style: ''")]
    public async Task InvalidDeclarations_DrawNoRedMarks(string declaration)
    {
        var canvas = await PaintAsync(declaration + "; text-emphasis-color: red");

        Assert.Equal(0, Count(canvas, 30, 44, IsRed));
    }

    [Theory]
    [InlineData("left")]
    [InlineData("over under")]
    [InlineData("over right left")]
    public async Task InvalidPosition_IsDropped_SoTheMarksStayOver(string position)
    {
        var canvas = await PaintAsync($"text-emphasis: circle red; text-emphasis-position: {position}");

        Assert.True(Count(canvas, 30, 44, IsRed) > 0);
        Assert.Equal(0, Count(canvas, 60, 120, IsRed));
    }

    [Fact]
    public async Task Inset_ShortensTheUnderlineFromBothEnds()
    {
        var plain = await PaintAsync("text-decoration: underline red");
        var inset = await PaintAsync("text-decoration: underline red; text-decoration-inset: 10pt 20pt");
        var one = await PaintAsync("text-decoration: underline red; text-decoration-inset: 10pt");

        Assert.True(Count(inset, 0, 120, IsRed) < Count(one, 0, 120, IsRed));
        Assert.True(Count(one, 0, 120, IsRed) < Count(plain, 0, 120, IsRed));
    }

    [Fact]
    public async Task Inset_AutoChangesNothing_AndOversizedDrawsNothing()
    {
        var plain = await PaintAsync("text-decoration: underline red");
        var auto = await PaintAsync("text-decoration: underline red; text-decoration-inset: auto");
        var huge = await PaintAsync("text-decoration: underline red; text-decoration-inset: 250pt");

        Assert.Equal(Count(plain, 0, 120, IsRed), Count(auto, 0, 120, IsRed));
        Assert.Equal(0, Count(huge, 0, 120, IsRed));
    }

    [Theory]
    [InlineData("text-decoration-inset: 1pt 2pt 3pt")]
    [InlineData("text-decoration-inset: red")]
    [InlineData("text-decoration-inset: auto auto")]
    [InlineData("text-decoration-inset: 50%")]
    public async Task InvalidOrUnsupportedInset_LeavesTheLineAlone(string declaration)
    {
        var plain = await PaintAsync("text-decoration: underline red");
        var painted = await PaintAsync("text-decoration: underline red; " + declaration);

        Assert.Equal(Count(plain, 0, 120, IsRed), Count(painted, 0, 120, IsRed));
    }

    [Theory]
    [InlineData("text-decoration-skip: none")]
    [InlineData("text-decoration-skip: auto")]
    [InlineData("text-decoration-skip: objects spaces")]
    [InlineData("text-decoration-skip: leading-spaces trailing-spaces box-decoration ink")]
    [InlineData("text-decoration-skip: spaces spaces")]
    [InlineData("text-decoration-skip: spaces leading-spaces")]
    [InlineData("text-decoration-skip: nonsense")]
    [InlineData("text-decoration-skip: none auto")]
    [InlineData("text-combine-upright: none")]
    [InlineData("text-combine-upright: all")]
    [InlineData("text-combine-upright: digits")]
    [InlineData("text-combine-upright: digits 3")]
    [InlineData("text-combine-upright: digits 9")]
    [InlineData("text-combine-upright: digits x")]
    [InlineData("text-combine-upright: digits 2 3")]
    [InlineData("text-combine-upright: sideways")]
    public async Task SkipAndCombineValuesWithNothingToAct_On_DoNotChangeHowTheUnderlineIsDrawn(string declaration)
    {
        var plain = await PaintAsync("text-decoration: underline red");
        var painted = await PaintAsync("text-decoration: underline red; " + declaration);

        Assert.Equal(Count(plain, 0, 120, IsRed), Count(painted, 0, 120, IsRed));
    }

    [Fact]
    public async Task VerticalText_DrawsMarksBesideTheColumn()
    {
        var options = new HtmlDocumentOptions { MediaType = MediaType.Screen, PageWidthPoints = 300, ViewportHeightPoints = 120 };
        options.SetMargins(0);
        string Body(string position) =>
            $"<p style='margin:0 0 0 100pt; width:60pt; height:100pt; writing-mode:vertical-rl; text-orientation:upright; font:20pt sans-serif; color:#fff; text-emphasis: circle red; {position}'>HHH</p>";
        var (rightContext, rightDocument) = await TestDocument.LoadAsync(TestDocument.Wrap(Body(""), "<style>html,body{background:#fff;margin:0}</style>"), options);
        var right = await TestDocument.PaintAsync(rightContext, rightDocument);
        var (leftContext, leftDocument) = await TestDocument.LoadAsync(TestDocument.Wrap(Body("text-emphasis-position: over left"), "<style>html,body{background:#fff;margin:0}</style>"), options);
        var left = await TestDocument.PaintAsync(leftContext, leftDocument);

        double MeanRedX(RasterCanvas canvas)
        {
            double sum = 0;
            var count = 0;
            for (var x = 0; x < 300; x++)
                for (var y = 0; y < 120; y++)
                    if (IsRed(TestDocument.Pixel(canvas, x, y))) { sum += x; count++; }
            return count == 0 ? -1 : sum / count;
        }

        Assert.True(MeanRedX(right) > 0, "the marks are drawn");
        // The default side is the right of the column; left puts them on the other side of the glyphs.
        Assert.True(MeanRedX(right) > MeanRedX(left) + 5);
    }

    [Fact]
    public async Task CombineUpright_SetsTheDigitsInOneCell()
    {
        var options = new HtmlDocumentOptions { MediaType = MediaType.Screen, PageWidthPoints = 300, ViewportHeightPoints = 200 };
        options.SetMargins(0);
        string Body(string combine) =>
            $"<p style='margin:0; width:60pt; writing-mode:vertical-rl; text-orientation:upright; font:20pt sans-serif; background:#000; {combine}'>2024</p>";
        var (_, plain) = await TestDocument.LoadAsync(TestDocument.Wrap(Body(""), "<style>html,body{margin:0}</style>"), options);
        var (_, combined) = await TestDocument.LoadAsync(TestDocument.Wrap(Body("text-combine-upright: all"), "<style>html,body{margin:0}</style>"), options);

        Assert.True(combined.GetContentHeight(0) < plain.GetContentHeight(0));
    }

    [Fact]
    public async Task SkipSpaces_LeavesTheGapBetweenWordsUndecorated()
    {
        var plain = await PaintAsync("text-decoration: underline red; text-decoration-skip-ink: none", "H H");
        var spaces = await PaintAsync("text-decoration: underline red; text-decoration-skip-ink: none; text-decoration-skip: spaces", "H H");

        Assert.True(Count(spaces, 0, 120, IsRed) < Count(plain, 0, 120, IsRed));
    }
}
