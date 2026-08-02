using PeachPDF.PdfSharpCore.Utils;
using System;
using System.IO;
using System.Linq;

using PeachPDF.Fonts;

namespace PeachPDF.Tests.TestSupport
{
    /// <summary>
    /// Font-related tests should never depend on what fonts happen to be installed on the
    /// machine running them. These bundled, OFL-1.1-licensed assets guarantee at least one
    /// TrueType (glyf) and one OpenType (CFF) font are always available, regardless of
    /// platform or CI environment.
    ///
    /// The TTF (Source Sans 3) and OTF (Source Code Pro) intentionally come from different
    /// font families rather than being TTF/OTF flavors of the same family: PeachPDF's
    /// process-wide <c>FontFamilyCache</c> caches resolved font data keyed only by family
    /// name, so a same-named TTF and OTF loaded in the same test run would collide and one
    /// would silently shadow the other.
    ///
    /// The files themselves live in the repository-root <c>assets/fonts/</c> directory, shared
    /// with PeachPDF.TestHarness and PeachPDF.Demo.BlazorWasm; the csproj links each one into
    /// the output root, which is what these <see cref="AppContext.BaseDirectory"/> lookups find.
    /// Each font's license notice sits beside it there as a <c>.LICENSE.txt</c>.
    /// </summary>
    internal static class BundledFonts
    {
        internal static string Ttf => Path.Combine(AppContext.BaseDirectory, "SourceSans3-Regular.ttf");

        internal static string Otf => Path.Combine(AppContext.BaseDirectory, "SourceCodePro-Regular.otf");

        internal static string Woff2 => Path.Combine(AppContext.BaseDirectory, "Inter-Medium.woff2");

        /// <summary>
        /// A subset of the monochrome "Noto Emoji" font (see NotoEmoji-Regular.LICENSE.txt): a real
        /// TrueType font with <c>glyf</c> outlines and a cmap <b>format-12</b> subtable mapping
        /// supplementary-plane (astral) emoji codepoints such as U+1F600. Used to exercise real astral
        /// glyph resolution and rendering end to end.
        /// </summary>
        internal static string Emoji => Path.Combine(AppContext.BaseDirectory, "NotoEmoji-Regular.ttf");

        /// <summary>
        /// A subset of "Noto Sans Hebrew" (see NotoSansHebrewSubset.LICENSE.txt): the Hebrew alphabet
        /// plus common punctuation/digits - a real font covering a script <see cref="Ttf"/> (Source
        /// Sans 3, Latin-only) does not, forcing per-codepoint font-fallback resolution for Hebrew text.
        /// </summary>
        internal static string Hebrew => Path.Combine(AppContext.BaseDirectory, "NotoSansHebrewSubset.ttf");

        /// <summary>
        /// A hand-authored COLR <b>version 0</b> test font (public domain, see
        /// ColorTestFonts.LICENSE.txt): layered outline color glyphs backed by a CPAL palette.
        /// 'A' is a red box under a green triangle, 'B' a blue circle; 'X'/'Y'/'Z' are the plain
        /// outline layer glyphs, ' ' is empty.
        /// </summary>
        internal static string ColorV0 => Path.Combine(AppContext.BaseDirectory, "ColorTestV0.ttf");

        /// <summary>
        /// A hand-authored COLR <b>version 1</b> test font (public domain): paint graphs exercising
        /// layered solids ('A'), a linear gradient ('G'), and a translate transform ('T'), plus a
        /// single-glyph solid ('B'). Same outline/palette glyphs as <see cref="ColorV0"/>.
        /// </summary>
        internal static string ColorV1 => Path.Combine(AppContext.BaseDirectory, "ColorTestV1.ttf");

        /// <summary>
        /// A subset of the real COLR <b>version 1</b> build of Noto Color Emoji (see
        /// NotoColorEmoji-Subset.LICENSE.txt): color glyphs via COLR/CPAL over <c>glyf</c> outlines
        /// (gradients, transforms, compositing), covering a handful of common emoji. Used to prove the
        /// color-glyph pipeline end to end against a real production color font.
        /// </summary>
        internal static string ColorEmoji => Path.Combine(AppContext.BaseDirectory, "NotoColorEmoji-Subset.ttf");

        /// <summary>
        /// A subset of Google's "Nabla" (see NablaSubset.LICENSE.txt): a real COLR <b>version 1</b> color font
        /// with <b>7 CPAL palettes</b> (10 entries each) over <c>glyf</c> outlines, covering the letters in
        /// "PALETTE". The subset upgrades CPAL to v1 and flags palette 1 as dark- and palette 2 as
        /// light-background so <c>font-palette: light</c>/<c>dark</c> resolve to a real palette. Used to test and
        /// showcase the CSS <c>font-palette</c> property, <c>@font-palette-values</c>, and <c>palette-mix()</c>.
        /// </summary>
        internal static string Nabla => Path.Combine(AppContext.BaseDirectory, "NablaSubset.ttf");

        /// <summary>
        /// The web-platform-tests GSUB conformance font (see gsubtest-lookup3.LICENSE.txt): every
        /// feature tag (smcp/c2sc/pcap/c2pc/etc.) is implemented as a real GSUB <b>Alternate
        /// Substitution</b> (Lookup Type 3) feature - the only publicly available font found with real
        /// petite-caps/all-petite-caps data, since <see cref="Ttf"/>/<see cref="Otf"/> only cover the
        /// Lookup Type 1 (Single Substitution) path for caps. A feature's base codepoint (0xE000 +
        /// 4*index into the font's own feature table, see gsubtest-features.js) maps a "default"
        /// control glyph (glyph name <c>TAG.default</c>) plus one "altN" glyph per alternate-index
        /// value (<c>TAG.alt1</c>, <c>TAG.alt2</c>, ...) - each alt glyph's alternate set spells the
        /// literal word "PASS" only at the one alternate index it's designed to test, "FAIL" at every
        /// other index, so correct alternate-index selection is provable by codepoint alone, without
        /// needing to rasterize and read the glyph.
        /// </summary>
        internal static string GsubTestLookup3 => Path.Combine(AppContext.BaseDirectory, "gsubtest-lookup3.otf");

        /// <summary>
        /// A real font file path: the first one the host OS reports, or the bundled TTF
        /// if the host reports none.
        /// </summary>
        internal static string AnySupportedFontPath =>
            FontResolver.SupportedFonts.FirstOrDefault() ?? Ttf;

        /// <summary>
        /// Ensures <paramref name="resolver"/> can resolve at least one font family and
        /// returns its name, using a system font if one was detected or registering the
        /// bundled TTF as a custom font otherwise.
        /// </summary>
        internal static string GetOrRegisterKnownFamily(FontResolver resolver)
        {
            if (FontResolver.SupportedFonts.Length > 0)
                return TtfFontDescription.LoadDescription(FontResolver.SupportedFonts[0]).FontFamilyInvariantCulture;

            const string fallbackFamilyName = "__BundledTestFont__";
            using var stream = File.OpenRead(Ttf);
            resolver.AddFont(stream, fallbackFamilyName);
            return fallbackFamilyName;
        }
    }
}
