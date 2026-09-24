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
using System;
using System.Collections.Generic;

namespace PeachPDF.Svg
{
    /// <summary>
    /// Evaluates a <see cref="SvgFilter"/>'s primitive graph against real PDF tiles (<see cref="RImage"/>s
    /// from <see cref="RGraphics.CreateTile"/>) - never rasterizing. The overall shape generalizes
    /// <c>SvgRenderer.RenderMaskedElementContent</c>/<c>BuildMaskTile</c>'s own "resolve rect, CreateTile,
    /// paint content into it, composite" pattern from one mask tile to a whole ordered primitive list:
    /// render the element's own content into a <c>SourceGraphic</c> tile, walk each primitive producing
    /// another tile from its resolved input(s), and draw the last one onto the page.
    /// </summary>
    internal static class SvgFilterEvaluator
    {
        /// <inheritdoc cref="Render(RGraphics, SvgFilter, SvgElement, RRect?, Action{RGraphics}, SvgFilterInputs?)"/>
        public static void Render(RGraphics g, SvgFilter filter, SvgElement element, Action<RGraphics> paintSourceGraphic) =>
            Render(g, filter, element, null, paintSourceGraphic);

        /// <summary>
        /// Evaluates <paramref name="filter"/> for <paramref name="element"/> and draws the final
        /// composited result onto <paramref name="g"/> at the resolved filter region - this REPLACES
        /// what would have been <paramref name="element"/>'s own direct paint (see
        /// <c>SvgRenderer.RenderElement</c>'s filter branch). <paramref name="paintSourceGraphic"/> paints
        /// the element's ordinary content (what <c>RenderElementSwitch</c> would have painted directly)
        /// into a tile already translated so the filter region's own origin sits at local (0,0) - this is
        /// <c>SourceGraphic</c>. A no-op (nothing painted) when the resolved region is empty or tiles
        /// aren't available in the current rendering context (e.g. a measure-only pass - <c>CreateTile</c>
        /// returns null there), mirroring <c>BuildMaskTile</c>'s own graceful-bailout contract.
        /// </summary>
        /// <param name="g">The graphics to draw the filtered result onto.</param>
        /// <param name="filter">The filter to evaluate.</param>
        /// <param name="element">The element being filtered.</param>
        /// <param name="viewportBounds">Stands in for the element's bounding box when that cannot be measured (see <see cref="ElementBounds"/>); null keeps the region as authored.</param>
        /// <param name="paintSourceGraphic">Paints the element's ordinary content (the <c>SourceGraphic</c>).</param>
        /// <param name="inputs">What a raster evaluation needs beyond the source graphic (<c>FillPaint</c>, <c>feImage</c>, the backdrop); null when the caller has none, which leaves those inputs transparent.</param>
        public static void Render(RGraphics g, SvgFilter filter, SvgElement element, RRect? viewportBounds, Action<RGraphics> paintSourceGraphic, SvgFilterInputs? inputs = null)
        {
            if (filter.RequiresRaster)
            {
                SvgRasterFilterEvaluator.Render(g, filter, element, viewportBounds, paintSourceGraphic, inputs);
                return;
            }

            var bbox = ElementBounds(element, viewportBounds);
            var (x, y, width, height) = ResolveFilterRect(filter, bbox);
            if (width <= 0 || height <= 0)
                return;

            var sourceTile = g.CreateTile(width, height);
            if (sourceTile is not { } source)
                return;

            var pushedOffset = x != 0 || y != 0;
            if (pushedOffset)
                source.Graphics.PushTransform(new RMatrix(1, 0, 0, 1, -x, -y));

            paintSourceGraphic(source.Graphics);

            if (pushedOffset)
                source.Graphics.PopTransform();

            source.Graphics.Dispose();

            var sourceGraphic = source.Image;
            RImage? sourceAlpha = null;

            // "SourceGraphic"/"SourceAlpha" are reserved names (SVG Filter Effects §12.1) that also live
            // in this same dictionary - a named `result` a filter author happens to spell the same way
            // simply overwrites the reserved entry, matching how a real SVG UA resolves this ambiguity
            // (the later, author-defined one wins for any subsequent `in` reference).
            var named = new Dictionary<string, RImage>(StringComparer.Ordinal) { ["SourceGraphic"] = sourceGraphic };
            var last = sourceGraphic;

            RImage Resolve(string? name)
            {
                if (name is null)
                    return last; // "previous result" rule - `last` starts at SourceGraphic, covering "first primitive" too

                if (name == "SourceAlpha")
                    // Falls back to `last` on the same "no page context" failure EvaluatePrimitive's own
                    // CreateTile calls guard against elsewhere - BuildSourceAlpha needs two tiles of its
                    // own and can fail independently of whatever tile this primitive itself later makes.
                    return sourceAlpha ??= BuildSourceAlpha(g, sourceGraphic, width, height) ?? last;

                return named.TryGetValue(name, out var image) ? image : last;
            }

            foreach (var primitive in filter.Primitives)
            {
                var output = EvaluatePrimitive(g, primitive, Resolve, filter, bbox, width, height);
                if (output is null)
                    // CreateTile failed mid-graph (page context disappeared) - bail rather than draw a
                    // partial/stale result at the end.
                    return;

                if (primitive.Result is { } resultName)
                    named[resultName] = output;

                last = output;
            }

            g.DrawImage(last, new RRect(x, y, width, height));
        }

