namespace PeachPDF.Html.Core.Fragmentation
{
    /// <summary>
    /// How much of a break decision's ideal shape survived — the staged relaxation
    /// <see href="https://www.w3.org/TR/css-break-3/#possible-breaks">CSS Fragmentation Level 3
    /// §4.3</see> asks for, stated once rather than implied by which arm of layout happened to run first.
    /// </summary>
    /// <remarks>
    /// <para>
    /// §4.3's rule is that a constraint which cannot be satisfied is <b>given up progressively</b>, never
    /// all at once and never at the cost of losing content. PeachPDF's ladder, outermost tier first — each
    /// tier is only reached when the one above it cannot be satisfied:
    /// </para>
    /// <list type="number">
    /// <item><description>
    /// <b>Everything holds.</b> The box moves to its target and the whole keep-with-next run chained to it
    /// (§3.1) moves with it. <see cref="None"/>.
    /// </description></item>
    /// <item><description>
    /// <b>Part of the run is left behind</b> (<see cref="RunTrimmed"/>). The members nearest the breaking
    /// box are what the chain is about — a subheading immediately above a table matters more than the
    /// heading above that — so the run is trimmed from its <i>front</i> until what remains fits the
    /// destination, rather than being dropped whole the moment it does not.
    /// </description></item>
    /// <item><description>
    /// <b>The whole run is left behind</b> (<see cref="RunDropped"/>): no part of it can travel, so the box
    /// moves alone, exactly as if no <c>avoid</c> had been written between them.
    /// </description></item>
    /// <item><description>
    /// <b>The constraint itself is given up.</b> The box is not moved at all and the boundary cuts it —
    /// which is where a monolithic box that fits in no fragmentainer ends up
    /// (<c>CssBox.PerformLayoutEpilogue</c>; see
    /// <see href="https://github.com/jhaygood86/PeachPDF/issues/350">#350</see> for why overflowing instead
    /// would discard pages), and where a box inside an engine that positions its own children ends up
    /// (<c>CanBeLaidOutAgain</c>).
    /// </description></item>
    /// <item><description>
    /// <b>Break anywhere</b>, so content is never lost: the driver's own no-progress backstop lays the
    /// remainder out monolithically when a pass reproduces the record it was handed
    /// (<c>HtmlContainerInt.LayoutDocument</c>). This is §4.3's last resort, and it is the reason the tiers
    /// above are allowed to refuse.
    /// </description></item>
    /// </list>
    /// <para>
    /// <b>Relaxation must keep the decision terminating.</b> Every tier here either moves the box <i>once</i>
    /// or declines to move it — none of them re-asks the question. A box that fits nowhere still does not fit
    /// at its new top, so laying it out again there breaks again from that top, which opens another pass,
    /// which re-asks: measured at 100,000 pages when #332 landed. That is why tier 4 leaves the box alone
    /// rather than moving it somewhere it also does not fit, and why a box takes at most one correction per
    /// fragmentainer pass.
    /// </para>
    /// </remarks>
    internal enum BreakRelaxation
    {
        /// <summary>Nothing was given up.</summary>
        None,

        /// <summary>The earliest members of the keep-with-next run were left behind so the rest could travel.</summary>
        RunTrimmed,

        /// <summary>No part of the keep-with-next run could travel, so the box moves alone.</summary>
        RunDropped
    }
}
