using PeachDrawing.Core;
using PeachDrawing.Text.Export;
using System;
using System.IO;
using System.Text;

namespace PeachDrawing.Svg;

/// <summary>
/// Makes one SVG document. The <see cref="Canvas"/> is what you draw on, and <see cref="Save(Stream)"/> writes what was drawn; an SVG has
/// no pages, so there is one composer per document. Like the PDF composer, it works in points with the origin at the top left and y growing
/// downwards.
/// </summary>
/// <example>
/// <code>
/// var context = new SvgRenderContext();
/// var composer = new SvgComposer(context, 400, 200);
/// composer.Canvas.DrawRectangle(context.GetSolidBrush(PaintColor.FromArgb(255, 30, 144, 255)), 20, 20, 200, 100);
/// composer.Save(File.Create("out.svg"));
/// </code>
/// </example>
public sealed class SvgComposer
{
    private readonly SvgDefs _defs;
    private readonly double _pixelsPerPoint;

    /// <summary>Starts a document.</summary>
    /// <param name="renderContext">the context whose fonts and images the document uses</param>
    /// <param name="widthPoints">the document width, in points</param>
    /// <param name="heightPoints">the document height, in points</param>
    /// <param name="options">what to write into the document, or <see langword="null"/> for the defaults</param>
    /// <exception cref="ArgumentOutOfRangeException">a size is not positive and finite</exception>
    public SvgComposer(SvgRenderContext renderContext, double widthPoints, double heightPoints, SvgComposerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(renderContext);
        if (!(widthPoints > 0) || !(heightPoints > 0) || double.IsInfinity(widthPoints) || double.IsInfinity(heightPoints))
            throw new ArgumentOutOfRangeException(nameof(widthPoints), "A document needs a positive, finite size.");

        RenderContext = renderContext;
        Options = options ?? new SvgComposerOptions();
        WidthPoints = widthPoints;
        HeightPoints = heightPoints;
        _defs = new SvgDefs(Options.Precision);

        var ppp = _pixelsPerPoint = renderContext.PixelsPerPoint;
        Canvas = new SvgCanvas(renderContext, _defs, new Rect(0, 0, widthPoints * ppp, heightPoints * ppp), ppp, Options, isTile: false);
    }

    /// <summary>The context the document draws with.</summary>
    public SvgRenderContext RenderContext { get; }

    /// <summary>What is written into the document.</summary>
    public SvgComposerOptions Options { get; }

    /// <summary>The document width, in points.</summary>
    public double WidthPoints { get; }

    /// <summary>The document height, in points.</summary>
    public double HeightPoints { get; }

    /// <summary>The canvas the document is drawn on, in the render context's layout units (<see cref="SvgRenderContext.PixelsPerPoint"/> of them to a point).</summary>
    public SvgCanvas Canvas { get; }

    /// <summary>Writes the document (UTF-8, no byte order mark).</summary>
    /// <param name="stream">where to write it; not closed</param>
    public void Save(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, leaveOpen: true);
        Save(writer);
    }

    /// <summary>Writes the document.</summary>
    /// <param name="writer">where to write it</param>
    public void Save(TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Write(ToSvg());
        writer.Flush();
    }

    /// <summary>The document as its SVG text.</summary>
    public string ToSvg()
    {
        var options = Options;
        var ppp = _pixelsPerPoint;
        var body = Canvas.CompleteContent();

        var sb = new StringBuilder(body.Length + 1024);
        sb.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"").Append(_defs.Num(WidthPoints)).Append("pt\" height=\"")
            .Append(_defs.Num(HeightPoints)).Append("pt\" viewBox=\"0 0 ").Append(_defs.Num(WidthPoints * ppp)).Append(' ')
            .Append(_defs.Num(HeightPoints * ppp)).Append("\">\n");

        if (!string.IsNullOrEmpty(options.Title))
            sb.Append("<title>").Append(SvgDefs.Escape(options.Title)).Append("</title>\n");
        if (!string.IsNullOrEmpty(options.Description))
            sb.Append("<desc>").Append(SvgDefs.Escape(options.Description)).Append("</desc>\n");

        AppendFontStyles(sb);

        if (!_defs.IsEmpty)
            sb.Append("<defs>\n").Append(_defs.Content).Append("</defs>\n");

        if (options.Background is { } background && background.A > 0)
        {
            sb.Append("<rect width=\"").Append(_defs.Num(WidthPoints * ppp)).Append("\" height=\"").Append(_defs.Num(HeightPoints * ppp))
                .Append("\" fill=\"").Append(SvgDefs.Hex(background)).Append('"');
            if (background.A < 255)
                sb.Append(" fill-opacity=\"").Append(_defs.Num(SvgDefs.Alpha(background))).Append('"');
            sb.Append("/>\n");
        }

        sb.Append(body).Append("</svg>\n");
        return sb.ToString();
    }

    /// <summary>One <c>@font-face</c> per typeface the document drew as <c>&lt;text&gt;</c>, holding only the glyphs it used.</summary>
    private void AppendFontStyles(StringBuilder sb)
    {
        var any = false;
        foreach (var (typeface, use) in _defs.Fonts)
        {
            if (!use.Eligible || use.Glyphs.Count <= 1)
                continue;

            var exported = TypefaceExporter.ExportSubset(typeface, use.Glyphs, keepCharacterMap: true);
            if (exported.Format != ExportedFontFormat.OpenType)
                continue;

            if (!any)
                sb.Append("<style>\n");
            any = true;
            var mime = exported.HasCffOutlines ? "font/otf" : "font/ttf";
            sb.Append("@font-face{font-family:\"").Append(use.Name).Append("\";src:url(data:").Append(mime).Append(";base64,")
                .Append(Convert.ToBase64String(SvgFontPackager.ForBrowser(exported.Data.Span, use.Name))).Append(");}\n");
        }

        if (any)
            sb.Append("</style>\n");
    }
}