        private static RImage? EvaluatePrimitive(RGraphics g, FilterPrimitive primitive, Func<string?, RImage> resolve, SvgFilter filter, RRect? bbox, double width, double height)
        {
            switch (primitive)
            {
                case FeFlood feFlood:
                    return EvaluateFeFlood(g, feFlood, width, height);

                case FeOffset feOffset:
                    return EvaluateFeOffset(g, resolve(feOffset.In), feOffset, filter, bbox, width, height);

                case FeMerge feMerge:
                    return EvaluateFeMerge(g, feMerge, resolve, width, height);

                case FeTile feTile:
                    return EvaluateFeTile(g, resolve(feTile.In), width, height);

                case FeComposite feComposite:
                    return EvaluateFeComposite(g, resolve(feComposite.In), resolve(feComposite.In2), feComposite.Operator, width, height);

                case FeBlend feBlend:
                    return EvaluateFeBlend(g, resolve(feBlend.In), resolve(feBlend.In2), feBlend.Mode, width, height);

                case FeColorMatrix { IsLuminanceToAlpha: true }:
                    return EvaluateLuminanceToAlpha(g, resolve(primitive.In), width, height);

                case FeColorMatrix feColorMatrix:
                    return EvaluateColorMatrix(g, resolve(feColorMatrix.In), feColorMatrix.Matrix, width, height);

                case FeComponentTransfer feComponentTransfer:
                    return EvaluateColorMatrix(g, resolve(feComponentTransfer.In), feComponentTransfer.Matrix, width, height);

                default:
                    // Unreachable given SvgTreeBuilder.BuildFilterPrimitive's own closed set, but fail
                    // closed (no output) rather than silently skip if the hierarchy ever grows a case the
                    // switch hasn't caught up with yet.
                    return null;
            }
        }

        /// <summary>Builds a solid-black tile masked by <paramref name="sourceGraphic"/>'s own ALPHA (not luminosity) - the reserved <c>SourceAlpha</c> input, materialized lazily on first reference.</summary>
        private static RImage? BuildSourceAlpha(RGraphics g, RImage sourceGraphic, double width, double height)
        {
            var blackTile = g.CreateTile(width, height);
            if (blackTile is not { } black)
                return null;

            black.Graphics.DrawRectangle(black.Graphics.GetSolidBrush(RColor.Black), 0, 0, width, height);
            black.Graphics.Dispose();

            var tile = g.CreateTile(width, height);
            if (tile is not { } t)
                return null;

            t.Graphics.DrawImageAlphaMasked(black.Image, sourceGraphic, new RRect(0, 0, width, height));
            t.Graphics.Dispose();
            return t.Image;
        }

        private static RImage? EvaluateFeFlood(RGraphics g, FeFlood feFlood, double width, double height)
        {
            var tile = g.CreateTile(width, height);
            if (tile is not { } t)
                return null;

            // Same "multiply alpha into the color" combination SvgValueParsers.ParseStopColor already
            // uses for a gradient stop's stop-color/stop-opacity pair - flood-color/flood-opacity is the
            // same two-attribute-into-one-RGBA shape.
            var color = feFlood.Opacity >= 1.0
                ? feFlood.Color
                : RColor.FromArgb((int)Math.Round(feFlood.Color.A * feFlood.Opacity), feFlood.Color.R, feFlood.Color.G, feFlood.Color.B);

            t.Graphics.DrawRectangle(t.Graphics.GetSolidBrush(color), 0, 0, width, height);
            t.Graphics.Dispose();
            return t.Image;
        }

