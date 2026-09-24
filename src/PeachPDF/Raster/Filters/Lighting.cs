using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Svg;
using System;

namespace PeachPDF.Raster.Filters;

/// <summary>
/// <c>feDiffuseLighting</c> and <c>feSpecularLighting</c>: the input's alpha channel is a height map, lit by a distant, point
/// or spot light (Filter Effects 1). The surface normal comes from the specification's Sobel kernels, with the edge and
/// corner variants generalised as "the same kernel over the rows and columns that exist".
/// </summary>
internal static class Lighting
{
    /// <param name="source">The height map (its alpha channel).</param>
    /// <param name="destination">Receives the lit result.</param>
    /// <param name="p">The primitive.</param>
    /// <param name="lightColor">The lighting colour, already in the primitive's colour space.</param>
    /// <param name="pixelsPerUserX">Pixels per user unit, horizontally.</param>
    /// <param name="pixelsPerUserY">Pixels per user unit, vertically.</param>
    public static void Render(RasterSurface source, RasterSurface destination, FeLighting p, RColor lightColor,
        double pixelsPerUserX, double pixelsPerUserY)
    {
        var w = source.Width;
        var h = source.Height;
        var ps = source.Buffer;
        var pd = destination.Pixels;

        double Alpha(int x, int y) => ps[(y * w + x) * 4 + 3] / 255.0;

        var light = p.Light;
        var lr = lightColor.R / 255.0;
        var lg = lightColor.G / 255.0;
        var lb = lightColor.B / 255.0;

        // Distant light: a constant direction.
        double dlx = 0, dly = 0, dlz = 0;
        if (light.Kind == LightKind.Distant)
        {
            var az = light.Azimuth * Math.PI / 180.0;
            var el = light.Elevation * Math.PI / 180.0;
            dlx = Math.Cos(az) * Math.Cos(el);
            dly = Math.Sin(az) * Math.Cos(el);
            dlz = Math.Sin(el);
        }

        // Spot light: the unit vector from the light towards the point it aims at.
        double sx = 0, sy = 0, sz = 0;
        if (light.Kind == LightKind.Spot)
        {
            sx = light.PointsAtX - light.X;
            sy = light.PointsAtY - light.Y;
            sz = light.PointsAtZ - light.Z;
            var length = Math.Sqrt(sx * sx + sy * sy + sz * sz);
            if (length > 0)
            {
                sx /= length;
                sy /= length;
                sz /= length;
            }
        }

        var coneCos = light.LimitingConeAngle is { } cone ? Math.Cos(Math.Abs(cone) * Math.PI / 180.0) : (double?)null;

        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                // Sobel over the rows and columns that exist: derivative = 2 * (mean of the far column - mean of the near
                // column) / distance between them, with the rows weighted 1-2-1.
                var (dx, dy) = Gradient(Alpha, x, y, w, h);

                // Height differences are per pixel; light and height live in user units.
                var nx = -p.SurfaceScale * dx * pixelsPerUserX;
                var ny = -p.SurfaceScale * dy * pixelsPerUserY;
                var nLength = Math.Sqrt(nx * nx + ny * ny + 1.0);
                nx /= nLength;
                ny /= nLength;
                var nz = 1.0 / nLength;

                var height = p.SurfaceScale * Alpha(x, y);

                double lx, ly, lz;
                double cr = lr, cg = lg, cb = lb;
                switch (light.Kind)
                {
                    case LightKind.Distant:
                        lx = dlx;
                        ly = dly;
                        lz = dlz;
                        break;

                    default:
                    {
                        var ux = (source.GridX + x + 0.5) / pixelsPerUserX;
                        var uy = (source.GridY + y + 0.5) / pixelsPerUserY;
                        lx = light.X - ux;
                        ly = light.Y - uy;
                        lz = light.Z - height;
                        var length = Math.Sqrt(lx * lx + ly * ly + lz * lz);
                        if (length > 0)
                        {
                            lx /= length;
                            ly /= length;
                            lz /= length;
                        }

                        if (light.Kind == LightKind.Spot)
                        {
                            var minusLDotS = -(lx * sx + ly * sy + lz * sz);
                            if (minusLDotS <= 0 || (coneCos is { } c && minusLDotS < c))
                            {
                                cr = cg = cb = 0;
                            }
                            else
                            {
                                var factor = Math.Pow(minusLDotS, light.SpecularExponent);
                                cr *= factor;
                                cg *= factor;
                                cb *= factor;
                            }
                        }

                        break;
                    }
                }

                double r, g, b, a;
                if (p.Specular)
                {
                    var hx = lx;
                    var hy = ly;
                    var hz = lz + 1.0;
                    var hLength = Math.Sqrt(hx * hx + hy * hy + hz * hz);
                    var nDotH = hLength > 0 ? (nx * hx + ny * hy + nz * hz) / hLength : 0.0;
                    var factor = nDotH > 0 ? p.Constant * Math.Pow(nDotH, p.SpecularExponent) : 0.0;
                    r = Math.Clamp(factor * cr, 0.0, 1.0);
                    g = Math.Clamp(factor * cg, 0.0, 1.0);
                    b = Math.Clamp(factor * cb, 0.0, 1.0);
                    a = Math.Max(r, Math.Max(g, b));
                }
                else
                {
                    var nDotL = Math.Max(0.0, nx * lx + ny * ly + nz * lz);
                    var factor = p.Constant * nDotL;
                    r = Math.Clamp(factor * cr, 0.0, 1.0);
                    g = Math.Clamp(factor * cg, 0.0, 1.0);
                    b = Math.Clamp(factor * cb, 0.0, 1.0);
                    a = 1.0;
                }

                var o = (y * w + x) * 4;
                pd[o] = (byte)Math.Round(r * 255.0);
                pd[o + 1] = (byte)Math.Round(g * 255.0);
                pd[o + 2] = (byte)Math.Round(b * 255.0);
                pd[o + 3] = (byte)Math.Round(a * 255.0);
            }
        }
    }

    private static (double Dx, double Dy) Gradient(Func<int, int, double> alpha, int x, int y, int w, int h)
    {
        var x0 = Math.Max(x - 1, 0);
        var x1 = Math.Min(x + 1, w - 1);
        var y0 = Math.Max(y - 1, 0);
        var y1 = Math.Min(y + 1, h - 1);

        double dx = 0, dy = 0;
        if (x1 > x0)
        {
            double far = 0, near = 0, weight = 0;
            for (var yy = y0; yy <= y1; yy++)
            {
                var wgt = yy == y ? 2.0 : 1.0;
                far += wgt * alpha(x1, yy);
                near += wgt * alpha(x0, yy);
                weight += wgt;
            }

            dx = 2.0 * (far - near) / weight / (x1 - x0);
        }

        if (y1 > y0)
        {
            double far = 0, near = 0, weight = 0;
            for (var xx = x0; xx <= x1; xx++)
            {
                var wgt = xx == x ? 2.0 : 1.0;
                far += wgt * alpha(xx, y1);
                near += wgt * alpha(xx, y0);
                weight += wgt;
            }

            dy = 2.0 * (far - near) / weight / (y1 - y0);
        }

        return (dx, dy);
    }
}
