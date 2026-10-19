#nullable enable

using PeachDrawing.Core;
using PeachDrawing.PDF;
using PeachDrawing.Text;
using PeachDrawing.Text.Unicode;
using PeachPDF.Html.Core.Utils;
using PeachPDF.Network;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace PeachPDF.Rendering
{
    /// <summary>
    /// The <see cref="PdfRenderContext"/> HTML and CSS are rendered with: it adds the CSS generic font families and the default font,
    /// the syntax of <c>font-variation-settings</c>, SVG color glyphs, and the loading of the document's resources (images, style sheets,
    /// fonts) through the configured <see cref="RNetworkLoader"/>.
    /// </summary>
    internal sealed class HtmlRenderContext : PdfRenderContext
    {
        /// <summary>
        /// Init: maps the font names HTML and CSS use onto the fonts installed.
        /// </summary>
        internal HtmlRenderContext()
        {
            AddFontFamilyMapping("Helvetica", "Arial");

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
                AddFontFamilyMapping(keyword, FontSet.ResolveGeneric(generic, IsFontExists) ?? DefaultFontResolver.DefaultFont);
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
            AddFontFamilyMapping("system-ui", FontSet.ResolveGeneric(GenericFamily.SystemUi, IsFontExists) ?? DefaultFontResolver.DefaultFont);
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

        /// <summary>
        /// Every PeachPDF-driven canvas (the PDF one, and the raster fallback) shares PeachPDF's own SVG
        /// engine through <see cref="PdfSvgGlyphPainter"/> - the only <see cref="RenderContext"/> in this
        /// codebase with one to offer.
        /// </summary>
        public override ISvgGlyphPainter CreateSvgGlyphPainter(Canvas host) => new PdfSvgGlyphPainter(host, this);

        /// <summary><c>font-variation-settings</c>, in the encoding the HTML engine hands the font layer.</summary>
        protected override IReadOnlyList<AxisSetting>? ResolveVariationAxes(string? variations, double sizeInPixels) =>
            FontVariationSettingsResolver.ToAxes(variations, sizeInPixels);

        /// <summary>
        /// Points <see cref="DefaultFontResolver.DefaultFont"/> at the family just registered when nothing currently resolves it.
        /// </summary>
        protected override void OnFontFamilyAdded(string familyName) =>
            AdoptDefaultFontIfMissing(DefaultFontResolver.DefaultFont, familyName, IsFontExists(DefaultFontResolver.DefaultFont));

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
    }
}
