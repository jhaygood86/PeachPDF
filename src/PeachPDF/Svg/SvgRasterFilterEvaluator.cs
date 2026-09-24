using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Raster;
using PeachPDF.Raster.Filters;
using System;
using System.Collections.Generic;

namespace PeachPDF.Svg
{
    /// <summary>
    /// Evaluates a <see cref="SvgFilter"/> that has a primitive PDF cannot express (see <see cref="SvgFilter.RequiresRaster"/>)
    /// over pixels. The element's ordinary content is painted into a raster surface covering the filter region, the primitive
    /// graph runs over premultiplied RGBA8 surfaces of that size, and the result is drawn back onto the page as one bitmap at
    /// the document's raster resolution. Every primitive - the ones PDF could also express included - is evaluated here, so a
    /// filter is either entirely vector or entirely raster and never a mixture of the two computations.
    /// </summary>
    /// <remarks>
    /// Colour spaces follow <c>color-interpolation-filters</c>: each primitive converts its inputs to its own space
    /// (linearRGB unless the filter says otherwise) and labels its output with it, and the last result is converted to sRGB for
    /// the page. The vector evaluation cannot do this (PDF has no working space to interpolate in), which is why it computes in
    /// sRGB throughout.
    /// </remarks>
    internal static class SvgRasterFilterEvaluator
    {
        /// <summary>A primitive's result: its pixels, the colour space they are in and the subregion the primitive produced.</summary>
        private sealed class Image(RasterSurface surface, bool linear, IntRect subregion)
        {
            public RasterSurface Surface { get; } = surface;
            public bool Linear { get; } = linear;
            public IntRect Subregion { get; } = subregion;
        }

        public static void Render(RGraphics g, SvgFilter filter, SvgElement element, RRect? viewportBounds, Action<RGraphics> paintSourceGraphic)
        {
            var bbox = SvgFilterEvaluator.ElementBounds(element, viewportBounds);
            var (x, y, width, height) = SvgFilterEvaluator.ResolveFilterRect(filter, bbox);
            if (!(width > 0) || !(height > 0))
                return;

            // No raster context (a measure-only pass) - the same graceful bail-out the tile-based evaluation makes.
            using var scope = g.BeginRasterSurface(new RRect(x, y, width, height));
            if (scope is null)
                return;

            paintSourceGraphic(scope.Graphics);

            var result = Evaluate(filter, bbox, scope.Surface);
            if (result is null)
                return;

            result.Pixels.CopyTo(scope.Surface.Pixels);
            result.Dispose();
            g.DrawRaster(scope.Surface);
        }

