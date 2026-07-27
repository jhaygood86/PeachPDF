using PeachPDF.Html.Core.Dom;

namespace PeachPDF.Html.Core.Fragmentation
{
    /// <summary>
    /// Undoes the layout produced <i>after</i> a fragmentainer pass was entered, so that pass can be run
    /// again from the record it was entered with
    /// (<see href="https://www.w3.org/TR/css-break-3/#possible-breaks">css-break-3 §4.3</see>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two callers re-run a pass and so need the same rollback: the multi-column engine, which abandons a
    /// fill attempt when <c>column-fill: balance</c> has to try another target, and the driver, which
    /// re-enters the pass that placed a keep-with-next run's head. Both hand back a box tree that must
    /// look the way it did when that pass began, and "the way it looked" is stated entirely by the
    /// resumption record itself — one link per ancestor, down to the box that stopped.
    /// </para>
    /// <para>
    /// A resumed pass divides a container's children in three, and that division is the whole of it.
    /// Children below the resume point were placed in fragmentainers already filled and still hold the
    /// geometry those fragments were built from: resetting one blanks a fragment already emitted.
    /// Children above it are laid out by the pass from the start, exactly as on a starting pass, and need
    /// the full reset — without it the abandoned attempt's per-line rectangles stay on the box, keyed by
    /// line boxes nothing points at any more. And the child <i>at</i> it is neither: it is continuing, so
    /// only what the abandoned attempt added to it may go
    /// (<see cref="CssBox.DiscardLineBoxesFrom"/>).
    /// </para>
    /// <para>
    /// Skipping the lot is what let a retry re-finalize the abandoned attempt's own line boxes and throw
    /// out of <see cref="CssLineBox.AssignRectanglesToBoxes"/> — once from the columns engine, and once
    /// from the driver's own re-entry.
    /// </para>
    /// </remarks>
    internal static class PassRewind
    {
        /// <summary>
        /// Rolls the box tree back to the state it was in when the pass entered with
        /// <paramref name="resume"/> began.
        /// </summary>
        /// <remarks>
        /// Walks the same chain the resumption itself walks, so at every level the boundary child is told
        /// to discard only what it added while its later siblings, which the pass laid out from the start,
        /// are reset outright. Each link names its own box, so nothing here has to assume an index and a
        /// box agree.
        /// </remarks>
        internal static void RollBackTo(BreakToken resume)
        {
            switch (resume)
            {
                case InlineBreakToken inline:
                    inline.Box.DiscardLineBoxesFrom(inline.CompletedLineCount);
                    break;

                // A break-before names a child the pass lays out from the start, so it is reset like any
                // other rather than descended into: there is no deeper link to follow.
                case BlockBreakToken block:
                    for (var i = block.ResumeChildIndex; i < block.Box.Boxes.Count; i++)
                    {
                        if (i == block.ResumeChildIndex && block.ChildToken is { } childToken)
                        {
                            RollBackTo(childToken);
                        }
                        else
                        {
                            block.Box.Boxes[i].ResetForRefill();
                        }
                    }

                    break;
            }
        }
    }
}
