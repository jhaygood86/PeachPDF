using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Fragments;
using PeachBrackets.Html.Parse;
using PeachBrackets.Html.TextGeometry;
using PeachBrackets.Html.Utils;
using PeachBrackets.Updates;
using PeachDrawing.Core;
using System;
using System.Collections.Generic;

namespace PeachBrackets.Html.Updates
{
    /// <summary>
    /// Restyles an element of a laid-out document in place for the properties that cannot change layout, and says which parts of which pages
    /// look different. The fragment tree holds geometry only and the painter reads style live off the boxes, so repainting a page after one of
    /// these writes shows the change with no layout pass.
    /// </summary>
    internal static class StyleUpdater
    {
        // Properties whose value only ever changes how a box is painted. Anything that can move or resize something stays out: it needs a layout
        // pass this class does not have.
        private static readonly HashSet<string> PaintOnly = new(StringComparer.Ordinal)
        {
            "color", "background-color", "text-decoration-line", "text-decoration-style", "text-decoration-color", "outline-color",
        };

        // Wide enough for an underline, an outline or a glyph that overhangs its box.
        private const double InflatePoints = 4;

        public static UpdateResult Apply(HtmlContainerInt container, CssBox element, string property, string value, IReadOnlyList<FragmentainerFragment> fragmentainers)
        {
            var name = property.Trim().ToLowerInvariant();
            if (!PaintOnly.Contains(name))
                return UpdateResult.NotApplied($"'{name}' can change layout; only {string.Join(", ", PaintOnly)} are applied in place.");

            // The root element's and the body's background is the page canvas's, resolved once for the whole document.
            if (name == "background-color" && element.HtmlTag is { Name: "html" or "body" })
                return UpdateResult.NotApplied("The background of html and body is the page's canvas background, which is not restyled in place.");

            var parser = new CssValueParser(container.RenderContext);
            var oldColor = name == "color" ? element.Color : null;
            if (!CssPropertyRegistry.TrySet(parser, element, name, value.Trim()))
                return UpdateResult.NotApplied($"'{value}' is not a valid value for {name}.");

            // currentColor in a background or an outline is the element's colour, substituted as soon as the colour is final.
            CssUtils.ApplyCurrentColor(element, parser);

            if (oldColor is not null)
                PropagateColor(element, oldColor, element.Color, parser);

            return new UpdateResult(true, null, DirtyRegions(container, element, fragmentainers));
        }

        // There is no live inheritance: a descendant has its own copy of the colour it inherited. Those that still hold the old one take the new
        // one; a descendant with a colour of its own (and so everything under it) is left alone. The border colours are resolved against the
        // colour lazily and cached, and nothing but their own longhands clears the cache, so it is cleared by hand on every box whose colour moved.
        private static void PropagateColor(CssBox box, string oldColor, string newColor, CssValueParser parser)
        {
            InvalidateColorCaches(box);
            foreach (var child in box.Boxes)
            {
                if (!string.Equals(child.Color, oldColor, StringComparison.Ordinal))
                    continue;

                CssPropertyRegistry.TrySet(parser, child, "color", newColor);
                CssUtils.ApplyCurrentColor(child, parser);
                PropagateColor(child, oldColor, newColor, parser);
            }
        }

        private static void InvalidateColorCaches(CssBox box)
        {
            var style = box.DerivedStyle;
            style.InvalidateBorderTopColor();
            style.InvalidateBorderRightColor();
            style.InvalidateBorderBottomColor();
            style.InvalidateBorderLeftColor();
            style.InvalidateOutlineColor();
        }

        /// <summary>The rectangles, in points on their pages, that painting <paramref name="element"/> and everything in it covers.</summary>
        private static List<DirtyRegion> DirtyRegions(HtmlContainerInt container, CssBox element, IReadOnlyList<FragmentainerFragment> fragmentainers)
        {
            var regions = new List<DirtyRegion>();
            var ppp = container.RenderContext.LayoutUnitsPerPoint;

            for (var page = 0; page < fragmentainers.Count; page++)
            {
                var geometry = fragmentainers[page].Geometry;
                var frame = new TextRunCollector.Frame(geometry.ContentLeftPt, geometry.ContentTopPt, container.MarginLeft, container.MarginTop, ppp);
                var rects = new List<Rect>();
                Walk(fragmentainers[page].Root, box => ReferenceEquals(box, element), inside: false, frame, rects);
                foreach (var rect in Merge(rects))
                    regions.Add(new DirtyRegion(page, rect));
            }

            return regions;
        }

