namespace PeachBrackets.Css
{
    /// <summary>
    /// The keywords of <c>pointer-events</c>: <c>auto</c> and <c>none</c> apply to every element (CSS Basic User Interface 4), the rest
    /// choose which parts of an SVG shape receive pointer events (SVG 2 section 16.4).
    /// </summary>
    internal enum PointerEvents : byte
    {
        Auto,
        None,
        VisiblePainted,
        VisibleFill,
        VisibleStroke,
        Visible,
        Painted,
        Fill,
        Stroke,
        All
    }
}
