using PeachPDF.Html.Adapters;
using PeachPDF.Html.Core.Utils;
using System.Collections.Generic;
using System.Threading.Tasks;
using PeachPDF.Html.Core.Fragments;

namespace PeachPDF.Html.Core.Dom
{
    /// <summary>
    /// Used to make space on vertical cell combination
    /// </summary>
    internal sealed class CssSpacingBox : CssBox
    {
        public CssSpacingBox(CssBox tableBox, ref CssBox extendedBox, int startRow)
            : base(tableBox, new HtmlTag("none", false, new Dictionary<string, string> { { "colspan", "1" } }))
        {
            ExtendedBox = extendedBox;
            Display = CssConstants.TableCell;

            StartRow = startRow;
            EndRow = startRow + int.Parse(extendedBox.GetAttribute("rowspan", "1")) - 1;
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

        // The spanned cell shows through this placeholder because the fragment builder makes it this
        // box's fragment child - so the ordinary paint walk reaches it, with no re-entrant paint of a
        // box that lives elsewhere in the tree.

    }
}