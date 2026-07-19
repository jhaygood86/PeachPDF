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

        public override double Width
        {
            get { return _image.PixelWidth; }
        }

        public override double Height
        {
            get { return _image.PixelHeight; }
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