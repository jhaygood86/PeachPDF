namespace PeachDrawing.Svg;

/// <summary>How an <see cref="SvgCanvas"/> writes a conic gradient, which SVG has no paint server for.</summary>
public enum SvgConicGradientMode
{
    /// <summary>A fan of 360 thin wedges, one degree each, clipped to the shape (or, for a stroke, painted through a mask of it). Every viewer draws it; a very large gradient shows the steps.</summary>
    Wedges = 0,

    /// <summary>
    /// A CSS <c>conic-gradient()</c> in a <c>&lt;foreignObject&gt;</c> - exact, smooth and as large as it is drawn - for a viewer that supports it (browsers).
    /// The wedges follow in the same <c>&lt;switch&gt;</c> as the fallback for every other viewer, which ignore the foreign content.
    /// </summary>
    Css = 1,
}
