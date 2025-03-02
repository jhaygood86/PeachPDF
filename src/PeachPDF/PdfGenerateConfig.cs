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

#nullable enable

using PeachPDF.Network;
using PeachPDF.PdfSharpCore;
using PeachPDF.PdfSharpCore.Drawing;

namespace PeachPDF
{
    /// <summary>
    /// The settings for generating PDF using <see cref="PdfGenerator"/>
    /// </summary>
    public sealed class PdfGenerateConfig
    {
        #region Fields/Consts

        /// <summary>
        /// the top margin between the page start and the text
        /// </summary>
        private int _marginTop = 10;

        /// <summary>
        /// the bottom margin between the page end and the text
        /// </summary>
        private int _marginBottom = 10;

        /// <summary>
        /// the left margin between the page start and the text
        /// </summary>
        private int _marginLeft = 10;

        /// <summary>
        /// the right margin between the page end and the text
        /// </summary>
        private int _marginRight = 10;

        #endregion

        /// <summary>
        /// the amount of pixels per inch. We always render at 72 points per inch, so this is used to determine how many pixels a point is
        /// </summary>
        public double PixelsPerInch { get; set; } = 72d;

        /// <summary>
        /// When set to true, this renders the page and automatically scales PixelsPerInch to fit the page contents
        /// </summary>
        public bool ScaleToPageSize { get; set; }

        /// <summary>
        /// When set to true, sets the PixelsPerInch to fit the page contents only if the page contents is larger than the window
        /// </summary>
        public bool ShrinkToFit { get; set; }

        /// <summary>
        /// When set to a positive number, sets the PixelsPerInch to fit the minimum content width
        /// </summary>
        public double MinContentWidth { get; set; }

        /// <summary>
        /// the page size to use for each page in the generated pdf
        /// </summary>
        public PageSize PageSize { get; set; }

        /// <summary>
        /// if the page size is undefined this allow you to set manually the page size
        /// </summary>
        public XSize ManualPageSize { get; set; }

        /// <summary>
        /// the orientation of each page of the generated pdf
        /// </summary>
        public PageOrientation PageOrientation { get; set; }

        /// <summary>
        /// The resources to load network content for the renderer.
        /// If null is provided, then an implementation that loads only the default document and any resources with data: URIs is provided
        /// We ship with MimeKit (MHTML) and HttpClient based implementations that can be used instead.
        /// </summary>
        public RNetworkLoader? NetworkLoader { get; set; } = null;

        /// <summary>
        /// the top margin between the page start and the text
        /// </summary>
        public int MarginTop
        {
            get => _marginTop;
            set
            {
                if (value > -1)
                    _marginTop = value;
            }
        }

        /// <summary>
        /// the bottom margin between the page end and the text
        /// </summary>
        public int MarginBottom
        {
            get => _marginBottom;
            set
            {
                if (value > -1)
                    _marginBottom = value;
            }
        }

        /// <summary>
        /// the left margin between the page start and the text
        /// </summary>
        public int MarginLeft
        {
            get => _marginLeft;
            set
            {
                if (value > -1)
                    _marginLeft = value;
            }
        }

        /// <summary>
        /// the right margin between the page end and the text
        /// </summary>
        public int MarginRight
        {
            get => _marginRight;
            set
            {
                if (value > -1)
                    _marginRight = value;
            }
        }

        /// <summary>
        /// Set all 4 margins to the given value.
        /// </summary>
        /// <param name="value"></param>
        public void SetMargins(int value)
        {
            if (value > -1)
                _marginBottom = _marginLeft = _marginTop = _marginRight = value;
        }
    }
}