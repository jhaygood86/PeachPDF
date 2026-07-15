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
using PeachPDF.Html.Core.Utils;
using PeachPDF.Network;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.PdfSharpCore.Pdf;
using PeachPDF.PdfSharpCore.Fonts;
using PeachPDF.PdfSharpCore.Utils;
using PeachPDF.Utilities;
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

            foreach (var fontPath in FontResolver.SupportedFonts)
            {
                try
                {
                    var fontDesc = TtfFontDescription.LoadDescription(fontPath);
                    AddFontFamily(new FontFamilyAdapter(new XFontFamily(fontDesc.FontFamilyInvariantCulture)));
                }
                catch (Exception)
                {
#if DEBUG
                    Console.Error.WriteLine($"Failed to load font from path: {fontPath}");
#endif
                }
            }

            // "Arial" itself isn't installed on most Linux distros; fall back to whatever
            // metrically-compatible substitute CssConstants.DefaultFont already resolved to
            // (e.g. Liberation Sans) so explicit `font-family: Arial` behaves the same as
            // the platform's implicit default font.
            if (!IsFontExists("Arial"))
            {
                AddFontFamilyMapping("Arial", CssConstants.DefaultFont);
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
            using var memoryStream = new MemoryStream();
            await stream.CopyToAsync(memoryStream);

            byte[] fontBytes = FontFormatConverter.ToOpenType(memoryStream.ToArray());
            using var convertedStream = new MemoryStream(fontBytes);

            var fontDesc = TtfFontDescription.LoadDescription(convertedStream);
            fontFamilyName ??= fontDesc.FontFamilyInvariantCulture;

            AddFontFamily(new FontFamilyAdapter(new XFontFamily(fontFamilyName)));

            convertedStream.Seek(0, SeekOrigin.Begin);

            _fontResolver.AddFont(convertedStream, fontFamilyName);
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

        protected override RPen CreatePen(RBrush brush)
        {
            return new PenAdapter(new XPen(((BrushAdapter)brush).Brush));
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

        protected override RBrush CreateLinearGradientBrush(RPoint p1, RPoint p2, (RColor Color, double Position)[] stops, bool isRepeating = false)
        {
            var xp1 = new XPoint(p1.X / PixelsPerPoint, p1.Y / PixelsPerPoint);
            var xp2 = new XPoint(p2.X / PixelsPerPoint, p2.Y / PixelsPerPoint);
            var colors = stops.Select(s => Utils.Convert(s.Color)).ToArray();
            var positions = stops.Select(s => s.Position).ToArray();
            return new BrushAdapter(new XLinearGradientBrush(xp1, xp2, colors, positions) { IsRepeating = isRepeating });
        }

        protected override RBrush CreateRadialGradientBrush(RPoint center, double radiusX, double radiusY, (RColor Color, double Position)[] stops, bool isRepeating = false, RPoint? focalCenter = null)
        {
            var xCenter = new XPoint(center.X / PixelsPerPoint, center.Y / PixelsPerPoint);
            var rxPt = radiusX / PixelsPerPoint;
            var ryPt = radiusY / PixelsPerPoint;
            var colors = stops.Select(s => Utils.Convert(s.Color)).ToArray();
            var positions = stops.Select(s => s.Position).ToArray();
            var xFocal = focalCenter is { } f ? new XPoint(f.X / PixelsPerPoint, f.Y / PixelsPerPoint) : (XPoint?)null;
            return new BrushAdapter(new XRadialGradientBrush(xCenter, rxPt, ryPt, colors, positions, xFocal) { IsRepeating = isRepeating });
        }

        protected override RBrush CreateConicGradientBrush(RPoint center, double outerRadius, RColor[] colors, double[] anglesRad)
        {
            var xCenter = new XPoint(center.X / PixelsPerPoint, center.Y / PixelsPerPoint);
            var rPt = outerRadius / PixelsPerPoint;
            var xColors = colors.Select(Utils.Convert).ToArray();
            return new BrushAdapter(new XConicGradientBrush(xCenter, rPt, xColors, anglesRad));
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
            // A missing format() hint is valid CSS (it's an optional hint, not a requirement) and must
            // still be attempted - real-world stylesheets (e.g. css4.pub's Icelandic dictionary page)
            // ship bare `src: url("Font.otf")` with no format() at all. AddFont itself is already
            // format-agnostic (FontFormatConverter.ToOpenType sniffs actual byte content; TtfFontDescription
            // just walks the sfnt table directory), so there's nothing to lose by attempting the load here
            // - only explicitly-declared, genuinely unsupported formats (embedded-opentype, svg, collection)
            // should still be skipped.
            if (format is null or "truetype" or "woff" or "woff2" or "opentype")
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