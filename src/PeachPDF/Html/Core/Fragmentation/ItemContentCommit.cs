using PeachPDF.Adapters;
using PeachPDF.CSS;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Utils;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;

namespace PeachPDF.Html.Core.Fragmentation
{
    /// <summary>
    /// Lays a single item's own content out for real, attached to a live fragmentainer, at the
    /// position an engine (flex or grid) has already decided is final for this pass — the shared
    /// primitive behind every engine's "commit pass" (<c>CssLayoutEngineFlex.CommitItemContent</c>,
    /// <c>CssLayoutEngineGrid.CommitItemContent</c>).
    /// </summary>
    /// <remarks>
    /// Originally <c>CssLayoutEngineFlex.PerformCommitLayout</c>/<c>PerformLayoutBlockifiedAtFinalPosition</c>
    /// (issue #430/PR #527) — extracted here, unchanged, once grid needed the identical primitive
    /// (issue #517/#526): nothing in either method is flex-specific, both operate purely on the
    /// <see cref="CssBox"/> passed in.
    /// </remarks>
    internal static class ItemContentCommit
    {
        /// <summary>
        /// Lays <paramref name="box"/>'s own content out, attached to a real fragmentainer rather
        /// than a detached one — the one place in the calling engine breaking is genuinely live for
        /// an item's content.
        /// </summary>
        /// <param name="g">the graphics context layout is running against</param>
        /// <param name="box">the item to lay out</param>
        /// <param name="resume">
        /// the item's own break token from an earlier pass, or null to lay it out from the start.
        /// </param>
        /// <remarks>
        /// <para>
        /// A <b>fresh</b> commit (<paramref name="resume"/> null) pins <paramref name="box"/>'s
        /// <c>Width</c>/<c>Height</c> to its already-resolved outer size
        /// (<see cref="CssBox.ActualBoxSizingWidth"/>/<see cref="CssBox.ActualBoxSizingHeight"/>)
        /// before laying out — every earlier phase already decided this item's size, and
        /// re-deriving it from an "auto" property here (the value every earlier phase temporarily
        /// sets and then reverts, since none of them are the item's <i>final</i> layout) would let
        /// this, genuinely final, layout disagree with the size the rest of the engine's algorithm
        /// already committed to. The value pinned is content-space for <c>content-box</c> (the
        /// outer size minus this box's own padding/border) but the outer size itself for
        /// <c>border-box</c> — <see cref="CssBox.ActualBoxSizeIncludedWidth"/>/<c>Height</c>'s own
        /// box-sizing contract, which a border-box item's <c>Width</c>/<c>Height</c> string must
        /// keep honoring here: unlike <c>Width</c> (only consumed by this box's own content layout,
        /// which needs a content-space wrap bound either way), <c>Height</c> is re-read by every
        /// pass's own epilogue (<see cref="CssLayoutEngine.ApplyHeight"/>), which assigns it straight
        /// to the used outer height — pinning a border-box item's content height there would shrink
        /// it back down by its own padding+border on this, the pass whose result actually ships
        /// (issue #811). Unlike those earlier phases, this pin is <b>not</b> reverted
        /// afterward: a later fragmentainer pass resuming this same item (<paramref name="resume"/>
        /// non-null) must see the same <c>Width</c>/<c>Height</c> the first pass used, or a nested
        /// engine that re-derives its own content box from them
        /// (<c>CssLayoutEngineColumns.Layout</c>'s <c>containerWidth</c>) would size itself
        /// differently pass to pass. <see cref="CssBox.RectanglesReset"/> only runs on the fresh
        /// path too, for the same reason a resumed table cell's continuation must not call it — see
        /// <c>CssBox.PerformLayoutPrologue</c>'s own remarks: it would discard geometry an earlier
        /// fragmentainer has already frozen a fragment around.
        /// </para>
        /// <para>
        /// A <b>resumed</b> commit (<paramref name="resume"/> non-null) instead calls
        /// <see cref="CssBox.ResumeAt"/> — the same primitive a table row loop uses to re-enter a
        /// cell mid content — and touches nothing else.
        /// </para>
        /// </remarks>
        internal static async ValueTask CommitLayout(RGraphics g, CssBox box, BreakToken? resume)
        {
            if (resume is null)
            {
                // box.Width/Height must hold whatever CssBox.ActualBoxSizeIncludedWidth/Height's own
                // box-sizing contract expects: content-space for content-box (subtract its own
                // padding/border back out of the outer size), or the outer size directly for
                // border-box (ActualBoxSizeIncludedWidth/Height is already 0 there, so this is a no-op).
                box.Width = FormatLayoutUnits(Math.Max(0, box.ActualBoxSizingWidth - box.ActualBoxSizeIncludedWidth), box);
                box.Height = FormatLayoutUnits(Math.Max(0, box.ActualBoxSizingHeight - box.ActualBoxSizeIncludedHeight), box);

                box.RectanglesReset();
            }
            else
            {
                box.ResumeAt(resume, resumeTopOverride: null);
            }

            // Every earlier item layout in the calling engine is a measurement, translated into
            // place afterward - the frame's block-flow placement running during one of those is
            // harmless, since the engine's own placement phase overwrites its result unconditionally.
            // This is the item's real, final content layout, with nothing after it to correct a wrong
            // position back, so it is laid out through the entry point that asks the frame for no
            // position at all (see CssBox.LayoutContentAtItsAssignedPosition). The flag says the same
            // thing to the epilogue's own movers, which run after this box is complete.
            box.PositionAssignedByEngine = true;
            try
            {
                await LayoutBlockifiedAtFinalPosition(g, box);
            }
            finally
            {
                box.PositionAssignedByEngine = false;
            }
        }

