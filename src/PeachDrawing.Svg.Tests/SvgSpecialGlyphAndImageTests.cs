using PeachBrackets;
using PeachDrawing.Core;
using PeachDrawing.Text;
using PeachPDF.Tests.TestSupport;
using System.Buffers.Binary;
using System.Text;
using static PeachDrawing.Svg.Tests.SvgScene;

namespace PeachDrawing.Svg.Tests;

/// <summary>Bitmap and OpenType-SVG glyphs, an image that stores an orientation, and whole HTML documents drawn onto the canvas.</summary>
public class SvgSpecialGlyphAndImageTests
{
    [Theory]
    [InlineData("cbdt")]
    [InlineData("sbix")]
    public async Task ABitmapGlyphIsAnEmbeddedPngPlacedByItsBearingsAndDefinedOnce(string which)
    {
        var context = new SvgRenderContext();
        await using (var stream = File.OpenRead(which == "cbdt" ? BundledFonts.RealCbdt : BundledFonts.RealSbix))
            await context.AddFont(stream, "Bitmap Test");
        using var scene = new SvgScene(context, 300, 150);
        var font = context.GetFont("Bitmap Test", 64, PaintFontStyle.Regular)!;

        scene.Canvas.DrawString("AA", font, PaintColor.Black, new PaintPoint(5, 5), scene.Canvas.MeasureString("AA", font));

        var image = Assert.Single(scene.Defs(), e => e.Name == Ns + "image");
        Assert.StartsWith("data:image/png;base64,", Attr(image, "href"));
        var uses = scene.Drawn().Where(e => e.Name == Ns + "use" || e.Name == Ns + "g").ToList();
        Assert.True(uses.Count >= 2, "the glyph is drawn each time it occurs");
        Assert.DoesNotContain(scene.Defs(), e => e.Name == Ns + "path"); // no outline of a picture glyph
    }

    [Fact]
    public async Task AnOpenTypeSvgGlyphIsHandedToTheContextsGlyphPainterInPointsAndNotDrawnAsAnOutline()
    {
        var painter = new RecordingGlyphPainter(result: true);
        var context = new PaintingContext(painter) { PixelsPerPoint = 2 };
        await using (var stream = File.OpenRead(BundledFonts.SvgTest))
            await context.AddFont(stream, "Svg Glyph Test");
        using var scene = new SvgScene(context, 200, 200);
        var font = context.GetFont("Svg Glyph Test", 120, PaintFontStyle.Regular)!; // 60 points

        scene.Canvas.DrawString("A", font, PaintColor.FromArgb(255, 1, 2, 3), new PaintPoint(20, 20), scene.Canvas.MeasureString("A", font));

        var call = Assert.Single(painter.Calls);
        Assert.Equal(60, call.FontSize);
        Assert.Equal(10, call.OriginX, 6);     // 20 layout units at two to the point
        Assert.Equal(PaintColor.FromArgb(255, 1, 2, 3), call.Foreground);
        Assert.Empty(scene.Drawn()); // the painter drew the glyph (here, nothing), so no outline of it is written
    }

    [Fact]
    public async Task AGlyphPainterThatDeclinesLeavesTheOutline()
    {
        var painter = new RecordingGlyphPainter(result: false);
        var context = new PaintingContext(painter);
        await using (var stream = File.OpenRead(BundledFonts.SvgTest))
            await context.AddFont(stream, "Svg Glyph Test");
        using var scene = new SvgScene(context, 200, 200);
        var font = context.GetFont("Svg Glyph Test", 60, PaintFontStyle.Regular)!;

        scene.Canvas.DrawString("A", font, PaintColor.Black, new PaintPoint(20, 20), scene.Canvas.MeasureString("A", font));

        Assert.Single(painter.Calls);
        Assert.NotEmpty(scene.Drawn());
    }

    [Fact]
    public async Task ImagesStoringAnOrientationAreEmbeddedAsPngOfTheirStoredPixelsAndReportIt()
    {
        using var scene = Create();
        var raster = new RasterRenderContext();
        using var canvas = raster.CreateCanvas(6, 4);
        canvas.DrawRectangle(raster.GetSolidBrush(PaintColor.FromArgb(255, 10, 200, 30)), 0, 0, 6, 4);
        using var png = new MemoryStream();
        canvas.Save(png, "png", new PeachImage.Formats.Png.PngEncoderOptions());
        var upright = png.ToArray();
        var rotated = WithExifOrientation(upright, 6);

        var plain = scene.Context.ImageFromStream(new MemoryStream(upright));
        var oriented = scene.Context.ImageFromStream(new MemoryStream(rotated));
        await Task.CompletedTask;

        Assert.Equal(1, plain.ExifOrientation);
        Assert.Equal(6, oriented.ExifOrientation);
        Assert.Equal((6.0, 4.0), (oriented.Width, oriented.Height)); // as stored: the layout applies the orientation itself

        scene.Canvas.DrawImage(plain, new Rect(0, 0, 6, 4));
        scene.Canvas.DrawImage(oriented, new Rect(10, 0, 6, 4));
        var hrefs = scene.Defs().Where(e => e.Name == Ns + "image").Select(e => Attr(e, "href")).ToList();
        Assert.Equal(2, hrefs.Count);
        Assert.Equal("data:image/png;base64," + Convert.ToBase64String(upright), hrefs[0]); // an upright PNG is embedded byte for byte
        var embedded = Convert.FromBase64String(hrefs[1]["data:image/png;base64,".Length..]);
        Assert.DoesNotContain("eXIf", Encoding.Latin1.GetString(embedded)); // the viewer has no orientation left to apply
    }

