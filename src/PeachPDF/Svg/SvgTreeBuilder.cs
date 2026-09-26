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

using PeachDrawing.Text.Shaping;
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
using System.Numerics;
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
        private readonly List<FeImage> _feImageReferences = [];
        private readonly SvgDocument _document = new();
        private int _useDepth;

        /// <summary>
        /// A gradient/marker/pattern/mask/clipPath found by <see cref="CollectDefinitions"/>, with the chain of
        /// elements between the root and it (root excluded). Its content inherits from that chain, not from
        /// whatever references it, but the chain's computed values can't be known until the root font, the
        /// viewport and the id registry exist - so the build is deferred to <see cref="BuildDeferredDefinitions"/>
        /// and the chain is replayed then (see <see cref="ResolveAncestorContext"/>).
        /// </summary>
        private readonly record struct DeferredDefinition(ISvgSourceNode Node, string Id, ISvgSourceNode[] Ancestors);

        private readonly List<DeferredDefinition> _deferredDefinitions = [];
        private readonly Dictionary<string, ISvgSourceNode[]> _clipPathAncestors = new(StringComparer.Ordinal);
        private readonly List<ISvgSourceNode> _ancestorStack = [];
        private readonly HashSet<string> _resolvingClipPaths = new(StringComparer.Ordinal);

        /// <summary>The root's own resolved inherited paint, the seed every definition's ancestor chain folds from.</summary>
        private InheritedPaint _rootPaint = InheritedPaint.Initial;
        private double? _rootViewportWidth;
        private double? _rootViewportHeight;

        /// <summary>
        /// True while <see cref="ApplyCommon"/> is run only to obtain the inherited values it returns (the root,
        /// a definition's ancestors, the definition element itself), discarding the throwaway element. It stops
        /// <c>clip-path</c> resolution there: that would build a clipPath - which itself replays an ancestor chain
        /// through here - before the context it needs exists, and could recurse.
        /// </summary>
        private bool _contextOnly;

        /// <summary>
        /// The root element's used <c>font-size</c> (the value <c>rem</c> font-sizes resolve against),
        /// captured once in <see cref="BuildDocument"/>. Defaults to the UA initial font size until then.
        /// </summary>
        private double _rootFontSize = Html.Core.Utils.DefaultFontResolver.FontSize;

        /// <summary>The root element's resolved font context, and the length basis built on it (what the
        /// root-element units <c>rem</c>/<c>rex</c>/<c>rch</c>/... resolve against). Null until
        /// <see cref="BuildDocument"/> has computed it, and while it is being computed - so the root's own
        /// font resolution takes each unit's fallback instead of recursing into itself.</summary>
        private FontContext? _rootFont;
        private LengthBasis? _rootBasis;

        /// <summary>The basis of the element currently being built: the font its geometry attributes'
        /// <c>em</c>/<c>ex</c>/<c>ch</c>/... resolve against. Set (and restored) by <see cref="BuildElement"/>,
        /// so every length parsed while building an element sees that element's own font.</summary>
        private LengthBasis? _lengthBasis;


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
        /// <see cref="_rootFontSize"/>). <c>SizeDeclared</c> records whether any element on the way down
        /// actually declared a <c>font-size</c>: text always has a size (the UA default), but a geometry
        /// <c>em</c> in an SVG that never mentions one keeps meaning the CSS initial 16px.
        /// </summary>
        private readonly record struct FontContext(
            string Family, double Size, bool Bold, bool Italic, int Stretch,
            double LetterSpacing, double WordSpacing, TextTransform TextTransform,
            LigatureSet Ligatures, CapsMode CapsRequested,
            NumeralSet Numeric, EastAsianSet EastAsian,
            IReadOnlyList<(string Tag, int Value)> FeatureSettings, bool Kerning, string? Language = null,
            SubSuperMode PositionRequested = SubSuperMode.None,
            bool SizeDeclared = false)
        {
            public static readonly FontContext Default = new(
                Html.Core.Utils.DefaultFontResolver.DefaultFont, Html.Core.Utils.DefaultFontResolver.FontSize, false, false,
                Html.Core.Utils.FontStretchResolver.Normal, 0, 0, TextTransform.None,
                LigatureSet.Default, CapsMode.None, NumeralSet.None, EastAsianSet.None,
                [], true);
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
                && text.IndexOf(":image", StringComparison.OrdinalIgnoreCase) < 0
                && text.IndexOf("feImage", StringComparison.OrdinalIgnoreCase) < 0)
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
                // A document reached from another (an <image> with a data: URI) is as untrusted as the first: no DTD, so no entity expansion.
                var settings = new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Prohibit, XmlResolver = null };
                using var reader = System.Xml.XmlReader.Create(new System.IO.StringReader(text), settings);
                var xdoc = System.Xml.Linq.XDocument.Load(reader);
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
                if (child.Name is "image" or "feImage")
                {
                    var href = child.GetAttribute("href") ?? child.GetAttribute("xlink:href");

                    // An feImage's "#id" names an element of this document, not something to fetch.
                    if (!string.IsNullOrEmpty(href) && !(child.Name == "feImage" && href[0] == '#'))
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
            // basis for rem. (When the root sets no font-size, this stays the UA default.) Language
            // inheritance seeds from the embedding context's own fallback (an inline <svg>'s surrounding
            // HTML document's resolved lang, if any) - root's own lang/xml:lang, resolved right below,
            // still wins over this when present.
            var rootFont = ComputeFontContext(root, FontContext.Default with { Language = root.DocumentLanguageFallback });
            _rootFontSize = rootFont.Size;
            _rootFont = rootFont;
            _rootBasis = new LengthBasis(this, rootFont);

            // The root's own width/height were parsed before its font was known (the definitions above need the
            // viewport); only a font-relative unit can differ now, so re-resolve just those against the root font.
            if (root.GetAttribute("width") is { } rootWidth && rootWidth.AsSpan().ContainsAny("emxchlcp"))
                _document.Width = SvgValueParsers.ParseLength(rootWidth, null, _rootBasis);
            if (root.GetAttribute("height") is { } rootHeight && rootHeight.AsSpan().ContainsAny("emxchlcp"))
                _document.Height = SvgValueParsers.ParseLength(rootHeight, null, _rootBasis);
            _viewportWidth = _document.ViewBox?.Width ?? _document.Width;
            _viewportHeight = _document.ViewBox?.Height ?? _document.Height;
            _rootViewportWidth = _viewportWidth;
            _rootViewportHeight = _viewportHeight;

            // The root <svg>'s own inherited presentation properties (e.g. <svg fill="#fff">, a common
            // icon idiom) seed inheritance for the whole tree, like its font-* above. The root has no
            // SvgElement of its own, so they are resolved onto a throwaway group purely for the returned
            // inherited values; its non-inherited properties (opacity/transform/clip-path/...) are unused.
            // Resolved before the definitions below: they inherit from the root through their ancestors.
            _lengthBasis = _rootBasis;
            _contextOnly = true;
            var rootPaint = _rootPaint = ApplyCommon(new SvgGroupElement(), root, InheritedPaint.Initial);
            _contextOnly = false;
            _lengthBasis = null;

            // Gradients/markers/patterns/masks/clipPaths are built only now, not while CollectDefinitions walks
            // the tree: their content inherits from the definition's own ancestors, which needs the root font
            // (rem), the root viewport and the complete id registry (a clip-path: url(#id) or fill: url(#id)
            // inside the content) - none of which exist during that walk. Every clipPath id is resolved here
            // unconditionally (not only when something in this subtree references it), so a document-wide
            // `<svg style="display: none">` used purely as a defs resource for an unrelated HTML element's own
            // clip-path: url(#id) (see SvgClipPathRegistry) still lands in SvgDocument.ClipPaths.
            BuildDeferredDefinitions();

            ResolveFeImageReferences(rootPaint, rootFont);

            foreach (var child in root.Children)
            {
                var element = BuildElement(child, rootPaint, rootFont);
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
                    // Recorded with their ancestor chain and built later (BuildDeferredDefinitions): their
                    // content inherits from that chain, whose values aren't computable during this walk.
                    case "linearGradient" or "radialGradient" or "marker" or "pattern" or "mask" or "clipPath"
                        when !string.IsNullOrEmpty(id):
                        var ancestors = _ancestorStack.ToArray();
                        _deferredDefinitions.Add(new DeferredDefinition(child, id, ancestors));
                        if (child.Name == "clipPath")
                            _clipPathAncestors[id] = ancestors;
                        break;
                    // Filters are self-contained (a primitive's `in`/`in2`/child `in` only reference
                    // earlier results within the SAME filter, never forward or cross-filter), and
                    // nothing in one inherits from its ancestors, so eager building fits here - no
                    // deferral needed. A filter whose graph isn't fully natively representable is
                    // simply never added (BuildFilter returns null - see SvgFilter's remarks), so a
                    // referencing element's FilterRef resolves to nothing and it paints unfiltered.
                    case "filter" when !string.IsNullOrEmpty(id):
                        if (BuildFilter(child) is { } filter)
                            _document.Filters[id] = filter;
                        break;
                    // <style> elements are no longer collected here: SVG styling is matched through the
                    // full CSS engine (ISvgSourceNode.GetMatchedCssDeclarations) against the relevant
                    // CssData - the host document's for inline <svg> (which already contains every nested
                    // and document-level <style>), or the SVG's own for standalone (built by the loader).
                }

                _ancestorStack.Add(child);
                CollectDefinitions(child);
                _ancestorStack.RemoveAt(_ancestorStack.Count - 1);
            }
        }

        /// <summary>Builds every definition <see cref="CollectDefinitions"/> deferred, in document order, each against its own inherited context.</summary>
        private void BuildDeferredDefinitions()
        {
            foreach (var (node, id, ancestors) in _deferredDefinitions)
            {
                switch (node.Name)
                {
                    case "linearGradient":
                        _document.Gradients[id] = InDefinitionContext(node, ancestors, (_, _) => BuildLinearGradient(node));
                        break;
                    case "radialGradient":
                        _document.Gradients[id] = InDefinitionContext(node, ancestors, (_, _) => BuildRadialGradient(node));
                        break;
                    case "marker":
                        _document.Markers[id] = InDefinitionContext(node, ancestors, (paint, font) => BuildMarker(node, paint, font));
                        break;
                    case "pattern":
                        _document.Patterns[id] = InDefinitionContext(node, ancestors, (paint, font) => BuildPattern(node, paint, font));
                        break;
                    case "mask":
                        _document.Masks[id] = InDefinitionContext(node, ancestors, (paint, font) => BuildMask(node, paint, font));
                        break;
                    case "clipPath":
                        ResolveClipPath(id);
                        break;
                }
            }
        }

        /// <summary>
        /// Runs <paramref name="build"/> with the builder positioned where <paramref name="node"/> sits in the
        /// document: the viewport its ancestors establish, and the length basis of its own font. The callback
        /// receives the paint/font it inherits from <paramref name="ancestors"/> (the element's own properties
        /// are applied by <see cref="EnterDefinition"/>). The builder's ambient state is restored afterwards -
        /// a clipPath can be resolved from inside another definition's build.
        /// </summary>
        private T InDefinitionContext<T>(ISvgSourceNode node, ISvgSourceNode[] ancestors, Func<InheritedPaint, FontContext, T> build)
        {
            var outerBasis = _lengthBasis;
            var outerWidth = _viewportWidth;
            var outerHeight = _viewportHeight;

            try
            {
                var (paint, font) = ResolveAncestorContext(ancestors);
                _lengthBasis = new LengthBasis(this, node, font);
                return build(paint, font);
            }
            finally
            {
                _lengthBasis = outerBasis;
                _viewportWidth = outerWidth;
                _viewportHeight = outerHeight;
            }
        }

        /// <summary>
        /// Replays <paramref name="ancestors"/> (root excluded) the way pass 2 walks down to an element: each
        /// one's presentation properties and font layer over its parent's, and a nested <c>&lt;svg&gt;</c>/
        /// <c>&lt;symbol&gt;</c> replaces the viewport percentage lengths resolve against. Leaves
        /// <see cref="_viewportWidth"/>/<see cref="_viewportHeight"/> set to the resulting viewport.
        /// </summary>
        private (InheritedPaint Paint, FontContext Font) ResolveAncestorContext(ISvgSourceNode[] ancestors)
        {
            // Sibling definitions share their ancestor nodes (the same instances, from CollectDefinitions' stack), and
            // computing one ancestor's context costs a full presentation-property cascade - so resume from the deepest
            // ancestor already computed rather than replaying from the root for every definition.
            var context = new AncestorContext(_rootPaint, _rootFont ?? FontContext.Default, _rootViewportWidth, _rootViewportHeight);
            var start = 0;

            for (var i = ancestors.Length; i > 0; i--)
            {
                if (_ancestorContexts.TryGetValue(ancestors[i - 1], out var cached))
                {
                    context = cached;
                    start = i;
                    break;
                }
            }

            var (paint, font) = (context.Paint, context.Font);
            _viewportWidth = context.ViewportWidth;
            _viewportHeight = context.ViewportHeight;

            for (var i = start; i < ancestors.Length; i++)
            {
                var ancestor = ancestors[i];

                // Same order BuildElement/BuildNestedSvg use: lengths resolve against the element's own font.
                _lengthBasis = new LengthBasis(this, ancestor, font);
                (paint, font) = EnterDefinition(ancestor, paint, font);
                EnterViewport(ancestor);

                _ancestorContexts[ancestor] = new AncestorContext(paint, font, _viewportWidth, _viewportHeight);
            }

            return (paint, font);
        }

        /// <summary>What an element's children inherit, and the viewport they sit in: one step of <see cref="ResolveAncestorContext"/>'s replay, kept so it isn't repeated.</summary>
        private readonly record struct AncestorContext(InheritedPaint Paint, FontContext Font, double? ViewportWidth, double? ViewportHeight);

        private readonly Dictionary<ISvgSourceNode, AncestorContext> _ancestorContexts = new(ReferenceEqualityComparer.Instance);

        /// <summary>The paint and font <paramref name="node"/>'s children inherit: its own properties layered over its parent's.</summary>
        private (InheritedPaint Paint, FontContext Font) EnterDefinition(ISvgSourceNode node, InheritedPaint parentPaint, FontContext parentFont)
        {
            var wasContextOnly = _contextOnly;
            _contextOnly = true;

            try
            {
                return (ApplyCommon(new SvgGroupElement(), node, parentPaint), ComputeFontContext(node, parentFont));
            }
            finally
            {
                _contextOnly = wasContextOnly;
            }
        }

        /// <summary>Applies the viewport change a nested <c>&lt;svg&gt;</c> (see <see cref="BuildNestedSvg"/>) or <c>&lt;symbol&gt;</c> (see <see cref="BuildSymbol"/>) makes for its content.</summary>
        private void EnterViewport(ISvgSourceNode node)
        {
            switch (node.Name)
            {
                case "svg":
                    var viewBox = SvgValueParsers.ParseViewBox(node.GetAttribute("viewBox"));
                    var width = SvgValueParsers.ParseLength(node.GetAttribute("width"), _viewportWidth, _lengthBasis) ?? _viewportWidth ?? 0;
                    var height = SvgValueParsers.ParseLength(node.GetAttribute("height"), _viewportHeight, _lengthBasis) ?? _viewportHeight ?? 0;
                    _viewportWidth = viewBox?.Width ?? width;
                    _viewportHeight = viewBox?.Height ?? height;
                    break;
                case "symbol":
                    var symbolViewBox = SvgValueParsers.ParseViewBox(node.GetAttribute("viewBox"));
                    _viewportWidth = symbolViewBox?.Width ?? _viewportWidth;
                    _viewportHeight = symbolViewBox?.Height ?? _viewportHeight;
                    break;
            }
        }

        /// <summary>
        /// Builds the renderable element for one node, or null if the node isn't directly paintable
        /// (definitions like &lt;defs&gt;/&lt;linearGradient&gt;/&lt;radialGradient&gt;/&lt;clipPath&gt;/
        /// &lt;stop&gt;, or any unrecognized element).
        /// </summary>
        private SvgElement? BuildElement(ISvgSourceNode node, InheritedPaint inherited, FontContext fontContext)
        {
            // Every length parsed while this element is built (its geometry, its stroke-width, ...) resolves its
            // font-relative units against THIS element's font; restored so a parent's later attributes see theirs.
            var outer = _lengthBasis;
            _lengthBasis = new LengthBasis(this, node, fontContext);
            try
            {
                return BuildElementCore(node, inherited, fontContext);
            }
            finally
            {
                _lengthBasis = outer;
            }
        }

        private SvgElement? BuildElementCore(ISvgSourceNode node, InheritedPaint inherited, FontContext fontContext)
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
                Cx = SvgValueParsers.ParseLength(node.GetAttribute("cx"), _viewportWidth, _lengthBasis) ?? 0,
                Cy = SvgValueParsers.ParseLength(node.GetAttribute("cy"), _viewportHeight, _lengthBasis) ?? 0,
                R = SvgValueParsers.ParseLength(node.GetAttribute("r"), ViewportDiagonal, _lengthBasis) ?? 0,
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
            var width = SvgValueParsers.ParseLength(node.GetAttribute("width"), _viewportWidth, _lengthBasis) ?? 0;
            var height = SvgValueParsers.ParseLength(node.GetAttribute("height"), _viewportHeight, _lengthBasis) ?? 0;

            // rx/ry each default to the other when only one is specified; both default to 0 (no
            // rounding) when neither is specified.
            double? rx = SvgValueParsers.ParseLength(node.GetAttribute("rx"), _viewportWidth, _lengthBasis);
            double? ry = SvgValueParsers.ParseLength(node.GetAttribute("ry"), _viewportHeight, _lengthBasis);
            rx ??= ry;
            ry ??= rx;

            var rect = new SvgRectElement
            {
                X = SvgValueParsers.ParseLength(node.GetAttribute("x"), _viewportWidth, _lengthBasis) ?? 0,
                Y = SvgValueParsers.ParseLength(node.GetAttribute("y"), _viewportHeight, _lengthBasis) ?? 0,
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
                Cx = SvgValueParsers.ParseLength(node.GetAttribute("cx"), _viewportWidth, _lengthBasis) ?? 0,
                Cy = SvgValueParsers.ParseLength(node.GetAttribute("cy"), _viewportHeight, _lengthBasis) ?? 0,
                Rx = SvgValueParsers.ParseLength(node.GetAttribute("rx"), _viewportWidth, _lengthBasis) ?? 0,
                Ry = SvgValueParsers.ParseLength(node.GetAttribute("ry"), _viewportHeight, _lengthBasis) ?? 0,
            };
            ApplyCommon(ellipse, node, inherited);
            return ellipse;
        }

        private SvgLineElement BuildLine(ISvgSourceNode node, InheritedPaint inherited)
        {
            var line = new SvgLineElement
            {
                X1 = SvgValueParsers.ParseLength(node.GetAttribute("x1"), _viewportWidth, _lengthBasis) ?? 0,
                Y1 = SvgValueParsers.ParseLength(node.GetAttribute("y1"), _viewportHeight, _lengthBasis) ?? 0,
                X2 = SvgValueParsers.ParseLength(node.GetAttribute("x2"), _viewportWidth, _lengthBasis) ?? 0,
                Y2 = SvgValueParsers.ParseLength(node.GetAttribute("y2"), _viewportHeight, _lengthBasis) ?? 0,
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
                X = SvgValueParsers.ParseLength(node.GetAttribute("x"), _viewportWidth, _lengthBasis) ?? 0,
                Y = SvgValueParsers.ParseLength(node.GetAttribute("y"), _viewportHeight, _lengthBasis) ?? 0,
                Width = SvgValueParsers.ParseLength(node.GetAttribute("width"), _viewportWidth, _lengthBasis),
                Height = SvgValueParsers.ParseLength(node.GetAttribute("height"), _viewportHeight, _lengthBasis),
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
                X = SvgValueParsers.ParseLength(node.GetAttribute("x"), _viewportWidth, _lengthBasis) ?? 0,
                Y = SvgValueParsers.ParseLength(node.GetAttribute("y"), _viewportHeight, _lengthBasis) ?? 0,
                Width = SvgValueParsers.ParseLength(node.GetAttribute("width"), _viewportWidth, _lengthBasis) ?? _viewportWidth ?? 0,
                Height = SvgValueParsers.ParseLength(node.GetAttribute("height"), _viewportHeight, _lengthBasis) ?? _viewportHeight ?? 0,
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
                X = SvgValueParsers.ParseLength(node.GetAttribute("x"), _viewportWidth, _lengthBasis) ?? 0,
                Y = SvgValueParsers.ParseLength(node.GetAttribute("y"), _viewportHeight, _lengthBasis) ?? 0,
                Width = SvgValueParsers.ParseLength(node.GetAttribute("width"), _viewportWidth, _lengthBasis) ?? 0,
                Height = SvgValueParsers.ParseLength(node.GetAttribute("height"), _viewportHeight, _lengthBasis) ?? 0,
                PreserveAspectRatio = SvgValueParsers.ParsePreserveAspectRatio(node.GetAttribute("preserveAspectRatio")),
            };
            ApplyCommon(image, node, inherited);

            ResolveImageHref(image, node.GetAttribute("href") ?? node.GetAttribute("xlink:href"));
            return image;
        }

        /// <summary>
        /// Resolves an <c>href</c> into <paramref name="image"/>'s raster or nested-document payload: a <c>data:</c> URI is decoded
        /// here, anything else is looked up in the images fetched ahead of the build. Shared by <c>&lt;image&gt;</c> and <c>feImage</c>.
        /// </summary>
        private void ResolveImageHref(SvgImageElement image, string? href)
        {
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
        /// <see cref="CollectDefinitions"/> is in place and the root context is known, i.e. any time
        /// during <see cref="BuildDeferredDefinitions"/> or pass 2. The shapes are built against the
        /// clipPath's own inherited context (its ancestors' font, so <c>em</c>/<c>rem</c> geometry resolves
        /// correctly); only their geometry is ever used for clipping.
        /// </summary>
        private void ResolveClipPath(string id)
        {
            if (_document.ClipPaths.ContainsKey(id))
                return;

            if (!_nodesById.TryGetValue(id, out var node) || node.Name != "clipPath")
                return;

            // A shape inside the clipPath that itself references this clipPath would otherwise recurse.
            if (!_resolvingClipPaths.Add(id))
                return;

            try
            {
                var ancestors = _clipPathAncestors.GetValueOrDefault(id) ?? [];

                _document.ClipPaths[id] = InDefinitionContext(node, ancestors, (parentPaint, parentFont) =>
                {
                    var clipPath = new SvgClipPath
                    {
                        Id = id,
                        ClipRule = SvgValueParsers.ParseFillRule(node.GetAttribute("clip-rule")),
                        // Default is userSpaceOnUse; objectBoundingBox maps 0..1 child geometry to the
                        // referencing element's bounding box (resolved at render time).
                        ClipPathUnitsUserSpaceOnUse =
                            !string.Equals(node.GetAttribute("clipPathUnits"), "objectBoundingBox", StringComparison.OrdinalIgnoreCase),
                    };

                    var (paint, font) = EnterDefinition(node, parentPaint, parentFont);
                    clipPath.Shapes.AddRange(BuildDefinitionChildren(node, paint, font));
                    return clipPath;
                });
            }
            finally
            {
                _resolvingClipPaths.Remove(id);
            }
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
            var ctx = new SvgPropertyContext(_adapter, _contextColor, ViewportDiagonal, ResolveUrlPaintKind, _lengthBasis);

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

                    if (!_contextOnly)
                        ResolveClipPath(clipId);
                }
            }

            // Same url(#id)/none grammar as a marker reference - reused directly rather than
            // duplicating the tiny parser.
            element.MaskRef = SvgValueParsers.ParseMarkerReference(Attr("mask"));

            // Hand-parsed here rather than through css-properties.json, same reasoning as mask/clip-path
            // above (CLAUDE.md's documented convention: URL extraction, not a pure grammar).
            element.FilterRef = SvgValueParsers.ParseMarkerReference(Attr("filter"));

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

            var declaredSize = ResolveFontSize(ResolveStyledAttr(node, "font-size"), inherited);
            var size = declaredSize ?? inherited.Size;

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

            var stretchAttr = ResolveStyledAttr(node, "font-stretch");
            var stretch = stretchAttr is null || stretchAttr.Trim().Equals("inherit", StringComparison.OrdinalIgnoreCase)
                ? inherited.Stretch
                : Html.Core.Utils.FontStretchResolver.Resolve(stretchAttr.Trim());

            // letter-spacing/word-spacing's em/ex resolve against THIS element's own font-size (the
            // just-computed `size` above), unlike font-size's own em/ex (which resolve against the
            // PARENT's) - see ResolveSpacingLength's own remarks.
            var ownFont = inherited with { Family = family, Size = size, Bold = bold, Italic = italic, Stretch = stretch, SizeDeclared = inherited.SizeDeclared || declaredSize is not null };
            var letterSpacing = ResolveSpacingLength(ResolveStyledAttr(node, "letter-spacing"), ownFont, inherited.LetterSpacing);
            var wordSpacing = ResolveSpacingLength(ResolveStyledAttr(node, "word-spacing"), ownFont, inherited.WordSpacing);

            var textTransformAttr = ResolveStyledAttr(node, "text-transform");
            var textTransform = textTransformAttr is null || textTransformAttr.Trim().Equals("inherit", StringComparison.OrdinalIgnoreCase)
                ? inherited.TextTransform
                : Map.TextTransforms.GetValueOrDefault(textTransformAttr.Trim(), inherited.TextTransform);

            // font-variant-*/font-kerning are plain CSS keyword grammars (unlike font-feature-settings
            // just below, whose quoted OpenType tag literal is genuinely case-sensitive per spec and must
            // NOT be folded) - lowercase before resolving so a presentation attribute's raw, unnormalized
            // case (e.g. font-kerning="NONE") matches the resolvers' keyword constants the same way a
            // CSS-cascade-tokenized value would for HTML.
            var ligaturesAttr = ResolveStyledAttr(node, "font-variant-ligatures");
            var ligatures = ligaturesAttr is null || ligaturesAttr.Trim().Equals("inherit", StringComparison.OrdinalIgnoreCase)
                ? inherited.Ligatures
                : TextShapingFeatureResolver.ResolveLigatures(ligaturesAttr.Trim().ToLowerInvariant());

            var capsAttr = ResolveStyledAttr(node, "font-variant-caps");
            var capsRequested = capsAttr is null || capsAttr.Trim().Equals("inherit", StringComparison.OrdinalIgnoreCase)
                ? inherited.CapsRequested
                : TextShapingFeatureResolver.ResolveCapsRequested(capsAttr.Trim().ToLowerInvariant());

            var positionAttr = ResolveStyledAttr(node, "font-variant-position");
            var positionRequested = positionAttr is null || positionAttr.Trim().Equals("inherit", StringComparison.OrdinalIgnoreCase)
                ? inherited.PositionRequested
                : TextShapingFeatureResolver.ResolvePositionRequested(positionAttr.Trim().ToLowerInvariant());

            var numericAttr = ResolveStyledAttr(node, "font-variant-numeric");
            var numeric = numericAttr is null || numericAttr.Trim().Equals("inherit", StringComparison.OrdinalIgnoreCase)
                ? inherited.Numeric
                : TextShapingFeatureResolver.ResolveNumeric(numericAttr.Trim().ToLowerInvariant());

            var eastAsianAttr = ResolveStyledAttr(node, "font-variant-east-asian");
            var eastAsian = eastAsianAttr is null || eastAsianAttr.Trim().Equals("inherit", StringComparison.OrdinalIgnoreCase)
                ? inherited.EastAsian
                : TextShapingFeatureResolver.ResolveEastAsian(eastAsianAttr.Trim().ToLowerInvariant());

            var featureSettingsAttr = ResolveStyledAttr(node, "font-feature-settings");
            var featureSettings = featureSettingsAttr is null || featureSettingsAttr.Trim().Equals("inherit", StringComparison.OrdinalIgnoreCase)
                ? inherited.FeatureSettings
                : TextShapingFeatureResolver.ResolveFeatureSettings(featureSettingsAttr.Trim());

            var kerningAttr = ResolveStyledAttr(node, "font-kerning");
            var kerning = kerningAttr is null || kerningAttr.Trim().Equals("inherit", StringComparison.OrdinalIgnoreCase)
                ? inherited.Kerning
                : TextShapingFeatureResolver.ResolveKerning(kerningAttr.Trim().ToLowerInvariant());

            // lang/xml:lang are plain XML/HTML attributes, not a CSS-styled property - read directly
            // (SVG2's own unprefixed lang first, falling back to the legacy xml:lang, same href/xlink:href
            // precedence tref/textPath already use), never through ResolveStyledAttr's style=""/matched-
            // rule tiers. Mirrors CssBox.Language's own "own value, else nearest ancestor's" resolution -
            // an empty lang="" falls through to the inherited value rather than resetting to "no language",
            // the same simplification CssBox.Language already makes.
            var langAttr = node.GetAttribute("lang") ?? node.GetAttribute("xml:lang");
            var language = string.IsNullOrEmpty(langAttr) ? inherited.Language : langAttr;

            return new FontContext(family, size, bold, italic, stretch, letterSpacing, wordSpacing, textTransform,
                ligatures, capsRequested, numeric, eastAsian, featureSettings, kerning, language,
                positionRequested, ownFont.SizeDeclared);
        }

        /// <summary>
        /// Resolves a <c>letter-spacing</c>/<c>word-spacing</c> value: <c>normal</c> is 0, relative
        /// units (<c>em</c>/<c>ex</c>/<c>rem</c>) resolve against <paramref name="ownFont"/>'s size (the
        /// current element's own resolved font-size - unlike <see cref="ResolveFontSize"/>'s em/ex,
        /// which resolve against the PARENT's, per each property's own CSS definition), absolute units
        /// defer to <see cref="SvgValueParsers.ParseLength"/>. The measured units (<c>ex</c>/<c>ch</c>/<c>cap</c>/
        /// <c>ic</c>/<c>lh</c> and the root-element variants) are read from <paramref name="ownFont"/>. Unset/
        /// <c>inherit</c>/unparseable all fall back to <paramref name="inheritedValue"/>.
        /// </summary>
        private double ResolveSpacingLength(string? value, FontContext ownFont, double inheritedValue)
        {
            var fontSize = ownFont.Size;
            if (string.IsNullOrWhiteSpace(value))
                return inheritedValue;

            var t = value.Trim();
            if (t.Equals("inherit", StringComparison.OrdinalIgnoreCase))
                return inheritedValue;
            if (t.Equals("normal", StringComparison.OrdinalIgnoreCase))
                return 0;

            static double? Number(string s) =>
                double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;

            if (t.EndsWith("rem", StringComparison.OrdinalIgnoreCase))
                return Number(t[..^3]) is { } r ? r * _rootFontSize : inheritedValue;
            if (t.EndsWith("em", StringComparison.OrdinalIgnoreCase))
                return Number(t[..^2]) is { } e ? e * fontSize : inheritedValue;
            if (SvgValueParsers.TryParseMeasuredUnit(t, out var measured, out var measuredUnit))
                return SvgValueParsers.ResolveMeasuredUnit(measured, measuredUnit, new LengthBasis(this, ownFont));

            return SvgValueParsers.ParseLength(t, null, new LengthBasis(this, ownFont)) ?? inheritedValue;
        }

        /// <summary>
        /// The font a length is resolved against (<see cref="ISvgLengthBasis"/>): a <see cref="FontContext"/> - the
        /// element's own, computed on first use when built from a node - realized as an <see cref="RFont"/> only when a
        /// measured unit actually asks for a measurement, then cached per metric. 1em is the context's size once any
        /// element on the way down declared a <c>font-size</c>, else the CSS initial 16px.
        /// </summary>
        private sealed class LengthBasis : ISvgLengthBasis
        {
            private readonly SvgTreeBuilder _builder;
            private readonly ISvgSourceNode? _node;
            private readonly FontContext _inherited;
            private FontContext? _font;
            private double?[]? _ratios;

            /// <summary>A basis for <paramref name="node"/>'s own font, layered over <paramref name="inherited"/>.</summary>
            public LengthBasis(SvgTreeBuilder builder, ISvgSourceNode node, FontContext inherited)
            {
                _builder = builder;
                _node = node;
                _inherited = inherited;
            }

            /// <summary>A basis for an already-resolved font.</summary>
            public LengthBasis(SvgTreeBuilder builder, FontContext font)
            {
                _builder = builder;
                _inherited = font;
                _font = font;
            }

            private FontContext Font => _font ??= _builder.ComputeFontContext(_node!, _inherited);

            public double EmPx => Font.SizeDeclared ? Font.Size : SvgValueParsers.DefaultEmPx;

            public double RootEmPx => _builder._rootFont is { SizeDeclared: true } root ? root.Size : SvgValueParsers.DefaultEmPx;

            public double GetRatio(FontMetric metric, bool rootElement)
            {
                // Null while the root's own font is being computed, which is what keeps that from recursing.
                if (rootElement)
                    return _builder._rootBasis?.GetRatio(metric, false) ?? FontMetricRatios.Approximate(metric);

                _ratios ??= new double?[5];
                if (_ratios[(int)metric] is { } cached) return cached;

                var font = _builder.RealizeFont(Font);
                var pixelsPerPoint = (_builder._adapter as PeachPDF.Adapters.PdfSharpAdapter)?.PixelsPerPoint ?? 1.0;
                return (_ratios[(int)metric] = FontMetricMeasurement.Ratio(font, metric, pixelsPerPoint)).Value;
            }
        }

        /// <summary>Realizes <paramref name="font"/> the way a text run does, so a measurement is taken from the very face the run would use.</summary>
        private RFont? RealizeFont(FontContext font)
        {
            var fontStyle = RFontStyle.Regular;
            if (font.Bold) fontStyle |= RFontStyle.Bold;
            if (font.Italic) fontStyle |= RFontStyle.Italic;

            var size = Math.Max(font.Size, 1);
            return _adapter.GetFont(font.Family, size, fontStyle, stretch: font.Stretch)
                   ?? _adapter.GetFont(Html.Core.Utils.DefaultFontResolver.DefaultFont, size, fontStyle, stretch: font.Stretch);
        }

        /// <summary>
        /// Resolves a <c>font-size</c> value in the same unit domain the rest of the text pipeline uses.
        /// Relative units resolve against the real inherited size: <c>em</c>/<c>%</c> against
        /// <paramref name="parent"/>'s size (the parent's used font-size), the measured units (<c>ex</c>/<c>ch</c>/...) from its font,, <c>rem</c> against the root's
        /// (<see cref="_rootFontSize"/>). Absolute units (<c>pt</c>/<c>px</c>/<c>pc</c>/<c>in</c>/<c>cm</c>/
        /// <c>mm</c>), a unitless number, and <c>calc()</c> defer to <see cref="SvgValueParsers.ParseLength"/>.
        /// Returns null for a missing/unparseable value (caller falls back to the inherited size).
        /// </summary>
        private double? ResolveFontSize(string? value, FontContext parent)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            var parentSize = parent.Size;

            var t = value.Trim();

            if (t.Equals("inherit", StringComparison.OrdinalIgnoreCase))
                return parentSize;

            static double? Number(string s) =>
                double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;

            if (t.EndsWith("rem", StringComparison.OrdinalIgnoreCase))
                return Number(t[..^3]) is { } r ? r * _rootFontSize : null;
            if (t.EndsWith("em", StringComparison.OrdinalIgnoreCase))
                return Number(t[..^2]) is { } e ? e * parentSize : null;
            // ex/ch/cap/ic/lh (and the root-element variants): measured from the PARENT's font, since this element's
            // own is what is being computed (CSS Values 4 §6.1.1); em is the parent's size, rem the root's.
            if (SvgValueParsers.TryParseMeasuredUnit(t, out var measured, out var measuredUnit))
                return SvgValueParsers.ResolveMeasuredUnit(measured, measuredUnit,
                    new LengthBasis(this, parent with { SizeDeclared = true }));
            if (t.EndsWith('%'))
                return Number(t[..^1]) is { } p ? p / 100.0 * parentSize : null;

            // Absolute units / unitless / calc(). A '%' inside a calc resolves against the parent size.
            return SvgValueParsers.ParseLength(t, parentSize, new LengthBasis(this, parent with { SizeDeclared = true }));
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
            // BuildElement scopes a top-level <text>; a nested run (<tspan>/<tref>/<textPath>) reaches here by
            // recursion instead, and its x/y/dx/dy and stroke lengths are relative to its own font-size.
            var outer = _lengthBasis;
            _lengthBasis = new LengthBasis(this, node, fontContext);
            try
            {
                return BuildTextRunCore(node, inherited, fontContext, state);
            }
            finally
            {
                _lengthBasis = outer;
            }
        }

        private SvgTextElement BuildTextRunCore(ISvgSourceNode node, InheritedPaint inherited, FontContext fontContext, TextWhitespaceState state)
        {
            var xAttr = node.GetAttribute("x");
            var yAttr = node.GetAttribute("y");
            var xList = SvgValueParsers.ParseLengthList(xAttr, _viewportWidth, _lengthBasis);
            var yList = SvgValueParsers.ParseLengthList(yAttr, _viewportHeight, _lengthBasis);
            var dxList = SvgValueParsers.ParseLengthList(node.GetAttribute("dx"), _viewportWidth, _lengthBasis);
            var dyList = SvgValueParsers.ParseLengthList(node.GetAttribute("dy"), _viewportHeight, _lengthBasis);
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

            run.Font = _adapter.GetFont(runFont.Family, runFont.Size, fontStyle, stretch: runFont.Stretch)
                       ?? _adapter.GetFont(Html.Core.Utils.DefaultFontResolver.DefaultFont, runFont.Size, fontStyle, stretch: runFont.Stretch);

            // font-variant-caps is gated by the resolved font's own GSUB support (same rule
            // DerivedStyle.ActualFontVariantCaps applies for HTML) - real substitution only, no
            // small-caps synthesis fallback for SVG (a smaller, deliberately scoped gap; see
            // .claude/accepted-gaps/no-text-shaping.md).
            var resolvedCaps = runFont.CapsRequested != CapsMode.None && run.Font is { } font && font.SupportsFontVariantCaps(runFont.CapsRequested)
                ? runFont.CapsRequested
                : CapsMode.None;

            // font-variant-position is gated the same way, and for the same reason has no synthesis
            // fallback here: HTML synthesizes a sub/superscript by splitting the run onto a smaller font
            // with a shifted baseline (CssBox.AddWord), machinery SVG text runs don't share.
            var resolvedPosition = runFont.PositionRequested != SubSuperMode.None && run.Font is { } positionFont && positionFont.SupportsFontVariantPosition(runFont.PositionRequested)
                ? runFont.PositionRequested
                : SubSuperMode.None;

            run.LetterSpacing = runFont.LetterSpacing;
            run.WordSpacing = runFont.WordSpacing;
            run.ShapingFeatures = new ShapeSettings(
                runFont.Ligatures, resolvedCaps, runFont.Numeric, runFont.EastAsian,
                TextShapingFeatureResolver.ToFeatureSettings(runFont.FeatureSettings), Kerning: runFont.Kerning, Language: runFont.Language,
                Position: resolvedPosition);

            // text-decoration is this run's own value only - CSS Text Decoration 3 §2 explicitly makes
            // it non-inherited (a descendant's decoration "flows across" via painting every glyph whose
            // ancestor chain requested one, not via the property inheriting).
            run.TextDecorationLine = ResolveStyledAttr(node, "text-decoration-line")?.Trim().ToLowerInvariant() ?? "none";
            run.TextDecorationStyle = ResolveStyledAttr(node, "text-decoration-style")?.Trim().ToLowerInvariant() ?? "solid";
            var decorationColorAttr = ResolveStyledAttr(node, "text-decoration-color")?.Trim();
            run.TextDecorationColor = !string.IsNullOrEmpty(decorationColorAttr) && !decorationColorAttr.Equals("currentColor", StringComparison.OrdinalIgnoreCase)
                ? new CssValueParser(_adapter).GetActualColor(decorationColorAttr)
                : null;

            var childFontContext = runFont;

            // Walk this run's loose text and child elements in document order, so text authored after a
            // child element keeps its place and whitespace collapses across the boundaries (SVG §10.15).
            foreach (var content in node.ContentNodes)
            {
                if (content.IsText)
                {
                    var transformed = ApplyTextTransform(content.Text ?? "", runFont.TextTransform, state);
                    var text = state.Collapse(transformed);
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
                        // A <tref> is required to be empty by the SVG content model, but nothing here
                        // rejects a document that violates that - so build defensively: snapshot/restore
                        // the shared whitespace-collapse/capitalize state around this call, since its
                        // return value (trefRun.Content) is unconditionally discarded below and any state
                        // the walk advanced along the way must not leak into the text that follows.
                        var stateSnapshot = state.Snapshot();
                        var trefRun = BuildTextRun(child, resolved, childFontContext, state);
                        state.Restore(stateSnapshot);
                        var href = child.GetAttribute("href") ?? child.GetAttribute("xlink:href");
                        var id = href?.TrimStart('#');
                        if (!string.IsNullOrEmpty(id) && _nodesById.TryGetValue(id, out var target))
                        {
                            // A tref's own text is the referenced element's text, collapsed as part of the
                            // same subtree stream (replacing whatever the tref element itself contained) -
                            // text-transform still applies using the <tref>'s own resolved font context.
                            trefRun.Content.Clear();
                            var trefFont = ComputeFontContext(child, childFontContext);
                            var transformed = ApplyTextTransform(target.GetTextContent(), trefFont.TextTransform, state);
                            var text = state.Collapse(transformed);
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
        /// Applies <paramref name="transform"/> to <paramref name="raw"/> before whitespace collapsing
        /// (<see cref="TextWhitespaceState.Collapse"/> runs on the result) - <c>capitalize</c> threads
        /// its word-start tracking through <paramref name="state"/> so a word split across a
        /// <c>&lt;tspan&gt;</c>/<c>&lt;tref&gt;</c> boundary still capitalizes correctly, even when the
        /// two runs have different <c>text-transform</c> values (the shared state reflects real
        /// whitespace seen so far across the whole subtree, independent of which run is currently
        /// applying <c>capitalize</c>).
        /// </summary>
        private static string ApplyTextTransform(string raw, TextTransform transform, TextWhitespaceState state) =>
            transform == TextTransform.Capitalize
                ? TextTransformer.ApplyCapitalize(raw, ref state.CapitalizeAtWordStart)
                : TextTransformer.Apply(raw, transform);

        /// <summary>
        /// Parses a <c>&lt;textPath&gt;</c> <c>startOffset</c>: a length (user units) or a percentage of
        /// the path's total length. A percentage is stored as its 0..1 fraction plus the percent flag,
        /// so the render side can resolve it against the (build-time-unknown) total path length.
        /// </summary>
        private (double Offset, bool IsPercent) ParseStartOffset(string? raw)
        {
            raw = raw?.Trim();
            if (string.IsNullOrEmpty(raw))
                return (0, false);

            if (raw.EndsWith('%'))
                return ((SvgValueParsers.ParseLength(raw[..^1], null, _lengthBasis) ?? 0) / 100.0, true);

            return (SvgValueParsers.ParseLength(raw, null, _lengthBasis) ?? 0, false);
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

            /// <summary>Cross-run <c>text-transform: capitalize</c> word-start tracking (see
            /// <see cref="ApplyTextTransform"/>) - shared across the whole subtree the same way
            /// whitespace-collapsing state is, independent of <see cref="_atStart"/>/<see cref="_pendingSpace"/>
            /// since it must reflect real whitespace seen so far even for a run that doesn't itself use
            /// <c>capitalize</c>.</summary>
            public bool CapitalizeAtWordStart = true;

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

            /// <summary>Captures the mutable collapse/capitalize state so a discarded speculative walk
            /// (see the <c>&lt;tref&gt;</c> case in <see cref="BuildTextRun"/>, where any content nodes the
            /// tref element itself carries are invalid per SVG's content model and thrown away) can be
            /// undone rather than leaking into whatever text follows in the same subtree.</summary>
            public (bool AtStart, bool PendingSpace, bool CapitalizeAtWordStart) Snapshot() =>
                (_atStart, _pendingSpace, CapitalizeAtWordStart);

            public void Restore((bool AtStart, bool PendingSpace, bool CapitalizeAtWordStart) snapshot)
            {
                _atStart = snapshot.AtStart;
                _pendingSpace = snapshot.PendingSpace;
                CapitalizeAtWordStart = snapshot.CapitalizeAtWordStart;
            }
        }

        private SvgMarkerElement BuildMarker(ISvgSourceNode node, InheritedPaint parentPaint, FontContext parentFont)
        {
            var orient = node.GetAttribute("orient");

            var marker = new SvgMarkerElement
            {
                RefX = SvgValueParsers.ParseLength(node.GetAttribute("refX"), null, _lengthBasis) ?? 0,
                RefY = SvgValueParsers.ParseLength(node.GetAttribute("refY"), null, _lengthBasis) ?? 0,
                MarkerWidth = SvgValueParsers.ParseLength(node.GetAttribute("markerWidth"), null, _lengthBasis) ?? 3,
                MarkerHeight = SvgValueParsers.ParseLength(node.GetAttribute("markerHeight"), null, _lengthBasis) ?? 3,
                ViewBox = SvgValueParsers.ParseViewBox(node.GetAttribute("viewBox")),
                PreserveAspectRatio = SvgValueParsers.ParsePreserveAspectRatio(node.GetAttribute("preserveAspectRatio")),
                MarkerUnitsStrokeWidth = !string.Equals(node.GetAttribute("markerUnits"), "userSpaceOnUse", StringComparison.OrdinalIgnoreCase),
                OrientAuto = string.Equals(orient, "auto", StringComparison.OrdinalIgnoreCase),
                OrientAutoStartReverse = string.Equals(orient, "auto-start-reverse", StringComparison.OrdinalIgnoreCase),
                OrientAngle = SvgValueParsers.ParseLength(orient) ?? 0,
            };

            var (paint, font) = EnterDefinition(node, parentPaint, parentFont);

            // A shape inside the marker that inherits `marker-end: url(#thisMarker)` from an ancestor would draw the
            // marker inside itself, without end - so drop just the inherited references to this marker. A reference
            // to another marker is ordinary inheritance; a cycle through two markers is cut by the renderer's nesting cap.
            var markerId = node.GetAttribute("id");
            paint = paint with
            {
                MarkerStartRef = paint.MarkerStartRef == markerId ? null : paint.MarkerStartRef,
                MarkerMidRef = paint.MarkerMidRef == markerId ? null : paint.MarkerMidRef,
                MarkerEndRef = paint.MarkerEndRef == markerId ? null : paint.MarkerEndRef,
            };
            marker.Children.AddRange(BuildDefinitionChildren(node, paint, font));

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
            paint.Kind == SvgPaintKind.GradientRef && paint.ReferenceId is { } id && _nodesById.TryGetValue(id, out var target) && target.Name == "pattern"
                ? SvgPaint.PatternRef(id)
                : paint;

        private SvgMask BuildMask(ISvgSourceNode node, InheritedPaint parentPaint, FontContext parentFont)
        {
            var isObjectBoundingBox = !string.Equals(node.GetAttribute("maskUnits"), "userSpaceOnUse", StringComparison.OrdinalIgnoreCase);
            var contentUnitsUserSpaceOnUse = !string.Equals(node.GetAttribute("maskContentUnits"), "objectBoundingBox", StringComparison.OrdinalIgnoreCase);

            var (paint, font) = EnterDefinition(node, parentPaint, parentFont);

            var defaultMask = new SvgMask();
            var mask = new SvgMask
            {
                Id = node.GetAttribute("id"),
                X = SvgValueParsers.ParseGradientCoordinate(node.GetAttribute("x"), isObjectBoundingBox, _viewportWidth, _lengthBasis) ?? defaultMask.X,
                Y = SvgValueParsers.ParseGradientCoordinate(node.GetAttribute("y"), isObjectBoundingBox, _viewportHeight, _lengthBasis) ?? defaultMask.Y,
                Width = SvgValueParsers.ParseGradientCoordinate(node.GetAttribute("width"), isObjectBoundingBox, _viewportWidth, _lengthBasis) ?? defaultMask.Width,
                Height = SvgValueParsers.ParseGradientCoordinate(node.GetAttribute("height"), isObjectBoundingBox, _viewportHeight, _lengthBasis) ?? defaultMask.Height,
                MaskUnitsUserSpaceOnUse = !isObjectBoundingBox,
                MaskContentUnitsUserSpaceOnUse = contentUnitsUserSpaceOnUse,
                Children = BuildDefinitionChildren(node, paint, font),
            };

            return mask;
        }

        private SvgPattern BuildPattern(ISvgSourceNode node, InheritedPaint parentPaint, FontContext parentFont)
        {
            var isObjectBoundingBox = !string.Equals(node.GetAttribute("patternUnits"), "userSpaceOnUse", StringComparison.OrdinalIgnoreCase);
            var contentUnitsUserSpaceOnUse = !string.Equals(node.GetAttribute("patternContentUnits"), "objectBoundingBox", StringComparison.OrdinalIgnoreCase);

            var (paint, font) = EnterDefinition(node, parentPaint, parentFont);

            var id = node.GetAttribute("id");

            // Inheriting `fill="url(#thisPattern)"` from an ancestor into the pattern's own content would make
            // the pattern paint itself without end (browsers treat the cyclic reference as none).
            if (paint.Fill.Kind == SvgPaintKind.PatternRef && paint.Fill.ReferenceId == id)
                paint = paint with { Fill = SvgPaint.None };
            if (paint.Stroke.Kind == SvgPaintKind.PatternRef && paint.Stroke.ReferenceId == id)
                paint = paint with { Stroke = SvgPaint.None };

            var pattern = new SvgPattern
            {
                Id = id,
                X = SvgValueParsers.ParseGradientCoordinate(node.GetAttribute("x"), isObjectBoundingBox, _viewportWidth, _lengthBasis) ?? 0,
                Y = SvgValueParsers.ParseGradientCoordinate(node.GetAttribute("y"), isObjectBoundingBox, _viewportHeight, _lengthBasis) ?? 0,
                Width = SvgValueParsers.ParseGradientCoordinate(node.GetAttribute("width"), isObjectBoundingBox, _viewportWidth, _lengthBasis) ?? 0,
                Height = SvgValueParsers.ParseGradientCoordinate(node.GetAttribute("height"), isObjectBoundingBox, _viewportHeight, _lengthBasis) ?? 0,
                PatternUnitsUserSpaceOnUse = !isObjectBoundingBox,
                PatternContentUnitsUserSpaceOnUse = contentUnitsUserSpaceOnUse,
                PatternTransform = SvgTransformParser.Parse(node.GetAttribute("patternTransform")),
                ViewBox = SvgValueParsers.ParseViewBox(node.GetAttribute("viewBox")),
                PreserveAspectRatio = SvgValueParsers.ParsePreserveAspectRatio(node.GetAttribute("preserveAspectRatio")),
                Children = BuildDefinitionChildren(node, paint, font),
            };

            return pattern;
        }

        /// <summary>
        /// Builds a <c>&lt;filter&gt;</c> definition, or null when ANY of its primitives (or the values/
        /// operators/types they use) falls outside the supported set - a whole-filter rejection, not a
        /// partial graph, per <see cref="SvgFilter"/>'s remarks: an element referencing a rejected (or
        /// absent) filter id simply paints unfiltered. Rejected: an unknown primitive element; an
        /// feComposite operator, feColorMatrix type, feComponentTransfer type or numeric attribute that
        /// does not parse. Every input keyword (SourceGraphic/SourceAlpha/FillPaint/StrokePaint/
        /// BackgroundImage/BackgroundAlpha), <c>feImage</c> and a primitive subregion are supported, but the
        /// ones PDF has no operator for make <see cref="SvgFilter.RequiresRaster"/> true.
        /// </summary>
        private SvgFilter? BuildFilter(ISvgSourceNode node)
        {
            var isObjectBoundingBox = !string.Equals(node.GetAttribute("filterUnits"), "userSpaceOnUse", StringComparison.OrdinalIgnoreCase);
            var primitiveUnitsUserSpaceOnUse = !string.Equals(node.GetAttribute("primitiveUnits"), "objectBoundingBox", StringComparison.OrdinalIgnoreCase);

            var primitives = BuildFilterPrimitives(node, primitiveUnitsUserSpaceOnUse);
            if (primitives is null)
                return null;

            var defaultFilter = new SvgFilter();
            return new SvgFilter
            {
                Id = node.GetAttribute("id"),
                X = SvgValueParsers.ParseGradientCoordinate(node.GetAttribute("x"), isObjectBoundingBox, _viewportWidth) ?? defaultFilter.X,
                Y = SvgValueParsers.ParseGradientCoordinate(node.GetAttribute("y"), isObjectBoundingBox, _viewportHeight) ?? defaultFilter.Y,
                Width = SvgValueParsers.ParseGradientCoordinate(node.GetAttribute("width"), isObjectBoundingBox, _viewportWidth) ?? defaultFilter.Width,
                Height = SvgValueParsers.ParseGradientCoordinate(node.GetAttribute("height"), isObjectBoundingBox, _viewportHeight) ?? defaultFilter.Height,
                FilterUnitsUserSpaceOnUse = !isObjectBoundingBox,
                PrimitiveUnitsUserSpaceOnUse = primitiveUnitsUserSpaceOnUse,
                Primitives = primitives,
            };
        }

        /// <summary>Walks a <c>&lt;filter&gt;</c>'s direct children into a primitive list, or null on the first unsupported one (see <see cref="BuildFilter"/>'s remarks). A non-<c>fe*</c> child (<c>&lt;title&gt;</c>/<c>&lt;desc&gt;</c>/etc.) is skipped, not a rejection.</summary>
        private List<FilterPrimitive>? BuildFilterPrimitives(ISvgSourceNode node, bool primitiveUnitsUserSpaceOnUse)
        {
            var primitives = new List<FilterPrimitive>();
            var filterLinear = ParseColorInterpolationFilters(node.GetAttribute("color-interpolation-filters"), true);

            foreach (var child in node.Children)
            {
                if (!child.Name.StartsWith("fe", StringComparison.Ordinal))
                    continue;

                if (BuildFilterPrimitive(child) is not { } primitive)
                    return null;

                primitive.LinearRgb = ParseColorInterpolationFilters(child.GetAttribute("color-interpolation-filters"), filterLinear);
                primitive.ReadsReservedInput = ReadsReservedInput(child, out var readsBackdrop);
                if (readsBackdrop)
                    _document.ReadsBackdrop = true;

                primitive.Subregion = ParseSubregion(child, primitiveUnitsUserSpaceOnUse);
                primitives.Add(primitive);
            }

            return primitives;
        }

        private static bool ParseColorInterpolationFilters(string? value, bool inherited) => value?.Trim() switch
        {
            "linearRGB" => true,
            "sRGB" or "auto" => false,
            _ => inherited,
        };

        private FilterSubregion? ParseSubregion(ISvgSourceNode node, bool primitiveUnitsUserSpaceOnUse)
        {
            if (node.GetAttribute("x") is null && node.GetAttribute("y") is null &&
                node.GetAttribute("width") is null && node.GetAttribute("height") is null)
            {
                return null;
            }

            var objectBoundingBox = !primitiveUnitsUserSpaceOnUse;
            return new FilterSubregion(
                SvgValueParsers.ParseGradientCoordinate(node.GetAttribute("x"), objectBoundingBox, _viewportWidth),
                SvgValueParsers.ParseGradientCoordinate(node.GetAttribute("y"), objectBoundingBox, _viewportHeight),
                SvgValueParsers.ParseGradientCoordinate(node.GetAttribute("width"), objectBoundingBox, _viewportWidth),
                SvgValueParsers.ParseGradientCoordinate(node.GetAttribute("height"), objectBoundingBox, _viewportHeight));
        }

        private static bool IsReservedInput(string? value) =>
            value is "BackgroundImage" or "BackgroundAlpha" or "FillPaint" or "StrokePaint";

        private static bool IsBackdropInput(string? value) => value is "BackgroundImage" or "BackgroundAlpha";

        /// <summary>Whether a primitive names <c>FillPaint</c>/<c>StrokePaint</c>/<c>BackgroundImage</c>/<c>BackgroundAlpha</c> as an input, and whether one of them is a backdrop.</summary>
        private static bool ReadsReservedInput(ISvgSourceNode primitive, out bool readsBackdrop)
        {
            var reads = false;
            var backdrop = false;

            void Note(string? value)
            {
                reads |= IsReservedInput(value);
                backdrop |= IsBackdropInput(value);
            }

            Note(primitive.GetAttribute("in"));
            Note(primitive.GetAttribute("in2"));

            if (primitive.Name == "feMerge")
            {
                foreach (var child in primitive.Children)
                {
                    if (child.Name == "feMergeNode")
                        Note(child.GetAttribute("in"));
                }
            }

            readsBackdrop = backdrop;
            return reads;
        }

        private FilterPrimitive? BuildFilterPrimitive(ISvgSourceNode node) => node.Name switch
        {
            "feFlood" => BuildFeFlood(node),
            "feOffset" => BuildFeOffset(node),
            "feMerge" => BuildFeMerge(node),
            "feTile" => BuildFeTile(node),
            "feComposite" => BuildFeComposite(node),
            "feBlend" => BuildFeBlend(node),
            "feColorMatrix" => BuildFeColorMatrix(node),
            "feComponentTransfer" => BuildFeComponentTransfer(node),
            "feGaussianBlur" => BuildFeGaussianBlur(node),
            "feDropShadow" => BuildFeDropShadow(node),
            "feMorphology" => BuildFeMorphology(node),
            "feConvolveMatrix" => BuildFeConvolveMatrix(node),
            "feTurbulence" => BuildFeTurbulence(node),
            "feDisplacementMap" => BuildFeDisplacementMap(node),
            "feDiffuseLighting" => BuildFeLighting(node, specular: false),
            "feSpecularLighting" => BuildFeLighting(node, specular: true),
            "feImage" => BuildFeImage(node),
            _ => null, // anything unknown - unsupported
        };

        /// <summary>
        /// Builds an <c>feImage</c>. A <c>#id</c> href is only recorded here: filters are built while the id registry is still being
        /// collected, so the referenced element is built afterwards by <see cref="ResolveFeImageReferences"/>. Any other href is an image
        /// resolved like an <c>&lt;image&gt;</c>'s (an unresolvable one renders nothing, which leaves the primitive transparent).
        /// </summary>
        private FilterPrimitive BuildFeImage(ISvgSourceNode node)
        {
            var href = node.GetAttribute("href") ?? node.GetAttribute("xlink:href");
            var result = node.GetAttribute("result");

            if (href is { Length: > 1 } && href[0] == '#')
            {
                var feImage = new FeImage { Result = result, ReferenceId = href[1..] };
                _feImageReferences.Add(feImage);
                return feImage;
            }

            var image = new SvgImageElement { PreserveAspectRatio = SvgValueParsers.ParsePreserveAspectRatio(node.GetAttribute("preserveAspectRatio")) };
            ResolveImageHref(image, href);
            return new FeImage { Result = result, Image = image };
        }

        /// <summary>Builds the element each <c>feImage href="#id"</c> names, now that every node of the document is registered.</summary>
        private void ResolveFeImageReferences(InheritedPaint inherited, FontContext fontContext)
        {
            if (_feImageReferences.Count == 0)
                return;

            foreach (var feImage in _feImageReferences)
            {
                if (feImage.ReferenceId is not { } id || !_nodesById.TryGetValue(id, out var target) || _useDepth >= MaxUseDepth)
                    continue;

                _useDepth++;
                feImage.Target = target.Name == "symbol"
                    ? BuildSymbol(target, inherited, fontContext)
                    : BuildElement(target, inherited, fontContext);
                _useDepth--;
            }

            _feImageReferences.Clear();
        }

        private FilterPrimitive? BuildFeFlood(ISvgSourceNode node)
        {
            var inAttr = node.GetAttribute("in");
            var floodColorAttr = node.GetAttribute("flood-color");
            var color = string.IsNullOrWhiteSpace(floodColorAttr)
                ? RColor.Black
                : floodColorAttr.Trim().Equals("currentColor", StringComparison.OrdinalIgnoreCase)
                    ? _contextColor
                    : new CssValueParser(_adapter).GetActualColor(floodColorAttr);

            return new FeFlood
            {
                In = inAttr,
                Result = node.GetAttribute("result"),
                Color = color,
                Opacity = SvgValueParsers.ParseOpacity(node.GetAttribute("flood-opacity")),
            };
        }

        private FilterPrimitive? BuildFeOffset(ISvgSourceNode node)
        {
            var inAttr = node.GetAttribute("in");
            return new FeOffset
            {
                In = inAttr,
                Result = node.GetAttribute("result"),
                Dx = ParseFilterNumber(node.GetAttribute("dx")),
                Dy = ParseFilterNumber(node.GetAttribute("dy")),
            };
        }

        private FilterPrimitive? BuildFeMerge(ISvgSourceNode node)
        {
            var inputs = new List<string?>();

            foreach (var child in node.Children)
            {
                if (child.Name != "feMergeNode")
                    continue;

                var inAttr = child.GetAttribute("in");
                inputs.Add(inAttr);
            }

            return new FeMerge { Result = node.GetAttribute("result"), Inputs = inputs };
        }

        private FilterPrimitive? BuildFeTile(ISvgSourceNode node)
        {
            var inAttr = node.GetAttribute("in");
            return new FeTile { In = inAttr, Result = node.GetAttribute("result") };
        }

        private FilterPrimitive? BuildFeComposite(ISvgSourceNode node)
        {
            var inAttr = node.GetAttribute("in");
            var in2Attr = node.GetAttribute("in2");
            // "arithmetic" (k1*i1*i2 + k2*i1 + k3*i2 + k4) needs per-pixel computation, so it makes the whole filter a raster
            // one. Any other/unrecognized operator value is rejected rather than silently falling back to "over" (unlike
            // feBlend's mode, which does default leniently - there is no safe default here since the author's INTENDED
            // operator is unknown).
            var op = (node.GetAttribute("operator") ?? "over").Trim().ToLowerInvariant();
            if (op is not ("over" or "in" or "out" or "atop" or "xor" or "arithmetic"))
                return null;

            return new FeComposite
            {
                In = inAttr,
                In2 = in2Attr,
                Result = node.GetAttribute("result"),
                Operator = op,
                K1 = ParseFilterNumber(node.GetAttribute("k1")),
                K2 = ParseFilterNumber(node.GetAttribute("k2")),
                K3 = ParseFilterNumber(node.GetAttribute("k3")),
                K4 = ParseFilterNumber(node.GetAttribute("k4")),
            };
        }

        private FilterPrimitive? BuildFeBlend(ISvgSourceNode node)
        {
            var inAttr = node.GetAttribute("in");
            var in2Attr = node.GetAttribute("in2");
            return new FeBlend
            {
                In = inAttr,
                In2 = in2Attr,
                Result = node.GetAttribute("result"),
                Mode = ParseFeBlendMode(node.GetAttribute("mode")),
            };
        }

        /// <summary>
        /// <c>mode</c>'s keyword vocabulary is exactly <see cref="RBlendMode"/>'s own member set
        /// (separable + non-separable PDF 32000-1 §11.3.5 modes), so this maps 1:1 rather than through
        /// an intermediate enum - unlike CSS <c>mix-blend-mode</c> (<c>FragmentPainter</c>'s own mapping
        /// switch), which goes through the HTML-side <c>BlendMode</c> enum for CSS-OM reasons that don't
        /// apply to this hand-parsed SVG attribute. An unrecognized/absent value defaults to Normal, the
        /// same lenient-fallback shape every other enumerated presentation attribute in this file uses.
        /// </summary>
        private static RBlendMode ParseFeBlendMode(string? value) => value?.Trim().ToLowerInvariant() switch
        {
            "multiply" => RBlendMode.Multiply,
            "screen" => RBlendMode.Screen,
            "overlay" => RBlendMode.Overlay,
            "darken" => RBlendMode.Darken,
            "lighten" => RBlendMode.Lighten,
            "color-dodge" => RBlendMode.ColorDodge,
            "color-burn" => RBlendMode.ColorBurn,
            "hard-light" => RBlendMode.HardLight,
            "soft-light" => RBlendMode.SoftLight,
            "difference" => RBlendMode.Difference,
            "exclusion" => RBlendMode.Exclusion,
            "hue" => RBlendMode.Hue,
            "saturation" => RBlendMode.Saturation,
            "color" => RBlendMode.Color,
            "luminosity" => RBlendMode.Luminosity,
            _ => RBlendMode.Normal,
        };

        private FilterPrimitive? BuildFeColorMatrix(ISvgSourceNode node)
        {
            var inAttr = node.GetAttribute("in");
            var result = node.GetAttribute("result");
            var type = (node.GetAttribute("type") ?? "matrix").Trim().ToLowerInvariant();

            if (type == "luminancetoalpha")
                return new FeColorMatrix { In = inAttr, Result = result, Matrix = ColorMatrix.Identity, IsLuminanceToAlpha = true };

            // saturate/hueRotate and any matrix with an off-diagonal term mix channels, which PDF's per-channel /TR cannot
            // express; such a matrix is built as normal and FeColorMatrix.RequiresRaster routes the filter to pixels.
            ColorMatrix matrix;
            switch (type)
            {
                case "saturate":
                    matrix = PeachPDF.Html.Core.Paint.FilterEffectResolver.SaturateMatrix(
                        SvgValueParsers.ParseNumberList(node.GetAttribute("values")) is { Length: > 0 } sat ? sat[0] : 1.0);
                    break;

                case "huerotate":
                    matrix = PeachPDF.Html.Core.Paint.FilterEffectResolver.HueRotateMatrix(
                        (SvgValueParsers.ParseNumberList(node.GetAttribute("values")) is { Length: > 0 } hue ? hue[0] : 0.0) * Math.PI / 180.0);
                    break;

                case "matrix":
                {
                    var values = SvgValueParsers.ParseNumberList(node.GetAttribute("values")) ?? SvgColorMatrixTable.Identity;
                    if (values.Length != 20)
                        return null;

                    matrix = SvgColorMatrixTable.Build(values);
                    break;
                }

                default:
                    return null;
            }

            return new FeColorMatrix { In = inAttr, Result = result, Matrix = matrix, IsLuminanceToAlpha = false };
        }

        private FilterPrimitive? BuildFeComponentTransfer(ISvgSourceNode node)
        {
            var inAttr = node.GetAttribute("in");
            var functions = new[]
            {
                ReadTransferFunction(node, "feFuncR"),
                ReadTransferFunction(node, "feFuncG"),
                ReadTransferFunction(node, "feFuncB"),
                ReadTransferFunction(node, "feFuncA"),
            };

            // Identity or linear on R/G/B and no alpha function is a per-channel affine map, which a PDF /TR can carry; anything
            // else needs the raster path and keeps the whole function list.
            var vectorRepresentable = functions[3].Kind == TransferKind.Identity;
            for (var i = 0; i < 3 && vectorRepresentable; i++)
                vectorRepresentable = functions[i].Kind is TransferKind.Identity or TransferKind.Linear;

            var linear = new Matrix4x4(
                (float)LinearSlope(functions[0]), 0, 0, 0,
                0, (float)LinearSlope(functions[1]), 0, 0,
                0, 0, (float)LinearSlope(functions[2]), 0,
                0, 0, 0, 1);
            var offset = new Vector4((float)LinearIntercept(functions[0]), (float)LinearIntercept(functions[1]), (float)LinearIntercept(functions[2]), 0f);

            return new FeComponentTransfer
            {
                In = inAttr,
                Result = node.GetAttribute("result"),
                Matrix = new ColorMatrix(linear, offset),
                Functions = vectorRepresentable ? null : functions,
            };
        }

        private static double LinearSlope(TransferFunction f) => f.Kind == TransferKind.Linear ? f.Slope : 1.0;

        private static double LinearIntercept(TransferFunction f) => f.Kind == TransferKind.Linear ? f.Intercept : 0.0;

        /// <summary>Reads one <c>feFuncR</c>/<c>feFuncG</c>/<c>feFuncB</c>/<c>feFuncA</c> child; absent or unrecognised is the identity, as the spec says.</summary>
        private static TransferFunction ReadTransferFunction(ISvgSourceNode node, string childName)
        {
            ISvgSourceNode? func = null;
            foreach (var child in node.Children)
            {
                if (child.Name == childName)
                    func = child;
            }

            if (func is null)
                return TransferFunction.Identity;

            var table = SvgValueParsers.ParseNumberList(func.GetAttribute("tableValues")) ?? [];
            return (func.GetAttribute("type") ?? "identity").Trim().ToLowerInvariant() switch
            {
                "table" when table.Length > 0 => new TransferFunction(TransferKind.Table, table, 1, 0, 1, 1, 0),
                "discrete" when table.Length > 0 => new TransferFunction(TransferKind.Discrete, table, 1, 0, 1, 1, 0),
                "linear" => new TransferFunction(TransferKind.Linear, [], ParseFilterNumber(func.GetAttribute("slope"), 1), ParseFilterNumber(func.GetAttribute("intercept"), 0), 1, 1, 0),
                "gamma" => new TransferFunction(TransferKind.Gamma, [], 1, 0,
                    ParseFilterNumber(func.GetAttribute("amplitude"), 1), ParseFilterNumber(func.GetAttribute("exponent"), 1), ParseFilterNumber(func.GetAttribute("offset"), 0)),
                _ => TransferFunction.Identity,
            };
        }

        /// <summary>The one or two numbers of a <c>number-optional-number</c> attribute; null when the text is not one or two numbers.</summary>
        private static (double First, double Second)? ParseNumberOptionalNumber(string? value, double defaultFirst, double defaultSecond)
        {
            if (string.IsNullOrWhiteSpace(value))
                return (defaultFirst, defaultSecond);

            var numbers = SvgValueParsers.ParseNumberList(value);
            return numbers is { Length: 1 } ? (numbers[0], numbers[0])
                : numbers is { Length: 2 } ? (numbers[0], numbers[1])
                : null;
        }

        private FilterPrimitive? BuildFeGaussianBlur(ISvgSourceNode node)
        {
            var inAttr = node.GetAttribute("in");
            if (ParseNumberOptionalNumber(node.GetAttribute("stdDeviation"), 0, 0) is not { } deviation)
                return null;

            return new FeGaussianBlur { In = inAttr, Result = node.GetAttribute("result"), StdDeviationX = deviation.First, StdDeviationY = deviation.Second };
        }

        private FilterPrimitive? BuildFeDropShadow(ISvgSourceNode node)
        {
            var inAttr = node.GetAttribute("in");
            if (ParseNumberOptionalNumber(node.GetAttribute("stdDeviation"), 2, 2) is not { } deviation)
                return null;

            var colorAttr = node.GetAttribute("flood-color");
            var color = string.IsNullOrWhiteSpace(colorAttr)
                ? RColor.Black
                : colorAttr.Trim().Equals("currentColor", StringComparison.OrdinalIgnoreCase)
                    ? _contextColor
                    : new CssValueParser(_adapter).GetActualColor(colorAttr);

            return new FeDropShadow
            {
                In = inAttr,
                Result = node.GetAttribute("result"),
                Dx = ParseFilterNumber(node.GetAttribute("dx"), 2),
                Dy = ParseFilterNumber(node.GetAttribute("dy"), 2),
                StdDeviationX = deviation.First,
                StdDeviationY = deviation.Second,
                Color = color,
                Opacity = SvgValueParsers.ParseOpacity(node.GetAttribute("flood-opacity")),
            };
        }

        private FilterPrimitive? BuildFeMorphology(ISvgSourceNode node)
        {
            var inAttr = node.GetAttribute("in");
            if (ParseNumberOptionalNumber(node.GetAttribute("radius"), 0, 0) is not { } radius)
                return null;

            return new FeMorphology
            {
                In = inAttr,
                Result = node.GetAttribute("result"),
                Dilate = string.Equals(node.GetAttribute("operator")?.Trim(), "dilate", StringComparison.OrdinalIgnoreCase),
                RadiusX = radius.First,
                RadiusY = radius.Second,
            };
        }

        private FilterPrimitive? BuildFeConvolveMatrix(ISvgSourceNode node)
        {
            var inAttr = node.GetAttribute("in");
            if (ParseNumberOptionalNumber(node.GetAttribute("order"), 3, 3) is not { } order)
                return null;

            var orderX = (int)order.First;
            var orderY = (int)order.Second;
            if (orderX < 1 || orderY < 1 || orderX != order.First || orderY != order.Second || (long)orderX * orderY > 10_000)
                return null;

            var kernel = SvgValueParsers.ParseNumberList(node.GetAttribute("kernelMatrix"));
            if (kernel is null || kernel.Length != orderX * orderY)
                return null;

            var divisor = ParseFilterNumber(node.GetAttribute("divisor"), 0);
            if (divisor == 0)
            {
                foreach (var k in kernel)
                    divisor += k;

                if (divisor == 0)
                    divisor = 1;
            }

            var targetX = node.GetAttribute("targetX") is { } tx ? (int)ParseFilterNumber(tx, orderX / 2) : orderX / 2;
            var targetY = node.GetAttribute("targetY") is { } ty ? (int)ParseFilterNumber(ty, orderY / 2) : orderY / 2;
            if (targetX < 0 || targetX >= orderX || targetY < 0 || targetY >= orderY)
                return null;

            return new FeConvolveMatrix
            {
                In = inAttr,
                Result = node.GetAttribute("result"),
                OrderX = orderX,
                OrderY = orderY,
                Kernel = kernel,
                Divisor = divisor,
                Bias = ParseFilterNumber(node.GetAttribute("bias"), 0),
                TargetX = targetX,
                TargetY = targetY,
                EdgeMode = (node.GetAttribute("edgeMode") ?? "duplicate").Trim().ToLowerInvariant() switch
                {
                    "wrap" => FilterEdgeMode.Wrap,
                    "none" => FilterEdgeMode.None,
                    _ => FilterEdgeMode.Duplicate,
                },
                PreserveAlpha = string.Equals(node.GetAttribute("preserveAlpha")?.Trim(), "true", StringComparison.OrdinalIgnoreCase),
            };
        }

        private FilterPrimitive? BuildFeTurbulence(ISvgSourceNode node)
        {
            if (ParseNumberOptionalNumber(node.GetAttribute("baseFrequency"), 0, 0) is not { } frequency ||
                frequency.First < 0 || frequency.Second < 0)
            {
                return null;
            }

            var octaves = (int)ParseFilterNumber(node.GetAttribute("numOctaves"), 1);
            return new FeTurbulence
            {
                Result = node.GetAttribute("result"),
                BaseFrequencyX = frequency.First,
                BaseFrequencyY = frequency.Second,
                NumOctaves = Math.Clamp(octaves, 0, 16),
                Seed = ParseFilterNumber(node.GetAttribute("seed"), 0),
                Stitch = string.Equals(node.GetAttribute("stitchTiles")?.Trim(), "stitch", StringComparison.OrdinalIgnoreCase),
                FractalNoise = string.Equals(node.GetAttribute("type")?.Trim(), "fractalNoise", StringComparison.OrdinalIgnoreCase),
            };
        }

        private static int ParseChannelSelector(string? value) => value?.Trim() switch
        {
            "R" => 0,
            "G" => 1,
            "B" => 2,
            _ => 3,
        };

        private FilterPrimitive? BuildFeDisplacementMap(ISvgSourceNode node)
        {
            var inAttr = node.GetAttribute("in");
            var in2Attr = node.GetAttribute("in2");
            return new FeDisplacementMap
            {
                In = inAttr,
                In2 = in2Attr,
                Result = node.GetAttribute("result"),
                Scale = ParseFilterNumber(node.GetAttribute("scale"), 0),
                XChannel = ParseChannelSelector(node.GetAttribute("xChannelSelector")),
                YChannel = ParseChannelSelector(node.GetAttribute("yChannelSelector")),
            };
        }

        private FilterPrimitive? BuildFeLighting(ISvgSourceNode node, bool specular)
        {
            var inAttr = node.GetAttribute("in");
            LightSource? light = null;
            foreach (var child in node.Children)
            {
                light = child.Name switch
                {
                    "feDistantLight" => new LightSource(LightKind.Distant,
                        ParseFilterNumber(child.GetAttribute("azimuth")), ParseFilterNumber(child.GetAttribute("elevation")),
                        0, 0, 0, 0, 0, 0, 1, null),
                    "fePointLight" => new LightSource(LightKind.Point, 0, 0,
                        ParseFilterNumber(child.GetAttribute("x")), ParseFilterNumber(child.GetAttribute("y")), ParseFilterNumber(child.GetAttribute("z")),
                        0, 0, 0, 1, null),
                    "feSpotLight" => new LightSource(LightKind.Spot, 0, 0,
                        ParseFilterNumber(child.GetAttribute("x")), ParseFilterNumber(child.GetAttribute("y")), ParseFilterNumber(child.GetAttribute("z")),
                        ParseFilterNumber(child.GetAttribute("pointsAtX")), ParseFilterNumber(child.GetAttribute("pointsAtY")), ParseFilterNumber(child.GetAttribute("pointsAtZ")),
                        ParseFilterNumber(child.GetAttribute("specularExponent"), 1),
                        child.GetAttribute("limitingConeAngle") is { } cone ? ParseFilterNumber(cone, 0) : null),
                    _ => null,
                };

                if (light is not null)
                    break;
            }

            if (light is null)
                return null;

            var colorAttr = node.GetAttribute("lighting-color");
            var color = string.IsNullOrWhiteSpace(colorAttr)
                ? RColor.White
                : colorAttr.Trim().Equals("currentColor", StringComparison.OrdinalIgnoreCase)
                    ? _contextColor
                    : new CssValueParser(_adapter).GetActualColor(colorAttr);

            return new FeLighting
            {
                In = inAttr,
                Result = node.GetAttribute("result"),
                Specular = specular,
                SurfaceScale = ParseFilterNumber(node.GetAttribute("surfaceScale"), 1),
                Constant = ParseFilterNumber(node.GetAttribute(specular ? "specularConstant" : "diffuseConstant"), 1),
                SpecularExponent = specular ? Math.Clamp(ParseFilterNumber(node.GetAttribute("specularExponent"), 1), 1, 128) : 1,
                LightingColor = color,
                Light = light,
            };
        }

        private static double ParseFilterNumber(string? value, double fallback = 0)
        {
            if (string.IsNullOrWhiteSpace(value))
                return fallback;

            return double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;
        }

        /// <summary>Builds the renderable children of a pure definition element (<c>&lt;pattern&gt;</c>/<c>&lt;marker&gt;</c>/<c>&lt;mask&gt;</c>/<c>&lt;clipPath&gt;</c>) - same recursion <see cref="BuildGroup"/> uses for an ordinary container, just not itself wrapped in a paintable <see cref="SvgElement"/>. <paramref name="paint"/>/<paramref name="font"/> are what the definition element's own children inherit (see <see cref="EnterDefinition"/>).</summary>
        private List<SvgElement> BuildDefinitionChildren(ISvgSourceNode node, InheritedPaint paint, FontContext font)
        {
            var children = new List<SvgElement>();

            foreach (var child in node.Children)
            {
                var element = BuildElement(child, paint, font);
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
                X1 = SvgValueParsers.ParseGradientCoordinate(node.GetAttribute("x1"), isObjectBoundingBox, _viewportWidth, _lengthBasis) ?? 0,
                Y1 = SvgValueParsers.ParseGradientCoordinate(node.GetAttribute("y1"), isObjectBoundingBox, _viewportHeight, _lengthBasis) ?? 0,
                X2 = SvgValueParsers.ParseGradientCoordinate(node.GetAttribute("x2"), isObjectBoundingBox, _viewportWidth, _lengthBasis) ?? (isObjectBoundingBox ? 1.0 : 0.0),
                Y2 = SvgValueParsers.ParseGradientCoordinate(node.GetAttribute("y2"), isObjectBoundingBox, _viewportHeight, _lengthBasis) ?? 0,
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
                Cx = SvgValueParsers.ParseGradientCoordinate(node.GetAttribute("cx"), isObjectBoundingBox, _viewportWidth, _lengthBasis) ?? (isObjectBoundingBox ? 0.5 : 0.0),
                Cy = SvgValueParsers.ParseGradientCoordinate(node.GetAttribute("cy"), isObjectBoundingBox, _viewportHeight, _lengthBasis) ?? (isObjectBoundingBox ? 0.5 : 0.0),
                R = SvgValueParsers.ParseGradientCoordinate(node.GetAttribute("r"), isObjectBoundingBox, ViewportDiagonal, _lengthBasis) ?? (isObjectBoundingBox ? 0.5 : 0.0),
                Fx = SvgValueParsers.ParseGradientCoordinate(node.GetAttribute("fx"), isObjectBoundingBox, _viewportWidth, _lengthBasis),
                Fy = SvgValueParsers.ParseGradientCoordinate(node.GetAttribute("fy"), isObjectBoundingBox, _viewportHeight, _lengthBasis),
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
