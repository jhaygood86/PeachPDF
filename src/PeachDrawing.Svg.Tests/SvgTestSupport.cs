using PeachDrawing.Core;
using PeachDrawing.Svg;
using PeachImage.Formats.Png;
using PeachPDF.Tests.TestSupport;
using System.Xml.Linq;

namespace PeachDrawing.Svg.Tests;

/// <summary>Helpers for drawing on an SVG page and reading the document back as XML.</summary>
internal sealed class SvgScene : IDisposable
{
    public static readonly XNamespace Ns = "http://www.w3.org/2000/svg";

    private readonly SvgComposer _composer;

    public SvgScene(SvgRenderContext context, double width = 200, double height = 100, SvgComposerOptions? options = null)
    {
        Context = context;
        _composer = new SvgComposer(context, options);
        Page = _composer.AddPage(width, height);
    }

    public SvgRenderContext Context { get; }

    public SvgComposerPage Page { get; }

    public SvgCanvas Canvas => Page.Canvas;

    public static SvgScene Create(double width = 200, double height = 100, SvgComposerOptions? options = null, double pixelsPerPoint = 1) =>
        new(new SvgRenderContext { PixelsPerPoint = pixelsPerPoint }, width, height, options);

    /// <summary>A scene whose context has the bundled Source Sans 3 registered as "Source Sans 3".</summary>
    public static async Task<SvgScene> WithFontAsync(double width = 200, double height = 100, SvgComposerOptions? options = null)
    {
        var context = new SvgRenderContext();
        await using var stream = File.OpenRead(BundledFonts.Ttf);
        await context.AddFont(stream, "Source Sans 3");
        return new SvgScene(context, width, height, options);
    }

    public XDocument Document => XDocument.Parse(Page.ToSvg());

    /// <summary>The elements the canvas drew (everything in the document outside the definitions), in document order.</summary>
    public IEnumerable<XElement> Drawn() => Document.Root!.Elements().Where(e => e.Name != Ns + "defs" && e.Name != Ns + "title" && e.Name != Ns + "desc" && e.Name != Ns + "style");

    /// <summary>The definitions block's elements.</summary>
    public IEnumerable<XElement> Defs() => Document.Root!.Element(Ns + "defs")?.Elements() ?? [];

    public static string Attr(XElement element, string name) => (string?)element.Attribute(name) ?? string.Empty;

    public Brush Solid(int r, int g, int b, int a = 255) => Context.GetSolidBrush(PaintColor.FromArgb(a, r, g, b));

    public Pen Pen(int r, int g, int b, double width)
    {
        var pen = Context.GetPen(PaintColor.FromArgb(255, r, g, b));
        pen.Width = width;
        return pen;
    }

    /// <summary>A tiny PNG made by the raster canvas, decoded through the SVG context.</summary>
    public Image Png(int size = 8)
    {
        var raster = new RasterRenderContext();
        using var canvas = raster.CreateCanvas(size, size);
        canvas.DrawRectangle(raster.GetSolidBrush(PaintColor.FromArgb(255, 200, 30, 30)), 0, 0, size, size);
        using var stream = new MemoryStream();
        canvas.Save(stream, "png", new PngEncoderOptions());
        stream.Position = 0;
        return Context.ImageFromStream(stream);
    }

    public void Dispose()
    {
        _composer.Dispose();
    }
}
