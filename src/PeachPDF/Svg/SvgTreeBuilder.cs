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
        private readonly Dictionary<string, ISvgSourceNode> _nodesById = new(StringComparer.Ordinal);
        private readonly SvgDocument _document = new();
        private int _useDepth;

        private SvgTreeBuilder(RAdapter adapter)
        {
            _adapter = adapter;
        }

        /// <summary>
        /// The inheritable SVG presentation properties (fill/stroke/stroke-width/stroke-miterlimit),
        /// threaded down through the recursive build so an element that doesn't specify one of these
        /// itself inherits its nearest ancestor's resolved value, per normal SVG/CSS inheritance -
        /// including through &lt;use&gt;, whose own attributes become the inherited context for
        /// building the (otherwise unstyled) referenced content.
        /// </summary>
        private readonly record struct InheritedPaint(SvgPaint Fill, SvgPaint Stroke, double StrokeWidth, double StrokeMiterLimit)
        {
            public static readonly InheritedPaint Initial = new(
                Fill: SvgPaint.Solid(Html.Adapters.Entities.RColor.Black),
                Stroke: SvgPaint.None,
                StrokeWidth: 1,
                StrokeMiterLimit: 4);
        }

        public static SvgDocument Build(ISvgSourceNode root, RAdapter adapter)
        {
            var builder = new SvgTreeBuilder(adapter);
            return builder.BuildDocument(root);
        }

        private SvgDocument BuildDocument(ISvgSourceNode root)
        {
            _document.ViewBox = SvgValueParsers.ParseViewBox(root.GetAttribute("viewBox"));
            _document.Width = SvgValueParsers.ParseLength(root.GetAttribute("width"));
            _document.Height = SvgValueParsers.ParseLength(root.GetAttribute("height"));

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
                "use" => BuildUse(node, inherited),
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
                Cx = SvgValueParsers.ParseLength(node.GetAttribute("cx")) ?? 0,
                Cy = SvgValueParsers.ParseLength(node.GetAttribute("cy")) ?? 0,
                R = SvgValueParsers.ParseLength(node.GetAttribute("r")) ?? 0,
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
                X = SvgValueParsers.ParseLength(node.GetAttribute("x")) ?? 0,
                Y = SvgValueParsers.ParseLength(node.GetAttribute("y")) ?? 0,
            };
            // The <use> element's own resolved paint becomes the inherited context for the
            // (otherwise unstyled) referenced content - e.g. <use fill="none" stroke="red"
            // xlink:href="#circleWithNoFillOfItsOwn"/> paints the circle stroked red, not the
            // SVG-wide default black fill.
            var resolved = ApplyCommon(use, node, inherited);

            _useDepth++;
            var target = BuildElement(targetNode, resolved);
            _useDepth--;

            if (target is null)
                return null;

            use.Target = target;
            return use;
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

            var clipPath = new SvgClipPath { Id = id };

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

            var fillAttr = node.GetAttribute("fill");
            element.Fill = fillAttr is null || fillAttr.Equals("inherit", StringComparison.OrdinalIgnoreCase)
                ? inherited.Fill
                : SvgValueParsers.ParsePaint(fillAttr, _adapter);

            var strokeAttr = node.GetAttribute("stroke");
            element.Stroke = strokeAttr is null || strokeAttr.Equals("inherit", StringComparison.OrdinalIgnoreCase)
                ? inherited.Stroke
                : SvgValueParsers.ParsePaint(strokeAttr, _adapter);

            element.StrokeWidth = SvgValueParsers.ParseLength(node.GetAttribute("stroke-width")) ?? inherited.StrokeWidth;
            element.StrokeMiterLimit = SvgValueParsers.ParseLength(node.GetAttribute("stroke-miterlimit")) ?? inherited.StrokeMiterLimit;
            element.Opacity = SvgValueParsers.ParseOpacity(node.GetAttribute("opacity"));
            element.Transform = SvgTransformParser.Parse(node.GetAttribute("transform"));

            var clipPathAttr = node.GetAttribute("clip-path");
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

            return new InheritedPaint(element.Fill, element.Stroke, element.StrokeWidth, element.StrokeMiterLimit);
        }

        private SvgLinearGradient BuildLinearGradient(ISvgSourceNode node)
        {
            return new SvgLinearGradient
            {
                Id = node.GetAttribute("id"),
                GradientUnitsUserSpaceOnUse = IsUserSpaceOnUse(node),
                GradientTransform = SvgTransformParser.Parse(node.GetAttribute("gradientTransform")),
                X1 = SvgValueParsers.ParseLength(node.GetAttribute("x1")) ?? 0,
                Y1 = SvgValueParsers.ParseLength(node.GetAttribute("y1")) ?? 0,
                X2 = SvgValueParsers.ParseLength(node.GetAttribute("x2")) ?? 0,
                Y2 = SvgValueParsers.ParseLength(node.GetAttribute("y2")) ?? 0,
                Stops = BuildStops(node),
            };
        }

        private SvgRadialGradient BuildRadialGradient(ISvgSourceNode node)
        {
            return new SvgRadialGradient
            {
                Id = node.GetAttribute("id"),
                GradientUnitsUserSpaceOnUse = IsUserSpaceOnUse(node),
                GradientTransform = SvgTransformParser.Parse(node.GetAttribute("gradientTransform")),
                Cx = SvgValueParsers.ParseLength(node.GetAttribute("cx")) ?? 0,
                Cy = SvgValueParsers.ParseLength(node.GetAttribute("cy")) ?? 0,
                R = SvgValueParsers.ParseLength(node.GetAttribute("r")) ?? 0,
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