    [Theory]
    [InlineData("<div style='opacity:.5'><div style='position:fixed;top:10px;left:10px'>fixed</div>text</div>")]
    [InlineData("<div style='transform:translate(5px,5px)'><div style='position:fixed;top:10px;left:10px'>fixed</div>text</div>")]
    [InlineData("<div style='overflow:hidden;height:40px;mix-blend-mode:multiply'><div style='position:fixed;top:10px;left:10px'>fixed</div>text</div>")]
    public async Task AFixedPositionBoxInsideALayerATransformOrABlendDrawsWithoutFailing(string body)
    {
        var context = new SvgRenderContext();
        await using (var stream = File.OpenRead(BundledFonts.Ttf))
            await context.AddFont(stream, "Source Sans 3");
        var html = $"<!DOCTYPE html><html><head><style>body{{font:16px 'Source Sans 3'}} @page{{size:300pt 200pt;margin:10pt}}</style></head><body>{body}</body></html>";

        using var document = await HtmlDocument.LoadAsync(html, context, new HtmlDocumentOptions { PageWidthPoints = 300, PageHeightPoints = 200 });
        using var composer = new SvgComposer(context, new SvgComposerOptions { TextMode = SvgTextMode.Text });
        await document.LayoutAsync(composer.AddPage(300, 200).Canvas);
        var page = composer.AddPage(document.Pages[0].WidthPoints, document.Pages[0].HeightPoints);
        await document.PaintPageAsync(0, page.Canvas);

        var svg = System.Xml.Linq.XDocument.Parse(page.ToSvg());
        Assert.Contains(svg.Descendants(Ns + "text"), t => t.Value == "fixed");
    }

    // ---- helpers ------------------------------------------------------------------------------------------

    /// <summary>The PNG with an <c>eXIf</c> chunk (a TIFF structure holding only the orientation tag) inserted after its header.</summary>
    private static byte[] WithExifOrientation(byte[] png, int orientation)
    {
        byte[] tiff =
        [
            (byte)'M', (byte)'M', 0, 42, 0, 0, 0, 8,        // big-endian TIFF header, first IFD at 8
            0, 1,                                           // one entry
            0x01, 0x12, 0, 3, 0, 0, 0, 1, 0, (byte)orientation, 0, 0, // Orientation, SHORT, count 1
            0, 0, 0, 0,                                     // no next IFD
        ];

        var chunk = new byte[12 + tiff.Length];
        BinaryPrimitives.WriteUInt32BigEndian(chunk, (uint)tiff.Length);
        "eXIf"u8.CopyTo(chunk.AsSpan(4));
        tiff.CopyTo(chunk, 8);
        BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(8 + tiff.Length), Crc32(chunk.AsSpan(4, 4 + tiff.Length)));

        var headerEnd = 8 + 8 + 13 + 4; // signature, IHDR chunk header, its 13 bytes of data, its CRC
        var result = new byte[png.Length + chunk.Length];
        png.AsSpan(0, headerEnd).CopyTo(result);
        chunk.CopyTo(result, headerEnd);
        png.AsSpan(headerEnd).CopyTo(result.AsSpan(headerEnd + chunk.Length));
        return result;
    }

    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc ^= b;
            for (var k = 0; k < 8; k++)
                crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
        }

        return ~crc;
    }

    private sealed record GlyphCall(double FontSize, double OriginX, double BaselineY, PaintColor Foreground);

    private sealed class RecordingGlyphPainter(bool result) : ISvgGlyphPainter
    {
        public List<GlyphCall> Calls { get; } = [];

        public bool TryPaint(Typeface typeface, ushort glyph, PeachDrawing.Text.Outlines.SvgGlyph svg, double fontSize, double originX, double baselineY,
            PaintColor foreground, int paletteIndex, IReadOnlyDictionary<int, PaintColor>? overrides)
        {
            Calls.Add(new GlyphCall(fontSize, originX, baselineY, foreground));
            return result;
        }
    }

    private sealed class PaintingContext(ISvgGlyphPainter painter) : SvgRenderContext
    {
        public override ISvgGlyphPainter CreateSvgGlyphPainter(Canvas host) => painter;
    }
}
