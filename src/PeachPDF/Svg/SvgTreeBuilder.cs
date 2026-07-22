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

using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Parse;
using PeachPDF.Html.Core.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

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
        private readonly Dictionary<string, ISvgSourceNode> _nodesById = new(StringComparer.Ordinal);
        private readonly SvgDocument _document = new();
        private int _useDepth;


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

        private SvgTreeBuilder(RAdapter adapter, RColor contextColor)
        {
            _adapter = adapter;
            _contextColor = contextColor;
        }

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
            string? MarkerEndRef)
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
                MarkerEndRef: null);
        }

        /// <summary>
        /// Font properties inherited through a <c>&lt;text&gt;</c>/<c>&lt;tspan&gt;</c>/<c>&lt;tref&gt;</c>
        /// subtree only - a deliberate v1 scope reduction, since real SVG inherits font-family/
        /// font-size/font-weight/font-style from ANY ancestor (including <c>&lt;g&gt;</c>/<c>&lt;svg&gt;</c>),
        /// not just from a text run's own text-run ancestors. <see cref="InheritedPaint"/> is not
        /// extended with these instead, to avoid threading font resolution through every non-text
        /// element in the tree for a property only text ever consults.
        /// </summary>
        private readonly record struct FontContext(string Family, double Size, bool Bold, bool Italic)
        {
            public static readonly FontContext Default = new(Html.Core.Utils.CssConstants.DefaultFont, Html.Core.Utils.CssConstants.FontSize, false, false);
        }

        /// <param name="root">The root node to build from.</param>
        /// <param name="adapter">The graphics adapter, used to resolve paint colors.</param>
        /// <param name="contextColor">
        /// The resolved <c>currentColor</c> value - the CSS <c>color</c> property of the inline
        /// <c>&lt;svg&gt;</c>'s HTML ancestor for inline SVG, or omitted (defaulting to black) for
        /// standalone/<c>&lt;img&gt;</c> SVG, which has no CSS context to inherit from.
        /// </param>
        public static SvgDocument Build(ISvgSourceNode root, RAdapter adapter, RColor? contextColor = null)
        {
            var builder = new SvgTreeBuilder(adapter, contextColor ?? RColor.Black);
            return builder.BuildDocument(root);
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

            foreach (var child in root.Children)
            {
                var element = BuildElement(child, InheritedPaint.Initial);
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
        private SvgElement? BuildElement(ISvgSourceNode node, InheritedPaint inherited)
        {
            return node.Name switch
            {
                "g" => BuildGroup(node, inherited),
                "path" => BuildPath(node, inherited),
                "circle" => BuildCircle(node, inherited),
                "polygon" => BuildPolygon(node, inherited),
                "polyline" => BuildPolyline(node, inherited),
                "rect" => BuildRect(node, inherited),
                "ellipse" => BuildEllipse(node, inherited),
                "line" => BuildLine(node, inherited),
                "use" => BuildUse(node, inherited),
                "svg" => BuildNestedSvg(node, inherited),
                "image" => BuildImage(node, inherited),
                "text" => BuildTextRun(node, inherited, FontContext.Default),
                "switch" => BuildSwitch(node, inherited),
                "a" => BuildAnchor(node, inherited),
                _ => null,
            };
        }

        private SvgGroupElement BuildGroup(ISvgSourceNode node, InheritedPaint inherited)
        {
            var group = new SvgGroupElement();
            var resolved = ApplyCommon(group, node, inherited);

            foreach (var child in node.Children)
            {
                var element = BuildElement(child, resolved);
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
        private SvgElement? BuildSwitch(ISvgSourceNode node, InheritedPaint inherited)
        {
            foreach (var child in node.Children)
            {
                var element = BuildElement(child, inherited);
                if (element is not null)
                    return element;
            }

            return null;
        }

        private SvgAnchorElement BuildAnchor(ISvgSourceNode node, InheritedPaint inherited)
        {
            var anchor = new SvgAnchorElement
            {
                Href = node.GetAttribute("href") ?? node.GetAttribute("xlink:href"),
            };
            var resolved = ApplyCommon(anchor, node, inherited);

            foreach (var child in node.Children)
            {
                var element = BuildElement(child, resolved);
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

        private SvgUseElement? BuildUse(ISvgSourceNode node, InheritedPaint inherited)
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

            _useDepth++;
            // <symbol> is excluded from BuildElement's general dispatch (like <defs> content, it must
            // never be painted directly if encountered during ordinary traversal) - it only becomes
            // paintable through this explicit <use> reference, which is what actually establishes its
            // viewport (a <symbol> has no size of its own; see BuildSymbol/SvgSymbolElement).
            var target = targetNode.Name == "symbol"
                ? BuildSymbol(targetNode, resolved)
                : BuildElement(targetNode, resolved);
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
        private SvgSymbolElement BuildSymbol(ISvgSourceNode node, InheritedPaint inherited)
        {
            var symbol = new SvgSymbolElement
            {
                ViewBox = SvgValueParsers.ParseViewBox(node.GetAttribute("viewBox")),
                PreserveAspectRatio = SvgValueParsers.ParsePreserveAspectRatio(node.GetAttribute("preserveAspectRatio")),
            };
            var resolved = ApplyCommon(symbol, node, inherited);

            var previousWidth = _viewportWidth;
            var previousHeight = _viewportHeight;
            _viewportWidth = symbol.ViewBox?.Width ?? _viewportWidth;
            _viewportHeight = symbol.ViewBox?.Height ?? _viewportHeight;

            foreach (var child in node.Children)
            {
                var element = BuildElement(child, resolved);
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
        private SvgNestedSvgElement BuildNestedSvg(ISvgSourceNode node, InheritedPaint inherited)
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

            var previousWidth = _viewportWidth;
            var previousHeight = _viewportHeight;
            _viewportWidth = viewBox?.Width ?? nested.Width;
            _viewportHeight = viewBox?.Height ?? nested.Height;

            foreach (var child in node.Children)
            {
                var element = BuildElement(child, resolved);
                if (element is not null)
                    nested.Children.Add(element);
            }

            _viewportWidth = previousWidth;
            _viewportHeight = previousHeight;

            return nested;
        }

        /// <summary>
        /// Builds an <c>&lt;image&gt;</c> element. Only a <c>data:</c> URI <c>href</c> can be resolved
        /// synchronously here - see <see cref="SvgImageElement"/>'s doc comment for why network/file
        /// hrefs are a deliberate, documented v1 gap instead.
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
            if (!DataUriUtils.TryDecodeDataUri(href, out var mimeType, out var bytes))
                return image;

            if (mimeType.Equals("image/svg+xml", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var xdoc = System.Xml.Linq.XDocument.Parse(System.Text.Encoding.UTF8.GetString(bytes));
                    if (xdoc.Root is not null)
                    {
                        // A nested data:image/svg+xml is a standalone SVG document: match against its own
                        // <style> (built here), exactly like an <img>-referenced SVG.
                        var nestedRoot = xdoc.Root;
                        var nestedCssData = SvgCssStyling.BuildStyleData(SvgCssStyling.CollectStyleText(nestedRoot));

                        var valueParser = new CssValueParser(_adapter);
                        var registered = nestedCssData is null ? null : RegisteredProperty.BuildRegistry(nestedCssData, valueParser);
                        var nestedVarContext = registered is { Count: > 0 }
                            ? new CssVarResolver.VarContext(registered, valueParser)
                            : null;

                        if (nestedCssData is not null)
                            SvgCssStyling.CascadeCustomProperties(nestedRoot, nestedCssData, "print", registered);

                        var nestedSource = new XElementSvgSourceNode(nestedRoot, nestedRoot, nestedCssData, "print", nestedVarContext);
                        image.NestedDocument = Build(nestedSource, _adapter, _contextColor);
                    }
                }
                catch (System.Xml.XmlException)
                {
                    // Malformed embedded SVG - NestedDocument stays null, renders nothing.
                }
            }
            else
            {
                try
                {
                    image.Image = _adapter.ImageFromStream(new System.IO.MemoryStream(bytes));
                }
                catch (InvalidOperationException)
                {
                    // Undecodable image bytes - same exception ImageLoadHandler.LoadImageFromStream
                    // already treats as a non-fatal decode failure; Image stays null, renders nothing.
                }
            }

            return image;
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
            };

            foreach (var child in node.Children)
            {
                var shape = BuildElement(child, InheritedPaint.Initial);
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

            var fillAttr = Attr("fill");
            element.Fill = fillAttr is null || fillAttr.Equals("inherit", StringComparison.OrdinalIgnoreCase)
                ? inherited.Fill
                : ResolveUrlPaintKind(SvgValueParsers.ParsePaint(fillAttr, _adapter, _contextColor));

            var strokeAttr = Attr("stroke");
            element.Stroke = strokeAttr is null || strokeAttr.Equals("inherit", StringComparison.OrdinalIgnoreCase)
                ? inherited.Stroke
                : ResolveUrlPaintKind(SvgValueParsers.ParsePaint(strokeAttr, _adapter, _contextColor));

            element.StrokeWidth = SvgValueParsers.ParseLength(Attr("stroke-width"), ViewportDiagonal) ?? inherited.StrokeWidth;
            element.StrokeMiterLimit = SvgValueParsers.ParseLength(Attr("stroke-miterlimit")) ?? inherited.StrokeMiterLimit;
            element.Opacity = SvgValueParsers.ParseOpacity(Attr("opacity"));
            element.Transform = SvgTransformParser.Parse(Attr("transform"));

            var fillRuleAttr = Attr("fill-rule");
            element.FillRule = fillRuleAttr is null || fillRuleAttr.Equals("inherit", StringComparison.OrdinalIgnoreCase)
                ? inherited.FillRule
                : SvgValueParsers.ParseFillRule(fillRuleAttr);

            var fillOpacityAttr = Attr("fill-opacity");
            element.FillOpacity = fillOpacityAttr is null || fillOpacityAttr.Equals("inherit", StringComparison.OrdinalIgnoreCase)
                ? inherited.FillOpacity
                : SvgValueParsers.ParseOpacity(fillOpacityAttr);

            var strokeOpacityAttr = Attr("stroke-opacity");
            element.StrokeOpacity = strokeOpacityAttr is null || strokeOpacityAttr.Equals("inherit", StringComparison.OrdinalIgnoreCase)
                ? inherited.StrokeOpacity
                : SvgValueParsers.ParseOpacity(strokeOpacityAttr);

            var lineCapAttr = Attr("stroke-linecap");
            element.StrokeLineCap = lineCapAttr is null || lineCapAttr.Equals("inherit", StringComparison.OrdinalIgnoreCase)
                ? inherited.StrokeLineCap
                : SvgValueParsers.ParseLineCap(lineCapAttr);

            var lineJoinAttr = Attr("stroke-linejoin");
            element.StrokeLineJoin = lineJoinAttr is null || lineJoinAttr.Equals("inherit", StringComparison.OrdinalIgnoreCase)
                ? inherited.StrokeLineJoin
                : SvgValueParsers.ParseLineJoin(lineJoinAttr);

            var dashArrayAttr = Attr("stroke-dasharray");
            element.StrokeDashArray = dashArrayAttr is null || dashArrayAttr.Equals("inherit", StringComparison.OrdinalIgnoreCase)
                ? inherited.StrokeDashArray
                : SvgValueParsers.ParseDashArray(dashArrayAttr, ViewportDiagonal) ?? inherited.StrokeDashArray;

            var dashOffsetAttr = Attr("stroke-dashoffset");
            element.StrokeDashOffset = dashOffsetAttr is null || dashOffsetAttr.Equals("inherit", StringComparison.OrdinalIgnoreCase)
                ? inherited.StrokeDashOffset
                : SvgValueParsers.ParseLength(dashOffsetAttr, ViewportDiagonal) ?? inherited.StrokeDashOffset;

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
                element.MarkerEndRef);
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
            var styleDeclarations = SvgValueParsers.ParseStyleDeclarations(node.GetAttribute("style"));
            if (styleDeclarations.TryGetValue(name, out var styleValue))
            {
                var resolved = node.ResolveVar(styleValue);
                if (resolved is not null) return resolved;
            }

            var matched = node.GetMatchedCssDeclarations();
            if (matched is not null && matched.TryGetValue(name, out var matchedValue))
                return matchedValue;

            return node.GetAttribute(name);
        }

        /// <summary>
        /// Builds one text run - shared by <c>&lt;text&gt;</c> (the subtree root) and its
        /// <c>&lt;tspan&gt;</c>/<c>&lt;tref&gt;</c> children (see <see cref="SvgTextElement"/>).
        /// <paramref name="fontContext"/> is this run's inherited font (see <see cref="FontContext"/>'s
        /// doc comment for why it's a separate, text-subtree-only inheritance channel rather than part
        /// of <see cref="InheritedPaint"/>).
        /// </summary>
        private SvgTextElement BuildTextRun(ISvgSourceNode node, InheritedPaint inherited, FontContext fontContext)
        {
            var xAttr = node.GetAttribute("x");
            var yAttr = node.GetAttribute("y");

            var run = new SvgTextElement
            {
                HasOwnX = !string.IsNullOrEmpty(xAttr),
                HasOwnY = !string.IsNullOrEmpty(yAttr),
                X = SvgValueParsers.ParseLength(xAttr, _viewportWidth) ?? 0,
                Y = SvgValueParsers.ParseLength(yAttr, _viewportHeight) ?? 0,
                Dx = SvgValueParsers.ParseLength(node.GetAttribute("dx"), _viewportWidth) ?? 0,
                Dy = SvgValueParsers.ParseLength(node.GetAttribute("dy"), _viewportHeight) ?? 0,
                // Only a single leading rotation value applies to the whole run - see SvgTextElement's
                // doc comment re: per-character arrays being out of v1 scope.
                RotateDegrees = SvgValueParsers.ParseLength(node.GetAttribute("rotate")) ?? 0,
                Text = CollapseWhitespace(node.GetTextContent()),
            };

            var resolved = ApplyCommon(run, node, inherited);

            run.TextAnchor = ResolveStyledAttr(node, "text-anchor")?.Trim().ToLowerInvariant() switch
            {
                "middle" => SvgTextAnchor.Middle,
                "end" => SvgTextAnchor.End,
                _ => SvgTextAnchor.Start,
            };

            var familyAttr = ResolveStyledAttr(node, "font-family");
            var family = string.IsNullOrWhiteSpace(familyAttr) ? fontContext.Family : familyAttr.Split(',')[0].Trim().Trim('\'', '"');

            var size = SvgValueParsers.ParseLength(ResolveStyledAttr(node, "font-size"), ViewportDiagonal) ?? fontContext.Size;

            var weightAttr = ResolveStyledAttr(node, "font-weight");
            var bold = weightAttr switch
            {
                null => fontContext.Bold,
                _ when weightAttr.Equals("bold", StringComparison.OrdinalIgnoreCase) || weightAttr.Equals("bolder", StringComparison.OrdinalIgnoreCase) => true,
                _ when int.TryParse(weightAttr, out var weightValue) => weightValue >= 700,
                _ => false,
            };

            var styleAttr = ResolveStyledAttr(node, "font-style");
            var italic = styleAttr is null
                ? fontContext.Italic
                : styleAttr.Equals("italic", StringComparison.OrdinalIgnoreCase) || styleAttr.Equals("oblique", StringComparison.OrdinalIgnoreCase);

            var fontStyle = RFontStyle.Regular;
            if (bold) fontStyle |= RFontStyle.Bold;
            if (italic) fontStyle |= RFontStyle.Italic;

            run.Font = _adapter.GetFont(family, size, fontStyle) ?? _adapter.GetFont(Html.Core.Utils.CssConstants.DefaultFont, size, fontStyle);

            var childFontContext = new FontContext(family, size, bold, italic);

            foreach (var child in node.Children)
            {
                switch (child.Name)
                {
                    case "tspan":
                        run.Spans.Add(BuildTextRun(child, resolved, childFontContext));
                        break;

                    case "tref":
                    {
                        var trefRun = BuildTextRun(child, resolved, childFontContext);
                        var href = child.GetAttribute("href") ?? child.GetAttribute("xlink:href");
                        var id = href?.TrimStart('#');
                        if (!string.IsNullOrEmpty(id) && _nodesById.TryGetValue(id, out var target))
                            trefRun.Text = CollapseWhitespace(target.GetTextContent());
                        run.Spans.Add(trefRun);
                        break;
                    }
                }
            }

            return run;
        }

        /// <summary>
        /// SVG's default (<c>xml:space="default"</c>) whitespace handling: runs of whitespace collapse
        /// to a single space, leading/trailing whitespace is trimmed. Applied independently per text
        /// run rather than across a whole <c>&lt;text&gt;</c> subtree as one unit (the fully spec-correct
        /// behavior) - a documented v1 simplification, same category as <see cref="SvgTextElement"/>'s
        /// other scope reductions.
        /// </summary>
        private static string CollapseWhitespace(string text) =>
            string.IsNullOrEmpty(text) ? "" : System.Text.RegularExpressions.Regex.Replace(text, "\\s+", " ").Trim();

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
                var element = BuildElement(child, InheritedPaint.Initial);
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
                var element = BuildElement(child, InheritedPaint.Initial);
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
