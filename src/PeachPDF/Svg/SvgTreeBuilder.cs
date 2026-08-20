// "Therefore those skilled at the unorthodox
// are infinite as heaven and earth,
// inexhaustible as the great rivers.
// When they come to an end,
// they begin again,
// like the days and months;
// they die and are reborn,
// like the four seasons."
//
// - Sun Tsu,
// "The Art of War"

using MimeKit;
using PeachPDF.CSS;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Parse;
using PeachPDF.Html.Core.Utils;
using PeachPDF.Network;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PeachPDF.Svg
{
    /// <summary>
    /// Builds an <see cref="SvgDocument"/> scene graph from any <see cref="ISvgSourceNode"/> tree, in
    /// two passes: pass 1 (<see cref="CollectDefinitions"/>) walks the whole tree once to register
    /// every id-bearing element and fully resolve gradients (self-contained via their own
    /// <c>&lt;stop&gt;</c> children), since SVG allows forward references (a <c>&lt;use&gt;</c> or
    /// <c>clip-path</c> can reference an id defined later in document order). Pass 2 builds the
    /// renderable tree, resolving <c>fill:url(#..)</c>/<c>clip-path:url(#..)</c>/<c>&lt;use&gt;</c>
    /// references against the now-complete registry.
    /// </summary>
    internal sealed class SvgTreeBuilder
    {
        /// <summary>Guards against a pathological/malicious &lt;use&gt; reference cycle.</summary>
        private const int MaxUseDepth = 8;

        private readonly RAdapter _adapter;
        private readonly RColor _contextColor;
        private readonly IReadOnlyDictionary<string, SvgImageResource>? _prefetchedImages;
        private readonly Dictionary<string, ISvgSourceNode> _nodesById = new(StringComparer.Ordinal);
        private readonly SvgDocument _document = new();
        private int _useDepth;

        /// <summary>
        /// The root element's used <c>font-size</c> (the value <c>rem</c> font-sizes resolve against),
        /// captured once in <see cref="BuildDocument"/>. Defaults to the UA initial font size until then.
        /// </summary>
        private double _rootFontSize = Html.Core.Utils.DefaultFontResolver.FontSize;


        /// <summary>
        /// The document's own viewport dimensions (from <c>viewBox</c>, falling back to
        /// <c>width</c>/<c>height</c>), used as the reference length for resolving percentage-valued
        /// geometry attributes on its children (e.g. a <c>&lt;circle r="10%"&gt;</c>). Null when
        /// neither is present, in which case percentages on children are left unresolved - same as
        /// today's behavior for the root's own <c>width</c>/<c>height</c>, whose fallback to the
        /// actual rendered viewport already happens in <see cref="SvgRenderer"/>.
        /// </summary>
        private double? _viewportWidth;
        private double? _viewportHeight;

        /// <summary>Reference length for percentage lengths that aren't specifically x- or y-axis (e.g. <c>stroke-width</c>, a circle's <c>r</c>), per the SVG spec's diagonal formula.</summary>
        private double? ViewportDiagonal =>
            _viewportWidth is { } w && _viewportHeight is { } h ? Math.Sqrt((w * w + h * h) / 2.0) : null;

        private SvgTreeBuilder(RAdapter adapter, RColor contextColor, IReadOnlyDictionary<string, SvgImageResource>? prefetchedImages)
        {
            _adapter = adapter;
            _contextColor = contextColor;
            _prefetchedImages = prefetchedImages;
        }

        /// <summary>Guards against a pathological/malicious nesting of <c>&lt;image&gt;</c>-referenced SVG documents.</summary>
        private const int MaxImageNestingDepth = 8;

        /// <summary>
        /// A network/file <c>&lt;image&gt;</c> resource fetched by <see cref="PrefetchImageResourcesAsync"/>
        /// before the synchronous build runs: the raw response bytes plus whether they should be
        /// interpreted as an SVG document (detected from the href's <c>.svg</c> extension or a
        /// <c>Content-Type: image/svg+xml</c> response header) rather than a raster image.
        /// <paramref name="NestedImages"/> carries the recursively-prefetched resources for an SVG
        /// payload's own <c>&lt;image&gt;</c> hrefs (keyed by that nested document's raw hrefs, resolved
        /// against its own base) - null for a raster resource or an SVG with no fetchable nested images
        /// (issue #251).
        /// </summary>
        internal readonly record struct SvgImageResource(byte[] Bytes, bool IsSvg, IReadOnlyDictionary<string, SvgImageResource>? NestedImages = null);

        /// <summary>
        /// The inheritable SVG presentation properties (fill/stroke/stroke-width/stroke-miterlimit/
        /// fill-rule/fill-opacity/stroke-opacity), threaded down through the recursive build so an
        /// element that doesn't specify one of these itself inherits its nearest ancestor's resolved
        /// value, per normal SVG/CSS inheritance - including through &lt;use&gt;, whose own
        /// attributes become the inherited context for building the (otherwise unstyled) referenced
        /// content. Note <c>opacity</c> itself is deliberately excluded - it is not an inherited
        /// property; it composites down the subtree instead (see <see cref="SvgRenderer"/>).
        /// </summary>
        private readonly record struct InheritedPaint(
            SvgPaint Fill,
            SvgPaint Stroke,
            double StrokeWidth,
            double StrokeMiterLimit,
            RFillMode FillRule,
            double FillOpacity,
            double StrokeOpacity,
            RLineCap StrokeLineCap,
            RLineJoin StrokeLineJoin,
            double[] StrokeDashArray,
            double StrokeDashOffset,
            string? MarkerStartRef,
            string? MarkerMidRef,
            string? MarkerEndRef,
            string Direction,
            WritingMode WritingMode,
            TextOrientation TextOrientation)
        {
            public static readonly InheritedPaint Initial = new(
                Fill: SvgPaint.Solid(RColor.Black),
                Stroke: SvgPaint.None,
                StrokeWidth: 1,
                StrokeMiterLimit: 4,
                FillRule: RFillMode.Nonzero,
                FillOpacity: 1,
                StrokeOpacity: 1,
                StrokeLineCap: RLineCap.Butt,
                StrokeLineJoin: RLineJoin.Miter,
                StrokeDashArray: [],
                StrokeDashOffset: 0,
                MarkerStartRef: null,
                MarkerMidRef: null,
                MarkerEndRef: null,
                Direction: "ltr",
                WritingMode: WritingMode.HorizontalTb,
                TextOrientation: TextOrientation.Mixed);
        }

        /// <summary>
        /// The inherited font properties (font-family/font-size/font-weight/font-style), threaded down
        /// through the recursive build alongside <see cref="InheritedPaint"/> so a <c>&lt;text&gt;</c>
        /// inherits them from ANY ancestor (<c>&lt;g&gt;</c>/<c>&lt;svg&gt;</c>/<c>&lt;a&gt;</c>/<c>&lt;use&gt;</c>),
        /// per normal SVG/CSS inheritance - not just from its own <c>&lt;text&gt;</c>/<c>&lt;tspan&gt;</c>
        /// text-run ancestors. Deliberately kept separate from <see cref="InheritedPaint"/> and carried
        /// as cheap resolved values (no <see cref="RFont"/>): only <c>&lt;text&gt;</c>/<c>&lt;tspan&gt;</c>/
        /// <c>&lt;tref&gt;</c> resolve an actual font from it, so non-text elements only propagate the
        /// context, never realize a font. Relative <c>font-size</c> (<c>em</c>/<c>ex</c>/<c>%</c>) resolves
        /// against <see cref="Size"/> (the parent's used size); <c>rem</c> against the root's (see
        /// <see cref="_rootFontSize"/>).
        /// </summary>
        private readonly record struct FontContext(string Family, double Size, bool Bold, bool Italic)
        {
            public static readonly FontContext Default = new(Html.Core.Utils.DefaultFontResolver.DefaultFont, Html.Core.Utils.DefaultFontResolver.FontSize, false, false);
        }

        /// <param name="root">The root node to build from.</param>
        /// <param name="adapter">The graphics adapter, used to resolve paint colors.</param>
        /// <param name="contextColor">
        /// The resolved <c>currentColor</c> value - the CSS <c>color</c> property of the inline
        /// <c>&lt;svg&gt;</c>'s HTML ancestor for inline SVG, or omitted (defaulting to black) for
        /// standalone/<c>&lt;img&gt;</c> SVG, which has no CSS context to inherit from.
        /// </param>
        /// <param name="prefetchedImages">
        /// Network/file <c>&lt;image&gt;</c> resources already fetched by
        /// <see cref="PrefetchImageResourcesAsync"/> (keyed by their raw <c>href</c>), since the build
        /// itself is synchronous and cannot await the async resource pipeline. Null when there are no
        /// non-<c>data:</c> image references (or the caller didn't prefetch), in which case only
        /// <c>data:</c> URI hrefs resolve - the historical behavior.
        /// </param>
        public static SvgDocument Build(ISvgSourceNode root, RAdapter adapter, RColor? contextColor = null, IReadOnlyDictionary<string, SvgImageResource>? prefetchedImages = null)
        {
            var builder = new SvgTreeBuilder(adapter, contextColor ?? RColor.Black, prefetchedImages);
            return builder.BuildDocument(root);
        }

        /// <summary>
        /// Fetches every non-<c>data:</c> <c>&lt;image&gt;</c> href in <paramref name="root"/>'s tree
        /// through the same async resource pipeline HTML <c>&lt;img&gt;</c> uses
        /// (<see cref="RAdapter.GetResourceStream"/> over network/<c>file:</c>/archive schemes), so the
        /// synchronous <see cref="Build"/> can resolve them from the returned map. Runs in the caller's
        /// already-async context (measure/load), before the sync build. <c>data:</c> hrefs are skipped
        /// here - they decode in-memory inside <see cref="BuildImage"/> with no I/O.
        /// </summary>
        /// <param name="root">The SVG source tree to scan for <c>&lt;image&gt;</c> references.</param>
        /// <param name="container">The container whose adapter/document base resolve and fetch each href.</param>
        /// <param name="baseUriOverride">
        /// A base URI that takes precedence over the document's own base when resolving relative hrefs -
        /// the fetched SVG's own URL for a standalone <c>&lt;img src="x.svg"&gt;</c> (so its
        /// <c>&lt;image&gt;</c>s resolve against the SVG's location), null for inline SVG (resolve
        /// against the host document base).
        /// </param>
        /// <returns>A map of raw href → fetched resource, or null when nothing was fetched.</returns>
        public static ValueTask<IReadOnlyDictionary<string, SvgImageResource>?> PrefetchImageResourcesAsync(
            ISvgSourceNode root, HtmlContainerInt container, RUri? baseUriOverride = null) =>
            PrefetchTreeAsync(root, container, baseUriOverride, depth: 0);

        /// <summary>
        /// Recursive worker for <see cref="PrefetchImageResourcesAsync"/>: fetches every non-<c>data:</c>
        /// <c>&lt;image&gt;</c> href in <paramref name="root"/>'s tree and, for a fetched (or <c>data:</c>)
        /// <c>image/svg+xml</c> payload, recurses into it so the nested document's own <c>&lt;image&gt;</c>
        /// hrefs are prefetched too (issue #251) - keyed into a per-payload child map so relative hrefs in
        /// different nested documents can't collide across their differing bases. Depth-guarded.
        /// </summary>
        private static async ValueTask<IReadOnlyDictionary<string, SvgImageResource>?> PrefetchTreeAsync(
            ISvgSourceNode root, HtmlContainerInt container, RUri? baseUriOverride, int depth)
        {
            if (depth > MaxImageNestingDepth)
                return null;

            var hrefs = new HashSet<string>(StringComparer.Ordinal);
            CollectImageHrefs(root, hrefs);

            Dictionary<string, SvgImageResource>? resources = null;

            foreach (var href in hrefs)
            {
                if (await PrefetchImageAsync(href, container, baseUriOverride, depth) is { } resource)
                    (resources ??= new Dictionary<string, SvgImageResource>(StringComparer.Ordinal))[href] = resource;
            }

            return resources;
        }

        /// <summary>
        /// Fetches (or decodes) one <c>&lt;image&gt;</c> href into an <see cref="SvgImageResource"/>, or
        /// returns null when nothing needs prefetching (a <c>data:</c> raster, an unresolvable/failed
        /// fetch, or a <c>data:</c> SVG with no fetchable nested images). For an SVG payload, its own
        /// nested <c>&lt;image&gt;</c> hrefs are recursively prefetched into the resource's child map.
        /// </summary>
        private static async ValueTask<SvgImageResource?> PrefetchImageAsync(
            string href, HtmlContainerInt container, RUri? baseUriOverride, int depth)
        {
            // A data: payload decodes in-memory in BuildImage - it needs a prefetch entry only when it is
            // an image/svg+xml document whose OWN nested <image>s must be fetched (issue #251).
            if (DataUriUtils.TryDecodeDataUri(href, out var mimeType, out var dataBytes))
            {
                if (!mimeType.Equals("image/svg+xml", StringComparison.OrdinalIgnoreCase))
                    return null;

                // data: has no location, so nested relative hrefs resolve against the current base.
                var nested = await PrefetchNestedSvgAsync(dataBytes, container, baseUriOverride, depth);
                return nested is null ? null : new SvgImageResource(dataBytes, IsSvg: true, nested);
            }

            var uri = CommonUtils.ResolveAgainstDocumentBase(container, href, baseUriOverride);
            if (uri is not { IsAbsoluteUri: true })
                return null;

            try
            {
                var response = await container.Adapter.GetResourceStream(uri);
                if (response?.ResourceStream is not { } stream)
                    return null;

                byte[] bytes;
                await using (stream)
                {
                    using var buffer = new MemoryStream();
                    await stream.CopyToAsync(buffer);
                    bytes = buffer.ToArray();
                }

                // Detect SVG the same way ImageLoadHandler does: the href's .svg extension or a
                // Content-Type: image/svg+xml response header.
                var srcHintsSvg = href.Split('?', '#')[0].EndsWith(".svg", StringComparison.OrdinalIgnoreCase);
                var contentTypeHintsSvg = response.ResponseHeaders?.TryGetValue("Content-Type", out var contentTypeValues) == true
                    && contentTypeValues.Select(ContentType.Parse).Any(ct => ct.IsMimeType("image", "svg+xml"));
                var isSvg = srcHintsSvg || contentTypeHintsSvg;

                // A fetched SVG's own nested <image>s resolve relative to the SVG's own URL.
                var nested = isSvg ? await PrefetchNestedSvgAsync(bytes, container, uri, depth) : null;
                return new SvgImageResource(bytes, isSvg, nested);
            }
            catch (Exception)
            {
                // A transport failure (DNS/connection/TLS/timeout thrown by a user-supplied network
                // loader) or a malformed Content-Type header must not abort the whole render: this
                // <image> is skipped and left blank (the same outcome as a missing resource), while
                // the rest of the SVG - and any other prefetched images - still render. This is a
                // deliberate swallow; HtmlContainerInt.RenderError builds the exception that aborts the
                // render, and PeachPDF has no non-fatal diagnostic channel to use instead.
                return null;
            }
        }

        /// <summary>
        /// If <paramref name="bytes"/> is an SVG document that itself contains an <c>&lt;image&gt;</c>,
        /// parses it and recursively prefetches its nested <c>&lt;image&gt;</c> hrefs against
        /// <paramref name="baseUriOverride"/>; returns the child map (or null when there is nothing to
        /// fetch or the payload is malformed). The cheap <c>&lt;image</c> text scan keeps the common
        /// image-less SVG payload from paying a parse.
        /// </summary>
        private static async ValueTask<IReadOnlyDictionary<string, SvgImageResource>?> PrefetchNestedSvgAsync(
            byte[] bytes, HtmlContainerInt container, RUri? baseUriOverride, int depth)
        {
            if (depth + 1 > MaxImageNestingDepth)
                return null;

            // Cheap gate: skip parsing a payload with no <image> element. Match both the unprefixed
            // "<image" and a namespace-prefixed "<svg:image" (whose local name is still "image", which
            // CollectImageHrefs matches on) - the ":image" substring covers the latter without a parse.
            var text = Encoding.UTF8.GetString(bytes);
            if (text.IndexOf("<image", StringComparison.OrdinalIgnoreCase) < 0
                && text.IndexOf(":image", StringComparison.OrdinalIgnoreCase) < 0)
                return null;

            if (!TryParseSvgRoot(text, out var nestedRoot))
                return null;

            // Walking <image> hrefs only needs Name/GetAttribute, so a bare source node suffices.
            return await PrefetchTreeAsync(new XElementSvgSourceNode(nestedRoot), container, baseUriOverride, depth + 1);
        }

        /// <summary>Parses raw SVG XML text into its root element, or returns false for malformed XML.</summary>
        private static bool TryParseSvgRoot(string text, out System.Xml.Linq.XElement root)
        {
            root = null!;
            try
            {
                var xdoc = System.Xml.Linq.XDocument.Parse(text);
                if (xdoc.Root is null)
                    return false;
                root = xdoc.Root;
                return true;
            }
            catch (System.Xml.XmlException)
            {
                return false;
            }
        }

        private static void CollectImageHrefs(ISvgSourceNode node, HashSet<string> hrefs)
        {
            foreach (var child in node.Children)
            {
                if (child.Name == "image")
                {
                    var href = child.GetAttribute("href") ?? child.GetAttribute("xlink:href");
                    if (!string.IsNullOrEmpty(href))
                        hrefs.Add(href);
                }

                CollectImageHrefs(child, hrefs);
            }
        }

        private SvgDocument BuildDocument(ISvgSourceNode root)
        {
            _document.ViewBox = SvgValueParsers.ParseViewBox(root.GetAttribute("viewBox"));
            _document.Width = SvgValueParsers.ParseLength(root.GetAttribute("width"));
            _document.Height = SvgValueParsers.ParseLength(root.GetAttribute("height"));
            _document.PreserveAspectRatio = SvgValueParsers.ParsePreserveAspectRatio(root.GetAttribute("preserveAspectRatio"));

            _viewportWidth = _document.ViewBox?.Width ?? _document.Width;
            _viewportHeight = _document.ViewBox?.Height ?? _document.Height;

            CollectDefinitions(root);

            // The root <svg>'s own font-* seeds inheritance for the whole tree, and its font-size is the
            // basis for rem. (When the root sets no font-size, this stays the UA default.)
            var rootFont = ComputeFontContext(root, FontContext.Default);
            _rootFontSize = rootFont.Size;

            foreach (var child in root.Children)
            {
                var element = BuildElement(child, InheritedPaint.Initial, rootFont);
                if (element is not null)
                    _document.Children.Add(element);
            }

            return _document;
        }

        private void CollectDefinitions(ISvgSourceNode node)
        {
            foreach (var child in node.Children)
            {
                var id = child.GetAttribute("id");

                if (!string.IsNullOrEmpty(id))
                    _nodesById[id] = child;

                switch (child.Name)
                {
                    case "linearGradient" when !string.IsNullOrEmpty(id):
                        _document.Gradients[id] = BuildLinearGradient(child);
                        break;
                    case "radialGradient" when !string.IsNullOrEmpty(id):
                        _document.Gradients[id] = BuildRadialGradient(child);
                        break;
                    case "marker" when !string.IsNullOrEmpty(id):
                        _document.Markers[id] = BuildMarker(child);
                        break;
                    case "pattern" when !string.IsNullOrEmpty(id):
                        _document.Patterns[id] = BuildPattern(child);
                        break;
                    case "mask" when !string.IsNullOrEmpty(id):
                        _document.Masks[id] = BuildMask(child);
                        break;
                    // <style> elements are no longer collected here: SVG styling is matched through the
                    // full CSS engine (ISvgSourceNode.GetMatchedCssDeclarations) against the relevant
                    // CssData - the host document's for inline <svg> (which already contains every nested
                    // and document-level <style>), or the SVG's own for standalone (built by the loader).
                }

                CollectDefinitions(child);
            }
        }

        /// <summary>
        /// Builds the renderable element for one node, or null if the node isn't directly paintable
        /// (definitions like &lt;defs&gt;/&lt;linearGradient&gt;/&lt;radialGradient&gt;/&lt;clipPath&gt;/
        /// &lt;stop&gt;, or any unrecognized element).
        /// </summary>
        private SvgElement? BuildElement(ISvgSourceNode node, InheritedPaint inherited, FontContext fontContext)
        {
            return node.Name switch
            {
                "g" => BuildGroup(node, inherited, fontContext),
                "path" => BuildPath(node, inherited),
                "circle" => BuildCircle(node, inherited),
                "polygon" => BuildPolygon(node, inherited),
                "polyline" => BuildPolyline(node, inherited),
                "rect" => BuildRect(node, inherited),
                "ellipse" => BuildEllipse(node, inherited),
                "line" => BuildLine(node, inherited),
                "use" => BuildUse(node, inherited, fontContext),
                "svg" => BuildNestedSvg(node, inherited, fontContext),
                "image" => BuildImage(node, inherited),
                "text" => BuildTextRun(node, inherited, fontContext, new TextWhitespaceState()),
                "switch" => BuildSwitch(node, inherited, fontContext),
                "a" => BuildAnchor(node, inherited, fontContext),
                _ => null,
            };
        }

        private SvgGroupElement BuildGroup(ISvgSourceNode node, InheritedPaint inherited, FontContext fontContext)
        {
            var group = new SvgGroupElement();
            var resolved = ApplyCommon(group, node, inherited);
            var childFont = ComputeFontContext(node, fontContext);

            foreach (var child in node.Children)
            {
                var element = BuildElement(child, resolved, childFont);
                if (element is not null)
                    group.Children.Add(element);
            }

            return group;
        }

        /// <summary>
        /// PeachPDF has no "which features/extensions/languages does this renderer support" axis to
        /// evaluate <c>requiredFeatures</c>/<c>requiredExtensions</c>/<c>systemLanguage</c> against -
        /// so, per a deliberate v1 simplification, this always renders only the first buildable child,
        /// same as if every other candidate had failed its (nonexistent) test.
        /// </summary>
        private SvgElement? BuildSwitch(ISvgSourceNode node, InheritedPaint inherited, FontContext fontContext)
        {
            var childFont = ComputeFontContext(node, fontContext);

            foreach (var child in node.Children)
            {
                var element = BuildElement(child, inherited, childFont);
                if (element is not null)
                    return element;
            }

            return null;
        }

        private SvgAnchorElement BuildAnchor(ISvgSourceNode node, InheritedPaint inherited, FontContext fontContext)
        {
            var anchor = new SvgAnchorElement
            {
                Href = node.GetAttribute("href") ?? node.GetAttribute("xlink:href"),
            };
            var resolved = ApplyCommon(anchor, node, inherited);
            var childFont = ComputeFontContext(node, fontContext);

            foreach (var child in node.Children)
            {
                var element = BuildElement(child, resolved, childFont);
                if (element is not null)
                    anchor.Children.Add(element);
            }

            return anchor;
        }

        private SvgPathElement BuildPath(ISvgSourceNode node, InheritedPaint inherited)
        {
            var path = new SvgPathElement { Segments = SvgPathDataParser.Parse(node.GetAttribute("d")) };
            ApplyCommon(path, node, inherited);
            return path;
        }

        private SvgCircleElement BuildCircle(ISvgSourceNode node, InheritedPaint inherited)
        {
            var circle = new SvgCircleElement
            {
                Cx = SvgValueParsers.ParseLength(node.GetAttribute("cx"), _viewportWidth) ?? 0,
                Cy = SvgValueParsers.ParseLength(node.GetAttribute("cy"), _viewportHeight) ?? 0,
                R = SvgValueParsers.ParseLength(node.GetAttribute("r"), ViewportDiagonal) ?? 0,
            };
            ApplyCommon(circle, node, inherited);
            return circle;
        }

        private SvgPolygonElement BuildPolygon(ISvgSourceNode node, InheritedPaint inherited)
        {
            var polygon = new SvgPolygonElement { Points = SvgPointsParser.Parse(node.GetAttribute("points")) };
            ApplyCommon(polygon, node, inherited);
            return polygon;
        }

        private SvgPolylineElement BuildPolyline(ISvgSourceNode node, InheritedPaint inherited)
        {
            var polyline = new SvgPolylineElement { Points = SvgPointsParser.Parse(node.GetAttribute("points")) };
            ApplyCommon(polyline, node, inherited);
            return polyline;
        }

        private SvgRectElement BuildRect(ISvgSourceNode node, InheritedPaint inherited)
        {
            var width = SvgValueParsers.ParseLength(node.GetAttribute("width"), _viewportWidth) ?? 0;
            var height = SvgValueParsers.ParseLength(node.GetAttribute("height"), _viewportHeight) ?? 0;

            // rx/ry each default to the other when only one is specified; both default to 0 (no
            // rounding) when neither is specified.
            double? rx = SvgValueParsers.ParseLength(node.GetAttribute("rx"), _viewportWidth);
            double? ry = SvgValueParsers.ParseLength(node.GetAttribute("ry"), _viewportHeight);
            rx ??= ry;
            ry ??= rx;

            var rect = new SvgRectElement
            {
                X = SvgValueParsers.ParseLength(node.GetAttribute("x"), _viewportWidth) ?? 0,
                Y = SvgValueParsers.ParseLength(node.GetAttribute("y"), _viewportHeight) ?? 0,
                Width = width,
                Height = height,
                Rx = Math.Clamp(rx ?? 0, 0, Math.Max(0, width / 2)),
                Ry = Math.Clamp(ry ?? 0, 0, Math.Max(0, height / 2)),
            };
            ApplyCommon(rect, node, inherited);
            return rect;
        }

        private SvgEllipseElement BuildEllipse(ISvgSourceNode node, InheritedPaint inherited)
        {
            var ellipse = new SvgEllipseElement
            {
                Cx = SvgValueParsers.ParseLength(node.GetAttribute("cx"), _viewportWidth) ?? 0,
                Cy = SvgValueParsers.ParseLength(node.GetAttribute("cy"), _viewportHeight) ?? 0,
                Rx = SvgValueParsers.ParseLength(node.GetAttribute("rx"), _viewportWidth) ?? 0,
                Ry = SvgValueParsers.ParseLength(node.GetAttribute("ry"), _viewportHeight) ?? 0,
            };
            ApplyCommon(ellipse, node, inherited);
            return ellipse;
        }

        private SvgLineElement BuildLine(ISvgSourceNode node, InheritedPaint inherited)
        {
            var line = new SvgLineElement
            {
                X1 = SvgValueParsers.ParseLength(node.GetAttribute("x1"), _viewportWidth) ?? 0,
                Y1 = SvgValueParsers.ParseLength(node.GetAttribute("y1"), _viewportHeight) ?? 0,
                X2 = SvgValueParsers.ParseLength(node.GetAttribute("x2"), _viewportWidth) ?? 0,
                Y2 = SvgValueParsers.ParseLength(node.GetAttribute("y2"), _viewportHeight) ?? 0,
            };
            ApplyCommon(line, node, inherited);
            return line;
        }

        private SvgUseElement? BuildUse(ISvgSourceNode node, InheritedPaint inherited, FontContext fontContext)
        {
            var href = node.GetAttribute("href") ?? node.GetAttribute("xlink:href");
            var id = href?.TrimStart('#');

            if (string.IsNullOrEmpty(id) || !_nodesById.TryGetValue(id, out var targetNode))
                return null;

            if (_useDepth >= MaxUseDepth)
                return null;

            var use = new SvgUseElement
            {
                X = SvgValueParsers.ParseLength(node.GetAttribute("x"), _viewportWidth) ?? 0,
                Y = SvgValueParsers.ParseLength(node.GetAttribute("y"), _viewportHeight) ?? 0,
                Width = SvgValueParsers.ParseLength(node.GetAttribute("width"), _viewportWidth),
                Height = SvgValueParsers.ParseLength(node.GetAttribute("height"), _viewportHeight),
            };
            // The <use> element's own resolved paint becomes the inherited context for the
            // (otherwise unstyled) referenced content - e.g. <use fill="none" stroke="red"
            // xlink:href="#circleWithNoFillOfItsOwn"/> paints the circle stroked red, not the
            // SVG-wide default black fill.
            var resolved = ApplyCommon(use, node, inherited);
            // The <use>'s own font-* likewise become the inherited font context for the referenced
            // content (so <use font-size="30" href="#text"/> renders the referenced text at 30).
            var childFont = ComputeFontContext(node, fontContext);

            _useDepth++;
            // <symbol> is excluded from BuildElement's general dispatch (like <defs> content, it must
            // never be painted directly if encountered during ordinary traversal) - it only becomes
            // paintable through this explicit <use> reference, which is what actually establishes its
            // viewport (a <symbol> has no size of its own; see BuildSymbol/SvgSymbolElement).
            var target = targetNode.Name == "symbol"
                ? BuildSymbol(targetNode, resolved, childFont)
                : BuildElement(targetNode, resolved, childFont);
            _useDepth--;

            if (target is null)
                return null;

            use.Target = target;
            return use;
        }

        /// <summary>
        /// Builds a <c>&lt;symbol&gt;</c>'s content. A symbol's viewBox establishes the coordinate
        /// system its own children resolve percentage lengths against, but a symbol has no
        /// width/height of its own - see <see cref="SvgSymbolElement"/>.
        /// </summary>
        private SvgSymbolElement BuildSymbol(ISvgSourceNode node, InheritedPaint inherited, FontContext fontContext)
        {
            var symbol = new SvgSymbolElement
            {
                ViewBox = SvgValueParsers.ParseViewBox(node.GetAttribute("viewBox")),
                PreserveAspectRatio = SvgValueParsers.ParsePreserveAspectRatio(node.GetAttribute("preserveAspectRatio")),
            };
            var resolved = ApplyCommon(symbol, node, inherited);
            var childFont = ComputeFontContext(node, fontContext);

            var previousWidth = _viewportWidth;
            var previousHeight = _viewportHeight;
            _viewportWidth = symbol.ViewBox?.Width ?? _viewportWidth;
            _viewportHeight = symbol.ViewBox?.Height ?? _viewportHeight;

            foreach (var child in node.Children)
            {
                var element = BuildElement(child, resolved, childFont);
                if (element is not null)
                    symbol.Children.Add(element);
            }

            _viewportWidth = previousWidth;
            _viewportHeight = previousHeight;

            return symbol;
        }

        /// <summary>
        /// Builds a nested <c>&lt;svg&gt;</c>, establishing a new viewport. A missing <c>width</c>/
        /// <c>height</c> defaults to the enclosing viewport's own size (spec's 100% default) rather
        /// than 0, so an unsized nested <c>&lt;svg&gt;</c> still fills its available space.
        /// </summary>
        private SvgNestedSvgElement BuildNestedSvg(ISvgSourceNode node, InheritedPaint inherited, FontContext fontContext)
        {
            var viewBox = SvgValueParsers.ParseViewBox(node.GetAttribute("viewBox"));

            var nested = new SvgNestedSvgElement
            {
                X = SvgValueParsers.ParseLength(node.GetAttribute("x"), _viewportWidth) ?? 0,
                Y = SvgValueParsers.ParseLength(node.GetAttribute("y"), _viewportHeight) ?? 0,
                Width = SvgValueParsers.ParseLength(node.GetAttribute("width"), _viewportWidth) ?? _viewportWidth ?? 0,
                Height = SvgValueParsers.ParseLength(node.GetAttribute("height"), _viewportHeight) ?? _viewportHeight ?? 0,
                ViewBox = viewBox,
                PreserveAspectRatio = SvgValueParsers.ParsePreserveAspectRatio(node.GetAttribute("preserveAspectRatio")),
            };
            var resolved = ApplyCommon(nested, node, inherited);
            var childFont = ComputeFontContext(node, fontContext);

            var previousWidth = _viewportWidth;
            var previousHeight = _viewportHeight;
            _viewportWidth = viewBox?.Width ?? nested.Width;
            _viewportHeight = viewBox?.Height ?? nested.Height;

            foreach (var child in node.Children)
            {
                var element = BuildElement(child, resolved, childFont);
                if (element is not null)
                    nested.Children.Add(element);
            }

            _viewportWidth = previousWidth;
            _viewportHeight = previousHeight;

            return nested;
        }

        /// <summary>
        /// Builds an <c>&lt;image&gt;</c> element, resolving its <c>href</c> either from a <c>data:</c>
        /// URI (decoded in-memory here) or from a network/file resource already fetched into
        /// <see cref="_prefetchedImages"/> by <see cref="PrefetchImageResourcesAsync"/>. Either source
        /// yields a raster <see cref="SvgImageElement.Image"/> or a nested vector
        /// <see cref="SvgImageElement.NestedDocument"/> (for an <c>image/svg+xml</c> payload); an
        /// unresolvable href leaves both null and the element renders nothing.
        /// </summary>
        private SvgImageElement BuildImage(ISvgSourceNode node, InheritedPaint inherited)
        {
            var image = new SvgImageElement
            {
                X = SvgValueParsers.ParseLength(node.GetAttribute("x"), _viewportWidth) ?? 0,
                Y = SvgValueParsers.ParseLength(node.GetAttribute("y"), _viewportHeight) ?? 0,
                Width = SvgValueParsers.ParseLength(node.GetAttribute("width"), _viewportWidth) ?? 0,
                Height = SvgValueParsers.ParseLength(node.GetAttribute("height"), _viewportHeight) ?? 0,
                PreserveAspectRatio = SvgValueParsers.ParsePreserveAspectRatio(node.GetAttribute("preserveAspectRatio")),
            };
            ApplyCommon(image, node, inherited);

            var href = node.GetAttribute("href") ?? node.GetAttribute("xlink:href");

            if (DataUriUtils.TryDecodeDataUri(href, out var mimeType, out var bytes))
            {
                if (mimeType.Equals("image/svg+xml", StringComparison.OrdinalIgnoreCase))
                    // A data: SVG's own nested network <image>s were prefetched under this same href
                    // (issue #251); pass that child map down, or null when it decodes purely in-memory.
                    image.NestedDocument = BuildNestedSvgDocument(bytes, NestedImagesFor(href));
                else
                    image.Image = DecodeRasterImage(bytes);
            }
            else if (href is not null && _prefetchedImages is not null && _prefetchedImages.TryGetValue(href, out var resource))
            {
                if (resource.IsSvg)
                    image.NestedDocument = BuildNestedSvgDocument(resource.Bytes, resource.NestedImages);
                else
                    image.Image = DecodeRasterImage(resource.Bytes);
            }

            return image;
        }

        /// <summary>
        /// Builds an embedded <c>image/svg+xml</c> payload (from either a <c>data:</c> URI or a fetched
        /// resource) as a standalone SVG document: it matches against its own <c>&lt;style&gt;</c>
        /// (built here), exactly like an <c>&lt;img&gt;</c>-referenced SVG. Returns null for malformed
        /// XML, in which case the image renders nothing.
        /// </summary>
        private SvgDocument? BuildNestedSvgDocument(byte[] bytes, IReadOnlyDictionary<string, SvgImageResource>? nestedImages)
        {
            if (!TryParseSvgRoot(Encoding.UTF8.GetString(bytes), out var nestedRoot))
                return null;   // malformed embedded SVG - renders nothing

            var nestedCssData = SvgCssStyling.BuildStyleData(SvgCssStyling.CollectStyleText(nestedRoot));

            var valueParser = new CssValueParser(_adapter);
            var registered = nestedCssData is null ? null : RegisteredProperty.BuildRegistry(nestedCssData, valueParser);
            var nestedVarContext = registered is { Count: > 0 }
                ? new CssVarResolver.VarContext(registered, valueParser)
                : null;

            if (nestedCssData is not null)
                SvgCssStyling.CascadeCustomProperties(nestedRoot, nestedCssData, "print", registered);

            var nestedSource = new XElementSvgSourceNode(nestedRoot, nestedRoot, nestedCssData, "print", nestedVarContext);
            // Thread the recursively-prefetched child map so the nested document's own <image>s resolve.
            return Build(nestedSource, _adapter, _contextColor, nestedImages);
        }

        /// <summary>The recursively-prefetched nested-image map for <paramref name="href"/>, if any (issue #251).</summary>
        private IReadOnlyDictionary<string, SvgImageResource>? NestedImagesFor(string? href) =>
            href is not null && _prefetchedImages is not null && _prefetchedImages.TryGetValue(href, out var resource)
                ? resource.NestedImages
                : null;

        /// <summary>
        /// Decodes raster image bytes (from either a <c>data:</c> URI or a fetched resource) into an
        /// <see cref="RImage"/>, returning null on a decode failure - the same non-fatal
        /// <see cref="InvalidOperationException"/> <c>ImageLoadHandler.LoadImageFromStream</c> already
        /// swallows.
        /// </summary>
        private RImage? DecodeRasterImage(byte[] bytes)
        {
            try
            {
                return _adapter.ImageFromStream(new MemoryStream(bytes));
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        /// <summary>
        /// Resolves (and memoizes into <see cref="SvgDocument.ClipPaths"/>) the &lt;clipPath&gt;
        /// referenced by <paramref name="id"/>. Safe to call once the full id registry from
        /// <see cref="CollectDefinitions"/> is in place, i.e. any time during pass 2. Paint
        /// inheritance doesn't matter here - only the shapes' geometry is ever used for clipping.
        /// </summary>
        private void ResolveClipPath(string id)
        {
            if (_document.ClipPaths.ContainsKey(id))
                return;

            if (!_nodesById.TryGetValue(id, out var node) || node.Name != "clipPath")
                return;

            var clipPath = new SvgClipPath
            {
                Id = id,
                ClipRule = SvgValueParsers.ParseFillRule(node.GetAttribute("clip-rule")),
                // Default is userSpaceOnUse; objectBoundingBox maps 0..1 child geometry to the
                // referencing element's bounding box (resolved at render time).
                ClipPathUnitsUserSpaceOnUse =
                    !string.Equals(node.GetAttribute("clipPathUnits"), "objectBoundingBox", StringComparison.OrdinalIgnoreCase),
            };

            foreach (var child in node.Children)
            {
                var shape = BuildElement(child, InheritedPaint.Initial, FontContext.Default);
                if (shape is not null)
                    clipPath.Shapes.Add(shape);
            }

            _document.ClipPaths[id] = clipPath;
        }

        /// <summary>
        /// Applies the shared presentation attributes to <paramref name="element"/>, falling back to
        /// <paramref name="inherited"/> for fill/stroke/stroke-width/stroke-miterlimit when the
        /// element doesn't specify its own (or explicitly says <c>inherit</c>). Returns the resolved
        /// paint so callers can pass it down to children.
        /// </summary>
        private InheritedPaint ApplyCommon(SvgElement element, ISvgSourceNode node, InheritedPaint inherited)
        {
            element.Id = node.GetAttribute("id");

            // Per CSS precedence (lowest to highest): presentation attribute < <style> element rule
            // (by specificity) < inline style="" attribute. Attr() below checks each tier in that
            // order, so every existing attribute read transparently gains style=/<style> support
            // without duplicating the inherit/fallback logic per property.
            string? Attr(string name) => ResolveStyledAttr(node, name);

            // The context every generated common-property setter needs beyond the raw value: color
            // resolution, the current viewport diagonal (for percentage lengths), and the url()-to-
            // pattern-vs-gradient reclassification ParsePaint can't do on its own (see ResolveUrlPaintKind).
            var ctx = new SvgPropertyContext(_adapter, _contextColor, ViewportDiagonal, ResolveUrlPaintKind);

            // Each property below keeps ApplyCommon's own null/"inherit"/invalid-value fallback
            // decision (which genuinely differs per property — see css-properties.json's svg.invalidBehavior
            // comments) and delegates only the parse-and-validate step to the generated registry, whose
            // TrySet already returns false without writing the field on failure. fill/stroke/fill-opacity/
            // stroke-opacity/fill-rule/stroke-linecap/stroke-linejoin all check TrySet's return value and
            // fall back to the INHERITED value (not a hardcoded default) on invalid input, per SVG 1.1
            // §11.4 (see #599, #675) — the same shape stroke-width/-miterlimit/-dashoffset/-dasharray
            // already use. opacity is the one exception (see its own comment below): it is not inherited,
            // so an invalid value simply leaves the field at its hardcoded default.

            var fillAttr = Attr("fill");
            if (fillAttr is null || fillAttr.Equals("inherit", StringComparison.OrdinalIgnoreCase)
                || !SvgPropertyRegistry.TrySet(element, "fill", fillAttr, in ctx))
                element.Fill = inherited.Fill;

            var strokeAttr = Attr("stroke");
            if (strokeAttr is null || strokeAttr.Equals("inherit", StringComparison.OrdinalIgnoreCase)
                || !SvgPropertyRegistry.TrySet(element, "stroke", strokeAttr, in ctx))
                element.Stroke = inherited.Stroke;

            // stroke-width/-miterlimit/-dashoffset/-dasharray fall back to the INHERITED value (not a
            // hardcoded default) on an invalid value, unlike the properties above - TrySet's return is
            // checked explicitly here.
            var strokeWidthAttr = Attr("stroke-width");
            if (strokeWidthAttr is null || strokeWidthAttr.Equals("inherit", StringComparison.OrdinalIgnoreCase)
                || !SvgPropertyRegistry.TrySet(element, "stroke-width", strokeWidthAttr, in ctx))
                element.StrokeWidth = inherited.StrokeWidth;

            var strokeMiterLimitAttr = Attr("stroke-miterlimit");
            if (strokeMiterLimitAttr is null || strokeMiterLimitAttr.Equals("inherit", StringComparison.OrdinalIgnoreCase)
                || !SvgPropertyRegistry.TrySet(element, "stroke-miterlimit", strokeMiterLimitAttr, in ctx))
                element.StrokeMiterLimit = inherited.StrokeMiterLimit;

            // opacity is not inherited (composites down the subtree at paint time instead) and, unlike
            // every other property here, was never given "inherit" handling in the pre-cutover code
            // either - an explicit "inherit" value is simply invalid input, same as any other bogus value.
            var opacityAttr = Attr("opacity");
            if (opacityAttr is not null)
                SvgPropertyRegistry.TrySet(element, "opacity", opacityAttr, in ctx);

            element.Transform = SvgTransformParser.Parse(Attr("transform"));

            // fill-rule/stroke-linecap/stroke-linejoin fall back to the INHERITED value (not a hardcoded
            // default) on an invalid value, per SVG 1.1 §11.4 - same shape as stroke-width/etc. above.
            var fillRuleAttr = Attr("fill-rule");
            if (fillRuleAttr is null || fillRuleAttr.Equals("inherit", StringComparison.OrdinalIgnoreCase)
                || !SvgPropertyRegistry.TrySet(element, "fill-rule", fillRuleAttr, in ctx))
                element.FillRule = inherited.FillRule;

            var fillOpacityAttr = Attr("fill-opacity");
            if (fillOpacityAttr is null || fillOpacityAttr.Equals("inherit", StringComparison.OrdinalIgnoreCase)
                || !SvgPropertyRegistry.TrySet(element, "fill-opacity", fillOpacityAttr, in ctx))
                element.FillOpacity = inherited.FillOpacity;

            var strokeOpacityAttr = Attr("stroke-opacity");
            if (strokeOpacityAttr is null || strokeOpacityAttr.Equals("inherit", StringComparison.OrdinalIgnoreCase)
                || !SvgPropertyRegistry.TrySet(element, "stroke-opacity", strokeOpacityAttr, in ctx))
                element.StrokeOpacity = inherited.StrokeOpacity;

            var lineCapAttr = Attr("stroke-linecap");
            if (lineCapAttr is null || lineCapAttr.Equals("inherit", StringComparison.OrdinalIgnoreCase)
                || !SvgPropertyRegistry.TrySet(element, "stroke-linecap", lineCapAttr, in ctx))
                element.StrokeLineCap = inherited.StrokeLineCap;

            var lineJoinAttr = Attr("stroke-linejoin");
            if (lineJoinAttr is null || lineJoinAttr.Equals("inherit", StringComparison.OrdinalIgnoreCase)
                || !SvgPropertyRegistry.TrySet(element, "stroke-linejoin", lineJoinAttr, in ctx))
                element.StrokeLineJoin = inherited.StrokeLineJoin;

            var dashArrayAttr = Attr("stroke-dasharray");
            if (dashArrayAttr is null || dashArrayAttr.Equals("inherit", StringComparison.OrdinalIgnoreCase)
                || !SvgPropertyRegistry.TrySet(element, "stroke-dasharray", dashArrayAttr, in ctx))
                element.StrokeDashArray = inherited.StrokeDashArray;

            var dashOffsetAttr = Attr("stroke-dashoffset");
            if (dashOffsetAttr is null || dashOffsetAttr.Equals("inherit", StringComparison.OrdinalIgnoreCase)
                || !SvgPropertyRegistry.TrySet(element, "stroke-dashoffset", dashOffsetAttr, in ctx))
                element.StrokeDashOffset = inherited.StrokeDashOffset;

            var clipPathAttr = Attr("clip-path");
            if (!string.IsNullOrWhiteSpace(clipPathAttr))
            {
                var hashIndex = clipPathAttr.IndexOf('#');
                var closeIndex = clipPathAttr.IndexOf(')');

                if (hashIndex >= 0 && closeIndex > hashIndex)
                {
                    var clipId = clipPathAttr[(hashIndex + 1)..closeIndex].Trim();
                    element.ClipPathRef = clipId;
                    ResolveClipPath(clipId);
                }
            }

            // Same url(#id)/none grammar as a marker reference - reused directly rather than
            // duplicating the tiny parser.
            element.MaskRef = SvgValueParsers.ParseMarkerReference(Attr("mask"));

            // The `marker` shorthand sets all three individual properties at once; an individually
            // specified marker-start/mid/end (if present) then overrides just that one.
            var markerShorthandAttr = Attr("marker");
            var markerShorthandRef = markerShorthandAttr is null || markerShorthandAttr.Equals("inherit", StringComparison.OrdinalIgnoreCase)
                ? null
                : SvgValueParsers.ParseMarkerReference(markerShorthandAttr);

            string? ResolveMarkerProperty(string propertyName, string? inheritedRef)
            {
                var attr = Attr(propertyName);
                if (attr is null)
                    return markerShorthandRef ?? inheritedRef;
                return attr.Equals("inherit", StringComparison.OrdinalIgnoreCase) ? inheritedRef : SvgValueParsers.ParseMarkerReference(attr);
            }

            element.MarkerStartRef = ResolveMarkerProperty("marker-start", inherited.MarkerStartRef);
            element.MarkerMidRef = ResolveMarkerProperty("marker-mid", inherited.MarkerMidRef);
            element.MarkerEndRef = ResolveMarkerProperty("marker-end", inherited.MarkerEndRef);

            // direction is a real inherited CSS property (unlike unicode-bidi, which BuildTextRun
            // resolves separately, per-node, with no inheritance) - threaded through InheritedPaint the
            // same way font-family/font-size are threaded through FontContext, so a <text>/<tspan>
            // inherits it from ANY ancestor, not just its own text-run ancestors.
            var directionAttr = Attr("direction");
            var direction = directionAttr is null || directionAttr.Equals("inherit", StringComparison.OrdinalIgnoreCase)
                ? inherited.Direction
                : directionAttr.Trim().Equals("rtl", StringComparison.OrdinalIgnoreCase) ? "rtl" : "ltr";

            // writing-mode/text-orientation are real inherited CSS properties too, carried the same way
            // direction is - resolved once here and stored only on the SvgTextElement that actually
            // consumes them (BuildTextRun), not on every SvgElement, since only text has a pen model to
            // orient. Parsed through the same Map.WritingModes/Map.TextOrientations keyword tables the
            // HTML CSS-OM pipeline's WritingModeProperty/TextOrientationProperty converters use, rather
            // than a second, independently-written keyword parser (this repo's own "don't write two
            // parsers for the same CSS grammar across layers" convention). An unrecognized value
            // (including SVG 1.1's legacy tb/tb-rl/lr/lr-tb/rl/rl-tb writing-mode keywords, which Map
            // doesn't define) falls back to the inherited value via GetValueOrDefault, matching every
            // other inherited property above. sideways-rl/sideways-lr DO parse (Map.WritingModes defines
            // them) but render as horizontal-tb throughout, same as the HTML pipeline's own scope -
            // SvgRenderer.IsVerticalWritingMode only recognizes vertical-rl/vertical-lr as vertical.
            var writingModeAttr = Attr("writing-mode");
            var writingMode = writingModeAttr is null || writingModeAttr.Equals("inherit", StringComparison.OrdinalIgnoreCase)
                ? inherited.WritingMode
                : Map.WritingModes.GetValueOrDefault(writingModeAttr.Trim(), inherited.WritingMode);

            var textOrientationAttr = Attr("text-orientation");
            var textOrientation = textOrientationAttr is null || textOrientationAttr.Equals("inherit", StringComparison.OrdinalIgnoreCase)
                ? inherited.TextOrientation
                : Map.TextOrientations.GetValueOrDefault(textOrientationAttr.Trim(), inherited.TextOrientation);

            return new InheritedPaint(
                element.Fill,
                element.Stroke,
                element.StrokeWidth,
                element.StrokeMiterLimit,
                element.FillRule,
                element.FillOpacity,
                element.StrokeOpacity,
                element.StrokeLineCap,
                element.StrokeLineJoin,
                element.StrokeDashArray,
                element.StrokeDashOffset,
                element.MarkerStartRef,
                element.MarkerMidRef,
                element.MarkerEndRef,
                direction,
                writingMode,
                textOrientation);
        }

        /// <summary>
        /// Resolves one presentation-style property for <paramref name="node"/> with the same
        /// precedence <see cref="ApplyCommon"/>'s local <c>Attr</c> closure uses (inline <c>style=</c>
        /// beats a matching <c>&lt;style&gt;</c> rule beats a bare presentation attribute) - extracted
        /// so <see cref="BuildTextRun"/> can resolve font/text-anchor properties the same way without
        /// duplicating the precedence logic. Re-parses <paramref name="node"/>'s own <c>style=</c>/
        /// <c>class</c> per call rather than caching, matching this builder's existing preference for
        /// simplicity over micro-optimization elsewhere (e.g. <see cref="BuildDefinitionChildren"/>).
        /// </summary>
        private static string? ResolveStyledAttr(ISvgSourceNode node, string name)
        {
            // Precedence (highest to lowest): inline style="" attribute > matched author-stylesheet rules
            // (full CSS engine: combinators/attr/pseudo selectors, specificity, var()) > presentation
            // attribute. The matched-rule tier comes from the document/SVG-local CssData (see the source
            // node's GetMatchedCssDeclarations), replacing the former SVG-local mini stylesheet.
            //
            // The first tier that DECLARES the property is the cascade winner and is authoritative: if its
            // var() is guaranteed-invalid the declaration is invalid at computed-value time (CSS Custom
            // Properties 1 §3) — it still wins, so this returns null and the caller computes the property to
            // its inherited/initial value, rather than the cascade rolling back to a lower-priority tier.
            var styleDeclarations = SvgValueParsers.ParseStyleDeclarations(node.GetAttribute("style"));
            if (styleDeclarations.TryGetValue(name, out var styleValue))
                return node.ResolveVar(styleValue); // null (guaranteed-invalid) → inherited/initial, no fall-through

            var matched = node.GetMatchedCssDeclarations();
            if (matched is not null && matched.TryGetValue(name, out var matchedValue))
                return matchedValue; // present (incl. null = invalid at computed-value time) → authoritative

            return node.GetAttribute(name);
        }

        /// <summary>
        /// Resolves an element's own declared font properties (font-family/font-size/font-weight/
        /// font-style, via <see cref="ResolveStyledAttr"/>) layered over the <paramref name="inherited"/>
        /// context, honoring the literal <c>inherit</c> keyword. Cheap - it produces resolved values only
        /// (no <see cref="RFont"/>); a text run realizes the actual font from the result. Used both to
        /// propagate the font context through container elements and to resolve a text run's own font.
        /// </summary>
        private FontContext ComputeFontContext(ISvgSourceNode node, FontContext inherited)
        {
            var familyAttr = ResolveStyledAttr(node, "font-family");
            var family = string.IsNullOrWhiteSpace(familyAttr) || familyAttr.Trim().Equals("inherit", StringComparison.OrdinalIgnoreCase)
                ? inherited.Family
                : familyAttr.Split(',')[0].Trim().Trim('\'', '"');

            var size = ResolveFontSize(ResolveStyledAttr(node, "font-size"), inherited.Size) ?? inherited.Size;

            var weightAttr = ResolveStyledAttr(node, "font-weight");
            var bold = weightAttr switch
            {
                null => inherited.Bold,
                _ when weightAttr.Equals("inherit", StringComparison.OrdinalIgnoreCase) => inherited.Bold,
                _ when weightAttr.Equals("bold", StringComparison.OrdinalIgnoreCase) || weightAttr.Equals("bolder", StringComparison.OrdinalIgnoreCase) => true,
                _ when int.TryParse(weightAttr, out var weightValue) => weightValue >= 700,
                _ => false,
            };

            var styleAttr = ResolveStyledAttr(node, "font-style");
            var italic = styleAttr is null || styleAttr.Equals("inherit", StringComparison.OrdinalIgnoreCase)
                ? inherited.Italic
                : styleAttr.Equals("italic", StringComparison.OrdinalIgnoreCase) || styleAttr.Equals("oblique", StringComparison.OrdinalIgnoreCase);

            return new FontContext(family, size, bold, italic);
        }

        /// <summary>
        /// Resolves a <c>font-size</c> value in the same unit domain the rest of the text pipeline uses.
        /// Relative units resolve against the real inherited size: <c>em</c>/<c>%</c>/<c>ex</c> against
        /// <paramref name="parentSize"/> (the parent's used font-size), <c>rem</c> against the root's
        /// (<see cref="_rootFontSize"/>). Absolute units (<c>pt</c>/<c>px</c>/<c>pc</c>/<c>in</c>/<c>cm</c>/
        /// <c>mm</c>), a unitless number, and <c>calc()</c> defer to <see cref="SvgValueParsers.ParseLength"/>.
        /// Returns null for a missing/unparseable value (caller falls back to the inherited size).
        /// </summary>
        private double? ResolveFontSize(string? value, double parentSize)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            var t = value.Trim();

            if (t.Equals("inherit", StringComparison.OrdinalIgnoreCase))
                return parentSize;

            static double? Number(string s) =>
                double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;

            if (t.EndsWith("rem", StringComparison.OrdinalIgnoreCase))
                return Number(t[..^3]) is { } r ? r * _rootFontSize : null;
            if (t.EndsWith("em", StringComparison.OrdinalIgnoreCase))
                return Number(t[..^2]) is { } e ? e * parentSize : null;
            if (t.EndsWith("ex", StringComparison.OrdinalIgnoreCase))
                return Number(t[..^2]) is { } x ? x * parentSize * 0.5 : null;
            if (t.EndsWith('%'))
                return Number(t[..^1]) is { } p ? p / 100.0 * parentSize : null;

            // Absolute units / unitless / calc(). A '%' inside a calc resolves against the parent size.
            return SvgValueParsers.ParseLength(t, parentSize);
        }

        /// <summary>
        /// Builds one text run - shared by <c>&lt;text&gt;</c> (the subtree root) and its
        /// <c>&lt;tspan&gt;</c>/<c>&lt;tref&gt;</c>/<c>&lt;textPath&gt;</c> children (see
        /// <see cref="SvgTextElement"/>). <paramref name="fontContext"/> is this run's inherited font
        /// context (from any ancestor - see <see cref="FontContext"/>). <paramref name="state"/> collapses
        /// whitespace across the whole <c>&lt;text&gt;</c> subtree as one unit (SVG 1.1 §10.15): it is
        /// created fresh per top-level <c>&lt;text&gt;</c> and shared with every descendant run.
        /// </summary>
        private SvgTextElement BuildTextRun(ISvgSourceNode node, InheritedPaint inherited, FontContext fontContext, TextWhitespaceState state)
        {
            var xAttr = node.GetAttribute("x");
            var yAttr = node.GetAttribute("y");
            var xList = SvgValueParsers.ParseLengthList(xAttr, _viewportWidth);
            var yList = SvgValueParsers.ParseLengthList(yAttr, _viewportHeight);
            var dxList = SvgValueParsers.ParseLengthList(node.GetAttribute("dx"), _viewportWidth);
            var dyList = SvgValueParsers.ParseLengthList(node.GetAttribute("dy"), _viewportHeight);
            var rotateList = SvgValueParsers.ParseNumberList(node.GetAttribute("rotate"));

            var run = new SvgTextElement
            {
                HasOwnX = !string.IsNullOrEmpty(xAttr),
                HasOwnY = !string.IsNullOrEmpty(yAttr),
                // The scalar X/Y/Dx/Dy/RotateDegrees are the leading list value (the whole-run/chunk origin);
                // the full per-character lists live in XList/YList/DxList/DyList/RotateList.
                X = xList is { Length: > 0 } ? xList[0] : 0,
                Y = yList is { Length: > 0 } ? yList[0] : 0,
                Dx = dxList is { Length: > 0 } ? dxList[0] : 0,
                Dy = dyList is { Length: > 0 } ? dyList[0] : 0,
                RotateDegrees = rotateList is { Length: > 0 } ? rotateList[0] : 0,
                XList = xList,
                YList = yList,
                DxList = dxList,
                DyList = dyList,
                RotateList = rotateList,
            };

            var resolved = ApplyCommon(run, node, inherited);

            run.TextAnchor = ResolveStyledAttr(node, "text-anchor")?.Trim().ToLowerInvariant() switch
            {
                "middle" => SvgTextAnchor.Middle,
                "end" => SvgTextAnchor.End,
                _ => SvgTextAnchor.Start,
            };

            run.Direction = resolved.Direction;
            run.WritingMode = resolved.WritingMode;
            run.TextOrientation = resolved.TextOrientation;

            var unicodeBidiAttr = ResolveStyledAttr(node, "unicode-bidi")?.Trim().ToLowerInvariant();
            run.UnicodeBidi = unicodeBidiAttr switch
            {
                "embed" => "embed",
                "isolate" => "isolate",
                "bidi-override" => "bidi-override",
                "isolate-override" => "isolate-override",
                "plaintext" => "plaintext",
                _ => "normal",
            };

            var runFont = ComputeFontContext(node, fontContext);

            var fontStyle = RFontStyle.Regular;
            if (runFont.Bold) fontStyle |= RFontStyle.Bold;
            if (runFont.Italic) fontStyle |= RFontStyle.Italic;

            run.Font = _adapter.GetFont(runFont.Family, runFont.Size, fontStyle)
                       ?? _adapter.GetFont(Html.Core.Utils.DefaultFontResolver.DefaultFont, runFont.Size, fontStyle);

            var childFontContext = runFont;

            // Walk this run's loose text and child elements in document order, so text authored after a
            // child element keeps its place and whitespace collapses across the boundaries (SVG §10.15).
            foreach (var content in node.ContentNodes)
            {
                if (content.IsText)
                {
                    var text = state.Collapse(content.Text ?? "");
                    if (text.Length > 0)
                        run.Content.Add(new SvgTextFragment { Text = text });
                    continue;
                }

                var child = content.Element!;
                switch (child.Name)
                {
                    case "tspan":
                        run.Content.Add(new SvgTextSpan { Run = BuildTextRun(child, resolved, childFontContext, state) });
                        break;

                    case "tref":
                    {
                        var trefRun = BuildTextRun(child, resolved, childFontContext, state);
                        var href = child.GetAttribute("href") ?? child.GetAttribute("xlink:href");
                        var id = href?.TrimStart('#');
                        if (!string.IsNullOrEmpty(id) && _nodesById.TryGetValue(id, out var target))
                        {
                            // A tref's own text is the referenced element's text, collapsed as part of the
                            // same subtree stream (replacing whatever the tref element itself contained).
                            trefRun.Content.Clear();
                            var text = state.Collapse(target.GetTextContent());
                            if (text.Length > 0)
                                trefRun.Content.Add(new SvgTextFragment { Text = text });
                        }
                        run.Content.Add(new SvgTextSpan { Run = trefRun });
                        break;
                    }

                    case "textPath":
                    {
                        var textPathRun = BuildTextRun(child, resolved, childFontContext, state);
                        var href = child.GetAttribute("href") ?? child.GetAttribute("xlink:href");
                        var id = href?.TrimStart('#');

                        // The target may be a <path> (its own d geometry) or a basic shape (converted to
                        // path geometry). A missing/invalid/empty reference leaves PathData null, so the run
                        // just renders on the ordinary straight baseline.
                        if (!string.IsNullOrEmpty(id) && _nodesById.TryGetValue(id, out var target))
                        {
                            var pathData = target.Name == "path"
                                ? (string.IsNullOrEmpty(target.GetAttribute("d")) ? null : SvgPathDataParser.Parse(target.GetAttribute("d")))
                                : SvgShapeToPath.Convert(target);

                            if (pathData is { Count: > 0 })
                            {
                                textPathRun.PathData = pathData;
                                var (offset, isPercent) = ParseStartOffset(child.GetAttribute("startOffset"));
                                textPathRun.StartOffset = offset;
                                textPathRun.StartOffsetIsPercent = isPercent;
                                textPathRun.Side = string.Equals(child.GetAttribute("side")?.Trim(), "right", StringComparison.OrdinalIgnoreCase)
                                    ? SvgTextPathSide.Right
                                    : SvgTextPathSide.Left;
                            }
                        }

                        run.Content.Add(new SvgTextSpan { Run = textPathRun });
                        break;
                    }
                }
            }

            return run;
        }

        /// <summary>
        /// Parses a <c>&lt;textPath&gt;</c> <c>startOffset</c>: a length (user units) or a percentage of
        /// the path's total length. A percentage is stored as its 0..1 fraction plus the percent flag,
        /// so the render side can resolve it against the (build-time-unknown) total path length.
        /// </summary>
        private static (double Offset, bool IsPercent) ParseStartOffset(string? raw)
        {
            raw = raw?.Trim();
            if (string.IsNullOrEmpty(raw))
                return (0, false);

            if (raw.EndsWith('%'))
                return ((SvgValueParsers.ParseLength(raw[..^1]) ?? 0) / 100.0, true);

            return (SvgValueParsers.ParseLength(raw) ?? 0, false);
        }

        /// <summary>
        /// SVG's default (<c>xml:space="default"</c>) whitespace collapsing (SVG 1.1 §10.15), applied
        /// across a whole <c>&lt;text&gt;</c> subtree as one unit rather than per run: one instance is
        /// created per top-level <c>&lt;text&gt;</c> and threaded through every descendant run's build, so
        /// a run of whitespace spanning a run boundary collapses to a single space (kept on the following
        /// run's leading edge) and only the whole subtree's leading/trailing whitespace is trimmed.
        /// </summary>
        private sealed class TextWhitespaceState
        {
            private bool _atStart = true;      // still trimming the whole subtree's leading whitespace
            private bool _pendingSpace;        // a whitespace run has ended; emit one space before the next glyph

            /// <summary>Collapses one text fragment, advancing the shared cross-run state.</summary>
            public string Collapse(string raw)
            {
                if (string.IsNullOrEmpty(raw))
                    return "";

                var sb = new StringBuilder(raw.Length);
                foreach (var ch in raw)
                {
                    if (char.IsWhiteSpace(ch))
                    {
                        if (!_atStart)
                            _pendingSpace = true;
                        continue;
                    }

                    if (_pendingSpace)
                    {
                        sb.Append(' ');
                        _pendingSpace = false;
                    }

                    sb.Append(ch);
                    _atStart = false;
                }

                return sb.ToString();
            }
        }

        private SvgMarkerElement BuildMarker(ISvgSourceNode node)
        {
            var orient = node.GetAttribute("orient");

            var marker = new SvgMarkerElement
            {
                RefX = SvgValueParsers.ParseLength(node.GetAttribute("refX")) ?? 0,
                RefY = SvgValueParsers.ParseLength(node.GetAttribute("refY")) ?? 0,
                MarkerWidth = SvgValueParsers.ParseLength(node.GetAttribute("markerWidth")) ?? 3,
                MarkerHeight = SvgValueParsers.ParseLength(node.GetAttribute("markerHeight")) ?? 3,
                ViewBox = SvgValueParsers.ParseViewBox(node.GetAttribute("viewBox")),
                PreserveAspectRatio = SvgValueParsers.ParsePreserveAspectRatio(node.GetAttribute("preserveAspectRatio")),
                MarkerUnitsStrokeWidth = !string.Equals(node.GetAttribute("markerUnits"), "userSpaceOnUse", StringComparison.OrdinalIgnoreCase),
                OrientAuto = string.Equals(orient, "auto", StringComparison.OrdinalIgnoreCase),
                OrientAutoStartReverse = string.Equals(orient, "auto-start-reverse", StringComparison.OrdinalIgnoreCase),
                OrientAngle = SvgValueParsers.ParseLength(orient) ?? 0,
            };

            foreach (var child in node.Children)
            {
                var element = BuildElement(child, InheritedPaint.Initial, FontContext.Default);
                if (element is not null)
                    marker.Children.Add(element);
            }

            return marker;
        }

        /// <summary>
        /// <see cref="SvgValueParsers.ParsePaint"/> has no document context, so a <c>url(#id)</c> value
        /// always initially comes back as <see cref="SvgPaintKind.GradientRef"/> regardless of what
        /// <c>#id</c> actually names - reclassify it to <see cref="SvgPaintKind.PatternRef"/> here, now
        /// that the id registry (built by <see cref="CollectDefinitions"/>, which always runs before
        /// any element's own paint is resolved) is available.
        /// </summary>
        private SvgPaint ResolveUrlPaintKind(SvgPaint paint) =>
            paint.Kind == SvgPaintKind.GradientRef && paint.ReferenceId is { } id && !_document.Gradients.ContainsKey(id) && _document.Patterns.ContainsKey(id)
                ? SvgPaint.PatternRef(id)
                : paint;

        private SvgMask BuildMask(ISvgSourceNode node)
        {
            var isObjectBoundingBox = !string.Equals(node.GetAttribute("maskUnits"), "userSpaceOnUse", StringComparison.OrdinalIgnoreCase);
            var contentUnitsUserSpaceOnUse = !string.Equals(node.GetAttribute("maskContentUnits"), "objectBoundingBox", StringComparison.OrdinalIgnoreCase);

            var defaultMask = new SvgMask();
            var mask = new SvgMask
            {
                Id = node.GetAttribute("id"),
                X = SvgValueParsers.ParseGradientCoordinate(node.GetAttribute("x"), isObjectBoundingBox, _viewportWidth) ?? defaultMask.X,
                Y = SvgValueParsers.ParseGradientCoordinate(node.GetAttribute("y"), isObjectBoundingBox, _viewportHeight) ?? defaultMask.Y,
                Width = SvgValueParsers.ParseGradientCoordinate(node.GetAttribute("width"), isObjectBoundingBox, _viewportWidth) ?? defaultMask.Width,
                Height = SvgValueParsers.ParseGradientCoordinate(node.GetAttribute("height"), isObjectBoundingBox, _viewportHeight) ?? defaultMask.Height,
                MaskUnitsUserSpaceOnUse = !isObjectBoundingBox,
                MaskContentUnitsUserSpaceOnUse = contentUnitsUserSpaceOnUse,
                Children = BuildDefinitionChildren(node),
            };

            return mask;
        }

        private SvgPattern BuildPattern(ISvgSourceNode node)
        {
            var isObjectBoundingBox = !string.Equals(node.GetAttribute("patternUnits"), "userSpaceOnUse", StringComparison.OrdinalIgnoreCase);
            var contentUnitsUserSpaceOnUse = !string.Equals(node.GetAttribute("patternContentUnits"), "objectBoundingBox", StringComparison.OrdinalIgnoreCase);

            var pattern = new SvgPattern
            {
                Id = node.GetAttribute("id"),
                X = SvgValueParsers.ParseGradientCoordinate(node.GetAttribute("x"), isObjectBoundingBox, _viewportWidth) ?? 0,
                Y = SvgValueParsers.ParseGradientCoordinate(node.GetAttribute("y"), isObjectBoundingBox, _viewportHeight) ?? 0,
                Width = SvgValueParsers.ParseGradientCoordinate(node.GetAttribute("width"), isObjectBoundingBox, _viewportWidth) ?? 0,
                Height = SvgValueParsers.ParseGradientCoordinate(node.GetAttribute("height"), isObjectBoundingBox, _viewportHeight) ?? 0,
                PatternUnitsUserSpaceOnUse = !isObjectBoundingBox,
                PatternContentUnitsUserSpaceOnUse = contentUnitsUserSpaceOnUse,
                PatternTransform = SvgTransformParser.Parse(node.GetAttribute("patternTransform")),
                ViewBox = SvgValueParsers.ParseViewBox(node.GetAttribute("viewBox")),
                PreserveAspectRatio = SvgValueParsers.ParsePreserveAspectRatio(node.GetAttribute("preserveAspectRatio")),
                Children = BuildDefinitionChildren(node),
            };

            return pattern;
        }

        /// <summary>Builds the renderable children of a pure definition element (<c>&lt;pattern&gt;</c>/<c>&lt;marker&gt;</c>/<c>&lt;mask&gt;</c>) - same recursion <see cref="BuildGroup"/> uses for an ordinary container, just not itself wrapped in a paintable <see cref="SvgElement"/>.</summary>
        private List<SvgElement> BuildDefinitionChildren(ISvgSourceNode node)
        {
            var children = new List<SvgElement>();

            foreach (var child in node.Children)
            {
                var element = BuildElement(child, InheritedPaint.Initial, FontContext.Default);
                if (element is not null)
                    children.Add(element);
            }

            return children;
        }

        private SvgLinearGradient BuildLinearGradient(ISvgSourceNode node)
        {
            var isObjectBoundingBox = !IsUserSpaceOnUse(node);

            return new SvgLinearGradient
            {
                Id = node.GetAttribute("id"),
                GradientUnitsUserSpaceOnUse = !isObjectBoundingBox,
                GradientTransform = SvgTransformParser.Parse(node.GetAttribute("gradientTransform")),
                SpreadMethod = SvgValueParsers.ParseSpreadMethod(node.GetAttribute("spreadMethod")),
                // Spec defaults: x1/y1/y2 = 0%, x2 = 100% - expressed directly as the objectBoundingBox
                // fraction (0 or 1); the userSpaceOnUse-mode default (100% of the current viewport,
                // rather than a flat 0) is not resolved here, a minor known gap for the less common mode.
                X1 = SvgValueParsers.ParseGradientCoordinate(node.GetAttribute("x1"), isObjectBoundingBox, _viewportWidth) ?? 0,
                Y1 = SvgValueParsers.ParseGradientCoordinate(node.GetAttribute("y1"), isObjectBoundingBox, _viewportHeight) ?? 0,
                X2 = SvgValueParsers.ParseGradientCoordinate(node.GetAttribute("x2"), isObjectBoundingBox, _viewportWidth) ?? (isObjectBoundingBox ? 1.0 : 0.0),
                Y2 = SvgValueParsers.ParseGradientCoordinate(node.GetAttribute("y2"), isObjectBoundingBox, _viewportHeight) ?? 0,
                Stops = BuildStops(node),
            };
        }

        private SvgRadialGradient BuildRadialGradient(ISvgSourceNode node)
        {
            var isObjectBoundingBox = !IsUserSpaceOnUse(node);

            return new SvgRadialGradient
            {
                Id = node.GetAttribute("id"),
                GradientUnitsUserSpaceOnUse = !isObjectBoundingBox,
                GradientTransform = SvgTransformParser.Parse(node.GetAttribute("gradientTransform")),
                SpreadMethod = SvgValueParsers.ParseSpreadMethod(node.GetAttribute("spreadMethod")),
                // Spec defaults: cx/cy/r = 50% - expressed directly as the objectBoundingBox fraction
                // (0.5); see BuildLinearGradient's comment re: the userSpaceOnUse-mode default gap.
                Cx = SvgValueParsers.ParseGradientCoordinate(node.GetAttribute("cx"), isObjectBoundingBox, _viewportWidth) ?? (isObjectBoundingBox ? 0.5 : 0.0),
                Cy = SvgValueParsers.ParseGradientCoordinate(node.GetAttribute("cy"), isObjectBoundingBox, _viewportHeight) ?? (isObjectBoundingBox ? 0.5 : 0.0),
                R = SvgValueParsers.ParseGradientCoordinate(node.GetAttribute("r"), isObjectBoundingBox, ViewportDiagonal) ?? (isObjectBoundingBox ? 0.5 : 0.0),
                Fx = SvgValueParsers.ParseGradientCoordinate(node.GetAttribute("fx"), isObjectBoundingBox, _viewportWidth),
                Fy = SvgValueParsers.ParseGradientCoordinate(node.GetAttribute("fy"), isObjectBoundingBox, _viewportHeight),
                Stops = BuildStops(node),
            };
        }

        private static bool IsUserSpaceOnUse(ISvgSourceNode node) =>
            string.Equals(node.GetAttribute("gradientUnits"), "userSpaceOnUse", StringComparison.OrdinalIgnoreCase);

        private List<SvgGradientStop> BuildStops(ISvgSourceNode node)
        {
            var stops = new List<SvgGradientStop>();

            foreach (var child in node.Children)
            {
                if (child.Name != "stop")
                    continue;

                var offsetAttr = child.GetAttribute("offset");
                var offset = string.IsNullOrWhiteSpace(offsetAttr) ? 0.0 : SvgValueParsers.ParseOpacity(offsetAttr);

                var color = SvgValueParsers.ParseStopColor(
                    child.GetAttribute("stop-color"),
                    child.GetAttribute("stop-opacity"),
                    child.GetAttribute("style"),
                    _adapter);

                stops.Add(new SvgGradientStop { Offset = offset, Color = color });
            }

            // Defensive: stop offsets must be monotonically non-decreasing per spec.
            return [.. stops.OrderBy(s => s.Offset)];
        }
    }
}
