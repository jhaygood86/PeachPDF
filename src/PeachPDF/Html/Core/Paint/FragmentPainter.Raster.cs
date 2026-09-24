using PeachPDF.CSS;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Fragments;
using PeachPDF.Html.Core.Parse;
using System;
using System.Collections.Generic;

namespace PeachPDF.Html.Core.Paint
{
    internal sealed partial class FragmentPainter
    {
        /// <summary>Boxes currently being painted into a bitmap whose <c>drop-shadow()</c> the raster filter chain draws.</summary>
        private readonly HashSet<CssBox> _dropShadowsInRaster = new(ReferenceEqualityComparer.Instance);

        /// <summary>
        /// True for the painter that supplies only the <em>text</em> of a subtree that was drawn as a bitmap (see
        /// <see cref="PaintSelectableText"/>): it draws no shadows, backgrounds, borders, decorations, images or effects, and
        /// its text is invisible (PDF text render mode 3), so it costs nothing visually.
        /// </summary>
        private bool _textOnly;

        /// <summary>True while a subtree is painted into a bitmap: its structure elements are created by the text pass instead.</summary>
        private bool _taggingSuppressed;

        /// <summary>
        /// Paints a fragment - and, via <see cref="PaintTagged"/>, its whole subtree - into a bitmap, runs the
        /// element's <c>filter</c> list over it, and draws the result into <paramref name="g"/>. This is how the
        /// filter functions a PDF cannot express as vector content (<c>blur()</c>, <c>grayscale()</c>,
        /// <c>sepia()</c>, <c>saturate()</c>, <c>hue-rotate()</c>) are rendered.
        /// </summary>
        /// <returns>
        /// false, having painted nothing, when <paramref name="g"/> cannot rasterize (a measure-only pass, a test
        /// double) or the region is empty - the caller then falls back to the vector path, exactly as before the
        /// raster backend existed.
        /// </returns>
        /// <remarks>
        /// The bitmap covers the subtree's own extent (not the whole page the vector <see cref="PaintWithOpacity"/>
        /// tile does), grown by what the filters can spread ink by and cut to what can reach the current clip. Any
        /// transform or clip already pushed onto <paramref name="g"/> for this box applies to the bitmap's placement
        /// automatically, just as it does to a tile.
        /// </remarks>
        private bool PaintRasterized(RGraphics g, BoxFragment fragment, FilterEffectResolver.Resolved filter)
        {
            var box = fragment.Box;

            var extent = SubtreeExtent(fragment);
            if (extent is not { } ink)
                return false;

            var margin = RasterFilterChain.InkMargin(filter.Functions, box);
            var bleed = SubtreeBleed(fragment);
            var bounds = Inflate(ink, margin + bleed);

            // Ink outside the clip is invisible, except that a blur pulls in content from up to `margin` away.
            bounds = Intersect(bounds, Inflate(g.GetClip(), margin));
            if (bounds.Width <= 0 || bounds.Height <= 0)
                return false;

            using var scope = g.BeginRasterSurface(bounds);
            if (scope is null)
                return false;

            // The filter chain paints this box's drop-shadow() itself, from the real alpha shape; the decoration
            // painter's border-box approximation must not add a second one.
            _dropShadowsInRaster.Add(box);
            var wasSuppressed = _taggingSuppressed;
            _taggingSuppressed = true;
            try
            {
                PaintTagged(scope.Graphics, fragment);
            }
            finally
            {
                _taggingSuppressed = wasSuppressed;
                _dropShadowsInRaster.Remove(box);
            }

            RasterFilterChain.Apply(scope.Surface, filter.Functions, box, box.ActualOpacity);

            // The bitmap is presentation, not content: tagged output marks it an artifact and the text pass below supplies the content.
            var builder = _taggingSuppressed ? null : box.HtmlContainer?.StructureTagBuilder;
            using (builder?.OpenArtifact(g))
            {
                var mode = ToRBlendMode(box.ActualMixBlendMode);
                if (mode == RBlendMode.Normal)
                {
                    g.DrawRaster(scope.Surface);
                }
                else
                {
                    g.PushBlendMode(mode);
                    g.DrawRaster(scope.Surface);
                    g.PopBlendMode();
                }
            }

            PaintSelectableText(g, fragment);
            return true;
        }

