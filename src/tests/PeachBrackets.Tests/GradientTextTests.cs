using PeachDrawing.Core;

namespace PeachBrackets.Tests;

/// <summary>Gradient text, the way a site writes it: a gradient on an inline span clipped to the glyphs with transparent text colour.</summary>
public class GradientTextTests
{
    private const string Css =
        ".g { background: linear-gradient(120deg, #ff0000, #ff0000); -webkit-background-clip: text; background-clip: text; color: transparent; font-size: 40pt; font-weight: 700 }";

    private static async Task<(int Red, int Total)> CountAsync(string body)
    {
        var options = new HtmlDocumentOptions { MediaType = MediaType.Screen, PageWidthPoints = 400, ViewportHeightPoints = 200 };
        options.SetMargins(0);
        var (context, document) = await TestDocument.LoadAsync(TestDocument.Wrap(body, $"<style>{Css}</style>"), options);
        var canvas = await TestDocument.PaintAsync(context, document);
        int red = 0, total = 0;
        for (var y = 0; y < 140; y++)
        {
            for (var x = 0; x < 300; x++)
            {
                var p = TestDocument.Pixel(canvas, x, y);
                total++;
                if (p.R > 200 && p.G < 60 && p.B < 60)
                    red++;
            }
        }

        return (red, total);
    }

    private static async Task<int> RedOfAsync(string css, string body)
    {
        var options = new HtmlDocumentOptions { MediaType = MediaType.Screen, PageWidthPoints = 400, ViewportHeightPoints = 100 };
        options.SetMargins(0);
        var (context, document) = await TestDocument.LoadAsync(TestDocument.Wrap(body, $"<style>{css}</style>"), options);
        var canvas = await TestDocument.PaintAsync(context, document);
        var red = 0;
        for (var y = 0; y < 60; y++)
        {
            for (var x = 0; x < 300; x++)
            {
                var p = TestDocument.Pixel(canvas, x, y);
                if (p.R > 200 && p.G < 60 && p.B < 60)
                    red++;
            }
        }

        return red;
    }

    private const string Text = "font-size:40pt;font-weight:700;color:transparent";
    private const string Markup = "<p style='margin:0'><span class=v>Peach</span></p>";

    [Fact]
    public async Task AVariableInTheBackgroundShorthand_DoesNotUndoALaterBackgroundClip()
    {
        // The shorthand is written first, so the longhand written after it wins: the clip is text, not the border box the shorthand resets it to.
        var literal = await RedOfAsync(".v{background:linear-gradient(120deg,#ff0000,#ff0000);background-clip:text;" + Text + "}", Markup);
        var withVariable = await RedOfAsync(":root{--c:#ff0000}.v{background:linear-gradient(120deg,#ff0000,var(--c));-webkit-background-clip:text;background-clip:text;" + Text + "}", Markup);

        Assert.InRange(literal, 100, 8000);
        Assert.InRange(withVariable, literal * 0.8, literal * 1.2);
    }

    [Fact]
    public async Task AVariableInTheBackgroundShorthand_StillResetsAnEarlierBackgroundClip()
    {
        // Written the other way round the shorthand is last, so it wins and the clip is the border box: a gradient box behind transparent text.
        var literal = await RedOfAsync(".v{background-clip:text;background:linear-gradient(120deg,#ff0000,#ff0000);" + Text + "}", Markup);
        var withVariable = await RedOfAsync(":root{--c:#ff0000}.v{background-clip:text;background:linear-gradient(120deg,#ff0000,var(--c));" + Text + "}", Markup);

        Assert.True(literal > 3000, "literal " + literal);
        Assert.InRange(withVariable, literal * 0.9, literal * 1.1);
    }

    [Fact]
    public async Task ALaterLonghandThatUsesAVariable_WinsOverAnEarlierVariableShorthand()
    {
        var clipped = await RedOfAsync(":root{--k:text;--c:#ff0000}.v{background:linear-gradient(120deg,#ff0000,var(--c));background-clip:var(--k);" + Text + "}", Markup);

        Assert.InRange(clipped, 100, 8000);
    }

    [Theory]
    [InlineData("<h1 style='margin:0;font-size:40pt;font-weight:800;letter-spacing:-0.03em;line-height:1.1'>HTML to PDF.<br><span class=g>Pure .NET.</span></h1>")]
    [InlineData("<h1 style='margin:0;font-size:40pt;letter-spacing:-0.03em'><span class=g>Pure</span></h1>")]
    [InlineData("<h1 style='margin:0;font-size:40pt'>HTML<br><span class=g>Pure</span></h1>")]
    [InlineData("<div style='display:flex'><span style='font-size:30pt;font-weight:700;letter-spacing:-0.02em'><span class=g>Peach</span>PDF</span></div>")]
    public async Task GradientText_InRealisticMarkup_IsClippedToTheGlyphs(string body)
    {
        var (red, total) = await CountAsync(body);

        Assert.InRange(red, 100, total / 3);
    }

    [Fact]
    public async Task GradientText_IsClippedToTheGlyphs_NotABox()
    {
        var (red, total) = await CountAsync("<p style='margin:0'><span class=g>Peach</span></p>");

        // Glyph ink is a fraction of the line box; a filled box would be most of the sampled area.
        Assert.InRange(red, 100, total / 3);
    }

    [Fact]
    public async Task GradientText_InsideAnInlineBlock_IsClippedToTheGlyphs()
    {
        var (red, total) = await CountAsync("<h1 style='margin:0;font-size:40pt'>Pure <span class=g>.NET</span></h1>");

        Assert.InRange(red, 100, total / 3);
    }
}
