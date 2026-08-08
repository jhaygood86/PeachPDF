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
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Utils;
using System.Threading.Tasks;

namespace PeachPDF.Html.Core.Dom
{
    /// <summary>
    /// CSS box for iframe element.<br/>
    /// If the iframe is of embedded YouTube or Vimeo video it will show image with play.
    /// </summary>
    internal sealed class CssBoxFrame : CssBox
    {
        #region Fields and Consts

        /// <summary>
        /// the image word of this image box
        /// </summary>
        private readonly CssRectImage _imageWord;

        #endregion


        /// <summary>
        /// Init.
        /// </summary>
        /// <param name="parent">the parent box of this box</param>
        /// <param name="tag">the html tag data of this box</param>
        public CssBoxFrame(CssBox? parent, HtmlTag tag)
            : base(parent, tag)
        {
            _imageWord = new CssRectImage(this);
            Words.Add(_imageWord);

            SetErrorBorder();
        }

        /// <summary>
        /// Is the css box clickable ("a" element is clickable)
        /// </summary>
        public override bool IsClickable => true;

        /// <summary>
        /// The phantom word carrying this box's replaced content — the embedded video's still image,
        /// where one was recognized. Read by <c>FrameFragmentPainter</c>.
        /// </summary>
        internal CssRectImage ImageWord => _imageWord;

        #region Private methods

        /// <summary>
        /// Assigns words its width and height
        /// </summary>
        /// <param name="g">the device to use</param>
        internal override ValueTask MeasureWordsSize(RGraphics g)
        {
            if (!_wordsSizeMeasured)
            {
                MeasureWordSpacing(g);
                _wordsSizeMeasured = true;
            }

            CssLayoutEngine.MeasureImageSize(_imageWord);

            return ValueTask.CompletedTask;
        }

        /// <summary>
        /// Set error image border on the image box.
        /// </summary>
        private void SetErrorBorder()
        {
            SetAllBorders(Keywords.Solid, "2px", "#A0A0A0");
            BorderRightColor = BorderBottomColor = "#E3E3E3";
        }

        #endregion
    }
}