        /// <summary>
        /// Paints the text of a subtree that was just drawn as a bitmap, invisibly, on top of it, so the text stays selectable,
        /// searchable and (in tagged output) part of the document's structure. What is drawn is exactly what the bitmap shows, at the
        /// same positions, but as PDF text render mode 3.
        /// </summary>
        /// <remarks>
        /// A second painter instance does the walk, in <see cref="_textOnly"/> mode, so transforms, overflow clips, tagging and
        /// per-page state behave exactly as they do for visible text. Not done when <paramref name="g"/> is itself a bitmap (a raster
        /// region inside a raster region: the outermost one supplies the text). Text inside SVG, MathML and other replaced content is
        /// not supplied - only the CSS box tree's own text is.
        /// </remarks>
        private void PaintSelectableText(RGraphics g, BoxFragment fragment)
        {
            if (g.IsOffscreenTile)
                return;

            var overlay = new FragmentPainter(container) { _textOnly = true, _contextMembers = _contextMembers };
            g.InvisibleText = true;
            try
            {
                overlay.PaintTagged(g, fragment);
            }
            finally
            {
                g.InvisibleText = false;
            }
        }

        /// <summary>
        /// The union of everything <paramref name="fragment"/> and its descendants paint, or null when nothing has an extent. Fragments in
        /// <paramref name="excluded"/> (the other planes of a 3D rendering context) and their subtrees are left out.
        /// </summary>
        private static RRect? SubtreeExtent(BoxFragment fragment, HashSet<BoxFragment>? excluded = null)
        {
            var union = new ExtentUnion();
            union.Add(fragment.WholeBoxRect);
            AccumulateExtent(fragment, ref union, excluded);
            return union.ToRect();
        }

        private static void AccumulateExtent(BoxFragment node, ref ExtentUnion union, HashSet<BoxFragment>? excluded)
        {
            union.Add(node.Rect);

            var lines = node.Lines;
            for (var i = 0; i < lines.Count; i++)
                union.Add(lines[i].Rect);

            var words = node.Words;
            for (var i = 0; i < words.Count; i++)
                union.Add(words[i].Rect);

            var children = node.Children;
            for (var i = 0; i < children.Count; i++)
            {
                if (excluded is null || !excluded.Contains(children[i]))
                    AccumulateExtent(children[i], ref union, excluded);
            }
        }

        /// <summary>A running union of rectangles, on the stack. A rectangle without positive width and height is ignored (see the invariant on degenerate rectangles).</summary>
        private struct ExtentUnion
        {
            private double _left, _top, _right, _bottom;
            private bool _any;

            public void Add(RRect rect)
            {
                // A box that draws nothing of its own (an anonymous text box: its words carry the rectangles) reports a degenerate rectangle
                // at the origin, which would drag the union there.
                if (!(rect.Width > 0) || !(rect.Height > 0) || double.IsNaN(rect.X + rect.Y + rect.Width + rect.Height))
                    return;

                if (!_any)
                {
                    (_left, _top, _right, _bottom, _any) = (rect.Left, rect.Top, rect.Right, rect.Bottom, true);
                    return;
                }

                _left = Math.Min(_left, rect.Left);
                _top = Math.Min(_top, rect.Top);
                _right = Math.Max(_right, rect.Right);
                _bottom = Math.Max(_bottom, rect.Bottom);
            }

            public readonly RRect? ToRect() => _any ? new RRect(_left, _top, _right - _left, _bottom - _top) : null;
        }

        /// <summary>
        /// How far the box's own <c>box-shadow</c> layers and <c>drop-shadow()</c> filters reach beyond it, so the
        /// bitmap is large enough to hold them (both are painted with the element's own content).
        /// </summary>
        private static double ShadowBleed(CssBox box)
        {
            double bleed = 0;

            foreach (var function in box.ActualFilterFunctions)
            {
                if (function.Name != "drop-shadow")
                    continue;

                var dx = Math.Abs(CssValueParser.ParseLength(function.Arguments[0], 0, box));
                var dy = Math.Abs(CssValueParser.ParseLength(function.Arguments[1], 0, box));
                // The third length is the standard deviation itself here, so three deviations of blur.
                var blur = 3 * Math.Max(0, CssValueParser.ParseLength(function.Arguments[2], 0, box));
                bleed = Math.Max(bleed, Math.Max(dx, dy) + blur);
            }

            if (string.IsNullOrEmpty(box.BoxShadow) || box.BoxShadow == Keywords.None)
                return bleed;

            List<BoxShadowGrammar.ShadowLayer>? layers;
            using (var pooledTokens = CssValueParser.GetCssTokensPooled(box.BoxShadow))
            {
                List<Token> tokens = pooledTokens;
                layers = BoxShadowGrammar.TryParse(tokens);
            }

            if (layers is null)
                return bleed;

            foreach (var layer in layers)
            {
                var dx = Math.Abs(CssValueParser.ParseLength(layer.OffsetX, 0, box));
                var dy = Math.Abs(CssValueParser.ParseLength(layer.OffsetY, 0, box));
                var blur = Math.Max(0, CssValueParser.ParseLength(layer.Blur, 0, box));
                var spread = Math.Max(0, CssValueParser.ParseLength(layer.Spread, 0, box));
                bleed = Math.Max(bleed, Math.Max(dx, dy) + blur + spread);
            }

            return bleed;
        }

