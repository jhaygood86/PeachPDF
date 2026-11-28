using PeachBrackets.Svg;
using PeachDrawing.Core;
using System;

namespace PeachBrackets.ScriptHosting;

/// <summary>An SVG <c>filter</c> element of the document, ready to be applied to the pixels of a canvas's drawing (what <c>url(#id)</c> in a canvas's <c>filter</c> names).</summary>
public sealed class ScriptSvgFilter
{
    private readonly SvgFilter _filter;

    internal ScriptSvgFilter(SvgFilter filter) => _filter = filter;

    /// <summary>Runs the filter over <paramref name="surface"/>, which is replaced by the result.</summary>
    /// <param name="canvas">the canvas the surface is for, which the filter's primitives that paint (an image, a fill) draw on</param>
    /// <param name="surface">the pixels to filter</param>
    /// <param name="region">the area the surface covers, in the canvas's units, which the filter's own region is taken against</param>
    /// <exception cref="ArgumentNullException">an argument is <see langword="null"/></exception>
    public void Apply(Canvas canvas, RasterSurface surface, Rect region)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(surface);

        SvgRasterFilterEvaluator.ApplyTo(canvas, _filter, surface, region);
    }
}
