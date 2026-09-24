// "Therefore those skilled at the unorthodox
// are infinite as heaven and earth,
// inexhaustible as the great rivers.
// When they come to an end,
// they begin again,
// like the days and months;
// they die and are reborn,
// like the four seasons."
// 
// - Sun Tsu,
// "The Art of War"

using PeachPDF.CSS;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Entities;
using PeachPDF.Html.Core.Utils;
using System;
using System.Collections.Generic;

namespace PeachPDF.Html.Core.Dom
{
    /// <summary>
    /// Represents a line of text.
    /// </summary>
    /// <remarks>
    /// To learn more about line-boxes see CSS spec:
    /// http://www.w3.org/TR/CSS21/visuren.html
    /// </remarks>
    internal sealed class CssLineBox
    {
        /// <summary>
        /// For a column of a vertical writing-mode box (<c>CssLayoutEngine.CreateVerticalLineBoxes</c>): how far a
        /// float pinned to the physical top edge reaches into the column, and how far one pinned to the bottom
        /// edge does. Text alignment and bidi reordering work within the span these leave.
        /// </summary>
        internal double VerticalTopInset { get; set; }

        /// <inheritdoc cref="VerticalTopInset"/>
        internal double VerticalBottomInset { get; set; }

        /// <summary>
        /// Creates a new LineBox
        /// </summary>
        public CssLineBox(CssBox ownerBox)
        {
            Rectangles = [];
            Words = [];
            OwnerBox = ownerBox;
            OwnerBox.LineBoxes.Add(this);
        }

        /// <summary>
        /// The position in <c>CssLayoutEngine.FlowBox</c>'s walk of this line's first word — the same
        /// quantity an <see cref="Fragmentation.InlineBreakToken"/> resumes at.
        /// </summary>
        /// <remarks>
        /// Recorded so a flow can be re-entered <i>at a line that has already been laid out</i>, which is
        /// what §5.4's <c>widows</c> needs: how many lines fall after a break is only known once the box
        /// completes, so satisfying it means going back and keeping fewer lines in the fragment before it.
        /// A resumed pass leaves earlier lines untouched, so each line keeps the ordinal of the pass that
        /// produced it.
        /// </remarks>
        public int StartOrdinal { get; internal set; }

        /// <summary>
        /// Whether this line was created because a forced-break word (<see cref="CssRect.IsLineBreak"/> -
        /// the synthetic <c>"\n"</c> marker from a <c>&lt;br&gt;</c> or a preserved newline) ended the
        /// previous line, as opposed to a natural/no-wrap-box wrap. Defaults false (the seed line of a
        /// block, or a fragmentainer's resumed opening line, is never a forced break). This is the signal
        /// <c>text-indent: each-line</c> needs (CSS Text 3 §3) - see <c>CssLayoutEngine.GetLineTextIndent</c>.
        /// </summary>
        public bool FollowsForcedBreak { get; internal set; }

        /// <summary>
        /// The mirror image: whether a forced-break word ended <i>this</i> line, i.e. this is the last line
        /// before a forced break. Set by <c>CssLayoutEngine.FlowBox</c>/<c>CreateVerticalLineBoxes</c> at the
        /// moment the break closes the line, not derived by looking ahead at the next one - so it survives a
        /// fragmentation break that discards the line the break opened, and costs no per-line scan.
        /// <see href="https://www.w3.org/TR/css-text-3/#text-align-property">css-text-3 §6.1</see> ends a
        /// paragraph here as much as at the block's last line, so this line aligns per <c>text-align-last</c>
        /// (§6.3) rather than being stretched by <c>text-align: justify</c>.
        /// </summary>
        public bool PrecedesForcedBreak { get; internal set; }

        /// <summary>
        /// The content-box right edge this line was wrapped against — css-break-3 §5.1's per-fragmentainer
        /// measure, resolved at the moment this line started (<c>CssLayoutEngine.FlowBox</c>), not
        /// <c>OwnerBox.ClientRight</c>, which names only the measure of the page the box <i>started</i> on.
        /// <c>ApplyRightAlignment</c>/<c>ApplyCenterAlignment</c>/<c>ApplyJustifyAlignment</c> flush against
        /// this rather than the box's own edge, so a straddling box's continuation lines align correctly
        /// against whichever page they actually landed on. Equal to <c>OwnerBox.ClientRight</c> for a box
        /// that isn't eligible for per-fragmentainer re-wrap (the overwhelming majority), so alignment is
        /// unaffected there.
        /// </summary>
        public double ContentRight { get; internal set; }

