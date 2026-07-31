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

using PeachPDF.Html.Adapters.Entities;
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
        /// Get the top of this box line (the min top of all the words), which is what says which
        /// fragmentainer the line is in. Zero for a line that has not been given rectangles yet.
        /// </summary>
        public double LineTop
        {
            get
            {
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
            var clonesDecorations = box.BoxDecorationBreak == CssConstants.Clone;

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
        /// Sets the baseline of the words of the specified box to certain height
        /// </summary>
        /// <param name="b">box to check words</param>
        /// <param name="baseline">baseline</param>
        internal void SetBaseLine(CssBox b, double baseline)
        {
            //TODO: Aqui me quede, checar poniendo "by the" con un font-size de 3em
            List<CssRect> ws = WordsOf(b);

            if (!Rectangles.TryGetValue(b, out RRect r))
                return;

            //Save top of words related to the top of rectangle
            double gap = 0f;

            if (ws.Count > 0)
            {
                gap = ws[0].Top - r.Top;
            }
            else
            {
                var firstw = CssBox.FirstWordOccurence(b, this);

                if (firstw != null)
                {
                    gap = firstw.Top - r.Top;
                }
            }

            //New top that words will have
            //float newtop = baseline - (Height - OwnerBox.FontDescent - 3); //OLD
            double newtop = baseline; // -GetBaseLineHeight(b, g); //OLD

            var wordTop = newtop;

            if (b.ParentBox != null &&
                b.ParentBox.Rectangles.ContainsKey(this) &&
                r.Height < b.ParentBox.Rectangles[this].Height)
            {
                //Do this only if rectangle is shorter than parent's
                double recttop = newtop - gap;
                RRect newr = new(r.X, recttop, r.Width, r.Height);
                Rectangles[b] = newr;
                b.OffsetRectangle(this, gap);
            }
            else
            {
                // The rect is NOT being repositioned here, so re-anchoring the words flush to
                // the baseline would collapse the box's own word-to-rect gap - which is exactly
                // its border+padding-top content inset (CSS2.1 §8.1) for a box holding its
                // words directly (e.g. a padded inline-block ::before/::after pseudo-element).
                // Preserve it; unpadded boxes have gap == 0 and are unaffected.
                wordTop = newtop + gap;
            }

            foreach (var word in ws)
            {
                if (!word.IsImage)
                    word.Top = wordTop;
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