        private static RImage? EvaluateFeOffset(RGraphics g, RImage input, FeOffset feOffset, SvgFilter filter, RRect? bbox, double width, double height)
        {
            var tile = g.CreateTile(width, height);
            if (tile is not { } t)
                return null;

            var dx = feOffset.Dx;
            var dy = feOffset.Dy;
            if (!filter.PrimitiveUnitsUserSpaceOnUse && bbox is { } b)
            {
                dx *= b.Width;
                dy *= b.Height;
            }

            t.Graphics.DrawImage(input, new RRect(dx, dy, width, height));
            t.Graphics.Dispose();
            return t.Image;
        }

        private static RImage? EvaluateFeMerge(RGraphics g, FeMerge feMerge, Func<string?, RImage> resolve, double width, double height)
        {
            var tile = g.CreateTile(width, height);
            if (tile is not { } t)
                return null;

            // Ordinary sequential src-over painting IS the merge - PDF's default alpha compositing
            // layers each input in order, no dedicated "merge" primitive needed.
            foreach (var inputName in feMerge.Inputs)
                t.Graphics.DrawImage(resolve(inputName), new RRect(0, 0, width, height));

            t.Graphics.Dispose();
            return t.Image;
        }

        /// <summary>
        /// <c>feTile</c> repeats its input's own defined SUBREGION across the whole filter region - but
        /// per-primitive subregions (<c>x</c>/<c>y</c>/<c>width</c>/<c>height</c>) are out of scope for
        /// this evaluator (rejected at parse time, <c>SvgTreeBuilder.HasSubregion</c>), so every input
        /// this evaluator ever produces already occupies the FULL filter region - there is no smaller
        /// cell left to tile. Given that constraint, the spec-correct behavior degenerates to a single
        /// untiled copy of the input (a 1x1 "tiling"), which is what this does - not a shortcut around
        /// real tiling machinery, but the actually-correct result given no subregion was ever declared.
        /// A real repeating <c>PdfTilingPattern</c>/repeated-<c>DrawImage</c> cell only becomes reachable
        /// once per-primitive subregions are implemented, at which point this is the one primitive that
        /// needs revisiting.
        /// </summary>
        private static RImage? EvaluateFeTile(RGraphics g, RImage input, double width, double height)
        {
            var tile = g.CreateTile(width, height);
            if (tile is not { } t)
                return null;

            t.Graphics.DrawImage(input, new RRect(0, 0, width, height));
            t.Graphics.Dispose();
            return t.Image;
        }

        /// <summary>
        /// Porter-Duff compositing of <paramref name="inputA"/> (<c>in</c>) over/against
        /// <paramref name="inputB"/> (<c>in2</c>) via <paramref name="op"/> - <c>over</c>/<c>in</c>/<c>out</c>
        /// are the three PDF-native primitives (plain src-over paint order, and an <c>/Alpha</c>-subtype
        /// soft mask with/without its <c>/TR</c> inversion), and <c>atop</c>/<c>xor</c> are each built by
        /// combining two of those, verified against the formal Porter-Duff compositing algebra (Porter &amp;
        /// Duff, "Compositing Digital Images", 1984):
        /// <list type="bullet">
        /// <item><description><c>atop</c>: <c>Co = Csrc*Asrc*Adst + Cdst*Adst*(1-Asrc)</c>, <c>Ao = Adst</c> -
        /// result never extends past B's own footprint (<c>Ao = Adst</c>), and within it shows A
        /// wherever A covers, else B. Drawing B in full, then A masked-by-B's-alpha on top (ordinary
        /// src-over) reproduces exactly this: outside B nothing was drawn at all (B's own alpha is 0
        /// there, matching <c>Ao = Adst</c>); inside B, the masked A layer's own alpha (<c>Asrc</c>) is
        /// what src-over blends against the already-painted B beneath it - precisely the two weighted
        /// terms above.</description></item>
        /// <item><description><c>xor</c>: <c>Co = Csrc*Asrc*(1-Adst) + Cdst*Adst*(1-Asrc)</c> - the two
        /// terms are DISJOINT in space (the first only where B is absent, the second only where A is
        /// absent), so painting "A masked by NOT-B" and "B masked by NOT-A" in either order composites
        /// them independently with no overlap to blend - each masked draw simply reproduces its own
        /// term of the sum, since nothing painted by the other draw touches the same pixels a nonzero
        /// weight is at.</description></item>
        /// </list>
        /// </summary>
        private static RImage? EvaluateFeComposite(RGraphics g, RImage inputA, RImage inputB, string op, double width, double height)
        {
            var tile = g.CreateTile(width, height);
            if (tile is not { } t)
                return null;

            var rect = new RRect(0, 0, width, height);

            switch (op)
            {
                case "over":
                    t.Graphics.DrawImage(inputB, rect);
                    t.Graphics.DrawImage(inputA, rect);
                    break;

                case "in":
                    t.Graphics.DrawImageAlphaMasked(inputA, inputB, rect);
                    break;

                case "out":
                    t.Graphics.DrawImageAlphaMasked(inputA, inputB, rect, invert: true);
                    break;

                case "atop":
                    t.Graphics.DrawImage(inputB, rect);
                    t.Graphics.DrawImageAlphaMasked(inputA, inputB, rect);
                    break;

                case "xor":
                    t.Graphics.DrawImageAlphaMasked(inputA, inputB, rect, invert: true);
                    t.Graphics.DrawImageAlphaMasked(inputB, inputA, rect, invert: true);
                    break;
            }

            t.Graphics.Dispose();
            return t.Image;
        }