        /// <summary>
        /// The commit pass's own version of an engine's measurement-only blockify helper: the same
        /// blockify dance (CSS Display 3 §2.3's flex/grid-item requirement), but without detaching
        /// the fragmentainer or suppressing word-level breaking — this is the one item layout that
        /// runs at the item's real, final position, so breaking questions asked during it are
        /// meaningful.
        /// </summary>
        private static async ValueTask LayoutBlockifiedAtFinalPosition(RGraphics g, CssBox box)
        {
            CssProperty<DisplayMode>? savedDisplay = null;
            if (box.IsInline)
            {
                savedDisplay = box.Display;
                box.Display = CssProperty<DisplayMode>.FromValue(Keywords.Block, DisplayMode.Block);
            }

            await box.LayoutContentAtItsAssignedPosition(g);

            if (savedDisplay is not null)
                box.Display = savedDisplay;
        }

        // See CssLayoutEngineFlex.FormatLayoutUnits's identical comment: pre-divide by PixelsPerPoint
        // so a re-parse through CssValueParser's now-PixelsPerPoint-aware absolute-length resolution
        // (issue #814) lands back on this same internal-space value instead of PixelsPerPoint times it.
        private static string FormatLayoutUnits(double value, CssBox box) =>
            (value / ((box.HtmlContainer?.Adapter as PdfSharpAdapter)?.PixelsPerPoint ?? 1.0))
                .ToString("F4", CultureInfo.InvariantCulture) + "pt";

        /// <summary>
        /// Moves each of <paramref name="boxes"/> by <paramref name="delta"/> via a direct
        /// <see cref="CssBox.Location"/> reassignment — <b>not</b> <see cref="CssBox.OffsetLeft(double)"/>/
        /// <see cref="CssBox.OffsetTop(double)"/>, which would translate a box's already-placed content
        /// along with it.
        /// </summary>
        /// <remarks>
        /// Mirrors <see cref="CssBox.ResumeInTheNextFragmentainer"/>'s own choice, for the same reason: a
        /// resumed commit-pass item may already have content frozen in the fragmentainer being left (a
        /// paragraph that placed some lines before stopping), and only the origin new content flows from
        /// should move — the already-frozen lines must stay exactly where they are. This is what a
        /// resumed pass applies to every not-yet-committed item when the container itself moved to a new
        /// fragmentainer (a multicolumn column boundary, most concretely) since the token naming them was
        /// published — see each engine's own <c>ResumeCommitPass</c>.
        /// </remarks>
        internal static void RepositionForResume(IEnumerable<CssBox> boxes, RPoint delta)
        {
            if (delta.X == 0 && delta.Y == 0) return;

            foreach (var box in boxes)
                box.Location = new RPoint(box.Location.X + delta.X, box.Location.Y + delta.Y);
        }
    }
}
