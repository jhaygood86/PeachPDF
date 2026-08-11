namespace PeachPDF.CSS
{
    /// <summary>
    /// <c>outline-style</c>'s own value set — <c>&lt;'border-style'&gt; | auto</c> (CSS Basic User
    /// Interface 4 §4) — kept separate from <see cref="LineStyle"/> (used by every <c>border-*-style</c>
    /// and <c>column-rule-style</c>) so that <c>auto</c> can be a real, paintable value for outline
    /// without also becoming a silently-accepted value for border, which has no <c>auto</c> keyword.
    /// </summary>
    internal enum OutlineStyle : byte
    {
        None,
        Hidden,
        Dotted,
        Dashed,
        Solid,
        Double,
        Groove,
        Ridge,
        Inset,
        Outset,
        Auto
    }
}