        private static RImage? EvaluateFeBlend(RGraphics g, RImage top, RImage bottom, RBlendMode mode, double width, double height)
        {
            var tile = g.CreateTile(width, height);
            if (tile is not { } t)
                return null;

            t.Graphics.DrawImageBlendedOver(top, bottom, new RRect(0, 0, width, height), mode);
            t.Graphics.Dispose();
            return t.Image;
        }

        private static RImage? EvaluateColorMatrix(RGraphics g, RImage input, ColorMatrix matrix, double width, double height)
        {
            var tile = g.CreateTile(width, height);
            if (tile is not { } t)
                return null;

            t.Graphics.DrawImageWithColorMatrix(input, new RRect(0, 0, width, height), matrix);
            t.Graphics.Dispose();
            return t.Image;
        }

        /// <summary><c>feColorMatrix type="luminanceToAlpha"</c>: a solid-black fill whose per-pixel VISIBILITY is <paramref name="input"/>'s own luminosity - exactly PDF's existing <c>/Luminosity</c> soft mask, no new primitive needed.</summary>
        private static RImage? EvaluateLuminanceToAlpha(RGraphics g, RImage input, double width, double height)
        {
            var blackTile = g.CreateTile(width, height);
            if (blackTile is not { } black)
                return null;

            black.Graphics.DrawRectangle(black.Graphics.GetSolidBrush(RColor.Black), 0, 0, width, height);
            black.Graphics.Dispose();

            var tile = g.CreateTile(width, height);
            if (tile is not { } t)
                return null;

            t.Graphics.DrawImageMasked(black.Image, input, new RRect(0, 0, width, height));
            t.Graphics.Dispose();
            return t.Image;
        }

        /// <summary>Resolves a filter's region, same objectBoundingBox/userSpaceOnUse handling as <c>SvgRenderer.ResolveMaskRect</c>.</summary>
        /// <summary>
        /// The element's bounding box, or the viewport when it cannot be measured statically (text has no geometry until it is
        /// laid out): an <c>objectBoundingBox</c> filter region of a text-only element would otherwise collapse to a
        /// sliver at the origin and hide the element entirely.
        /// </summary>
        internal static RRect? ElementBounds(SvgElement element, RRect? viewportBounds) =>
            SvgGeometryBounds.GetBoundingBox(element) ?? viewportBounds;

        internal static (double X, double Y, double Width, double Height) ResolveFilterRect(SvgFilter filter, RRect? bbox)
        {
            if (filter.FilterUnitsUserSpaceOnUse)
                return (filter.X, filter.Y, filter.Width, filter.Height);

            if (bbox is not { } b)
                return (filter.X, filter.Y, filter.Width, filter.Height);

            return (b.X + filter.X * b.Width, b.Y + filter.Y * b.Height, filter.Width * b.Width, filter.Height * b.Height);
        }
    }
}
