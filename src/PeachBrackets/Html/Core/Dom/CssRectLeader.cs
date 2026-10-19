namespace PeachPDF.Html.Core.Dom
{
    /// <summary>
    /// Which repeating pattern a <c>leader()</c> content-list item (css-content-3 §6) fills its
    /// resolved width with.
    /// </summary>
    internal enum LeaderKind
    {
        Dotted,
        Solid,
        Space,
        Custom
    }

    /// <summary>
    /// A <c>leader()</c> content-list item, sitting in <see cref="CssBox.Words"/> alongside
    /// <see cref="CssRectWord"/>/<see cref="CssRectImage"/> - an already-established seam for
    /// heterogeneous generated-content items, not a new concept. Unlike every other <see cref="CssRect"/>,
    /// its real <see cref="CssRect.Width"/> is not known at measurement time - it is "however much of the
    /// line remains", decided post-flow by <c>CssLayoutEngine.ApplyLeaderFill</c> once the whole line
    /// (potentially spanning multiple sibling boxes) has finished flowing. See
    /// <see cref="CssBox.MeasureWordsSize"/> (measures as zero) and <c>CssLayoutEngine.FlowBox</c>
    /// (placed as a zero-width, non-breaking token).
    /// </summary>
    internal sealed class CssRectLeader : CssRect
    {
        public CssRectLeader(CssBox owner, LeaderKind kind, string? customPattern) : base(owner)
        {
            Kind = kind;
            CustomPattern = customPattern;
        }

        /// <summary>Which pattern to tile - see <see cref="LeaderKind"/>.</summary>
        public LeaderKind Kind { get; }

        /// <summary>The literal repeating pattern for <see cref="LeaderKind.Custom"/>; null otherwise.</summary>
        public string? CustomPattern { get; }

        public override bool HasSpaceBefore => false;

        public override bool HasSpaceAfter => false;

        public override bool IsSpaces => false;

        public override string ToString() => $"leader({Kind})";
    }
}
