using System.Text.RegularExpressions;
using PeachBrackets;
using PeachDrawing;
using PeachBrackets.WptRunner.Server;
using PeachDrawing.Core;
using PeachDrawing.Text;

namespace PeachBrackets.WptRunner;

/// <summary>
/// The render seam: one WPT file in, the pixels of an 800x600 screen viewport out, through PeachBrackets' public API only.
/// One CSS pixel is one device pixel (layout runs in CSS pixels).
/// </summary>
internal sealed partial class WptRenderer(string wptRoot) : IReftestRenderer
{
    public const int ViewportWidth = 800;
    public const int ViewportHeight = 600;
    private const string Origin = WptServer.Origin;

    [GeneratedRegex(@"<head\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HeadTag();

    [GeneratedRegex(@"<html\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HtmlTag();

    [GeneratedRegex(@"^\s*(<!--.*?-->\s*)*<!DOCTYPE[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex Doctype();

    private string? AhemPath()
    {
        var path = Path.Combine(wptRoot, "fonts", "Ahem.ttf");
        return File.Exists(path) ? path : null;
    }

    private static readonly string[] LiberationFaces =
    [
        "LiberationSans-Regular", "LiberationSans-Bold", "LiberationSans-Italic", "LiberationSans-BoldItalic",
        "LiberationSerif-Regular", "LiberationSerif-Bold", "LiberationSerif-Italic", "LiberationSerif-BoldItalic",
        "LiberationMono-Regular", "LiberationMono-Bold", "LiberationMono-Italic", "LiberationMono-BoldItalic",
    ];

    private static async Task PinGenericFontsAsync(RasterRenderContext context)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "fonts");
        foreach (var face in LiberationFaces)
        {
            var path = Path.Combine(directory, face + ".woff");
            if (!File.Exists(path))
                return;

            var family = face.StartsWith("LiberationSans", StringComparison.Ordinal) ? "Liberation Sans"
                : face.StartsWith("LiberationSerif", StringComparison.Ordinal) ? "Liberation Serif" : "Liberation Mono";
            // The same faces again under the names of the fonts they are metric-compatible with. A font added under a name takes precedence over an
            // installed family of that name, so a page that says Arial or Times New Roman is drawn with Liberation on every machine.
            var aliases = family switch
            {
                "Liberation Sans" => new[] { family, "Arial", "Helvetica", "Helvetica Neue", "Segoe UI" },
                "Liberation Serif" => new[] { family, "Times New Roman", "Times" },
                _ => new[] { family, "Courier New", "Courier" },
            };
            foreach (var name in aliases)
            {
                await using var stream = File.OpenRead(path);
                await context.AddFont(stream, name);
            }
        }

        context.AddFontFamilyMapping("sans-serif", "Liberation Sans");
        context.AddFontFamilyMapping("serif", "Liberation Serif");
        context.AddFontFamilyMapping("monospace", "Liberation Mono");
        context.AddFontFamilyMapping("ui-sans-serif", "Liberation Sans");
        context.AddFontFamilyMapping("ui-serif", "Liberation Serif");
        context.AddFontFamilyMapping("ui-monospace", "Liberation Mono");
    }

    public string FullPath(string wptPath) => Path.Combine(wptRoot, wptPath.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>The address of the empty page a reftest names as its reference (<c>&lt;link rel=match href=about:blank&gt;</c>).</summary>
    public const string BlankPage = "about:blank";

    public async Task<RgbaImage> RenderAsync(string wptPath)
    {
        // The page as the server serves it: .sub. files substituted, and the headers of its .headers file (an XHTML content type among them) known.
        var server = new WptServer(wptRoot);
        var served = server.Handle(new ServerRequest("GET", new Uri(Origin + "/" + wptPath)));
        var text = wptPath == BlankPage ? "<!DOCTYPE html>" : served.Status == 200 ? served.Text : await File.ReadAllTextAsync(FullPath(wptPath));
        var source = Prepare(wptPath, text);

        var context = new RasterRenderContext { ResourceLoader = uri => LoadAsync(server, uri), PixelsPerPoint = 96.0 / 72.0 };

        // A browser resolves each generic family (`monospace`...) to its platform's real family; without this the raster context falls back to
        // whatever the font set finds first, and a reftest written with `font: 24px monospace` measures a different font than the browser's.
        var fontSet = new FontSet();
        foreach (var (keyword, generic) in FontDefaults.GenericFamilies)
            if (fontSet.ResolveGeneric(generic, context.IsFontExists) is { } family)
                context.AddFontFamilyMapping(keyword, family);

        // Pinned: the serif, sans-serif and monospace families are the bundled Liberation faces whatever the machine has installed, so the
        // floors do not depend on the fonts of the runner image (they differed between Windows and Linux by dozens of reftests).
        await PinGenericFontsAsync(context);

        // The WPT harness installs the Ahem font (every glyph an em square, metrics known exactly) on the machine, and a test that sets
        // `font-family: Ahem` without an @font-face rule relies on that.
        if (AhemPath() is { } ahem)
        {
            await using var stream = File.OpenRead(ahem);
            await context.AddFont(stream, "Ahem");
        }

        var options = new HtmlDocumentOptions
        {
            MediaType = MediaType.Screen,
            PageWidthPoints = ViewportWidth * 0.75,
            ViewportHeightPoints = ViewportHeight * 0.75,

            // Browsers snap boxes and borders to whole CSS pixels, and the reference pages are written against that.
            SnapBoxDecorationsToCssPixels = true,
            SnapBorderWidthsToCssPixels = true,

            // An .xht or .xhtml file is served as application/xhtml+xml, so a browser parses it as XML (and never in quirks mode).
            Syntax = IsXmlDocument(wptPath) || IsXmlContentType(server.ContentTypeFor(wptPath)) ? DocumentSyntax.Xml : DocumentSyntax.Html,
        };
        options.SetMargins(0);

        if (ReftestParser.ScriptingEnabled)
        {
            options.ScriptHost = new PeachBrackets.Scripting.JintScriptHost(new PeachBrackets.Scripting.JintScriptOptions
            {
                ScriptTimeout = TimeSpan.FromSeconds(10),
                MaxVirtualTime = TimeSpan.FromSeconds(10),
            });
            options.MeasuringCanvasFactory = () => context.CreateCanvas(1, 1);
            options.ScriptNetwork = new ServerNetwork(server);
        }

        using var document = await HtmlDocument.LoadAsync(source, context, options);
        using (var measuring = context.CreateCanvas(1, 1))
        {
            await document.LayoutAsync(measuring);
        }

        // A browser shows the page through a viewport at scroll position 0, which is what makes `position: sticky` boxes stick; without one
        // the engine computes no sticky displacement at all.
        document.SetViewport(document.Viewport);

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

    /// <summary>Makes the source loadable as the WPT server would serve it: a base URL is set so relative references (stylesheets, images, fonts) resolve. An XML document is left as it is, apart from the base element, which is closed so the document stays well-formed.</summary>
    internal static string Prepare(string wptPath, string source)
    {
        var directory = ReftestParser.DirectoryOf(wptPath);
        var baseUri = new Uri($"{Origin}/{directory}/");
        var baseTag = IsXmlDocument(wptPath) ? $"<base href=\"{baseUri}\"/>" : $"<base href=\"{baseUri}\">";

        if (HeadTag().Match(source) is { Success: true } head)
            return source.Insert(head.Index + head.Length, baseTag);
        if (HtmlTag().Match(source) is { Success: true } html)
            return source.Insert(html.Index + html.Length, "<head>" + baseTag + "</head>");

        // A document with neither tag gets its head in front, but after the DOCTYPE: a document is in quirks mode unless its first token is
        // one, and a tag in front of it would put every such test in the wrong mode.
        var doctype = Doctype().Match(source);
        var at = doctype.Success ? doctype.Index + doctype.Length : 0;
        return source.Insert(at, "<head>" + baseTag + "</head>");
    }

    /// <summary>
    /// Whether the test is an XML document (<c>.xht</c>, <c>.xhtml</c>): the WPT server serves it as <c>application/xhtml+xml</c>, which a browser parses with its XML parser.
    /// </summary>
    internal static bool IsXmlDocument(string wptPath) =>
        wptPath.EndsWith(".xht", StringComparison.OrdinalIgnoreCase) || wptPath.EndsWith(".xhtml", StringComparison.OrdinalIgnoreCase);

    // The server answers; a data: address carries its own content.
    private static async ValueTask<RNetworkResponse?> LoadAsync(WptServer server, Uri uri)
    {
        if (uri.IsAbsoluteUri && uri.Scheme == "data")
            return await HostResourceLoader.LoadAsync(uri);

        return await server.LoadAsync(uri);
    }

    /// <summary>Whether a Content-Type makes the document XML (<c>application/xhtml+xml</c>), which a browser then parses with its XML parser.</summary>
    internal static bool IsXmlContentType(string contentType) =>
        contentType.StartsWith("application/xhtml+xml", StringComparison.OrdinalIgnoreCase);

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
        ".html" or ".htm" => "text/html",
        ".xht" or ".xhtml" => "application/xhtml+xml",
        _ => "application/octet-stream",
    };
}