        /// <summary>Runs the primitive graph and returns the final result in sRGB, or null when the graph produced nothing. The caller owns the returned surface.</summary>
        private static RasterSurface? Evaluate(SvgFilter filter, RRect? bbox, RasterSurface sourcePixels)
        {
            var owned = new List<RasterSurface>();

            RasterSurface Track(RasterSurface surface)
            {
                owned.Add(surface);
                return surface;
            }

            var full = sourcePixels.Bounds;
            var sx = sourcePixels.PixelsPerUnitX;
            var sy = sourcePixels.PixelsPerUnitY;

            var source = new Image(Track(FilterOps.Clone(sourcePixels)), false, full);
            Image? sourceAlpha = null;
            var named = new Dictionary<string, Image>(StringComparer.Ordinal) { ["SourceGraphic"] = source };
            var last = source;

            Image Resolve(string? name)
            {
                if (name is null)
                    return last;

                if (name == "SourceAlpha")
                {
                    if (sourceAlpha is null)
                    {
                        var alpha = Track(FilterOps.Clone(source.Surface));
                        var p = alpha.Pixels;
                        for (var i = 0; i + 3 < p.Length; i += 4)
                            p[i] = p[i + 1] = p[i + 2] = 0;

                        sourceAlpha = new Image(alpha, false, full);
                    }

                    return sourceAlpha;
                }

                return named.TryGetValue(name, out var image) ? image : last;
            }

            // A primitive computes in its own colour space: an input in the other one is converted first.
            Image InSpace(Image image, bool linear)
            {
                if (image.Linear == linear)
                    return image;

                var converted = Track(FilterOps.Clone(image.Surface));
                FilterOps.ConvertColorSpace(converted, linear);
                return new Image(converted, linear, image.Subregion);
            }

            double LengthX(double value) => (filter.PrimitiveUnitsUserSpaceOnUse || bbox is not { } b ? value : value * b.Width) * sx;
            double LengthY(double value) => (filter.PrimitiveUnitsUserSpaceOnUse || bbox is not { } b ? value : value * b.Height) * sy;

            // The names of the results a primitive reads; null stands for the previous primitive's result (the implicit input).
            static IEnumerable<string?> InputsOf(FilterPrimitive primitive) => primitive switch
            {
                FeMerge merge => merge.Inputs,
                FeOffset p => [p.In],
                FeTile p => [p.In],
                FeComposite p => [p.In, p.In2],
                FeBlend p => [p.In, p.In2],
                FeColorMatrix p => [p.In],
                FeComponentTransfer p => [p.In],
                FeGaussianBlur p => [p.In],
                FeDropShadow p => [p.In],
                FeMorphology p => [p.In],
                FeConvolveMatrix p => [p.In],
                FeDisplacementMap p => [p.In, p.In2],
                FeLighting p => [p.In],
                _ => [],
            };

            // The default subregion (Filter Effects 1): the union of the subregions of the results the primitive reads, or the whole filter
            // region when it reads none, reads a standard input (SourceGraphic, SourceAlpha), or is an feTile.
            IntRect DefaultSubregion(FilterPrimitive primitive)
            {
                if (primitive is FeTile)
                    return full;

                IntRect? union = null;
                foreach (var name in InputsOf(primitive))
                {
                    var input = Resolve(name);
                    if (ReferenceEquals(input, source) || ReferenceEquals(input, sourceAlpha))
                        return full;

                    if (input.Subregion.IsEmpty)
                        continue;

                    union = union is { } u
                        ? new IntRect(Math.Min(u.Left, input.Subregion.Left), Math.Min(u.Top, input.Subregion.Top),
                            Math.Max(u.Right, input.Subregion.Right), Math.Max(u.Bottom, input.Subregion.Bottom))
                        : input.Subregion;
                }

                return union ?? full;
            }

            IntRect SubregionOf(FilterPrimitive primitive)
            {
                var fallback = DefaultSubregion(primitive);
                if (primitive.Subregion is not { } sub)
                    return fallback;

                double Point(double value, double bboxOrigin, double bboxSize) =>
                    filter.PrimitiveUnitsUserSpaceOnUse || bbox is null ? value : bboxOrigin + value * bboxSize;

                double Size(double value, double bboxSize) =>
                    filter.PrimitiveUnitsUserSpaceOnUse || bbox is null ? value : value * bboxSize;

                // An attribute that is not given takes the default subregion's edge.
                var left = fallback.Left;
                var top = fallback.Top;
                var right = fallback.Right;
                var bottom = fallback.Bottom;

                double ux = 0, uy = 0;
                if (sub.X is { } x)
                {
                    ux = Point(x, bbox?.X ?? 0, bbox?.Width ?? 0);
                    left = (int)Math.Floor(Math.Clamp(ux * sx - sourcePixels.GridX, -1e9, 1e9) + 1e-6);
                }

                if (sub.Y is { } y)
                {
                    uy = Point(y, bbox?.Y ?? 0, bbox?.Height ?? 0);
                    top = (int)Math.Floor(Math.Clamp(uy * sy - sourcePixels.GridY, -1e9, 1e9) + 1e-6);
                }

                if (sub.Width is { } w)
                {
                    var uw = Size(w, bbox?.Width ?? 0);
                    var origin = sub.X is null ? (left + sourcePixels.GridX) / sx : ux;
                    right = (int)Math.Ceiling(Math.Clamp((origin + uw) * sx - sourcePixels.GridX, -1e9, 1e9) - 1e-6);
                }

                if (sub.Height is { } h)
                {
                    var uh = Size(h, bbox?.Height ?? 0);
                    var origin = sub.Y is null ? (top + sourcePixels.GridY) / sy : uy;
                    bottom = (int)Math.Ceiling(Math.Clamp((origin + uh) * sy - sourcePixels.GridY, -1e9, 1e9) - 1e-6);
                }

                return new IntRect(left, top, right, bottom).Intersect(full);
            }

            foreach (var primitive in filter.Primitives)
            {
                var linear = primitive.LinearRgb;
                var output = Track(FilterOps.Blank(sourcePixels));
                var subregion = SubregionOf(primitive);

                switch (primitive)
                {
                    case FeFlood flood:
                        FilterOps.Fill(output, linear ? FilterOps.ConvertColor(flood.Color, true) : flood.Color, flood.Opacity);
                        break;

                    case FeOffset offset:
                    {
                        var input = InSpace(Resolve(offset.In), linear);
                        var dx = filter.PrimitiveUnitsUserSpaceOnUse || bbox is not { } b1 ? offset.Dx : offset.Dx * b1.Width;
                        var dy = filter.PrimitiveUnitsUserSpaceOnUse || bbox is not { } b2 ? offset.Dy : offset.Dy * b2.Height;
                        FilterOps.Offset(input.Surface, output, (int)Math.Round(dx * sx), (int)Math.Round(dy * sy));
                        break;
                    }

                    case FeMerge merge:
                        foreach (var name in merge.Inputs)
                            FilterOps.OverInPlace(output, InSpace(Resolve(name), linear).Surface);
                        break;

                    case FeTile tile:
                    {
                        var input = InSpace(Resolve(tile.In), linear);
                        FilterOps.Tile(input.Surface, output, input.Subregion);
                        break;
                    }

                    case FeComposite composite:
                    {
                        var a = InSpace(Resolve(composite.In), linear);
                        var b = InSpace(Resolve(composite.In2), linear);
                        if (composite.Operator == "arithmetic")
                            FilterOps.Arithmetic(a.Surface, b.Surface, output, composite.K1, composite.K2, composite.K3, composite.K4);
                        else
                            FilterOps.PorterDuff(composite.Operator, a.Surface, b.Surface, output);
                        break;
                    }

                    case FeBlend blend:
                        FilterOps.Blend(blend.Mode, InSpace(Resolve(blend.In), linear).Surface, InSpace(Resolve(blend.In2), linear).Surface, output);
                        break;

                    case FeColorMatrix { IsLuminanceToAlpha: true } luminance:
                    {
                        var input = InSpace(Resolve(luminance.In), linear);
                        input.Surface.Pixels.CopyTo(output.Pixels);
                        ColorMatrixFilter.ApplyInPlace(output, SvgColorMatrixTable.Build(
                        [
                            0, 0, 0, 0, 0,
                            0, 0, 0, 0, 0,
                            0, 0, 0, 0, 0,
                            0.2125, 0.7154, 0.0721, 0, 0,
                        ]));
                        break;
                    }

                    case FeColorMatrix colorMatrix:
                    {
                        var input = InSpace(Resolve(colorMatrix.In), linear);
                        input.Surface.Pixels.CopyTo(output.Pixels);
                        ColorMatrixFilter.ApplyInPlace(output, colorMatrix.Matrix);
                        break;
                    }

                    case FeComponentTransfer transfer:
                    {
                        var input = InSpace(Resolve(transfer.In), linear);
                        var functions = transfer.Functions ?? LinearFunctions(transfer.Matrix);
                        FilterOps.ComponentTransfer(input.Surface, output,
                            FilterOps.BuildTransferLut(functions[0]), FilterOps.BuildTransferLut(functions[1]),
                            FilterOps.BuildTransferLut(functions[2]), FilterOps.BuildTransferLut(functions[3]));
                        break;
                    }

                    case FeGaussianBlur blur:
                    {
                        var input = InSpace(Resolve(blur.In), linear);
                        input.Surface.Pixels.CopyTo(output.Pixels);

                        // A negative or zero deviation disables the axis; both disabled leaves the input as it was.
                        var sigmaX = blur.StdDeviationX > 0 ? LengthX(blur.StdDeviationX) : 0;
                        var sigmaY = blur.StdDeviationY > 0 ? LengthY(blur.StdDeviationY) : 0;
                        GaussianBlur.Apply(output, sigmaX, sigmaY);
                        break;
                    }

                    case FeDropShadow shadow:
                    {
                        var input = InSpace(Resolve(shadow.In), linear);
                        input.Surface.Pixels.CopyTo(output.Pixels);
                        var color = linear ? FilterOps.ConvertColor(shadow.Color, true) : shadow.Color;
                        var opacity = Math.Clamp(shadow.Opacity * color.A / 255.0, 0.0, 1.0);
                        var shadowAlpha = (byte)Math.Round(opacity * 255.0);
                        // DropShadow.Apply takes a premultiplied colour.
                        DropShadow.Apply(output,
                            (int)Math.Round(LengthX(shadow.Dx)), (int)Math.Round(LengthY(shadow.Dy)),
                            shadow.StdDeviationX > 0 ? LengthX(shadow.StdDeviationX) : 0, shadow.StdDeviationY > 0 ? LengthY(shadow.StdDeviationY) : 0,
                            (byte)PixelKernels.Div255(color.R * shadowAlpha), (byte)PixelKernels.Div255(color.G * shadowAlpha), (byte)PixelKernels.Div255(color.B * shadowAlpha),
                            shadowAlpha);
                        break;
                    }

                    case FeMorphology morphology:
                    {
                        var input = InSpace(Resolve(morphology.In), linear);
                        var rx = morphology.RadiusX > 0 ? (int)Math.Round(LengthX(morphology.RadiusX)) : 0;
                        var ry = morphology.RadiusY > 0 ? (int)Math.Round(LengthY(morphology.RadiusY)) : 0;
                        if (morphology.RadiusX <= 0 && morphology.RadiusY <= 0)
                            input.Surface.Pixels.CopyTo(output.Pixels);
                        else
                            FilterOps.Morphology(input.Surface, output, rx, ry, morphology.Dilate);
                        break;
                    }

                    case FeConvolveMatrix convolve:
                        FilterOps.ConvolveMatrix(InSpace(Resolve(convolve.In), linear).Surface, output, convolve);
                        break;

                    case FeTurbulence turbulence:
                    {
                        // The stitching tile is the primitive's subregion, in user space.
                        var (rx, ry, rw, rh) = SvgFilterEvaluator.ResolveFilterRect(filter, bbox);
                        if (turbulence.Subregion is not null)
                        {
                            rx = (subregion.Left + sourcePixels.GridX) / sx;
                            ry = (subregion.Top + sourcePixels.GridY) / sy;
                            rw = subregion.Width / sx;
                            rh = subregion.Height / sy;
                        }

                        if (turbulence.NumOctaves > 0)
                        {
                            new Turbulence((long)Math.Truncate(Math.Clamp(turbulence.Seed, -2147483648.0, 2147483647.0))).Render(output, sx, sy,
                                turbulence.BaseFrequencyX, turbulence.BaseFrequencyY, turbulence.NumOctaves,
                                turbulence.FractalNoise, turbulence.Stitch, rx, ry, rw, rh);
                        }

                        break;
                    }

                    case FeDisplacementMap displacement:
                    {
                        var input = InSpace(Resolve(displacement.In), linear);
                        var map = InSpace(Resolve(displacement.In2), linear);
                        var scaleX = filter.PrimitiveUnitsUserSpaceOnUse || bbox is not { } b3 ? displacement.Scale : displacement.Scale * b3.Width;
                        var scaleY = filter.PrimitiveUnitsUserSpaceOnUse || bbox is not { } b4 ? displacement.Scale : displacement.Scale * b4.Height;
                        FilterOps.Displace(input.Surface, map.Surface, output, scaleX * sx, scaleY * sy, displacement.XChannel, displacement.YChannel);
                        break;
                    }

                    case FeLighting lighting:
                    {
                        var input = InSpace(Resolve(lighting.In), linear);
                        var color = linear ? FilterOps.ConvertColor(lighting.LightingColor, true) : lighting.LightingColor;
                        Lighting.Render(input.Surface, output, lighting, color, sx, sy);
                        break;
                    }

                    default:
                        return FinishEmpty(owned);
                }

                FilterOps.ClipTo(output, subregion);
                var image = new Image(output, linear, subregion);
                if (primitive.Result is { } resultName)
                    named[resultName] = image;

                last = image;
            }

            // The page is sRGB; convert the last result, then hand it back as its own surface (the intermediates are released).
            var final = FilterOps.Clone(last.Surface);
            if (last.Linear)
                FilterOps.ConvertColorSpace(final, false);

            foreach (var surface in owned)
                surface.Dispose();

            return final;
        }

        private static RasterSurface? FinishEmpty(List<RasterSurface> owned)
        {
            foreach (var surface in owned)
                surface.Dispose();

            return null;
        }

        /// <summary>The four transfer functions a channel-independent <see cref="ColorMatrix"/> stands for: slope and intercept per colour channel, identity alpha.</summary>
        private static TransferFunction[] LinearFunctions(ColorMatrix matrix) =>
        [
            new(TransferKind.Linear, [], matrix.Linear.M11, matrix.Offset.X, 1, 1, 0),
            new(TransferKind.Linear, [], matrix.Linear.M22, matrix.Offset.Y, 1, 1, 0),
            new(TransferKind.Linear, [], matrix.Linear.M33, matrix.Offset.Z, 1, 1, 0),
            TransferFunction.Identity,
        ];
    }
}