        /// <summary>
        /// The counterpart left edge. Always <c>OwnerBox.ClientLeft</c> today - layout stays anchored at
        /// the base left origin and only the right edge varies per page (see
        /// <c>HtmlContainerInt.PageContentRightOf</c>'s own remarks) - recorded here too so a future
        /// left-varying design has a seam already in place rather than a rewrite.
        /// </summary>
        public double ContentLeft { get; internal set; }

        /// <summary>
        /// How far this line box reaches on each side of its own baseline, per
        /// <see href="https://www.w3.org/TR/CSS21/visudet.html#line-height">CSS 2.1 §10.8.1</see>: the
        /// per-side largest <c>ascent + half-leading</c>/<c>descent + half-leading</c> among the strut and
        /// every inline box on the line. Null until the line holds content — §9.4.2 keeps a line box
        /// holding none at zero height — and left null by
        /// <c>CssLayoutEngine.CreateVerticalLineBoxes</c>, whose baseline runs along the other axis.
        /// </summary>
        /// <remarks>
        /// Nullable rather than a zero-seeded pair because either side can legitimately be
        /// <b>negative</b>: a <c>line-height</c> shorter than the font makes the leading negative, and the
        /// content area then overflows the line box. Accumulating into a zeroed pair silently floored such
        /// a side at zero, which made a line with a short <c>line-height</c> taller than the
        /// <c>line-height</c> said it was.
        /// </remarks>
        public LineBoxExtent? BaselineExtent { get; internal set; }

        /// <summary>
        /// The baseline-aligned portion of <see cref="BaselineExtent"/>, before edge-aligned replaced
        /// elements impose a minimum total height.
        /// </summary>
        internal LineBoxExtent? BaselineAlignedExtent { get; set; }

        /// <summary>
        /// The empty inlines <c>CssLayoutEngine.FlowBox</c> gave to this line as it went, with no word to
        /// carry them: an inline that places no word still counts toward the line's height (CSS 2.1 §9.4.2,
        /// §10.8.1). Kept so a pass resuming after this line knows which empty inlines at its resume point
        /// this line already holds.
        /// </summary>
        internal List<CssBox>? EmptyInlines { get; set; }

        /// <summary>
        /// The tallest replaced margin box aligned to the line box's top edge.
        /// </summary>
        internal double TopAlignedAtomicHeight { get; set; }

        /// <summary>
        /// The tallest replaced margin box aligned to the line box's bottom edge.
        /// </summary>
        internal double BottomAlignedAtomicHeight { get; set; }

        /// <summary>
        /// This line's baseline, in the same document-Y space as its words — filled in by
        /// <c>CssLayoutEngine.ApplyVerticalAlignment</c> once the line closes, and null for a line that
        /// never got one (an empty line, or one from the vertical-writing-mode engine). Read by
        /// <c>CssBoxMarker</c> to sit an <c>outside</c> marker on the baseline of the first line of the
        /// item it belongs to, rather than at that item's content-box top.
        /// </summary>
        public double? BaselineY { get; internal set; }

        /// <summary>
        /// Gets the words inside the linebox
        /// </summary>
        public List<CssRect> Words { get; }

        /// <summary>
        /// Gets the owner box
        /// </summary>
        public CssBox OwnerBox { get; }

        /// <summary>
        /// Gets a List of rectangles that are to be painted on this linebox
        /// </summary>
        public Dictionary<CssBox, RRect> Rectangles { get; }

        /// <summary>
        /// Get the height of this box line (the max height of all the words)
        /// </summary>
        public double LineHeight
        {
            get
            {
                double height = 0;
                foreach (var rect in Rectangles)
                {
                    height = Math.Max(height, rect.Value.Height);
                }
                return height;
            }
        }

        /// <summary>
        /// The top edge of the line box itself, as the flow placed it — <i>not</i> the top of the ink on
        /// it, which sits half a leading lower (CSS 2.1 §10.8.1). Null for a line the flow never placed
        /// content on.
        /// </summary>
        /// <remarks>
        /// Recorded because the two used to be the same number, and a good deal of this engine read a
        /// word's own top as though it were its line's. Once a word is placed at its baseline rather than
        /// flush with the line's top, that reading is off by the half-leading everywhere it happens —
        /// which is every question about <i>which fragmentainer a line is in</i>.
        /// </remarks>
        public double? FlowTop { get; internal set; }

