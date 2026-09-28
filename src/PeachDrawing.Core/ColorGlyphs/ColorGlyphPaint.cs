using System;
using System.Collections.Generic;

namespace PeachDrawing.Core.ColorGlyphs
{
    /// <summary>
    /// A fully resolved fill a <see cref="ColorGlyphPainter"/> asks its <see cref="IColorGlyphTarget"/> to make: palette
    /// entries already turned into colors, gradient geometry already mapped into the target's space and stop lists already
    /// sorted. A target maps it onto whatever gradient machinery it has.
    /// </summary>
    public abstract record ColorGlyphPaint
    {
        private protected ColorGlyphPaint()
        {
        }
    }

    /// <summary>A single color.</summary>
    /// <param name="Color">the color, alpha included</param>
    public sealed record SolidColorGlyphPaint(PaintColor Color) : ColorGlyphPaint;

    /// <summary>A gradient along the line from <c>Start</c> to <c>End</c>, padded beyond both ends.</summary>
    /// <param name="Start">the point where the first stop is reached, in target space</param>
    /// <param name="End">the point where the last stop is reached, in target space</param>
    /// <param name="Colors">the stop colors, in ascending <paramref name="Positions"/> order</param>
    /// <param name="Positions">each stop's position along the line, 0 at <paramref name="Start"/> and 1 at <paramref name="End"/></param>
    public sealed record LinearColorGlyphPaint(PaintPoint Start, PaintPoint End, IReadOnlyList<PaintColor> Colors, IReadOnlyList<double> Positions) : ColorGlyphPaint;

    /// <summary>A gradient that radiates from a focal point out to a circle, padded beyond it.</summary>
    /// <param name="Center">the center of the outer circle, in target space</param>
    /// <param name="Focal">the point where the first stop is reached, in target space</param>
    /// <param name="Radius">the outer circle's radius, in target units</param>
    /// <param name="Colors">the stop colors, in ascending <paramref name="Positions"/> order</param>
    /// <param name="Positions">each stop's position, 0 at <paramref name="Focal"/> and 1 on the circle</param>
    public sealed record RadialColorGlyphPaint(PaintPoint Center, PaintPoint Focal, double Radius, IReadOnlyList<PaintColor> Colors, IReadOnlyList<double> Positions) : ColorGlyphPaint;

    /// <summary>A gradient that sweeps around a center, like a conic gradient.</summary>
    /// <param name="Center">the sweep's center, in target space</param>
    /// <param name="Radius">a radius large enough to cover the glyph, in target units</param>
    /// <param name="Colors">the stop colors, in ascending <paramref name="AnglesRadians"/> order</param>
    /// <param name="AnglesRadians">each stop's angle in radians, measured clockwise from straight up in the target's own visual sense</param>
    public sealed record SweepColorGlyphPaint(PaintPoint Center, double Radius, IReadOnlyList<PaintColor> Colors, IReadOnlyList<double> AnglesRadians) : ColorGlyphPaint;
}
