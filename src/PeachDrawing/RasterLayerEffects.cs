using PeachDrawing.Core;
using PeachDrawing.Filters;
using System;
using System.Collections.Generic;

namespace PeachDrawing;

/// <summary>
/// Applies the <see cref="LayerEffect"/>s of a <see cref="LayerOptions"/> to the pixels of a raster surface. A <see cref="Canvas"/>
/// that draws layers through raster surfaces calls this from its <c>ApplyLayerEffects</c>; <see cref="RasterCanvas"/> does.
/// </summary>
public static class RasterLayerEffects
{
    /// <summary>
    /// How far, on each axis, the given effects can spread ink beyond the shape they are applied to, in the canvas's units: three
    /// standard deviations of a blur (past which a Gaussian is invisible), plus an offset for a drop shadow. Grow a layer's region
    /// by this before drawing into it, so the spread is not cut off.
    /// </summary>
    /// <param name="effects">the effects that will be applied</param>
    /// <returns>the margin to add on the left and right, and on the top and bottom</returns>
    /// <exception cref="ArgumentNullException"><paramref name="effects"/> is <see langword="null"/></exception>
    public static (double X, double Y) GetInkMargin(IReadOnlyList<LayerEffect> effects)
    {
        ArgumentNullException.ThrowIfNull(effects);

        double x = 0, y = 0;
        foreach (var effect in effects)
        {
            switch (effect)
            {
                case BlurEffect blur:
                    x += 3 * Math.Max(0, blur.SigmaX);
                    y += 3 * Math.Max(0, blur.SigmaY);
                    break;
                case DropShadowEffect shadow:
                    x += Math.Abs(shadow.OffsetX) + 3 * Math.Max(0, shadow.SigmaX);
                    y += Math.Abs(shadow.OffsetY) + 3 * Math.Max(0, shadow.SigmaY);
                    break;
            }
        }

        return (x, y);
    }

    /// <summary>Applies <paramref name="effects"/> to <paramref name="surface"/>, in order, changing it in place.</summary>
    /// <param name="surface">the pixels to change</param>
    /// <param name="effects">the effects to apply</param>
    /// <exception cref="ArgumentNullException">an argument is <see langword="null"/></exception>
    public static void Apply(RasterSurface surface, IReadOnlyList<LayerEffect> effects)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(effects);

        foreach (var effect in effects)
        {
            switch (effect)
            {
                case BlurEffect blur:
                    GaussianBlur.Apply(surface, blur.SigmaX * surface.PixelsPerUnitX, blur.SigmaY * surface.PixelsPerUnitY);
                    break;
                case DropShadowEffect shadow:
                    DropShadow.Apply(surface,
                        (int)Math.Round(shadow.OffsetX * surface.PixelsPerUnitX), (int)Math.Round(shadow.OffsetY * surface.PixelsPerUnitY),
                        shadow.SigmaX * surface.PixelsPerUnitX, shadow.SigmaY * surface.PixelsPerUnitY,
                        shadow.Color.R, shadow.Color.G, shadow.Color.B, shadow.Color.A);
                    break;
                case ColorMatrixEffect matrix:
                    ColorMatrixFilter.ApplyInPlace(surface, matrix.Matrix);
                    break;
            }
        }
    }
}