        /// <summary>
        /// Get the top of this box line, which is what says which fragmentainer the line is in — the line
        /// box's own <see cref="FlowTop"/> where the flow recorded one, falling back to the min top of its
        /// rectangles. Zero for a line that has neither.
        /// </summary>
        public double LineTop
        {
            get
            {
                if (FlowTop is { } flowTop) return flowTop;

                double? top = null;
                foreach (var rect in Rectangles)
                {
                    top = top is null ? rect.Value.Top : Math.Min(top.Value, rect.Value.Top);
                }
                return top ?? 0;
            }
        }

        /// <summary>
        /// Get the bottom of this box line (the max bottom of all the words)
        /// </summary>
        public double LineBottom
        {
            get
            {
                double bottom = 0;
                foreach (var rect in Rectangles)
                {
                    bottom = Math.Max(bottom, rect.Value.Bottom);
                }
                return bottom;
            }
        }

        /// <summary>
        /// Lets the linebox add the word an its box to their lists if necessary.
        /// </summary>
        /// <param name="word"></param>
        internal void ReportExistanceOf(CssRect word)
        {
            word.Line = this;

            if (!Words.Contains(word))
            {
                Words.Add(word);
            }
        }

        /// <summary>
        /// Return the words of the specified box that live in this linebox
        /// </summary>
        /// <param name="box"></param>
        /// <returns></returns>
        internal List<CssRect> WordsOf(CssBox box)
        {
            List<CssRect> r = [];

            foreach (CssRect word in Words)
                if (word.OwnerBox.Equals(box))
                    r.Add(word);

            return r;
        }

        /// <summary>
        /// Updates the specified rectangle of the specified box.
        /// </summary>
        /// <param name="box"></param>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <param name="r"></param>
        /// <param name="b"></param>
        internal void UpdateRectangle(CssBox box, double x, double y, double r, double b)
        {
            var leftSpacing = box.ActualBorderLeftWidth + box.ActualPaddingLeft;
            var rightSpacing = box.ActualBorderRightWidth + box.ActualPaddingRight;
            var topSpacing = box.ActualBorderTopWidth + box.ActualPaddingTop;
            var bottomSpacing = box.ActualBorderBottomWidth + box.ActualPaddingBottom;

            // Under box-decoration-break: clone every fragment is wrapped independently, so each line's
            // rectangle covers its own leading and trailing border and padding rather than only the ones at
            // the box's true ends (css-break-3 §6.2). CssLayoutEngine.FlowBox reserves the matching room, so
            // the rectangle and the content inside it agree.
            var clonesDecorations = box.BoxDecorationBreak.Value == BoxDecorationBreakMode.Clone;

            if (clonesDecorations || (box.FirstHostingLineBox != null && box.FirstHostingLineBox.Equals(this)) || box.IsImage)
                x -= leftSpacing;
            if (clonesDecorations || (box.LastHostingLineBox != null && box.LastHostingLineBox.Equals(this)) || box.IsImage)
                r += rightSpacing;

            if (!box.IsImage)
            {
                y -= topSpacing;
                b += bottomSpacing;
            }

            if (Rectangles.TryGetValue(box, out var f))
            {
                Rectangles[box] = RRect.FromLTRB(
                    Math.Min(f.X, x), Math.Min(f.Y, y),
                    Math.Max(f.Right, r), Math.Max(f.Bottom, b));
            }
            else
            {
                Rectangles.Add(box, RRect.FromLTRB(x, y, r, b));
            }

            if (box.ParentBox is { IsInline: true })
            {
                UpdateRectangle(box.ParentBox, x, y, r, b);
            }
        }

        /// <summary>
        /// Copies the rectangles to their specified box
        /// </summary>
        internal void AssignRectanglesToBoxes()
        {
            foreach (var b in Rectangles.Keys)
            {
                b.Rectangles.Add(this, Rectangles[b]);

                // An inline box's decoration area is exactly these per-line rectangles - its own
                // Location stays at a line-local value layout never updates - so this is the one write
                // that can make it non-empty in a fragmentainer the emitter had already observed it to
                // hold nothing in. UpdateRectangle has already walked the inline ancestor chain, so
                // every inline on the line is a key here and is told.
                b.DiscardEmittedNothing();
            }
        }

        /// <summary>
        /// Returns the words of the linebox
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            string[] ws = new string[Words.Count];
            for (int i = 0; i < ws.Length; i++)
            {
                ws[i] = Words[i].Text!;
            }
            return string.Join(" ", ws);
        }
    }
}