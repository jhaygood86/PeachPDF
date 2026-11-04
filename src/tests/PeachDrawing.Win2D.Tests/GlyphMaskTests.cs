using PeachDrawing.Core;
using PeachDrawing.Text.Shaping;
using PeachPDF.Tests.TestSupport;

namespace PeachDrawing.Win2D.Tests;

/// <summary>A word made from the masks of its letters has to be the word rasterized whole, to well within what the eye can tell apart.</summary>
public class GlyphMaskTests
{
    private static async Task<Win2dRenderContext> ContextAsync()
    {
        var context = Win2dRenderContext.CreateSoftware();
        await using var stream = File.OpenRead(BundledFonts.Ttf);
        await context.AddFont(stream, "Source Sans 3");
        return context;
    }

    // Coverage at a device pixel and stripe, 0 outside the mask.
    private static int At(Win2dTextMask mask, int x, int y, int stripe)
    {
        var px = x - mask.Left;
        var py = y - mask.Top;
        if ((uint)px >= (uint)mask.Width || (uint)py >= (uint)mask.Height)
            return 0;

        return mask.Samples[(((py * mask.Width) + px) * mask.SubPixels) + stripe];
    }

    private static (double Mean, int Max, double Within16) Compare(Win2dTextMask a, Win2dTextMask b)
    {
        var left = Math.Min(a.Left, b.Left);
        var top = Math.Min(a.Top, b.Top);
        var right = Math.Max(a.Left + a.Width, b.Left + b.Width);
        var bottom = Math.Max(a.Top + a.Height, b.Top + b.Height);
        long sum = 0, count = 0, near = 0;
        var max = 0;
        for (var y = top; y < bottom; y++)
        {
            for (var x = left; x < right; x++)
            {
                for (var s = 0; s < a.SubPixels; s++)
                {
                    var d = Math.Abs(At(a, x, y, s) - At(b, x, y, s));
                    sum += d;
                    count++;
                    if (d > max) max = d;
                    if (d <= 16) near++;
                }
            }
        }

        return ((double)sum / count, max, (double)near / count);
    }

    [Theory]
    [InlineData("Hamburgefonstiv", 16, 1.0, 1.0, 0.0, 0.0, 3)]
    [InlineData("quick brown fox", 13, 1.0, 1.0, 0.3, 0.0, 3)]
    [InlineData("AVAWAY To Ty", 16, 1.0, 1.0, 0.6, 0.0, 3)]
    [InlineData("fi fl ffi", 18, 4.0 / 3.0, 1.5, 0.45, 0.2, 3)]
    [InlineData("Typography", 24, 1.0, 1.0, 0.1, 0.4, 1)]
    [InlineData("Typography", 24, 1.0, 1.0, 0.1, 0.49, 1)]
    [InlineData("Typography", 24, 1.0, 1.0, 0.1, 0.51, 3)]
    [InlineData("Typography", 17, 4.0 / 3.0, 1.0, 0.0, 0.97, 3)]
    [InlineData("Second line, 123.45", 12, 1.0, 2.0, 0.77, 0.0, 3)]
    public async Task AWordFromItsLetters_LooksLikeTheWordWhole(string text, double size, double ppp, double scale, double phaseX, double phaseY, int subPixels)
    {
        using var context = await ContextAsync();
        var font = context.GetFont("Source Sans 3", size, PaintFontStyle.Regular)!;

        var whole = Win2dTextMask.Rasterize(context, text, font, new PaintPoint(0, 0), 0, ppp, scale, scale, phaseX, phaseY, null, ShapeSettings.Default, subPixels)!.Value;
        var letters = context.GlyphMasks.TryRasterize(context, text, font, 0, ppp, scale, scale, phaseX, phaseY, null, ShapeSettings.Default, subPixels);

        Assert.NotNull(letters);
        var (mean, max, within) = Compare(whole, letters.Value);
        Assert.True(mean < 1.5, $"mean difference {mean:F3} of 255");
        Assert.True(within > 0.99 && max <= 40, $"samples within 16: {within:P3}, largest difference {max}");
    }

    [Fact]
    public async Task ALetterIsRasterizedOnceAndThenReused()
    {
        using var context = await ContextAsync();
        var font = context.GetFont("Source Sans 3", 16, PaintFontStyle.Regular)!;

        context.GlyphMasks.TryRasterize(context, "anna", font, 0, 1, 1, 1, 0, 0, null, ShapeSettings.Default, 3);
        var misses = context.GlyphMasks.Misses;
        context.GlyphMasks.TryRasterize(context, "nana", font, 0, 1, 1, 1, 0, 0, null, ShapeSettings.Default, 3);

        Assert.True(misses > 0);
        Assert.True(context.GlyphMasks.Hits > 0);
    }

    [Fact]
    public async Task WordsThatCannotBeMadeFromLetters_AreLeftToTheWholeWordRasterizer()
    {
        using var context = await ContextAsync();
        var bold = context.GetFont("Source Sans 3", 16, PaintFontStyle.Bold)!;
        var plain = context.GetFont("Source Sans 3", 16, PaintFontStyle.Regular)!;

        if (bold.SyntheticStyle != PeachDrawing.Text.SyntheticStyle.None)
            Assert.Null(context.GlyphMasks.TryRasterize(context, "word", bold, 0, 1, 1, 1, 0, 0, null, ShapeSettings.Default, 3));

        Assert.Null(context.GlyphMasks.TryRasterize(context, "word", plain, 0, 1, 1, 1, 0, 0, new FontPalette(0, []), ShapeSettings.Default, 3));
    }

    [Fact]
    public async Task TheCacheIsEmptiedWithTheContext()
    {
        var context = await ContextAsync();
        var font = context.GetFont("Source Sans 3", 16, PaintFontStyle.Regular)!;
        context.GlyphMasks.TryRasterize(context, "word", font, 0, 1, 1, 1, 0, 0, null, ShapeSettings.Default, 3);
        Assert.True(context.GlyphMasks.Count > 0);

        context.Dispose();

        Assert.Equal(0, context.GlyphMasks.Count);
    }
}
