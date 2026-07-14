using PeachPDF.CSS;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Entities;
using PeachPDF.Html.Core.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PeachPDF.Html.Core.Handlers
{
    internal static class CssImagePainter
    {
        /// <summary>
        /// Paints one CSS image layer. Gradient types get a brush that is passed to <paramref name="drawBrush"/>
        /// (or, when a real <c>background-size</c> makes the layer smaller/larger than the box, get rendered
        /// once into a <see cref="RGraphics.CreateTile"/> tile and then positioned/repeated exactly like a
        /// url() image); URL images are drawn directly via <see cref="BackgroundImageDrawHandler"/>.
        /// The <paramref name="isFirst"/> flag gates URL painting (URL images only appear on the first rectangle).
        /// </summary>
        public static void Paint(
            RGraphics g,
            CssImage image,
            int layerIndex,
            bool isFirst,
            RRect originRect,
            RRect clipRect,
            RGraphicsPath? roundedClipPath,
            string positionList,
            string sizeList,
            string repeat,
            CssBoxProperties box,
            Action<RBrush> drawBrush)
        {
            switch (image)
            {
                case CssImage.Url urlImage when isFirst && urlImage.Image != null:
                {
                    var sizeValue = BackgroundLayerResolver.LayerAt(BackgroundLayerResolver.SplitLayers(sizeList), layerIndex);
                    var positionValue = BackgroundLayerResolver.LayerAt(BackgroundLayerResolver.SplitLayers(positionList), layerIndex);
                    BackgroundImageDrawHandler.DrawBackgroundImage(
                        g, urlImage.Image, sizeValue, positionValue, repeat, originRect, clipRect, roundedClipPath, box);
                    break;
                }
                case CssImage.LinearGradient lg:
                    PaintGradientLayer(g, originRect, clipRect, roundedClipPath, layerIndex, sizeList, positionList, repeat, box,
                        (brushGraphics, rect) => GetLinearGradientBrush(brushGraphics, lg.Gradient, rect), drawBrush);
                    break;
                case CssImage.RadialGradient rg:
                    PaintGradientLayer(g, originRect, clipRect, roundedClipPath, layerIndex, sizeList, positionList, repeat, box,
                        (brushGraphics, rect) => GetRadialGradientBrush(brushGraphics, rg.Gradient, rect), drawBrush);
                    break;
                case CssImage.ConicGradient cg:
                    PaintGradientLayer(g, originRect, clipRect, roundedClipPath, layerIndex, sizeList, positionList, repeat, box,
                        (brushGraphics, rect) => GetConicGradientBrush(brushGraphics, cg.Gradient, rect), drawBrush);
                    break;
            }
        }

        /// <summary>
        /// Resolves this gradient layer's background-size against <paramref name="originRect"/>. When it
        /// equals the full box (the common case - no explicit background-size, or cover/contain/auto,
        /// which are all equivalent to 100%/100% for a "generated image" with no intrinsic ratio), keeps
        /// the existing untiled full-box brush fill unchanged. Otherwise renders the gradient once into a
        /// tile sized to the resolved layer size and hands it to <see cref="BackgroundImageDrawHandler"/>
        /// so it gets positioned and repeated exactly like a url() image.
        /// </summary>
        private static void PaintGradientLayer(
            RGraphics g,
            RRect originRect, RRect clipRect, RGraphicsPath? roundedClipPath,
            int layerIndex, string sizeList, string positionList, string repeat,
            CssBoxProperties box,
            Func<RGraphics, RRect, RBrush> createBrush,
            Action<RBrush> drawBrush)
        {
            var sizeValue = BackgroundLayerResolver.LayerAt(BackgroundLayerResolver.SplitLayers(sizeList), layerIndex);
            var (tileWidth, tileHeight) = BackgroundLayerResolver.ResolveSize(
                sizeValue, originRect.Width, originRect.Height, null, null, null, box);

            if (tileWidth <= 0 || tileHeight <= 0)
                return;

            const double epsilon = 0.01;
            var isFullBox = Math.Abs(tileWidth - originRect.Width) < epsilon && Math.Abs(tileHeight - originRect.Height) < epsilon;
            if (isFullBox)
            {
                drawBrush(createBrush(g, originRect));
                return;
            }

            var tile = g.CreateTile(tileWidth, tileHeight);
            if (tile is not { } t)
            {
                // No real page/document context to own a Form XObject in (e.g. a measure-only pass) -
                // degrade gracefully to the untiled full-box brush rather than losing the layer entirely.
                drawBrush(createBrush(g, originRect));
                return;
            }

            var tileRect = new RRect(0, 0, tileWidth, tileHeight);
            using (var tileBrush = createBrush(t.Graphics, tileRect))
            {
                t.Graphics.DrawRectangle(tileBrush, 0, 0, tileWidth, tileHeight);
            }
            t.Graphics.Dispose();

            // "auto" here resolves to the tile's own intrinsic (natural) size via
            // BackgroundLayerResolver.ResolveSize's both-auto branch below - the tile was already
            // rendered at exactly the resolved layer size above, so it must be placed/repeated at
            // that same natural size, not re-stretched to the container (which "100% 100%" would do).
            var positionValue = BackgroundLayerResolver.LayerAt(BackgroundLayerResolver.SplitLayers(positionList), layerIndex);
            BackgroundImageDrawHandler.DrawBackgroundImage(
                g, t.Image, CssConstants.Auto, positionValue, repeat, originRect, clipRect, roundedClipPath, box);
        }

        private static RBrush GetLinearGradientBrush(RGraphics g, ParsedLinearGradient gradient, RRect originRect)
        {
            var (p1, p2) = ComputeGradientLine(originRect, gradient.AngleRad);
            double gdx = p2.X - p1.X, gdy = p2.Y - p1.Y;
            double gradientLength = Math.Sqrt(gdx * gdx + gdy * gdy);
            var stops = NormalizeGradientStops(gradient.Stops, gradientLength, gradient.ColorSpace, gradient.HueMethod);
            if (gradient.IsRepeating) stops = ExpandRepeatingStops(stops);
            return g.GetLinearGradientBrush(p1, p2, stops, gradient.IsRepeating);
        }

        private static RBrush GetRadialGradientBrush(RGraphics g, ParsedRadialGradient radialGradient, RRect originRect)
        {
            var center = new RPoint(
                originRect.X + radialGradient.CenterX * originRect.Width,
                originRect.Y + radialGradient.CenterY * originRect.Height);

            double cx = radialGradient.CenterX * originRect.Width;
            double cy = radialGradient.CenterY * originRect.Height;
            double dxNear = Math.Min(cx, originRect.Width - cx);
            double dxFar  = Math.Max(cx, originRect.Width - cx);
            double dyNear = Math.Min(cy, originRect.Height - cy);
            double dyFar  = Math.Max(cy, originRect.Height - cy);

            double radiusX, radiusY;
            if (radialGradient.ExplicitRadiusX.HasValue)
            {
                var rx = radialGradient.ExplicitRadiusX.Value;
                radiusX = rx.Type == Length.Unit.Percent
                    ? rx.Value / 100.0 * originRect.Width
                    : rx.ToPixel();
                if (radialGradient.ExplicitRadiusY.HasValue)
                {
                    var ry = radialGradient.ExplicitRadiusY.Value;
                    radiusY = ry.Type == Length.Unit.Percent
                        ? ry.Value / 100.0 * originRect.Height
                        : ry.ToPixel();
                }
                else
                {
                    radiusY = radiusX;
                }
            }
            else
            {
                switch (radialGradient.Size)
                {
                    case RadialGradientSize.ClosestSide:
                        if (radialGradient.IsCircle)
                        {
                            double r = Math.Min(dxNear, dyNear);
                            radiusX = r; radiusY = r;
                        }
                        else { radiusX = dxNear; radiusY = dyNear; }
                        break;

                    case RadialGradientSize.FarthestSide:
                        if (radialGradient.IsCircle)
                        {
                            double r = Math.Max(dxFar, dyFar);
                            radiusX = r; radiusY = r;
                        }
                        else { radiusX = dxFar; radiusY = dyFar; }
                        break;

                    case RadialGradientSize.ClosestCorner:
                        if (radialGradient.IsCircle)
                        {
                            double r = Math.Sqrt(dxNear * dxNear + dyNear * dyNear);
                            radiusX = r; radiusY = r;
                        }
                        else { radiusX = Math.Sqrt(2.0) * dxNear; radiusY = Math.Sqrt(2.0) * dyNear; }
                        break;

                    default: // FarthestCorner
                        if (radialGradient.IsCircle)
                        {
                            double r = Math.Sqrt(dxFar * dxFar + dyFar * dyFar);
                            radiusX = r; radiusY = r;
                        }
                        else { radiusX = Math.Sqrt(2.0) * dxFar; radiusY = Math.Sqrt(2.0) * dyFar; }
                        break;
                }
            }

            var radialStops = NormalizeGradientStops(radialGradient.Stops, radiusX, radialGradient.ColorSpace, radialGradient.HueMethod);
            if (radialGradient.IsRepeating) radialStops = ExpandRepeatingStops(radialStops);
            return g.GetRadialGradientBrush(center, radiusX, radiusY, radialStops, radialGradient.IsRepeating);
        }

        private static RBrush GetConicGradientBrush(RGraphics g, ParsedConicGradient conicGradient, RRect originRect)
        {
            double cx = originRect.X + conicGradient.CenterX * originRect.Width;
            double cy = originRect.Y + conicGradient.CenterY * originRect.Height;
            var conicCenter = new RPoint(cx, cy);

            double dxFar = Math.Max(conicGradient.CenterX * originRect.Width,
                                    (1.0 - conicGradient.CenterX) * originRect.Width);
            double dyFar = Math.Max(conicGradient.CenterY * originRect.Height,
                                    (1.0 - conicGradient.CenterY) * originRect.Height);
            double outerRadius = Math.Sqrt(dxFar * dxFar + dyFar * dyFar);

            var (conicColors, conicAngles) = NormalizeConicStops(conicGradient);
            return g.GetConicGradientBrush(conicCenter, outerRadius, conicColors, conicAngles);
        }

        private static (RPoint p1, RPoint p2) ComputeGradientLine(RRect rect, double angleRad)
        {
            double dx = Math.Sin(angleRad);
            double dy = -Math.Cos(angleRad);
            double cx = rect.X + rect.Width / 2;
            double cy = rect.Y + rect.Height / 2;
            double halfLen = Math.Abs(dx) * rect.Width / 2 + Math.Abs(dy) * rect.Height / 2;
            var p1 = new RPoint(cx - dx * halfLen, cy - dy * halfLen);
            var p2 = new RPoint(cx + dx * halfLen, cy + dy * halfLen);
            return (p1, p2);
        }

        private static double? ConvertLength(Length? length, double gradientLength, double emPx = 16.0)
        {
            if (!length.HasValue) return null;
            var len = length.Value;
            if (len.Type == Length.Unit.Percent)
                return len.Value / 100.0;
            if (len.IsAbsolute)
                return gradientLength > 0 ? len.ToPixel() / gradientLength : 0.0;
            if (len.Type == Length.Unit.Em)
                return gradientLength > 0 ? len.Value * emPx / gradientLength : 0.0;
            return null;
        }

        private static RColor LerpColor(RColor a, RColor b, double t)
        {
            t = Math.Clamp(t, 0.0, 1.0);
            return RColor.FromArgb(
                (int)Math.Round(a.A + t * (b.A - a.A)),
                (int)Math.Round(a.R + t * (b.R - a.R)),
                (int)Math.Round(a.G + t * (b.G - a.G)),
                (int)Math.Round(a.B + t * (b.B - a.B)));
        }

        private static (RColor Color, double Position)[] ApplyColorSpaceInterpolation(
            (RColor Color, double Position)[] stops,
            GradientColorSpace colorSpace,
            HueInterpolationMethod hueMethod)
        {
            if (colorSpace == GradientColorSpace.Srgb || stops.Length < 2)
                return stops;

            const int kSamples = 15;
            var result = new List<(RColor, double)>(stops.Length * (kSamples + 1));
            result.Add(stops[0]);
            for (int i = 1; i < stops.Length; i++)
            {
                var (c1, p1) = stops[i - 1];
                var (c2, p2) = stops[i];
                for (int k = 1; k <= kSamples; k++)
                {
                    double t = (double)k / (kSamples + 1);
                    result.Add((ColorSpaceConverter.Interpolate(c1, c2, t, colorSpace, hueMethod),
                                p1 + t * (p2 - p1)));
                }
                result.Add(stops[i]);
            }
            return result.ToArray();
        }

        private static (RColor[] Colors, double[] AnglesRad) ApplyConicColorSpaceInterpolation(
            List<RColor> colors,
            List<double> angles,
            GradientColorSpace colorSpace,
            HueInterpolationMethod hueMethod)
        {
            if (colorSpace == GradientColorSpace.Srgb || colors.Count < 2)
                return (colors.ToArray(), angles.ToArray());

            const int kSamples = 15;
            var outC = new List<RColor>(colors.Count * (kSamples + 1));
            var outA = new List<double>(angles.Count * (kSamples + 1));
            outC.Add(colors[0]); outA.Add(angles[0]);
            for (int i = 1; i < colors.Count; i++)
            {
                for (int k = 1; k <= kSamples; k++)
                {
                    double t = (double)k / (kSamples + 1);
                    outC.Add(ColorSpaceConverter.Interpolate(colors[i - 1], colors[i], t, colorSpace, hueMethod));
                    outA.Add(angles[i - 1] + t * (angles[i] - angles[i - 1]));
                }
                outC.Add(colors[i]); outA.Add(angles[i]);
            }
            return (outC.ToArray(), outA.ToArray());
        }

        private static (RColor[] Colors, double[] AnglesRad) NormalizeConicStops(ParsedConicGradient g)
        {
            const double TwoPi = 2.0 * Math.PI;
            var rawStops = g.Stops.Where(s => !s.IsHint).ToArray();
            int n = rawStops.Length;

            var pos = new double[n];
            pos[0]     = rawStops[0].PositionRad ?? 0.0;
            pos[n - 1] = rawStops[n - 1].PositionRad ?? TwoPi;

            int runStart = -1;
            for (int i = 1; i < n - 1; i++)
            {
                if (rawStops[i].PositionRad.HasValue)
                {
                    pos[i] = rawStops[i].PositionRad!.Value;
                    if (runStart >= 0)
                    {
                        double pA = pos[runStart - 1], pB = pos[i];
                        int count = i - runStart + 1;
                        for (int j = runStart; j < i; j++)
                            pos[j] = pA + (pB - pA) * (j - runStart + 1) / count;
                        runStart = -1;
                    }
                }
                else
                {
                    if (runStart < 0) runStart = i;
                }
            }
            if (runStart >= 0)
            {
                double pA = pos[runStart - 1], pB = pos[n - 1];
                int count = n - 1 - runStart + 1;
                for (int j = runStart; j < n - 1; j++)
                    pos[j] = pA + (pB - pA) * (j - runStart + 1) / count;
            }

            var colors  = new List<RColor>();
            var angles  = new List<double>();
            int colorIdx = 0;

            for (int i = 0; i < g.Stops.Length; i++)
            {
                if (!g.Stops[i].IsHint)
                {
                    colors.Add(rawStops[colorIdx].Color!.Value);
                    angles.Add(g.FromAngleRad + pos[colorIdx]);
                    colorIdx++;
                }
                else
                {
                    if (colorIdx == 0 || colorIdx >= n) continue;
                    var s1Color = rawStops[colorIdx - 1].Color!.Value;
                    var s2Color = rawStops[colorIdx].Color!.Value;
                    double p1 = pos[colorIdx - 1], p2 = pos[colorIdx];
                    double range = p2 - p1;
                    double hintPos = g.Stops[i].PositionRad ?? (p1 + range * 0.5);
                    double h = range > 1e-9
                        ? Math.Clamp((hintPos - p1) / range, 1e-9, 1.0 - 1e-9)
                        : 0.5;
                    double logHalf = Math.Log(0.5), logH = Math.Log(h);
                    const int kSteps = 7;
                    for (int k = 1; k <= kSteps; k++)
                    {
                        double t = (double)k / (kSteps + 1);
                        double curved = Math.Pow(t, logHalf / logH);
                        colors.Add(LerpColor(s1Color, s2Color, curved));
                        angles.Add(g.FromAngleRad + p1 + t * range);
                    }
                }
            }

            if (g.ColorSpace != GradientColorSpace.Srgb)
            {
                var (csColors, csAngles) = ApplyConicColorSpaceInterpolation(colors, angles, g.ColorSpace, g.HueMethod);
                colors = new List<RColor>(csColors);
                angles = new List<double>(csAngles);
            }

            if (g.IsRepeating && n >= 2)
            {
                double tileStart = g.FromAngleRad + pos[0];
                double tileEnd   = g.FromAngleRad + pos[n - 1];
                double tileLen   = tileEnd - tileStart;
                if (tileLen > 1e-6 && tileLen < TwoPi - 1e-6)
                {
                    var allC = new List<RColor>();
                    var allA = new List<double>();
                    int kMin = (int)Math.Floor((g.FromAngleRad - tileEnd) / tileLen);
                    int kMax = (int)Math.Ceiling((g.FromAngleRad + TwoPi - tileStart) / tileLen);
                    const double eps = 0.0001;
                    for (int k = kMin; k <= kMax; k++)
                    {
                        double kOffset = k * tileLen;
                        for (int i = 0; i < colors.Count; i++)
                        {
                            double raw = angles[i] + kOffset;
                            bool isLast = i == colors.Count - 1;
                            double adj = isLast && k < kMax ? raw - eps : raw;
                            double lo = g.FromAngleRad, hi = g.FromAngleRad + TwoPi;
                            if (adj >= lo - eps && adj <= hi + eps)
                            {
                                allC.Add(colors[i]);
                                allA.Add(Math.Clamp(adj, lo, hi));
                            }
                        }
                    }
                    if (allC.Count >= 2)
                    {
                        colors = allC;
                        angles = allA;
                    }
                }
            }

            return (colors.ToArray(), angles.ToArray());
        }

        private static (RColor Color, double Position)[] ExpandRepeatingStops((RColor Color, double Position)[] stops)
        {
            if (stops.Length < 2) return stops;
            double tileStart = stops[0].Position;
            double tileEnd   = stops[^1].Position;
            double tileLen   = tileEnd - tileStart;
            if (tileLen < 1e-6 || (tileStart <= 0.0 && tileEnd >= 1.0)) return stops;
            const double eps = 0.0001;
            var result = new List<(RColor Color, double Position)>();
            int kMin = (int)Math.Floor(-tileEnd / tileLen);
            int kMax = (int)Math.Ceiling((1.0 - tileStart) / tileLen);
            for (int k = kMin; k <= kMax; k++)
            {
                double kOffset = k * tileLen;
                for (int i = 0; i < stops.Length; i++)
                {
                    double rawPos = stops[i].Position + kOffset;
                    bool isLastStop = i == stops.Length - 1;
                    double adjPos = isLastStop && k < kMax ? rawPos - eps : rawPos;
                    if (adjPos >= -eps && adjPos <= 1.0 + eps)
                        result.Add((stops[i].Color, Math.Clamp(adjPos, 0.0, 1.0)));
                }
            }
            result.Sort((a, b) => a.Position.CompareTo(b.Position));
            if (result.Count == 0) return stops;
            if (result[0].Position > eps)
                result.Insert(0, (SampleRepeatingColor(stops, tileStart, tileLen, 0.0), 0.0));
            if (result[^1].Position < 1.0 - eps)
                result.Add((SampleRepeatingColor(stops, tileStart, tileLen, 1.0), 1.0));
            return result.ToArray();
        }

        private static RColor SampleRepeatingColor((RColor Color, double Position)[] stops, double tileStart, double tileLen, double pos)
        {
            double relPos = (pos - tileStart) % tileLen;
            if (relPos < 0) relPos += tileLen;
            double absWithinTile = tileStart + relPos;
            for (int i = 0; i < stops.Length - 1; i++)
            {
                if (absWithinTile >= stops[i].Position && absWithinTile <= stops[i + 1].Position)
                {
                    double range = stops[i + 1].Position - stops[i].Position;
                    double t = range > 1e-12 ? (absWithinTile - stops[i].Position) / range : 0.0;
                    return LerpColor(stops[i].Color, stops[i + 1].Color, t);
                }
            }
            return absWithinTile <= stops[0].Position ? stops[0].Color : stops[^1].Color;
        }

        private static (RColor Color, double Position)[] NormalizeGradientStops(
            (RColor? Color, Length? Position, bool IsHint)[] stops,
            double gradientLength,
            GradientColorSpace colorSpace = GradientColorSpace.Srgb,
            HueInterpolationMethod hueMethod = HueInterpolationMethod.Shorter,
            double emPx = 16.0)
        {
            var colorStops = stops.Where(s => !s.IsHint).ToArray();
            int n = colorStops.Length;
            if (n == 0) return Array.Empty<(RColor, double)>();

            var rawPos = new double?[n];
            for (int i = 0; i < n; i++)
                rawPos[i] = ConvertLength(colorStops[i].Position, gradientLength, emPx);

            var resolved = new (RColor Color, double Position)[n];
            double first = rawPos[0] ?? 0.0;
            double last  = rawPos[n - 1] ?? 1.0;
            resolved[0]     = (colorStops[0].Color!.Value, first);
            resolved[n - 1] = (colorStops[n - 1].Color!.Value, last);

            int runStart = -1;
            for (int i = 1; i < n - 1; i++)
            {
                if (rawPos[i].HasValue)
                {
                    resolved[i] = (colorStops[i].Color!.Value, rawPos[i]!.Value);
                    if (runStart >= 0)
                    {
                        double posA = resolved[runStart - 1].Position;
                        double posB = resolved[i].Position;
                        int count = i - runStart + 1;
                        for (int j = runStart; j < i; j++)
                        {
                            double t = (double)(j - runStart + 1) / count;
                            resolved[j] = (colorStops[j].Color!.Value, posA + t * (posB - posA));
                        }
                        runStart = -1;
                    }
                }
                else
                {
                    if (runStart < 0) runStart = i;
                    resolved[i] = (colorStops[i].Color!.Value, 0);
                }
            }
            if (runStart >= 0)
            {
                double posA = resolved[runStart - 1].Position;
                double posB = resolved[n - 1].Position;
                int count = n - 1 - runStart + 1;
                for (int j = runStart; j < n - 1; j++)
                {
                    double t = (double)(j - runStart + 1) / count;
                    resolved[j] = (colorStops[j].Color!.Value, posA + t * (posB - posA));
                }
            }

            if (!stops.Any(s => s.IsHint))
                return ApplyColorSpaceInterpolation(resolved, colorSpace, hueMethod);

            var result = new List<(RColor Color, double Position)>();
            int colorIdx = 0;
            for (int i = 0; i < stops.Length; i++)
            {
                if (!stops[i].IsHint)
                {
                    result.Add(resolved[colorIdx++]);
                }
                else
                {
                    if (colorIdx == 0 || colorIdx >= n) continue;
                    var s1 = resolved[colorIdx - 1];
                    var s2 = resolved[colorIdx];
                    double range = s2.Position - s1.Position;
                    double hintPos = ConvertLength(stops[i].Position, gradientLength, emPx) ?? (s1.Position + range * 0.5);
                    double h = range > 1e-9
                        ? Math.Clamp((hintPos - s1.Position) / range, 1e-9, 1.0 - 1e-9)
                        : 0.5;
                    const int kSteps = 7;
                    double logHalf = Math.Log(0.5);
                    double logH    = Math.Log(h);
                    for (int k = 1; k <= kSteps; k++)
                    {
                        double t       = (double)k / (kSteps + 1);
                        double curved  = Math.Pow(t, logHalf / logH);
                        result.Add((LerpColor(s1.Color, s2.Color, curved), s1.Position + t * range));
                    }
                }
            }
            return ApplyColorSpaceInterpolation(result.ToArray(), colorSpace, hueMethod);
        }
    }
}
