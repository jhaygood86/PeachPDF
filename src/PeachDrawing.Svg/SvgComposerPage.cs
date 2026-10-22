using PeachDrawing.Core;
using PeachDrawing.Text.Export;
using System;
using System.IO;
using System.Text;

namespace PeachDrawing.Svg;

/// <summary>One page of an <see cref="SvgComposer"/>: an SVG document, and the canvas it is drawn on.</summary>
public sealed class SvgComposerPage
{
    private readonly SvgComposer _composer;
    private readonly SvgDefs _defs;
    private readonly double _pixelsPerPoint;

    internal SvgComposerPage(SvgComposer composer, double widthPoints, double heightPoints)
    {
        _composer = composer;
        WidthPoints = widthPoints;
        HeightPoints = heightPoints;
        _defs = new SvgDefs(composer.Options.Precision);

        var ppp = _pixelsPerPoint = composer.RenderContext.PixelsPerPoint;
        Canvas = new SvgCanvas(composer.RenderContext, _defs, new Rect(0, 0, widthPoints * ppp, heightPoints * ppp), ppp, composer.Options, isTile: false);
    }

    /// <summary>The page width, in points.</summary>
    public double WidthPoints { get; }

    /// <summary>The page height, in points.</summary>
    public double HeightPoints { get; }

    /// <summary>The canvas the page is drawn on, in the render context's layout units (<see cref="SvgRenderContext.PixelsPerPoint"/> of them to a point).</summary>
    public SvgCanvas Canvas { get; }

    /// <summary>Writes the page as an SVG document (UTF-8, no byte order mark).</summary>
    /// <param name="stream">where to write it; not closed</param>
    public void Save(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, leaveOpen: true);
        Save(writer);
    }

    /// <summary>Writes the page as an SVG document.</summary>
    /// <param name="writer">where to write it</param>
    public void Save(TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Write(ToSvg());
        writer.Flush();
    }

    /// <summary>The page as the text of an SVG document.</summary>
    public string ToSvg()
    {
        var options = _composer.Options;
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

    /// <summary>One <c>@font-face</c> per typeface the page drew as <c>&lt;text&gt;</c>, holding only the glyphs it used.</summary>
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
