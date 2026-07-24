using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using PeachPDF;
using PeachPDF.Fonts;
using PeachPDF.PdfSharpCore;
using PeachPDF.Tests.TestSupport;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// End-to-end tests for the CSS <c>font-palette</c> property, the <c>@font-palette-values</c> at-rule, and
    /// <c>palette-mix()</c>, rendered with a real multi-palette COLR/CPAL color font (the Nabla subset). The
    /// palette a glyph paints with is asserted from the actual colors emitted in the content stream (gradient
    /// stop colors and solid fills) — a test that would fail if palette selection were a no-op.
    ///
    /// Nabla subset palettes (see BundledFonts.Nabla): palette 0 is warm yellow/orange, palette 1 (flagged
    /// dark-background) is pink/purple, palette 2 (flagged light-background) is blue.
    /// </summary>
    public class FontPaletteRenderingIntegrationTests
    {
        // A representative color from each palette that the 'A' glyph paints (CPAL entry 0).
        private static readonly (double, double, double) Palette0Yellow = (1, 0.824, 0.078);
        private static readonly (double, double, double) Palette2Blue = (0, 0.627, 0.882);
        private static readonly (double, double, double) Palette1Pink = (1, 0.078, 0.443);

        private static async Task<string> Render(string body, string fontPaletteDecl = "", string extraCss = "")
        {
            var family = TtfFontDescription.LoadDescription(BundledFonts.Nabla).FontFamilyInvariantCulture;
            var generator = new PdfGenerator();
            await using (var stream = File.OpenRead(BundledFonts.Nabla))
                await generator.AddFontFromStream(stream);

            var html = "<!DOCTYPE html><html><head><style>" +
                       $"body {{ font-family: '{family}'; font-size: 100pt; color: black; {fontPaletteDecl} }}" +
                       extraCss +
                       "</style></head><body>" + body + "</body></html>";

            var config = new PdfGenerateConfig { PageSize = PageSize.A4, CompressContentStreams = false };
            var doc = await generator.GeneratePdf(html, config);
            var ms = new MemoryStream();
            doc.Save(ms);
            return Encoding.Latin1.GetString(ms.ToArray());
        }

        // Every numeric RGB triple in the content: solid fills ("r g b rg") and gradient shading stop colors
        // ("/C0 [r g b]" / "/C1 [r g b]").
        private static List<(double R, double G, double B)> Colors(string pdf)
        {
            var colors = new List<(double, double, double)>();
            double D(string s) => double.Parse(s, CultureInfo.InvariantCulture);
            foreach (Match m in Regex.Matches(pdf, @"(-?\d*\.?\d+) (-?\d*\.?\d+) (-?\d*\.?\d+) rg"))
                colors.Add((D(m.Groups[1].Value), D(m.Groups[2].Value), D(m.Groups[3].Value)));
            foreach (Match m in Regex.Matches(pdf, @"/C[01] \[(-?\d*\.?\d+) (-?\d*\.?\d+) (-?\d*\.?\d+)\]"))
                colors.Add((D(m.Groups[1].Value), D(m.Groups[2].Value), D(m.Groups[3].Value)));
            return colors;
        }

        private static bool Has(List<(double R, double G, double B)> colors, (double R, double G, double B) target, double tol = 0.02)
            => colors.Exists(c => Math.Abs(c.R - target.R) < tol && Math.Abs(c.G - target.G) < tol && Math.Abs(c.B - target.B) < tol);

        [Fact]
        public async Task Normal_UsesPaletteZero()
        {
            // The default (font-palette: normal) selects CPAL palette 0.
            var colors = Colors(await Render("A"));
            Assert.True(Has(colors, Palette0Yellow), "normal should paint palette 0 (yellow)");
            Assert.False(Has(colors, Palette2Blue), "normal must not paint palette 2 (blue)");
        }

        [Fact]
        public async Task BasePalette_SelectsThatPalette()
        {
            var pdf = await Render("A", "font-palette: --blue;",
                "@font-palette-values --blue { font-family: 'Nabla'; base-palette: 2; }");
            var colors = Colors(pdf);
            Assert.True(Has(colors, Palette2Blue), "base-palette: 2 should paint palette 2 (blue)");
            Assert.False(Has(colors, Palette0Yellow), "base-palette: 2 must not paint palette 0 (yellow)");
        }

        [Fact]
        public async Task Light_SelectsLightFlaggedPalette()
        {
            // Palette 2 carries USABLE_WITH_LIGHT_BACKGROUND in the subset.
            var colors = Colors(await Render("A", "font-palette: light;"));
            Assert.True(Has(colors, Palette2Blue), "font-palette: light should select the light-flagged palette 2");
        }

        [Fact]
        public async Task Dark_SelectsDarkFlaggedPalette()
        {
            // Palette 1 carries USABLE_WITH_DARK_BACKGROUND in the subset.
            var colors = Colors(await Render("A", "font-palette: dark;"));
            Assert.True(Has(colors, Palette1Pink), "font-palette: dark should select the dark-flagged palette 1");
        }

        [Fact]
        public async Task OverrideColors_ReplacesEntry()
        {
            // Palette 0's yellow is CPAL entries 0 and 4 in the subset; override both to lime. The yellow must
            // be gone and lime present, proving override-colors replaces (not merely augments) palette entries.
            var pdf = await Render("A", "font-palette: --ov;",
                "@font-palette-values --ov { font-family: 'Nabla'; override-colors: 0 rgb(0, 255, 0), 4 rgb(0, 255, 0); }");
            var colors = Colors(pdf);
            Assert.True(Has(colors, (0, 1, 0)), "override-colors should paint the overridden entry (lime)");
            Assert.False(Has(colors, Palette0Yellow), "the overridden entries must no longer paint their palette color");
        }

        [Fact]
        public async Task PaletteMix_BlendsBothPalettes()
        {
            // 50/50 mix in sRGB of palette 0 (normal) and palette 2 blends entry 0 yellow<->blue to their
            // per-channel midpoint, a color present in neither source palette.
            var pdf = await Render("A", "font-palette: palette-mix(in srgb, normal, --blue);",
                "@font-palette-values --blue { font-family: 'Nabla'; base-palette: 2; }");
            var colors = Colors(pdf);

            var blended = ((Palette0Yellow.Item1 + Palette2Blue.Item1) / 2,
                           (Palette0Yellow.Item2 + Palette2Blue.Item2) / 2,
                           (Palette0Yellow.Item3 + Palette2Blue.Item3) / 2);
            Assert.True(Has(colors, blended, tol: 0.03), "palette-mix should paint the per-entry blend of the two palettes");
            Assert.False(Has(colors, Palette0Yellow), "the pure palette-0 color should not survive a 50/50 mix");
            Assert.False(Has(colors, Palette2Blue), "the pure palette-2 color should not survive a 50/50 mix");
        }

        [Fact]
        public async Task PaletteMix_ColorSpaceIsCaseInsensitive()
        {
            // A mixed-case color space must map the same as lowercase (CSS keywords are case-insensitive),
            // not silently fall back to sRGB - so an uppercase-space mix equals the lowercase-space mix.
            const string css = "@font-palette-values --blue { font-family: 'Nabla'; base-palette: 2; }";
            var lower = Colors(await Render("A", "font-palette: palette-mix(in oklab, normal, --blue);", css));
            var upper = Colors(await Render("A", "font-palette: palette-mix(in OKLAB, normal, --blue);", css));

            Assert.All(lower, c => Assert.True(Has(upper, c), $"upper-case space produced a different blend for {c}"));
        }

        [Fact]
        public async Task UnmatchedFamily_FallsBackToNormal()
        {
            // The @font-palette-values font-family doesn't match the element's font, so it doesn't apply.
            var pdf = await Render("A", "font-palette: --blue;",
                "@font-palette-values --blue { font-family: 'Some Other Font'; base-palette: 2; }");
            var colors = Colors(pdf);
            Assert.True(Has(colors, Palette0Yellow), "a family mismatch must fall back to normal (palette 0)");
            Assert.False(Has(colors, Palette2Blue));
        }

        [Fact]
        public async Task BasePalette_OutOfRange_FallsBackToPaletteZero()
        {
            // base-palette 99 doesn't exist (the font has 7 palettes) -> falls back to palette 0.
            var pdf = await Render("A", "font-palette: --oor;",
                "@font-palette-values --oor { font-family: 'Nabla'; base-palette: 99; }");
            Assert.True(Has(Colors(pdf), Palette0Yellow), "an out-of-range base-palette must fall back to palette 0");
        }

        [Fact]
        public async Task BasePalette_DarkKeyword_SelectsDarkFlaggedPalette()
        {
            // A registered base-palette: dark resolves via the font's CPAL dark flag (palette 1).
            var pdf = await Render("A", "font-palette: --auto;",
                "@font-palette-values --auto { font-family: 'Nabla'; base-palette: dark; }");
            Assert.True(Has(Colors(pdf), Palette1Pink), "base-palette: dark should resolve to the dark-flagged palette");
        }

        [Fact]
        public async Task NonColorFont_WithFontPalette_StillRendersText()
        {
            // font-palette on an ordinary (non-color) font is a no-op: text still renders via the normal path.
            var generator = new PdfGenerator();
            await using (var stream = File.OpenRead(BundledFonts.Ttf))
                await generator.AddFontFromStream(stream);
            var family = TtfFontDescription.LoadDescription(BundledFonts.Ttf).FontFamilyInvariantCulture;
            var html = $"<!DOCTYPE html><html><head><style>body {{ font-family: '{family}'; font-palette: light; }}</style>" +
                       "</head><body>Hi</body></html>";
            var config = new PdfGenerateConfig { PageSize = PageSize.A4, CompressContentStreams = false };
            var doc = await generator.GeneratePdf(html, config);
            var ms = new MemoryStream();
            doc.Save(ms);
            var pdf = Encoding.Latin1.GetString(ms.ToArray());

            Assert.Contains(" Tj", pdf); // ordinary text show, unaffected by font-palette
        }

        [Fact]
        public async Task UnknownPaletteName_FallsBackToNormal()
        {
            var colors = Colors(await Render("A", "font-palette: --does-not-exist;"));
            Assert.True(Has(colors, Palette0Yellow), "an unmatched palette name must fall back to normal");
        }

        [Fact]
        public async Task Inherited_AppliesToChild()
        {
            // font-palette is inherited: a child with no font-palette of its own uses the parent's.
            var pdf = await Render("<span>A</span>", "font-palette: --blue;",
                "@font-palette-values --blue { font-family: 'Nabla'; base-palette: 2; }");
            Assert.True(Has(Colors(pdf), Palette2Blue), "font-palette should inherit to the child span");
        }
    }
}
