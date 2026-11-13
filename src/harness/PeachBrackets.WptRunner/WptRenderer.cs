using System.Text.RegularExpressions;
using PeachBrackets;
using PeachDrawing;
using PeachDrawing.Core;

namespace PeachBrackets.WptRunner;

/// <summary>
/// The render seam: one WPT file in, the pixels of an 800x600 screen viewport out, through PeachBrackets' public API only.
/// One CSS pixel is one device pixel (layout runs in CSS pixels).
/// </summary>
internal sealed partial class WptRenderer(string wptRoot) : IReftestRenderer
{
    public const int ViewportWidth = 800;
    public const int ViewportHeight = 600;
    private const string Origin = "http://wpt.test";

    [GeneratedRegex(@"<head\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HeadTag();

    [GeneratedRegex(@"<html\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HtmlTag();

    [GeneratedRegex(@"<\?xml[^>]*\?>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex XmlDeclaration();

    public string FullPath(string wptPath) => Path.Combine(wptRoot, wptPath.Replace('/', Path.DirectorySeparatorChar));

    public async Task<RgbaImage> RenderAsync(string wptPath)
    {
        var source = Prepare(wptPath, await File.ReadAllTextAsync(FullPath(wptPath)));

        var context = new RasterRenderContext { ResourceLoader = LoadAsync, PixelsPerPoint = 96.0 / 72.0 };
        var options = new HtmlDocumentOptions
        {
            MediaType = MediaType.Screen,
            PageWidthPoints = ViewportWidth * 0.75,
            ViewportHeightPoints = ViewportHeight * 0.75,

            // Browsers snap boxes and borders to whole CSS pixels, and the reference pages are written against that.
            SnapBoxDecorationsToCssPixels = true,
            SnapBorderWidthsToCssPixels = true,
        };
        options.SetMargins(0);

        using var document = await HtmlDocument.LoadAsync(source, context, options);
        using (var measuring = context.CreateCanvas(1, 1))
        {
            await document.LayoutAsync(measuring);
        }

        // The layout unit is the point, so the canvas is scaled by 4/3 to make one CSS pixel one device pixel. (A surface declared as
        // 4/3 pixels per point instead was measured: it passes about a third fewer tests, with larger edge differences.)
        // Layout runs in CSS pixels (PixelsPerPoint below), so the canvas shows one CSS pixel per device pixel with no transform in between,
        // and the engine's snapping of boxes and borders to whole CSS pixels lands on whole device pixels, as a browser's does.
        using var canvas = context.CreateCanvas(ViewportWidth, ViewportHeight);
        canvas.DrawRectangle(canvas.GetSolidBrush(PaintColor.FromArgb(255, 255, 255, 255)), 0, 0, ViewportWidth, ViewportHeight);
        await document.PaintPageAsync(0, canvas);

        var buffer = canvas.ToPixelBuffer();
        var pixels = new byte[buffer.Width * buffer.Height * 4];
        PixelMath.Unpremultiply(buffer.PremultipliedRgba.Span, pixels);
        return new RgbaImage(buffer.Width, buffer.Height, pixels);
    }

    /// <summary>Makes the source loadable as the WPT server would serve it: XML syntax a browser's XML parser would take is rewritten, and a base URL is set so relative references (stylesheets, images, fonts) resolve.</summary>
    internal static string Prepare(string wptPath, string source)
    {
        if (wptPath.EndsWith(".xht", StringComparison.OrdinalIgnoreCase) || wptPath.EndsWith(".xhtml", StringComparison.OrdinalIgnoreCase))
        {
            source = XmlDeclaration().Replace(source, string.Empty);
            source = source.Replace("<![CDATA[", string.Empty, StringComparison.Ordinal).Replace("]]>", string.Empty, StringComparison.Ordinal);
        }

        var directory = ReftestParser.DirectoryOf(wptPath);
        var baseUri = new Uri($"{Origin}/{directory}/");
        var baseTag = $"<base href=\"{baseUri}\">";

        if (HeadTag().Match(source) is { Success: true } head)
            return source.Insert(head.Index + head.Length, baseTag);
        if (HtmlTag().Match(source) is { Success: true } html)
            return source.Insert(html.Index + html.Length, "<head>" + baseTag + "</head>");
        return "<head>" + baseTag + "</head>" + source;
    }

    private async ValueTask<RNetworkResponse?> LoadAsync(Uri uri)
    {
        if (!uri.IsAbsoluteUri || !uri.AbsoluteUri.StartsWith(Origin + "/", StringComparison.Ordinal))
            return null;

        var relative = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'));
        var path = Path.GetFullPath(FullPath(relative));
        if (!path.StartsWith(Path.GetFullPath(wptRoot), StringComparison.Ordinal) || !File.Exists(path))
            return null;

        var bytes = await File.ReadAllBytesAsync(path);
        return new RNetworkResponse(new MemoryStream(bytes), new Dictionary<string, string[]> { ["Content-Type"] = [ContentType(path)] });
    }

    internal static string ContentType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".css" => "text/css",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".svg" => "image/svg+xml",
        ".webp" => "image/webp",
        ".ttf" => "font/ttf",
        ".otf" => "font/otf",
        ".woff" => "font/woff",
        ".woff2" => "font/woff2",
        ".html" or ".htm" or ".xht" or ".xhtml" => "text/html",
        _ => "application/octet-stream",
    };
}
