using PeachPDF.CSS;
using PeachPDF.Html.Core.Utils;
using System.Collections.Generic;

namespace PeachPDF.Html.Core.Dom
{
    /// <summary>
    /// Used to make space on vertical cell combination
    /// </summary>
    internal sealed class CssSpacingBox : CssBox
    {
        // endRow is passed in rather than re-derived from extendedBox's rowspan attribute here: a span
        // that crosses a visibility:collapse row (CSS 2.1 §17.6.1) reaches fewer of _bodyRows's own,
        // already-filtered rows than its raw rowspan count would suggest, and
        // CssLayoutEngineTable.GetEffectiveEndRowIndex is the one place that mapping is done - see its
        // remarks for why (issue #665).
        public CssSpacingBox(CssBox tableBox, ref CssBox extendedBox, int startRow, int endRow)
            : base(tableBox, new HtmlTag("none", false, new Dictionary<string, string> { { "colspan", "1" } }))
        {
            ExtendedBox = extendedBox;
            Display = CssProperty<DisplayMode>.FromValue(Keywords.TableCell, DisplayMode.TableCell);

            StartRow = startRow;
            EndRow = endRow;
        }

        public CssBox ExtendedBox { get; }

        /// <summary>
        /// Gets the index of the row where box starts
        /// </summary>
        public int StartRow { get; }

        /// <summary>
        /// Gets the index of the row where box ends
        /// </summary>
        public int EndRow { get; }

        public override bool BreakPage()
        {
            return ExtendedBox.BreakPage();
        }

        // This box paints nothing of its own. The spanned cell shows through it because the fragment
        // builder makes that cell one of this box's fragment children, so the ordinary paint walk
        // reaches it - rather than this box re-entering paint on a box that lives elsewhere in the tree.
    }
}