        /// <summary>
        /// The regions that cover the elements named by their position in the source (and everything inside them), widened to take in a shadow or an
        /// outline: what to repaint when their state changed but nothing moved.
        /// </summary>
        public static List<DirtyRegion> DirtyRegionsFor(HtmlContainerInt container, HashSet<int> ordinals, IReadOnlyList<FragmentainerFragment> fragmentainers)
        {
            const double reach = 24;
            var regions = new List<DirtyRegion>();
            var ppp = container.RenderContext.LayoutUnitsPerPoint;

            for (var page = 0; page < fragmentainers.Count; page++)
            {
                var geometry = fragmentainers[page].Geometry;
                var frame = new TextRunCollector.Frame(geometry.ContentLeftPt, geometry.ContentTopPt, container.MarginLeft, container.MarginTop, ppp);
                var rects = new List<Rect>();
                Walk(fragmentainers[page].Root, box => box.HtmlTag is { } tag && ordinals.Contains(tag.SourceOrdinal), inside: false, frame, rects);
                foreach (var rect in Merge(rects))
                    regions.Add(new DirtyRegion(page, new Rect(rect.X - reach, rect.Y - reach, rect.Width + (reach * 2), rect.Height + (reach * 2))));
            }

            return regions;
        }

        private static void Walk(BoxFragment fragment, Func<CssBox, bool> isTarget, bool inside, TextRunCollector.Frame frame, List<Rect> rects)
        {
            inside |= isTarget(fragment.Box);
            if (inside)
            {
                // The decoration of each line it occupies, and its words (which can overhang the line's rectangle).
                foreach (var line in fragment.Lines)
                    rects.Add(ToPoints(line.Rect, frame));

                foreach (var word in fragment.Words)
                    rects.Add(ToPoints(word.Rect, frame));

                if (fragment.Lines.Count == 0 && fragment.Words.Count == 0)
                    rects.Add(ToPoints(fragment.Rect, frame));
            }

            foreach (var child in fragment.Children)
                Walk(child, isTarget, inside, frame, rects);
        }

        private static Rect ToPoints(Rect layout, TextRunCollector.Frame frame) =>
            new(frame.X(layout.X) - InflatePoints, frame.Y(layout.Y) - InflatePoints,
                (layout.Width / frame.Ppp) + (InflatePoints * 2), (layout.Height / frame.Ppp) + (InflatePoints * 2));

        // Rectangles that touch or overlap are one region.
        private static List<Rect> Merge(List<Rect> rects)
        {
            var merged = new List<Rect>();
            foreach (var rect in rects)
            {
                var current = rect;
                for (var i = merged.Count - 1; i >= 0; i--)
                {
                    if (merged[i].Right >= current.Left && merged[i].Left <= current.Right && merged[i].Bottom >= current.Top && merged[i].Top <= current.Bottom)
                    {
                        current = Union(merged[i], current);
                        merged.RemoveAt(i);
                    }
                }

                merged.Add(current);
            }

            return merged;
        }

        private static Rect Union(Rect a, Rect b)
        {
            var left = Math.Min(a.Left, b.Left);
            var top = Math.Min(a.Top, b.Top);
            return new Rect(left, top, Math.Max(a.Right, b.Right) - left, Math.Max(a.Bottom, b.Bottom) - top);
        }

        /// <summary>The innermost element whose line, word or box is under a point of a page, or null.</summary>
        public static CssBox? ElementAt(HtmlContainerInt container, FragmentainerFragment fragmentainer, double x, double y)
        {
            var geometry = fragmentainer.Geometry;
            var frame = new TextRunCollector.Frame(geometry.ContentLeftPt, geometry.ContentTopPt, container.MarginLeft, container.MarginTop, container.RenderContext.LayoutUnitsPerPoint);
            return Find(fragmentainer.Root, x, y, frame);
        }

        private static CssBox? Find(BoxFragment fragment, double x, double y, TextRunCollector.Frame frame)
        {
            // The deepest match wins, and among siblings the one painted last (on top).
            for (var i = fragment.Children.Count - 1; i >= 0; i--)
            {
                if (Find(fragment.Children[i], x, y, frame) is { } found)
                    return found;
            }

            // pointer-events: none lets the point through to whatever is beneath (its children, tried above, still receive it).
            if (fragment.Box.PointerEvents.Value == Css.PointerEvents.None || !Covers(fragment, x, y, frame))
                return null;

            var box = fragment.Box;
            while (box.HtmlTag is null && box.ParentBox is { } parent)
                box = parent;

            return box.HtmlTag is null ? null : box;
        }

        private static bool Covers(BoxFragment fragment, double x, double y, TextRunCollector.Frame frame)
        {
            foreach (var word in fragment.Words)
            {
                if (Contains(word.Rect, x, y, frame))
                    return true;
            }

            if (fragment.Lines.Count > 0)
            {
                foreach (var line in fragment.Lines)
                {
                    if (Contains(line.Rect, x, y, frame))
                        return true;
                }

                return false;
            }

            return Contains(fragment.Rect, x, y, frame);
        }

        private static bool Contains(Rect layout, double x, double y, TextRunCollector.Frame frame)
        {
            var left = frame.X(layout.X);
            var top = frame.Y(layout.Y);
            return x >= left && x < left + (layout.Width / frame.Ppp) && y >= top && y < top + (layout.Height / frame.Ppp);
        }
    }
}