        /// <summary>
        /// The most any box in <paramref name="fragment"/>'s subtree (the box itself included) paints beyond its own rectangle:
        /// <c>box-shadow</c>, <c>drop-shadow()</c>, <c>outline</c> and <c>text-shadow</c>. A descendant's shadow spills past the
        /// filtered element's own extent just as its own does, and would otherwise be cut off at the bitmap's edge. Fragments in
        /// <paramref name="excluded"/> and their subtrees are not counted.
        /// </summary>
        private static double SubtreeBleed(BoxFragment fragment, HashSet<BoxFragment>? excluded = null)
        {
            var bleed = ShadowBleed(fragment.Box);
            var children = fragment.Children;
            for (var i = 0; i < children.Count; i++)
            {
                if (excluded is null || !excluded.Contains(children[i]))
                    AccumulateBleed(children[i], ref bleed, excluded);
            }

            return Math.Max(bleed, Math.Max(OutlineBleed(fragment.Box), TextShadowBleed(fragment.Box)));
        }

        private static void AccumulateBleed(BoxFragment node, ref double bleed, HashSet<BoxFragment>? excluded)
        {
            var box = node.Box;
            bleed = Math.Max(bleed, ShadowBleed(box));
            bleed = Math.Max(bleed, OutlineBleed(box));
            bleed = Math.Max(bleed, TextShadowBleed(box));

            var children = node.Children;
            for (var i = 0; i < children.Count; i++)
            {
                if (excluded is null || !excluded.Contains(children[i]))
                    AccumulateBleed(children[i], ref bleed, excluded);
            }
        }

        private static double OutlineBleed(CssBox box) =>
            box.ActualOutlineWidth > 0 ? box.ActualOutlineWidth + Math.Max(0, box.ActualOutlineOffset) : 0;

        private static double TextShadowBleed(CssBox box)
        {
            var value = box.TextShadow;
            if (string.IsNullOrEmpty(value) || value.Equals(Keywords.None, StringComparison.OrdinalIgnoreCase))
                return 0;

            double bleed = 0;
            foreach (var layer in ParseTextShadow(value))
            {
                var dx = Math.Abs(CssValueParser.ParseLength(layer.OffsetX, 0, box));
                var dy = Math.Abs(CssValueParser.ParseLength(layer.OffsetY, 0, box));
                var blur = Math.Max(0, CssValueParser.ParseLength(layer.Blur, 0, box));
                bleed = Math.Max(bleed, Math.Max(dx, dy) + 1.5 * blur);
            }

            return bleed;
        }

        private static RRect Union(RRect a, RRect b)
        {
            var left = Math.Min(a.Left, b.Left);
            var top = Math.Min(a.Top, b.Top);
            return new RRect(left, top, Math.Max(a.Right, b.Right) - left, Math.Max(a.Bottom, b.Bottom) - top);
        }

        private static RRect Inflate(RRect rect, double amount) =>
            new(rect.X - amount, rect.Y - amount, rect.Width + 2 * amount, rect.Height + 2 * amount);

        private static RRect Intersect(RRect a, RRect b)
        {
            var left = Math.Max(a.Left, b.Left);
            var top = Math.Max(a.Top, b.Top);
            var right = Math.Min(a.Right, b.Right);
            var bottom = Math.Min(a.Bottom, b.Bottom);
            return right <= left || bottom <= top ? RRect.Empty : new RRect(left, top, right - left, bottom - top);
        }
    }
}
