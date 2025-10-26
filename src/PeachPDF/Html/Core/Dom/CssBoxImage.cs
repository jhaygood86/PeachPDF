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
using PeachPDF.Html.Core.Parse;
using PeachPDF.Html.Core.Utils;
using System;
using System.Threading.Tasks;

namespace PeachPDF.Html.Core.Dom
{
    /// <summary>
    /// CSS box for image element.
    /// </summary>
    internal sealed class CssBoxImage : CssBox
    {
        /// <summary>
        /// the image word of this image box
        /// </summary>
        private readonly CssRectImage _imageWord;

        /// <summary>
        /// handler used for image loading by source
        /// </summary>
        private ImageLoadHandler? _imageLoadHandler;

        /// <summary>
        /// Init.
        /// </summary>
        /// <param name="parent">the parent box of this box</param>
        /// <param name="tag">the html tag data of this box</param>
        public CssBoxImage(CssBox? parent, HtmlTag tag)
            : base(parent, tag)
        {
            _imageWord = new CssRectImage(this);
            Words.Add(_imageWord);
        }

        /// <summary>
        /// Get the image of this image box.
        /// </summary>
        public RImage? Image => _imageWord.Image;

        public string ImageSource => GetAttribute("src");

        /// <summary>
        /// Paints the fragment
        /// </summary>
        /// <param name="g">the device to draw to</param>
        protected override async ValueTask PaintImp(RGraphics g)
        {
            // load image if it is in visible rectangle
            if (_imageLoadHandler == null)
            {
                _imageLoadHandler = new ImageLoadHandler(HtmlContainer!);
                await _imageLoadHandler.LoadImage(ImageSource);
                OnLoadImageComplete(_imageLoadHandler.Image);
            }

            var rect = CommonUtils.GetFirstValueOrDefault(Rectangles);
            var offset = RPoint.Empty;

            if (!IsFixed)
                offset = HtmlContainer!.ScrollOffset;

            rect.Offset(offset);

            var clipped = RenderUtils.ClipGraphicsByOverflow(g, this);

            PaintBackground(g, rect, true);
            BordersDrawHandler.DrawBoxBorders(g, this, rect, true, true);

            var r = _imageWord.Rectangle;
            r.Offset(offset);
            r.Height -= ActualBorderTopWidth + ActualBorderBottomWidth + ActualPaddingTop + ActualPaddingBottom;
            r.Y += ActualBorderTopWidth + ActualPaddingTop;
            r.X = Math.Floor(r.X);
            r.Y = Math.Floor(r.Y);

            if (_imageWord.Image != null)
            {
                if (r is { Width: > 0, Height: > 0 })
                {
                    g.DrawImage(_imageWord.Image, r);
                }
            }

            if (clipped)
                g.PopClip();
        }

        /// <summary>
        /// Assigns words its width and height
        /// </summary>
        /// <param name="g">the device to use</param>
        internal override async ValueTask MeasureWordsSize(RGraphics g)
        {
            if (!_wordsSizeMeasured)
            {
                if (_imageLoadHandler == null)
                {
                    _imageLoadHandler = new ImageLoadHandler(HtmlContainer!);

                    if (Content != CssConstants.Normal)
                    {
                        var imageContent = CssValueParser.GetImagePropertyValue(Content);

                        if (imageContent is not null)
                        {
                            await _imageLoadHandler.LoadImage(imageContent);
                        }

                    }
                    else
                        await _imageLoadHandler.LoadImage(ImageSource);

                    OnLoadImageComplete(_imageLoadHandler.Image);
                }

                MeasureWordSpacing(g);
                _wordsSizeMeasured = true;
            }

            CssLayoutEngine.MeasureImageSize(_imageWord);
        }

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        public override void Dispose()
        {
            _imageLoadHandler?.Dispose();
            base.Dispose();
        }

        /// <summary>
        /// On image load process is complete with image or without update the image box.
        /// </summary>
        /// <param name="image">the image loaded or null if failed</param>
        private void OnLoadImageComplete(RImage? image)
        {
            _imageWord.Image = image;
            _wordsSizeMeasured = false;
        }
    }
}