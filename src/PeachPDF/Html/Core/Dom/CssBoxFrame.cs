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

using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Handlers;
using PeachPDF.Html.Core.Utils;
using System;
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

        #region Private methods

        /// <summary>
        /// Paints the fragment
        /// </summary>
        /// <param name="g">the device to draw to</param>
        protected override ValueTask PaintImp(RGraphics g)
        {
            var rects = CommonUtils.GetFirstValueOrDefault(Rectangles);

            var offset = (HtmlContainer != null && !IsFixed) ? HtmlContainer.ScrollOffset : RPoint.Empty;
            rects.Offset(offset);

            var clipped = RenderUtils.ClipGraphicsByOverflow(g, this);

            PaintBackground(g, rects, true);

            BordersDrawHandler.DrawBoxBorders(g, this, rects, true, true);

            var word = Words[0];
            var tmpRect = word.Rectangle;
            tmpRect.Offset(offset);
            tmpRect.Height -= ActualBorderTopWidth + ActualBorderBottomWidth + ActualPaddingTop + ActualPaddingBottom;
            tmpRect.Y += ActualBorderTopWidth + ActualPaddingTop;
            tmpRect.X = Math.Floor(tmpRect.X);
            tmpRect.Y = Math.Floor(tmpRect.Y);
            var rect = tmpRect;

            DrawImage(g, rect);

            if (clipped)
                g.PopClip();

            return ValueTask.CompletedTask;
        }

        /// <summary>
        /// Draw video image over the iframe if found.
        /// </summary>
        private void DrawImage(RGraphics g, RRect rect)
        {
            if (_imageWord.Image == null) return;

            if (rect is { Width: > 0, Height: > 0 })
            {
                g.DrawImage(_imageWord.Image, rect);
            }
        }

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
            SetAllBorders(CssConstants.Solid, "2px", "#A0A0A0");
            BorderRightColor = BorderBottomColor = "#E3E3E3";
        }

        #endregion
    }
}