using System.Collections.Generic;
using PeachPDF.Html.Core.Entities;

namespace PeachPDF.Html.Core.Dom
{
    /// <summary>
    /// Used to make space on vertical cell combination
    /// </summary>
    internal sealed class CssSpacingBox : CssBox
    {
        #region Fields and Consts

        /// <summary>
        /// the index of the row where box ends
        /// </summary>
        private readonly int _endRow;

        #endregion


        public CssSpacingBox(CssBox tableBox, ref CssBox extendedBox, int startRow)
            : base(tableBox, new HtmlTag("none", false, new Dictionary<string, string> { { "colspan", "1" } }))
        {
            ExtendedBox = extendedBox;
            Display = CssDisplay.None;

            StartRow = startRow;
            _endRow = startRow + int.Parse(extendedBox.GetAttribute("rowspan", "1")) - 1;
        }

        public CssBox ExtendedBox { get; }

        /// <summary>
        /// Gets the index of the row where box starts
        /// </summary>
        public int StartRow { get; }

        /// <summary>
        /// Gets the index of the row where box ends
        /// </summary>
        public int EndRow
        {
            get { return _endRow; }
        }
    }
}