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

using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Network;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.PdfSharpCore.Pdf;
using PeachPDF.PdfSharpCore.Utils;
using PeachPDF.Utilities;
using SixLabors.Fonts;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Adapters
{
    /// <summary>
    /// Adapter for PdfSharp library platform.
    /// </summary>
    internal sealed class PdfSharpAdapter : RAdapter
    {
        private readonly FontCollection _fontCollection;
        private readonly FontResolver _fontResolver;

        /// <summary>
        /// Init color resolve.
        /// </summary>
        internal PdfSharpAdapter()
        {
            AddFontFamilyMapping("monospace", "Courier New");
            AddFontFamilyMapping("serif", "Times New Roman");
            AddFontFamilyMapping("sans-serif", "Arial");
            AddFontFamilyMapping("fantasy", "Impact");
            AddFontFamilyMapping("Helvetica", "Arial");

            _fontResolver = new FontResolver();
            var fonts = FontResolver.SupportedFonts;

            _fontCollection = new FontCollection();
            _fontCollection.AddSystemFonts();

            foreach (var fontPath in fonts)
            {
                try
                {
                    var font = _fontCollection.Add(fontPath);
                    AddFontFamily(new FontFamilyAdapter(new XFontFamily(font.Name)));
                }
                catch (Exception)
                {
#if DEBUG
                    Console.Error.WriteLine($"Failed to load font from path: {fontPath}");
#endif
                }
            }
        }

        public RNetworkLoader NetworkLoader { get; set; } = new DataUriNetworkLoader();

        public override RUri? BaseUri => NetworkLoader.BaseUri;

        /// <summary>
        /// the amount of pixels per point
        /// </summary>
        public double PixelsPerPoint { get; set; } = 72d;

        internal FontResolver FontResolver => _fontResolver;

        public override async Task<RNetworkResponse?> GetResourceStream(RUri uri)
        {
            if (!uri.IsAbsoluteUri || uri.Scheme is not "data") return await NetworkLoader.GetResourceStream(uri);

            if (NetworkLoader is DataUriNetworkLoader dataUriNetworkLoader)
            {
                return await dataUriNetworkLoader.GetResourceStream(uri);
            }
            else
            {
                var loader = new DataUriNetworkLoader();
                return await loader.GetResourceStream(uri);
            }

        }

        public override string GetCssMediaType(IEnumerable<string> mediaTypesAvailable)
        {
            return mediaTypesAvailable.Contains("print") ? "print" : "all";
        }

        public async Task AddFont(Stream stream, string? fontFamilyName)
        {
            var memoryStream = new MemoryStream();
            await stream.CopyToAsync(memoryStream);

            memoryStream.Seek(0, SeekOrigin.Begin);

            var font = _fontCollection.Add(memoryStream);
            fontFamilyName ??= font.Name;

            AddFontFamily(new FontFamilyAdapter(new XFontFamily(fontFamilyName)));

            memoryStream.Seek(0, SeekOrigin.Begin);

            _fontResolver.AddFont(memoryStream, fontFamilyName);
        }

        protected override RColor GetColorInt(string colorName)
        {
            return System.Enum.TryParse(typeof(KnownColor), colorName, true, out var knownColor)
                ? Utils.Convert(Color.FromKnownColor((KnownColor)knownColor))
                : RColor.Empty;
        }

        protected override RPen CreatePen(RColor color)
        {
            return new PenAdapter(new XPen(Utils.Convert(color)));
        }

        protected override RBrush CreateSolidBrush(RColor color)
        {
            XBrush solidBrush;
            if (color == RColor.White)
                solidBrush = XBrushes.White;
            else if (color == RColor.Black)
                solidBrush = XBrushes.Black;
            else if (color.A < 1)
                solidBrush = XBrushes.Transparent;
            else
                solidBrush = new XSolidBrush(Utils.Convert(color));

            return new BrushAdapter(solidBrush);
        }

        protected override RBrush CreateLinearGradientBrush(RRect rect, RColor color1, RColor color2, double angle)
        {
            var mode = angle switch
            {
                < 45 => XLinearGradientMode.ForwardDiagonal,
                < 90 => XLinearGradientMode.Vertical,
                < 135 => XLinearGradientMode.BackwardDiagonal,
                _ => XLinearGradientMode.Horizontal
            };

            return new BrushAdapter(new XLinearGradientBrush(Utils.Convert(rect, PixelsPerPoint), Utils.Convert(color1), Utils.Convert(color2), mode));
        }

        protected override RImage? ConvertImageInt(object? image)
        {
            return image != null ? new ImageAdapter((XImage)image) : null;
        }

        protected override RImage ImageFromStreamInt(Stream memoryStream)
        {
            return new ImageAdapter(XImage.FromStream(() => memoryStream));
        }

        protected override RFont CreateFontInt(string family, double size, RFontStyle style)
        {
            var fontStyle = (XFontStyle)((int)style);
            var xFont = new XFont(family, size / PixelsPerPoint, fontStyle, new XPdfFontOptions(PdfFontEncoding.Unicode), _fontResolver);
            return new FontAdapter(xFont, PixelsPerPoint);
        }

        protected override RFont CreateFontInt(RFontFamily family, double size, RFontStyle style)
        {
            var fontStyle = (XFontStyle)((int)style);
            var xFont = new XFont(((FontFamilyAdapter)family).FontFamily.Name, size / PixelsPerPoint, fontStyle, new XPdfFontOptions(PdfFontEncoding.Unicode), _fontResolver);
            return new FontAdapter(xFont, PixelsPerPoint);
        }

        protected override async Task AddFontFromStream(string fontFamilyName, Stream stream, string? format)
        {
            if (format is "truetype" or "woff" or "woff2" or "opentype")
            {
                await AddFont(stream, fontFamilyName);
            }
        }

        protected override async Task<bool> AddLocalFont(string fontFamilyName, string localFontFaceName)
        {
            var hasLocalFont = _fontResolver.HasFont(localFontFaceName);

            if (!hasLocalFont) return false;

            var bytes = _fontResolver.GetFont(localFontFaceName);
            var stream = new MemoryStream(bytes);
            await AddFont(stream, fontFamilyName);

            return true;
        }
    }
}