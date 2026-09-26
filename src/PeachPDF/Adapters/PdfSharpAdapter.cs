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

using PeachDrawing.Text;
using PeachDrawing.Text.Unicode;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Utils;
using PeachPDF.Network;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.PdfSharpCore.Pdf;
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
        /// <summary>The fonts this adapter renders with: the installed fonts plus everything registered on it.</summary>
        private readonly FontSet _fontSet;

        /// <summary>
        /// Init color resolve.
        /// </summary>
        internal PdfSharpAdapter()
        {
            AddFontFamilyMapping("Helvetica", "Arial");

            _fontSet = new FontSet();

            // The engine's static constructor already opened and parsed every system font file once,
            // process-wide (including filtering out any file that failed to parse) -
            // registering family names here from that cache instead of re-reading every file avoids paying
            // that cost again on every single PdfSharpAdapter construction (see the fontconfig-caching fix
            // in .claude/recent-fixes/2026-09-10-fontconfig-is-asked-once-per-family.md for the same shape
            // of bug one loop earlier in this constructor).
            foreach (var familyName in FontSet.InstalledFamilyNames)
            {
                AddFontFamily(new FontFamilyAdapter(familyName));
            }

            // "Arial" itself isn't installed on most Linux distros; fall back to whatever
            // metrically-compatible substitute DefaultFontResolver.DefaultFont already resolved to
            // (e.g. Liberation Sans) so explicit `font-family: Arial` behaves the same as
            // the platform's implicit default font.
            if (!IsFontExists("Arial"))
            {
                AddFontFamilyMapping("Arial", DefaultFontResolver.DefaultFont);
            }

            // Chromium resolves each generic family differently per OS: hardcoded specific names on
            // Windows/macOS/Android, but delegated to the OS's own fontconfig on Linux (matching what
            // `fc-match <generic>` would return) rather than one hardcoded name that would be wrong for
            // whichever distro doesn't happen to have it. The font set knows that; what counts as "installed"
            // is this adapter's own registry (aliases included), so it is passed in.
            //
            // math is the one generic with no per-platform single name (nor a fontconfig answer worth
            // trusting): it is the first installed family of a candidate chain.
            //
            // The "verify installed, else fall back to the platform default font" correction applies to every
            // generic (previously only done for Arial above) - otherwise a hardcoded/fontconfig-returned name
            // that isn't actually installed would substitute an arbitrary, unrelated font via the font set's own
            // "give the caller SOMETHING" last resort.
            foreach (var (keyword, generic) in GenericFontFamilyResolver.Generics)
            {
                AddFontFamilyMapping(keyword, _fontSet.ResolveGeneric(generic, IsFontExists) ?? DefaultFontResolver.DefaultFont);
            }

            // Chromium's system-ui resolves to Segoe UI on Windows - an exact match for
            // DefaultFontResolver.DefaultFont there. On macOS/Android this stays a pragmatic
            // approximation (real native system-UI-font detection, e.g. macOS's private San
            // Francisco font, is out of scope - see docs).
            //
            // On Linux, ask fontconfig, exactly as the five generics above do.
            // Chromium delegates system-ui to fontconfig on Linux too, and hardcoding the
            // default font here made this the ONE family that ignored the host's own
            // configuration: with the deployment font set installed, `fc-match system-ui`
            // gives FreeSans while DefaultFont is Liberation Sans. Those two disagree on
            // line box height by 11.7% (ascent+descent 1.000em against 1.117em), and
            // system-ui is the family every generated header/footer band is drawn in - so
            // every band was 11.7% taller than the same band under Chromium, and on a page
            // whose top margin the band already fills, that difference is what tipped body
            // text into overprinting it.
            AddFontFamilyMapping("system-ui", _fontSet.ResolveGeneric(GenericFamily.SystemUi, IsFontExists) ?? DefaultFontResolver.DefaultFont);
        }

        public RNetworkLoader NetworkLoader { get; set; } = new DataUriNetworkLoader();

        // Mirrors PdfGenerateConfig.AllowLocalFileAccess, assigned per render in PdfGenerator.AddPdfPages.
        public bool AllowLocalFileAccess { get; set; } = true;

        // Serves file: URIs (and supplies the default working-directory base URI) whenever the configured
        // NetworkLoader isn't itself a FileUriNetworkLoader - mirroring how data: URIs are always handled
        // internally regardless of which loader is configured. Lazily created so its Directory.GetCurrentDirectory()
        // snapshot isn't taken until a file: resource (or the fallback base URI) is actually needed - which is
        // also why AllowLocalFileAccess is checked before every use: with it off, a host that has no file
        // system to snapshot (browser/WebAssembly) never constructs this at all.
        private FileUriNetworkLoader? _internalFileLoader;
        private FileUriNetworkLoader InternalFileLoader => _internalFileLoader ??= new FileUriNetworkLoader();

        // When the configured loader has no base URI of its own (e.g. the default DataUriNetworkLoader), relative
        // references resolve against the current working directory as a file: URI - preserving the historical
        // implicit "load relative paths from disk" behavior now that it flows through FileUriNetworkLoader.
        // With local file access denied there is nothing to resolve them against, so this reports null and
        // relative references stay relative (see GetResourceStream's guard).
        public override RUri? BaseUri =>
            NetworkLoader.BaseUri ?? (AllowLocalFileAccess ? InternalFileLoader.BaseUri : null);

        /// <summary>
        /// The scale between the box tree's internal layout coordinate space and true PDF points -
        /// <c>PdfGenerateConfig.PixelsPerInch / 72</c>, so <c>1.0</c> (no scaling) at the library's own
        /// default <c>PixelsPerInch</c> of 72. <c>PdfGenerator.AddPdfPages</c> always sets this
        /// explicitly before laying anything out; <c>1.0</c> here is only the value an adapter built
        /// without going through <c>PdfGenerator</c> (or before that assignment runs) sees - it must
        /// match the "no scaling" case, not literally repeat the unrelated 72 in "72 points per inch",
        /// or every absolute-length resolution that reads it
        /// (<see cref="PeachPDF.Html.Core.Parse.CssValueParser"/>,
        /// <see cref="PeachPDF.Html.Core.Dom.CssLayoutEngine"/>, issue #814) silently scales by 72x.
        /// </summary>
        public double PixelsPerPoint { get; set; } = 1.0;

        /// <summary>
        /// Fonts here are built at <c>size / PixelsPerPoint</c> points (see <c>CreateFontInt</c>), so
        /// <see cref="PixelsPerPoint"/> is part of a cached font's identity.
        /// </summary>
        internal override double LayoutUnitsPerPoint => PixelsPerPoint;

        public override async Task<RNetworkResponse?> GetResourceStream(RUri uri)
        {
            // BaseUri is normally never null, so every reference resolves to an absolute URI and loaders have
            // only ever been handed those - RUri.Scheme throws on a relative URI, and neither
            // DataUriNetworkLoader nor HttpClientNetworkLoader checks. Denying local file access is what makes
            // BaseUri nullable, so a relative reference can now survive resolution; answer "unresolved" for it
            // here rather than handing a loader a URI it cannot inspect.
            if (!uri.IsAbsoluteUri)
            {
                return null;
            }

            if (uri.Scheme is "data")
            {
                var dataLoader = NetworkLoader as DataUriNetworkLoader ?? new DataUriNetworkLoader();
                return await dataLoader.GetResourceStream(uri);
            }

            if (uri.Scheme is "file")
            {
                // Checked ahead of the configured loader, so a deny holds even when that loader is itself a
                // FileUriNetworkLoader - the two settings contradict each other, and refusing is the safe read.
                if (!AllowLocalFileAccess)
                {
                    return null;
                }

                var fileLoader = NetworkLoader as FileUriNetworkLoader ?? InternalFileLoader;
                return await fileLoader.GetResourceStream(uri);
            }

            return await NetworkLoader.GetResourceStream(uri);
        }

        public override string GetCssMediaType(IEnumerable<string> mediaTypesAvailable)
        {
            return mediaTypesAvailable.Contains("print") ? "print" : "all";
        }

        public async Task AddFont(Stream stream, string? fontFamilyName)
        {
            await AddFont(stream, fontFamilyName, weightOverride: null, isItalicOverride: null, stretchOverride: null);
        }

        internal async Task AddFont(Stream stream, string? fontFamilyName, int? weightOverride, bool? isItalicOverride, int? stretchOverride, IReadOnlyList<RuneInterval>? unicodeRanges = null)
        {
            using var memoryStream = new MemoryStream();
            await stream.CopyToAsync(memoryStream);

            // The font set recognises WOFF/WOFF2/TrueType/OpenType by content and reads the family name from the
            // file when the caller gave none.
            var family = _fontSet.AddData(memoryStream.ToArray(), new AddOptions
            {
                FamilyName = fontFamilyName,
                Weight = weightOverride,
                IsItalic = isItalicOverride,
                Width = stretchOverride,
                UnicodeRanges = unicodeRanges
            });

            AddFontFamily(new FontFamilyAdapter(family.Name));

            AdoptDefaultFontIfMissing(DefaultFontResolver.DefaultFont, family.Name, IsFontExists(DefaultFontResolver.DefaultFont));
        }

        /// <summary>
        /// Points <see cref="DefaultFontResolver.DefaultFont"/> at <paramref name="registeredFamily"/> when nothing
        /// currently resolves it. On a host with no discoverable system fonts (browser/WebAssembly) the
        /// default names a family that nothing satisfies until the caller registers one - and it is the
        /// engine's last resort, so <c>DerivedStyle.ActualFont</c> throws outright rather than rendering
        /// when it resolves to nothing.
        /// <para>
        /// This latches on its own, giving "the first font registered wins": <c>IsFontExists</c> follows one
        /// mapping hop, so once the mapping is in place the caller's next answer is <c>true</c> and a later
        /// registration cannot steal the default. A family whose real name IS the default still takes
        /// precedence whenever it is registered, because <c>FontsHandler.GetCachedFont</c> consults the
        /// registered families before the mapping table.
        /// </para>
        /// </summary>
        /// <param name="defaultFontFamily">The engine's default font, i.e. <see cref="DefaultFontResolver.DefaultFont"/>.</param>
        /// <param name="registeredFamily">The family just registered.</param>
        /// <param name="defaultFontExists">
        /// Whether <paramref name="defaultFontFamily"/> already resolves. Both it and the family name are
        /// passed in rather than read from <see cref="DefaultFontResolver"/> here - mirroring
        /// <see cref="DefaultFontResolver.DetermineDefaultFont"/>'s own precedent - so the rule stays unit-testable
        /// on a host whose default font genuinely is installed, which is every desktop CI runner.
        /// </param>
        internal void AdoptDefaultFontIfMissing(string defaultFontFamily, string registeredFamily, bool defaultFontExists)
        {
            if (!defaultFontExists)
            {
                AddFontFamilyMapping(defaultFontFamily, registeredFamily);
            }
        }

        protected override RColor GetColorInt(string colorName)
        {
            return Enum.TryParse<KnownColor>(colorName, true, out var knownColor)
                ? Utils.Convert(Color.FromKnownColor(knownColor))
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
            RejectMixedColorSpaceGradientStops(color1, color2);

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
            RejectMixedColorSpaceGradientStops(stops.Select(s => s.Color));

            var xp1 = new XPoint(p1.X / PixelsPerPoint, p1.Y / PixelsPerPoint);
            var xp2 = new XPoint(p2.X / PixelsPerPoint, p2.Y / PixelsPerPoint);
            var colors = stops.Select(s => Utils.Convert(s.Color)).ToArray();
            var positions = stops.Select(s => s.Position).ToArray();
            return new BrushAdapter(new XLinearGradientBrush(xp1, xp2, colors, positions) { IsRepeating = isRepeating });
        }

        protected override RBrush CreateRadialGradientBrush(RPoint center, double radiusX, double radiusY, (RColor Color, double Position)[] stops, bool isRepeating = false, RPoint? focalCenter = null)
        {
            RejectMixedColorSpaceGradientStops(stops.Select(s => s.Color));

            var xCenter = new XPoint(center.X / PixelsPerPoint, center.Y / PixelsPerPoint);
            var rxPt = radiusX / PixelsPerPoint;
            var ryPt = radiusY / PixelsPerPoint;
            var colors = stops.Select(s => Utils.Convert(s.Color)).ToArray();
            var positions = stops.Select(s => s.Position).ToArray();
            var xFocal = focalCenter is { } f ? new XPoint(f.X / PixelsPerPoint, f.Y / PixelsPerPoint) : (XPoint?)null;
            return new BrushAdapter(new XRadialGradientBrush(xCenter, rxPt, ryPt, colors, positions, xFocal) { IsRepeating = isRepeating });
        }

        /// <summary>
        /// Rejects a gradient whose stops mix <c>device-cmyk()</c> with RGB-authored colors, rather than
        /// silently corrupting the shading dictionary <see cref="PeachPDF.PdfSharpCore.Pdf.Advanced.PdfShading"/>
        /// writes: a shading's <c>/ColorSpace</c> is one value for the whole object, and every stop's
        /// <c>/C0</c>/<c>/C1</c> component count must agree with it - a mixed-space stop list has no single
        /// component count that fits every stop. An all-CMYK or all-RGB stop list has no such conflict:
        /// <see cref="PeachPDF.PdfSharpCore.Pdf.Advanced.PdfShading"/> resolves its <c>/ColorSpace</c> per
        /// -shading from the stops it's actually given (see <c>PdfShading.ResolveShadingColorMode</c>), not
        /// from the document's own <see cref="PeachPDF.PdfSharpCore.Pdf.PdfDocumentOptions.ColorMode"/>, so
        /// same-space CMYK gradients interpolate directly in C/M/Y/K space exactly like an all-RGB gradient
        /// interpolates in RGB space. Mixing the two spaces in one gradient has no defined conversion (no
        /// naive RGB&lt;-&gt;CMYK approximation is computed anywhere in this project) and stays rejected.
        /// </summary>
        private static void RejectMixedColorSpaceGradientStops(IEnumerable<RColor> colors)
        {
            var list = colors as IReadOnlyCollection<RColor> ?? colors.ToList();
            if (list.Any(c => c.IsCmyk) && list.Any(c => !c.IsCmyk))
            {
                throw new NotSupportedException(
                    "A gradient cannot mix device-cmyk() stops with RGB-authored stops - there is no defined " +
                    "conversion between the two color spaces. A gradient whose stops are all device-cmyk() " +
                    "(or all RGB-authored) is fully supported.");
            }
        }

        private static void RejectMixedColorSpaceGradientStops(params RColor[] colors) => RejectMixedColorSpaceGradientStops((IEnumerable<RColor>)colors);

        protected override RBrush CreateConicGradientBrush(RPoint center, double outerRadius, RColor[] colors, double[] anglesRad)
        {
            RejectMixedColorSpaceGradientStops(colors);

            var xCenter = new XPoint(center.X / PixelsPerPoint, center.Y / PixelsPerPoint);
            var rPt = outerRadius / PixelsPerPoint;
            var xColors = colors.Select(Utils.Convert).ToArray();
            return new BrushAdapter(new XConicGradientBrush(xCenter, rPt, xColors, anglesRad));
        }

        protected override RImage ImageFromStreamInt(Stream memoryStream)
        {
            return new ImageAdapter(XImage.FromStream(() => memoryStream));
        }

        protected override RFont CreateFontInt(string family, double size, RFontStyle style, int weight = 400, int stretch = 5, double? obliqueSkewSinus = null)
        {
            return MatchAndCreateFont(family, size, style, weight, stretch, obliqueSkewSinus);
        }

        protected override RFont CreateFontInt(RFontFamily family, double size, RFontStyle style, int weight = 400, int stretch = 5, double? obliqueSkewSinus = null)
        {
            return MatchAndCreateFont(((FontFamilyAdapter)family).Name, size, style, weight, stretch, obliqueSkewSinus);
        }

        private FontAdapter MatchAndCreateFont(string family, double size, RFontStyle style, int weight, int stretch, double? obliqueSkewSinus)
        {
            var fontStyle = (XFontStyle)((int)style);
            var isItalic = (fontStyle & XFontStyle.Italic) == XFontStyle.Italic;

            var match = _fontSet.MatchOrFallback(family, new TypefaceQuery(weight, stretch, isItalic));
            var xFont = new XFont(size / PixelsPerPoint, fontStyle, new XPdfFontOptions(PdfFontEncoding.Unicode), match, obliqueSkewSinus);
            return new FontAdapter(xFont, PixelsPerPoint);
        }

        protected override RFont? CreateFontForCodepointInt(string family, double size, RFontStyle style, int weight, int stretch, double? obliqueSkewSinus, System.Text.Rune codepoint)
        {
            var fontStyle = (XFontStyle)((int)style);
            var isItalic = (fontStyle & XFontStyle.Italic) == XFontStyle.Italic;

            // A null here tells the caller to try the next family in the stack: never build an XFont for a family
            // that can't render this codepoint.
            if (!_fontSet.TryFindFamily(family, out var typefaceFamily)
                || !typefaceFamily.TryMatch(new TypefaceQuery(weight, stretch, isItalic, codepoint), out var match))
            {
                return null;
            }

            var xFont = new XFont(size / PixelsPerPoint, fontStyle, new XPdfFontOptions(PdfFontEncoding.Unicode), match, obliqueSkewSinus);
            return new FontAdapter(xFont, PixelsPerPoint);
        }

        protected override RFont? CreateSystemFallbackFontForCodepointInt(double size, RFontStyle style, int weight, int stretch, double? obliqueSkewSinus, System.Text.Rune codepoint, PeachDrawing.Text.Unicode.EmojiPresentation presentation)
        {
            if (!_fontSet.TryFindCoveringFamily(codepoint, presentation, out var fallbackFamily))
                return null;

            var fontStyle = (XFontStyle)((int)style);
            var isItalic = (fontStyle & XFontStyle.Italic) == XFontStyle.Italic;

            try
            {
                if (!fallbackFamily.TryMatch(new TypefaceQuery(weight, stretch, isItalic, codepoint), out var match))
                    return null;

                var xFont = new XFont(size / PixelsPerPoint, fontStyle, new XPdfFontOptions(PdfFontEncoding.Unicode), match, obliqueSkewSinus);
                return new FontAdapter(xFont, PixelsPerPoint);
            }
            catch
            {
                // A candidate that passed the cmap-coverage pre-check can still fail to actually load -
                // e.g. a system font (reached here for the first time, since only the last-resort scan
                // ever attempts EVERY installed font) whose file PeachPDF's own OpenType parser can't
                // fully read. Treat exactly like "no candidate found" - the caller falls back to the
                // box's own default font - rather than letting the failure propagate through ordinary
                // text layout.
                return null;
            }
        }

        protected override bool FamilyHasExplicitUnicodeRangesInt(string family) => _fontSet.HasExplicitRanges(family);

        protected override async Task<bool> AddFontFromStream(string fontFamilyName, Stream stream, string? format, int? weightOverride = null, bool? isItalicOverride = null, int? stretchOverride = null, IReadOnlyList<RuneInterval>? unicodeRanges = null)
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
                await AddFont(stream, fontFamilyName, weightOverride, isItalicOverride, stretchOverride, unicodeRanges);
                return true;
            }

            return false;
        }

        protected override async Task<bool> AddLocalFont(string fontFamilyName, string localFontFaceName, int? weightOverride = null, bool? isItalicOverride = null, int? stretchOverride = null, IReadOnlyList<RuneInterval>? unicodeRanges = null)
        {
            if (!_fontSet.TryGetFontData(localFontFaceName, out var bytes)) return false;

            var stream = new MemoryStream(bytes);
            await AddFont(stream, fontFamilyName, weightOverride, isItalicOverride, stretchOverride, unicodeRanges);

            return true;
        }
    }
}