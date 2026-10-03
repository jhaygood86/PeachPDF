using PeachDrawing.Core;
using PeachDrawing;
using PeachDrawing.Text;
using PeachPDF.Adapters;
using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Raster
{
    /// <summary>
    /// <see cref="PaintFontStyle.NoSyntheticBold"/>/<see cref="PaintFontStyle.NoSyntheticItalic"/> (CSS <c>font-synthesis-weight</c>/<c>-style: none</c>)
    /// through both concrete <see cref="RenderContext"/>s: the face match is made as before and the synthesis the style forbids is dropped from it,
    /// and each variant is a distinct cached font.
    /// </summary>
    public class RasterFontSynthesisSwitchTests
    {
        private const PaintFontStyle BoldItalic = PaintFontStyle.Bold | PaintFontStyle.Italic;

        private static async Task<RenderContext> Context(bool raster)
        {
            if (raster)
            {
                var ctx = new RasterRenderContext();
                using var stream = File.OpenRead(BundledFonts.Ttf);
                await ctx.AddFont(stream, "SynthSwitchSans");
                return ctx;
            }

            var adapter = new PdfSharpAdapter();
            await BundledFonts.RegisterFont(adapter, BundledFonts.Ttf, "SynthSwitchSans");
            return adapter;
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task SynthesisSwitches_DropOnlyWhatTheyForbid(bool raster)
        {
            var ctx = await Context(raster);

            Assert.Equal(SyntheticStyle.BoldItalic, ctx.GetFont("SynthSwitchSans", 16, BoldItalic, 700)!.SyntheticStyle);
            Assert.Equal(SyntheticStyle.Italic, ctx.GetFont("SynthSwitchSans", 16, BoldItalic | PaintFontStyle.NoSyntheticBold, 700)!.SyntheticStyle);
            Assert.Equal(SyntheticStyle.Bold, ctx.GetFont("SynthSwitchSans", 16, BoldItalic | PaintFontStyle.NoSyntheticItalic, 700)!.SyntheticStyle);
            Assert.Equal(SyntheticStyle.None, ctx.GetFont("SynthSwitchSans", 16, BoldItalic | PaintFontStyle.NoSyntheticBold | PaintFontStyle.NoSyntheticItalic, 700)!.SyntheticStyle);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task SynthesisSwitches_AreDistinctCacheEntries_AndReused(bool raster)
        {
            var ctx = await Context(raster);

            var plain = ctx.GetFont("SynthSwitchSans", 16, BoldItalic, 700)!;
            var off = ctx.GetFont("SynthSwitchSans", 16, BoldItalic | PaintFontStyle.NoSyntheticBold, 700)!;

            Assert.NotSame(plain, off);
            Assert.Same(plain, ctx.GetFont("SynthSwitchSans", 16, BoldItalic, 700));
            Assert.Same(off, ctx.GetFont("SynthSwitchSans", 16, BoldItalic | PaintFontStyle.NoSyntheticBold, 700));

            // The per-codepoint creator honours the same switches (and keys its cache on the style too).
            var rune = new System.Text.Rune('x');
            Assert.Equal(SyntheticStyle.Italic, ctx.GetFontForCodepoint("SynthSwitchSans", 16, BoldItalic | PaintFontStyle.NoSyntheticBold, rune, 700)!.SyntheticStyle);
            Assert.Equal(SyntheticStyle.BoldItalic, ctx.GetFontForCodepoint("SynthSwitchSans", 16, BoldItalic, rune, 700)!.SyntheticStyle);
        }
    }
}
