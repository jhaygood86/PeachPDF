using PeachDrawing.Core;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfBackend;
using PeachPDF.PDF.Drawing;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// The colour a CSS animation mixes at one point of its run, against Chrome: in sRGB when both colours are written in a legacy syntax
    /// (hex, named colours, <c>rgb()</c>, <c>hsl()</c>, <c>hwb()</c>, <c>transparent</c>: the legacy sRGB color formats of CSS Color 4 §13.2), in Oklab as soon as one is not (<c>lab()</c>, <c>lch()</c>,
    /// <c>oklab()</c>, <c>oklch()</c>, <c>color-mix()</c>), always premultiplied by alpha (CSS Color 4, Interpolation).
    /// <para>
    /// The expected values are Chrome's (155.0.8059.40): a paused animation with a negative delay, the computed <c>background-color</c> drawn on a
    /// canvas and read back (8-bit sRGB, alpha unpremultiplied). Tolerance 2 per channel. Rows that also pass on an sRGB mix are controls: the <c>L*</c> rows (legacy pairs), the <c>currentcolor</c> rows M20 and M21, M15 (<c>lab()</c> to <c>transparent</c>),
    /// O5 (an overshooting legacy pair) and the milder overshoot rows; the other <c>M*</c> rows and the <c>O1</c> to <c>O4</c> overshoot rows are the ones a mix in the wrong
    /// space gets wrong (43 of the first 75 fail on that mix). Left out: <c>color()</c>, relative colour syntax and
    /// <c>rgb(none ...)</c> (not read by the colour parser, so the keyframe stands for the value the property has without the animation; <c>color-mix(in hsl)</c> is read but resolves to black, a separate gap).
    /// The <c>G*</c> rows are colours outside the sRGB gamut (<c>lab()</c>, <c>lch()</c>, <c>oklab()</c>, <c>oklch()</c>, <c>color-mix()</c> in lch or oklch), which a browser mixes
    /// from the coordinates they were written with and clips once, at the end; mixed from their clipped sRGB value the <c>G*</c> rows are up to 93 of 255 off. The <c>N*</c> rows
    /// are a <c>color-mix()</c> with an out-of-gamut operand of high weight (the operand must reach the mix unclipped too, in any mix space; up to 94 off otherwise). The <c>K*</c> rows are a <c>currentcolor</c> mixed with a legacy colour on a box whose own <c>color</c> is outside the gamut (Chrome mixes it unclipped in sRGB; up to 64 off otherwise).
    /// </para>
    /// </summary>
    public class AnimationColourInterpolationSpaceTests
    {
        private static readonly Dictionary<string, (string From, string To, string Extra)> Pairs = new()
        {
            ["L1_hex_hex"] = ("#ff0000", "#0000ff", ""),
            ["L2_rgb_hsl"] = ("rgb(255 0 0)", "hsl(240 100% 50%)", ""),
            ["L3_hsl_hue_wrap"] = ("hsl(10 100% 50%)", "hsl(350 100% 50%)", ""),
            ["L4_hwb_hwb"] = ("hwb(0 0% 0%)", "hwb(240 0% 0%)", ""),
            ["L5_rgba_alpha"] = ("rgb(255 0 0 / 0.2)", "rgb(0 0 255 / 0.8)", ""),
            ["L6_hex_transparent"] = ("#ff0000", "transparent", ""),
            ["M1_lab_lab"] = ("lab(50 40 20)", "lab(70 -30 30)", ""),
            ["M2_lch_lch_hue_opposite"] = ("lch(60 30 0)", "lch(60 30 180)", ""),
            ["M3_oklab_oklab"] = ("oklab(0.6 0.1 0.05)", "oklab(0.8 -0.1 0.1)", ""),
            ["M4_oklch_oklch"] = ("oklch(0.6 0.15 30)", "oklch(0.5 0.15 260)", ""),
            ["M5_hex_oklch"] = ("#ff0000", "oklch(0.5 0.15 260)", ""),
            ["M6_lab_oklch"] = ("lab(50 40 20)", "oklch(0.5 0.15 260)", ""),
            ["M10_color_mix_srgb_hex"] = ("color-mix(in srgb, red, blue)", "#00ff00", ""),
            ["M12_color_mix_oklab_hex"] = ("color-mix(in oklab, red, blue)", "#00ff00", ""),
            ["M14_lab_alpha"] = ("lab(50 40 20 / 0.2)", "lab(70 -30 30 / 0.8)", ""),
            ["M15_lab_transparent"] = ("lab(50 40 20)", "transparent", ""),
            ["M16_oklab_currentcolor"] = ("oklab(0.6 0.1 0.05)", "currentcolor", ""),
            ["M20_hex_currentcolor_lab_text"] = ("#ffffff", "currentcolor", "color: lab(50 40 20);"),
            ["M22_lch_lch_far"] = ("lch(30 40 280)", "lch(80 60 100)", ""),
            ["O1_overshoot_transparent_oklch"] = ("transparent", "oklch(0.6 0.15 30)", "animation-timing-function: cubic-bezier(.2, 2.5, .8, 2.5);"),
            ["O2_overshoot_hex_lab"] = ("#220000", "lab(90 -20 80)", "animation-timing-function: cubic-bezier(.2, 2.5, .8, 2.5);"),
            ["O3_overshoot_lab_lab"] = ("lab(10 5 5)", "lab(90 -5 -5)", "animation-timing-function: cubic-bezier(.3, 1.8, .7, 1.4);"),
            ["O4_undershoot_hex_lab"] = ("#220000", "lab(90 -20 80)", "animation-timing-function: cubic-bezier(.2, -1.5, .8, -1.5);"),
            ["O5_overshoot_legacy"] = ("#336699", "#cc9933", "animation-timing-function: cubic-bezier(.3, 1.8, .7, 1.4);"),
            ["M21_hex_currentcolor_hex_text"] = ("#ffffff", "currentcolor", "color: #0000ff;"),
            ["G1_oklch_oog_rgba"] = ("oklch(0.7 0.3 150)", "rgba(230,60,70,.6)", ""),
            ["G2_lab_oog_oklch_oog"] = ("lab(60 90 60)", "oklch(0.7 0.3 150)", ""),
            ["G3_lch_oog_hex"] = ("lch(50 60 200)", "#ff0000", ""),
            ["G4_colormix_lch_hex"] = ("color-mix(in lch, red, blue)", "#00ff00", ""),
            ["G5_colormix_oklch_white"] = ("color-mix(in oklch, red, blue)", "#ffffff", ""),
            ["G6_lch_chroma_hex"] = ("lch(55 120 40)", "#0000ff", ""),
            ["G7_oklab_oog_hsl"] = ("oklab(0.8 0.2 0.1)", "hsl(200 80% 40%)", ""),
            ["G8_oklch_oog_oklch_oog"] = ("oklch(.6 .2 100)", "oklch(.6 .2 300)", ""),
            ["N1_mix_oklch_90_oog"] = ("color-mix(in oklch, oklch(0.7 0.3 150) 90%, red)", "#0000ff", ""),
            ["N2_mix_oklch_90_oog_last"] = ("#00ff00", "color-mix(in oklch, red, oklch(0.7 0.3 150) 90%)", ""),
            ["N3_mix_lch_80_oog"] = ("color-mix(in lch, oklch(0.7 0.3 150) 80%, red)", "#0000ff", ""),
            ["N4_mix_oklab_80_oog"] = ("color-mix(in oklab, oklch(0.7 0.3 150) 80%, red)", "#0000ff", ""),
            ["N5_mix_srgb_70_oog_last"] = ("#00ff00", "color-mix(in srgb, red, oklch(0.7 0.3 150) 70%)", ""),
            ["K1_red_to_currentcolor_wide_lch"] = ("#ff0000", "currentcolor", "color: lch(50 60 200);"),
            ["K2_currentcolor_to_red_wide_oklch"] = ("currentcolor", "#ff0000", "color: oklch(0.7 0.3 150);"),
            ["K3_alpha_to_currentcolor_wide_lab"] = ("rgb(10 200 30 / 0.5)", "currentcolor", "color: lab(60 90 60);"),
        };

        public static TheoryData<string, double, int[]> ChromeColours => new()
        {
            { "L1_hex_hex", 0.25, new[] { 191, 0, 64, 255 } },
            { "L1_hex_hex", 0.5, new[] { 128, 0, 128, 255 } },
            { "L1_hex_hex", 0.75, new[] { 64, 0, 191, 255 } },
            { "L2_rgb_hsl", 0.25, new[] { 191, 0, 64, 255 } },
            { "L2_rgb_hsl", 0.5, new[] { 128, 0, 128, 255 } },
            { "L2_rgb_hsl", 0.75, new[] { 64, 0, 191, 255 } },
            { "L3_hsl_hue_wrap", 0.25, new[] { 255, 32, 11, 255 } },
            { "L3_hsl_hue_wrap", 0.5, new[] { 255, 21, 21, 255 } },
            { "L3_hsl_hue_wrap", 0.75, new[] { 255, 11, 32, 255 } },
            { "L4_hwb_hwb", 0.25, new[] { 191, 0, 64, 255 } },
            { "L4_hwb_hwb", 0.5, new[] { 128, 0, 128, 255 } },
            { "L4_hwb_hwb", 0.75, new[] { 64, 0, 191, 255 } },
            { "L5_rgba_alpha", 0.25, new[] { 109, 0, 146, 89 } },
            { "L5_rgba_alpha", 0.5, new[] { 52, 0, 203, 128 } },
            { "L5_rgba_alpha", 0.75, new[] { 20, 0, 235, 166 } },
            { "L6_hex_transparent", 0.25, new[] { 255, 0, 0, 191 } },
            { "L6_hex_transparent", 0.5, new[] { 255, 0, 0, 128 } },
            { "L6_hex_transparent", 0.75, new[] { 255, 0, 0, 64 } },
            { "M1_lab_lab", 0.25, new[] { 177, 116, 94, 255 } },
            { "M1_lab_lab", 0.5, new[] { 166, 140, 101, 255 } },
            { "M1_lab_lab", 0.75, new[] { 150, 163, 107, 255 } },
            { "M2_lch_lch_hue_opposite", 0.25, new[] { 171, 135, 145, 255 } },
            { "M2_lch_lch_hue_opposite", 0.5, new[] { 146, 144, 145, 255 } },
            { "M2_lch_lch_hue_opposite", 0.75, new[] { 116, 152, 144, 255 } },
            { "M3_oklab_oklab", 0.25, new[] { 183, 129, 98, 255 } },
            { "M3_oklab_oklab", 0.5, new[] { 176, 157, 103, 255 } },
            { "M3_oklab_oklab", 0.75, new[] { 166, 184, 109, 255 } },
            { "M4_oklch_oklch", 0.25, new[] { 168, 97, 106, 255 } },
            { "M4_oklch_oklch", 0.5, new[] { 133, 101, 134, 255 } },
            { "M4_oklch_oklch", 0.75, new[] { 94, 100, 159, 255 } },
            { "M5_hex_oklch", 0.25, new[] { 210, 73, 84, 255 } },
            { "M5_hex_oklch", 0.5, new[] { 163, 92, 122, 255 } },
            { "M5_hex_oklch", 0.75, new[] { 112, 98, 154, 255 } },
            { "M6_lab_oklch", 0.25, new[] { 155, 96, 115, 255 } },
            { "M6_lab_oklch", 0.5, new[] { 124, 99, 139, 255 } },
            { "M6_lab_oklch", 0.75, new[] { 89, 99, 162, 255 } },
            { "M10_color_mix_srgb_hex", 0.25, new[] { 134, 92, 125, 255 } },
            { "M10_color_mix_srgb_hex", 0.5, new[] { 130, 148, 115, 255 } },
            { "M10_color_mix_srgb_hex", 0.75, new[] { 107, 202, 91, 255 } },
            { "M12_color_mix_oklab_hex", 0.25, new[] { 137, 131, 150, 255 } },
            { "M12_color_mix_oklab_hex", 0.5, new[] { 125, 174, 131, 255 } },
            { "M12_color_mix_oklab_hex", 0.75, new[] { 98, 215, 99, 255 } },
            { "M14_lab_alpha", 0.25, new[] { 163, 146, 103, 89 } },
            { "M14_lab_alpha", 0.5, new[] { 147, 167, 110, 128 } },
            { "M14_lab_alpha", 0.75, new[] { 135, 180, 112, 166 } },
            { "M15_lab_transparent", 0.25, new[] { 186, 88, 87, 191 } },
            { "M15_lab_transparent", 0.5, new[] { 185, 88, 88, 128 } },
            { "M15_lab_transparent", 0.75, new[] { 183, 88, 88, 64 } },
            { "M16_oklab_currentcolor", 0.25, new[] { 125, 65, 60, 255 } },
            { "M16_oklab_currentcolor", 0.5, new[] { 70, 34, 30, 255 } },
            { "M16_oklab_currentcolor", 0.75, new[] { 21, 7, 5, 255 } },
            { "M20_hex_currentcolor_lab_text", 0.25, new[] { 238, 213, 213, 255 } },
            { "M20_hex_currentcolor_lab_text", 0.5, new[] { 220, 172, 171, 255 } },
            { "M20_hex_currentcolor_lab_text", 0.75, new[] { 203, 130, 129, 255 } },
            { "M22_lch_lch_far", 0.25, new[] { 78, 105, 131, 255 } },
            { "M22_lch_lch_far", 0.5, new[] { 118, 138, 124, 255 } },
            { "M22_lch_lch_far", 0.75, new[] { 160, 170, 110, 255 } },
            { "O1_overshoot_transparent_oklch", 0.25, new[] { 255, 161, 133, 255 } },
            { "O1_overshoot_transparent_oklch", 0.5, new[] { 255, 151, 119, 255 } },
            { "O1_overshoot_transparent_oklch", 0.75, new[] { 255, 160, 129, 255 } },
            { "O2_overshoot_hex_lab", 0.25, new[] { 228, 255, 0, 255 } },
            { "O2_overshoot_hex_lab", 0.5, new[] { 204, 255, 0, 255 } },
            { "O2_overshoot_hex_lab", 0.75, new[] { 210, 255, 0, 255 } },
            { "O3_overshoot_lab_lab", 0.25, new[] { 211, 228, 234, 255 } },
            { "O3_overshoot_lab_lab", 0.5, new[] { 232, 255, 255, 255 } },
            { "O3_overshoot_lab_lab", 0.75, new[] { 234, 255, 255, 255 } },
            { "O4_undershoot_hex_lab", 0.25, new[] { 3, 0, 7, 255 } },
            { "O4_undershoot_hex_lab", 0.5, new[] { 3, 0, 12, 255 } },
            { "O4_undershoot_hex_lab", 0.75, new[] { 1, 0, 1, 255 } },
            { "O5_overshoot_legacy", 0.25, new[] { 203, 153, 52, 255 } },
            { "O5_overshoot_legacy", 0.5, new[] { 254, 170, 18, 255 } },
            { "O5_overshoot_legacy", 0.75, new[] { 246, 167, 23, 255 } },
            { "M21_hex_currentcolor_hex_text", 0.25, new[] { 191, 191, 255, 255 } },
            { "M21_hex_currentcolor_hex_text", 0.5, new[] { 128, 128, 255, 255 } },
            { "M21_hex_currentcolor_hex_text", 0.75, new[] { 64, 64, 255, 255 } },
            { "G1_oklch_oog_rgba", 0.1, new[] { 0, 197, 0, 245 } },
            { "G1_oklch_oog_rgba", 0.25, new[] { 0, 186, 19, 230 } },
            { "G1_oklch_oog_rgba", 0.9, new[] { 210, 99, 66, 163 } },
            { "G2_lab_oog_oklch_oog", 0.1, new[] { 255, 67, 44, 255 } },
            { "G2_lab_oog_oklch_oog", 0.5, new[] { 187, 151, 28, 255 } },
            { "G2_lab_oog_oklch_oog", 0.9, new[] { 0, 195, 0, 255 } },
            { "G3_lch_oog_hex", 0.1, new[] { 0, 140, 143, 255 } },
            { "G3_lch_oog_hex", 0.25, new[] { 63, 134, 128, 255 } },
            { "G3_lch_oog_hex", 0.9, new[] { 235, 60, 41, 255 } },
            { "G4_colormix_lch_hex", 0.1, new[] { 242, 0, 132, 255 } },
            { "G4_colormix_lch_hex", 0.5, new[] { 212, 139, 113, 255 } },
            { "G4_colormix_lch_hex", 0.9, new[] { 114, 234, 58, 255 } },
            { "G5_colormix_oklch_white", 0.1, new[] { 195, 29, 201, 255 } },
            { "G5_colormix_oklch_white", 0.25, new[] { 208, 85, 212, 255 } },
            { "G5_colormix_oklch_white", 0.9, new[] { 251, 234, 250, 255 } },
            { "G6_lch_chroma_hex", 0.1, new[] { 247, 13, 64, 255 } },
            { "G6_lch_chroma_hex", 0.5, new[] { 149, 79, 162, 255 } },
            { "G6_lch_chroma_hex", 0.9, new[] { 44, 48, 237, 255 } },
            { "G7_oklab_oog_hsl", 0.1, new[] { 255, 125, 119, 255 } },
            { "G7_oklab_oog_hsl", 0.5, new[] { 190, 137, 154, 255 } },
            { "G7_oklab_oog_hsl", 0.9, new[] { 78, 132, 178, 255 } },
            { "G8_oklch_oog_oklch_oog", 0.1, new[] { 155, 126, 0, 255 } },
            { "G8_oklch_oog_oklch_oog", 0.25, new[] { 152, 125, 43, 255 } },
            { "G8_oklch_oog_oklch_oog", 0.9, new[] { 148, 100, 207, 255 } },
            { "N1_mix_oklch_90_oog", 0.1, new[] { 0, 184, 0, 255 } },
            { "N1_mix_oklch_90_oog", 0.5, new[] { 0, 142, 157, 255 } },
            { "N2_mix_oklch_90_oog_last", 0.25, new[] { 0, 239, 0, 255 } },
            { "N2_mix_oklch_90_oog_last", 0.75, new[] { 0, 208, 0, 255 } },
            { "N3_mix_lch_80_oog", 0.25, new[] { 0, 164, 102, 255 } },
            { "N3_mix_lch_80_oog", 0.9, new[] { 0, 65, 238, 255 } },
            { "N4_mix_oklab_80_oog", 0.1, new[] { 46, 175, 72, 255 } },
            { "N4_mix_oklab_80_oog", 0.5, new[] { 0, 135, 171, 255 } },
            { "N5_mix_srgb_70_oog_last", 0.25, new[] { 0, 226, 0, 255 } },
            { "N5_mix_srgb_70_oog_last", 0.75, new[] { 0, 169, 0, 255 } },
            { "K1_red_to_currentcolor_wide_lch", 0.25, new[] { 166, 36, 38, 255 } },
            { "K1_red_to_currentcolor_wide_lch", 0.5, new[] { 76, 71, 76, 255 } },
            { "K1_red_to_currentcolor_wide_lch", 0.75, new[] { 0, 107, 114, 255 } },
            { "K2_currentcolor_to_red_wide_oklch", 0.25, new[] { 0, 152, 0, 255 } },
            { "K2_currentcolor_to_red_wide_oklch", 0.75, new[] { 162, 51, 0, 255 } },
            { "K3_alpha_to_currentcolor_wide_lab", 0.25, new[] { 120, 111, 37, 159 } },
            { "K3_alpha_to_currentcolor_wide_lab", 0.75, new[] { 246, 7, 43, 223 } },
        };

        [Theory]
        [MemberData(nameof(ChromeColours))]
        public async Task MatchesChrome(string name, double progress, int[] rgba)
        {
            var (from, to, extra) = Pairs[name];
            var box = await BuildAsync($@"<!DOCTYPE html><html><head><style>
                html, body {{ margin: 0 }}
                @keyframes k {{ from {{ background-color: {from} }} to {{ background-color: {to} }} }}
                #a {{ width: 20px; height: 20px; animation: k 1s linear; {extra} }}</style></head><body><div id=""a""></div></body></html>", progress);

            var actual = box.ActualBackgroundColor;
            Assert.True(
                Math.Abs(actual.R - rgba[0]) <= 2 && Math.Abs(actual.G - rgba[1]) <= 2 && Math.Abs(actual.B - rgba[2]) <= 2 && Math.Abs(actual.A - rgba[3]) <= 2,
                $"{name} at {progress}: Chrome rgba({rgba[0]}, {rgba[1]}, {rgba[2]}, {rgba[3]}), got rgba({actual.R}, {actual.G}, {actual.B}, {actual.A})");
        }

        private static async Task<CssBox> BuildAsync(string html, double progress)
        {
            var renderContext = new PdfRenderContext();
            var container = new HtmlContainerInt(renderContext) { AnimationProgress = progress };
            await container.SetHtml(html, null);

            var size = new Size(595, 842);
            container.PageSize = PeachPDF.Utilities.Utils.ToPixels(size, 1.0);
            container.MaxSize = PeachPDF.Utilities.Utils.ToPixels(size, 1.0);

            var measure = PdfGraphics.CreateMeasureContext(size);
            using var graphics = new PdfCanvas(renderContext, measure, 1.0);
            await container.PerformLayout(graphics);

            Assert.NotNull(container.Root);
            return Find(container.Root!, "a") ?? throw new InvalidOperationException("No #a");
        }

        private static CssBox? Find(CssBox box, string id)
        {
            if (box.HtmlTag?.Attributes?.TryGetValue("id", out var boxId) == true && string.Equals(boxId, id, StringComparison.OrdinalIgnoreCase))
                return box;

            foreach (var child in box.Boxes)
            {
                if (Find(child, id) is { } found) return found;
            }

            return null;
        }
    }
}
