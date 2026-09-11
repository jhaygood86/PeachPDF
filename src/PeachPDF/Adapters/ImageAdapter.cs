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
using PeachPDF.PdfSharpCore.Drawing;

namespace PeachPDF.Adapters
{
    /// <summary>
    /// Adapter for WinForms Image object for core.
    /// </summary>
    internal sealed class ImageAdapter : RImage
    {
        /// <summary>
        /// the underline win-forms image.
        /// </summary>
        private readonly XImage _image;

        /// <summary>
        /// Initializes a new instance of the <see cref="T:System.Object"/> class.
        /// </summary>
        public ImageAdapter(XImage image)
        {
            _image = image;
        }

        /// <summary>
        /// the underline win-forms image.
        /// </summary>
        public XImage Image
        {
            get { return _image; }
        }

        /// <summary>
        /// This image's natural size. A raster <see cref="XImage"/> reports its pixel count, which the
        /// caller converts; an <see cref="XForm"/> — a vector Form XObject, as
        /// <see cref="GraphicsAdapter.CreateTile"/> produces — has no pixels at all, so it reports the exact
        /// point size it was created at.
        /// </summary>
        /// <remarks>
        /// <see cref="XImage.PixelWidth"/>/<see cref="XImage.PixelHeight"/> are <c>(int)</c> casts of a
        /// form's own view box, so reading them truncated a tile to whole points. A background tile is
        /// repeated at its natural size, so that error did not stay sub-point: it accumulated once per tile.
        /// Charts.css's grid lines are a <c>background-size: 100% calc(100% / 4)</c> tile, and at 32.65pt a
        /// truncation to 32pt walked the fourth line 2.6pt clear of the axis it is supposed to sit under.
        /// </remarks>
        public override double Width
        {
            get { return _image is XForm form ? form.PointWidth : _image.PixelWidth; }
        }

        /// <inheritdoc cref="Width"/>
        public override double Height
        {
            get { return _image is XForm form ? form.PointHeight : _image.PixelHeight; }
        }

        public override bool Interpolate
        {
            get { return _image.Interpolate; }
            set { _image.Interpolate = value; }
        }

        public override void Dispose()
        {
            _image.Dispose();
        }
    }
}