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
    /// What the animation step sees of <c>!important</c> declarations and of several animations, checked against Chrome: an <c>!important</c>
    /// custom property reaches a <c>var()</c> in an animated property or keyframe, an <c>!important</c> <c>color</c> is the <c>currentcolor</c> of a
    /// mix and is not animated, a plain <c>!important</c> value supersedes an earlier deferred <c>var()</c> one, and a <c>color</c> set by a later
    /// animation in the list is the <c>currentcolor</c> of an earlier one.
    /// <para>
    /// The expected numbers are Chrome's, read back with <c>getComputedStyle()</c> from a page whose animations are paused with a negative delay of
    /// the progress in seconds (PeachPDF ignores delay and play state and takes the progress from <c>AnimationProgress</c>, so the CSS here has
    /// neither). Tolerance: 2 per colour channel, 1px of width (1px = 0.75pt here). Rows for 0.1 and 0.5 are kept.
    /// </para>
    /// <para>
    /// Rows that also pass without the change are controls: A4, D2, E2, Z1 and Z2 (the input has nothing the change affects).
    /// No row uses <c>@media</c>: <c>BuildAsync</c> sets the page size after <c>SetHtml</c>, so a width query would match while the cascade runs
    /// and the row would be green for the wrong reason.
    /// </para>
    /// </summary>
    public class AnimationImportantDeclarationTests
    {
        private static readonly Dictionary<string, (string Css, string Body)> Cases = new()
        {
            ["A1_important_var_underlying"] = (
                """@keyframes k { from { width: 50px } } #a { --w: 100px; width: var(--w); height: 10px; animation: k 1s linear both; } #a { --w: 300px !important }""",
                """<div id="a"></div>"""),
            ["A2_important_var_in_keyframe"] = (
                """@keyframes k { from { width: 50px } to { width: var(--w) } } #a { --w: 100px; height: 10px; animation: k 1s linear both; } #a { --w: 300px !important }""",
                """<div id="a"></div>"""),
            ["A3_inline_important_custom"] = (
                """@keyframes k { from { width: 50px } } #a { width: var(--w); height: 10px; animation: k 1s linear both; }""",
                """<div id="a" style="--w: 300px !important"></div>"""),
            ["A4_parent_important_custom"] = (
                """@keyframes k { from { width: 50px } } #p { --w: 300px !important } #a { --w: 100px; width: var(--w); height: 10px; animation: k 1s linear both; }""",
                """<div id="p"><div id="a"></div></div>"""),
            ["A5_layers_important_reverse"] = (
                """@layer one, two; @keyframes k { from { width: 50px } to { width: var(--w) } } @layer one { #a { --w: 110px !important } } @layer two { #a { --w: 220px !important } } #a { --w: 330px !important; height: 10px; animation: k 1s linear both; }""",
                """<div id="a"></div>"""),
            ["A6_important_wins_over_normal_later"] = (
                """@keyframes k { from { width: 50px } } #a { --w: 100px; width: var(--w); height: 10px; animation: k 1s linear both; } #a { --w: 300px !important } #a#a { --w: 700px }""",
                """<div id="a"></div>"""),
            ["B1_important_color_bg_currentcolor"] = (
                """@keyframes k { from { background-color: currentcolor } to { background-color: #fff } } #a { color: #00f !important; width: 20px; height: 20px; animation: k 1s linear both; }""",
                """<div id="a"></div>"""),
            ["B2_important_color_normal_loses"] = (
                """@keyframes k { from { background-color: currentcolor } to { background-color: #fff } } #a { color: #00f; width: 20px; height: 20px; animation: k 1s linear both; } #a { color: #f00 !important }""",
                """<div id="a"></div>"""),
            ["B3_inline_important_color"] = (
                """@keyframes k { from { background-color: currentcolor } to { background-color: #fff } } #a { width: 20px; height: 20px; animation: k 1s linear both; }""",
                """<div id="a" style="color: #0a0 !important"></div>"""),
            ["B4_important_color_via_important_var"] = (
                """@keyframes k { from { background-color: currentcolor } to { background-color: #fff } } #a { --c: #f00; color: var(--c) !important; width: 20px; height: 20px; animation: k 1s linear both; } #a { --c: #00f !important }""",
                """<div id="a"></div>"""),
            ["B5_color_var_with_important_custom"] = (
                """@keyframes k { from { background-color: currentcolor } to { background-color: #fff } } #a { --c: #f00; color: var(--c); width: 20px; height: 20px; animation: k 1s linear both; } #a { --c: #00f !important }""",
                """<div id="a"></div>"""),
            ["E1_important_color_also_animated"] = (
                """@keyframes k { from { color: #0f0; background-color: currentcolor } to { color: #00f; background-color: #fff } } #a { color: #f00 !important; width: 20px; height: 20px; animation: k 1s linear both; }""",
                """<div id="a"></div>"""),
            ["E2_important_color_only_color_animated"] = (
                """@keyframes k { from { color: #0f0 } to { color: #00f } } #a { color: #f00 !important; width: 20px; height: 20px; animation: k 1s linear both; }""",
                """<div id="a"></div>"""),
            ["D1_later_animation_sets_color"] = (
                """@keyframes k { from { background-color: #fff } to { background-color: currentcolor } } @keyframes k2 { from { color: #f00 } to { color: #00f } } #a { width: 20px; height: 20px; animation: k 1s linear both, k2 1s linear both; animation-delay: -0s, -0s }""",
                """<div id="a"></div>"""),
            ["D2_earlier_animation_sets_color"] = (
                """@keyframes k { from { background-color: #fff } to { background-color: currentcolor } } @keyframes k2 { from { color: #f00 } to { color: #00f } } #a { width: 20px; height: 20px; animation: k2 1s linear both, k 1s linear both; animation-delay: -0s, -0s }""",
                """<div id="a"></div>"""),
            ["D3_three_animations_color_in_middle"] = (
                """@keyframes k { from { background-color: #fff } to { background-color: currentcolor } } @keyframes k2 { from { color: #f00 } to { color: #00f } } @keyframes k3 { from { opacity: 1 } to { opacity: .5 } } #a { width: 20px; height: 20px; animation: k 1s linear both, k2 1s linear both, k3 1s linear both; animation-delay: -0s, -0s, -0s }""",
                """<div id="a"></div>"""),
            ["D4_two_animations_both_color"] = (
                """@keyframes k { from { color: #f00; background-color: currentcolor } to { color: #0f0; background-color: #fff } } @keyframes k2 { from { color: #00f } to { color: #ff0 } } #a { width: 20px; height: 20px; animation: k 1s linear both, k2 1s linear both; animation-delay: -0s, -0s }""",
                """<div id="a"></div>"""),
            ["F1_normal_var_color_then_important_plain"] = (
                """@keyframes k { from { background-color: currentcolor } to { background-color: #fff } } #a { --c: #00f; color: var(--c); width: 20px; height: 20px; animation: k 1s linear both; } #a { color: #f00 !important }""",
                """<div id="a"></div>"""),
            ["F2_inline_important_over_normal_var"] = (
                """@keyframes k { from { background-color: currentcolor } to { background-color: #fff } } #a { --c: #00f; color: var(--c); width: 20px; height: 20px; animation: k 1s linear both; }""",
                """<div id="a" style="color: #f00 !important"></div>"""),
            ["F3_important_inherit_over_normal_var"] = (
                """@keyframes k { from { background-color: currentcolor } to { background-color: #fff } } #p { color: #f80 } #a { --c: #00f; color: var(--c); width: 20px; height: 20px; animation: k 1s linear both; } #a { color: inherit !important }""",
                """<div id="p"><div id="a"></div></div>"""),
            ["F4_layered_normal_var_unlayered_important"] = (
                """@layer x { #a { --c: #00f; color: var(--c) } } @keyframes k { from { background-color: currentcolor } to { background-color: #fff } } #a { color: #f00 !important; width: 20px; height: 20px; animation: k 1s linear both; }""",
                """<div id="a"></div>"""),
            ["F5_important_initial_over_normal_var"] = (
                """@keyframes k { from { background-color: currentcolor } to { background-color: #fff } } #a { --c: #00f; color: var(--c); width: 20px; height: 20px; animation: k 1s linear both; } #a { color: initial !important }""",
                """<div id="a"></div>"""),
            ["F6_important_plain_over_important_var"] = (
                """@keyframes k { from { background-color: currentcolor } to { background-color: #fff } } #a { --c: #00f; color: var(--c) !important; width: 20px; height: 20px; animation: k 1s linear both; }""",
                """<div id="a" style="color: #f00 !important"></div>"""),
            ["F7_animation_name_var_then_important_none"] = (
                """@keyframes k { from { width: 50px } to { width: 200px } } #a { --n: k; animation-name: var(--n); animation-duration: 1s; animation-timing-function: linear; animation-fill-mode: both; animation-play-state:; width: 100px; height: 10px } #a { animation-name: none !important }""",
                """<div id="a"></div>"""),
            ["E3_important_var_color_also_animated"] = (
                """@keyframes k { from { color: #0f0; background-color: currentcolor } to { color: #00f; background-color: #fff } } #a { --c: #f00; color: var(--c) !important; width: 20px; height: 20px; animation: k 1s linear both; }""",
                """<div id="a"></div>"""),
            ["E4_important_inherit_also_animated"] = (
                """@keyframes k { from { color: #0f0; background-color: currentcolor } to { color: #00f; background-color: #fff } } #p { color: #f80 } #a { color: inherit !important; width: 20px; height: 20px; animation: k 1s linear both; }""",
                """<div id="p"><div id="a"></div></div>"""),
            ["G1_registered_property_important"] = (
                """@property --w { syntax: '<length>'; inherits: true; initial-value: 10px } @keyframes k { from { width: 50px } } #a { --w: 100px; width: var(--w); height: 10px; animation: k 1s linear both; } #a { --w: 300px !important }""",
                """<div id="a"></div>"""),
            ["G4_custom_references_important_custom"] = (
                """@keyframes k { from { width: 50px } } #a { --b: 100px; --w: var(--b); width: var(--w); height: 10px; animation: k 1s linear both; } #a { --b: 300px !important }""",
                """<div id="a"></div>"""),
            ["G4b_important_custom_references_important_custom"] = (
                """@keyframes k { from { width: 50px } } #a { --b: 100px; --w: var(--b) !important; width: var(--w); height: 10px; animation: k 1s linear both; } #a { --b: 300px !important }""",
                """<div id="a"></div>"""),
            ["G5_important_custom_inherit"] = (
                """@keyframes k { from { width: 50px } } #p { --w: 300px } #a { --w: 100px; width: var(--w); height: 10px; animation: k 1s linear both; } #a { --w: inherit !important }""",
                """<div id="p"><div id="a"></div></div>"""),
            ["G8_important_color_unset"] = (
                """@keyframes k { from { background-color: currentcolor } to { background-color: #fff } } #p { color: #f80 } #a { color: #00f; width: 20px; height: 20px; animation: k 1s linear both; } #a { color: unset !important }""",
                """<div id="p"><div id="a"></div></div>"""),
            ["D5_color_pass_applies_width_once"] = (
                """@keyframes kw { from { width: 50px; color: #0f0 } } @keyframes kc { from { color: #f00 } to { color: #00f } } #a { width: 100px; height: 10px; animation: kw 1s linear both, kc 1s linear both; animation-delay: -0s, -0s }""",
                """<div id="a"></div>"""),
            ["Z1_plain_currentcolor"] = (
                """@keyframes k { from { background-color: currentcolor } to { background-color: #fff } } #a { color: #00f; width: 20px; height: 20px; animation: k 1s linear both; }""",
                """<div id="a"></div>"""),
            ["Z2_plain_var"] = (
                """@keyframes k { from { width: 50px } } #a { --w: 100px; width: var(--w); height: 10px; animation: k 1s linear both; }""",
                """<div id="a"></div>"""),
        };

        public static TheoryData<string, double, double?, int[]?, int[]?> ChromeValues => new()
        {
            { "A1_important_var_underlying", 0.1, 75.0, null, null },
            { "A1_important_var_underlying", 0.5, 175.0, null, null },
            { "A2_important_var_in_keyframe", 0.1, 75.0, null, null },
            { "A2_important_var_in_keyframe", 0.5, 175.0, null, null },
            { "A3_inline_important_custom", 0.1, 75.0, null, null },
            { "A3_inline_important_custom", 0.5, 175.0, null, null },
            { "A4_parent_important_custom", 0.1, 55.0, null, null },
            { "A4_parent_important_custom", 0.5, 75.0, null, null },
            { "A5_layers_important_reverse", 0.1, 56.0, null, null },
            { "A5_layers_important_reverse", 0.5, 80.0, null, null },
            { "A6_important_wins_over_normal_later", 0.1, 75.0, null, null },
            { "A6_important_wins_over_normal_later", 0.5, 175.0, null, null },
            { "B1_important_color_bg_currentcolor", 0.1, null, new[] { 0, 0, 255 }, new[] { 26, 26, 255 } },
            { "B1_important_color_bg_currentcolor", 0.5, null, new[] { 0, 0, 255 }, new[] { 128, 128, 255 } },
            { "B2_important_color_normal_loses", 0.1, null, new[] { 255, 0, 0 }, new[] { 255, 26, 26 } },
            { "B2_important_color_normal_loses", 0.5, null, new[] { 255, 0, 0 }, new[] { 255, 128, 128 } },
            { "B3_inline_important_color", 0.1, null, new[] { 0, 170, 0 }, new[] { 26, 179, 26 } },
            { "B3_inline_important_color", 0.5, null, new[] { 0, 170, 0 }, new[] { 128, 213, 128 } },
            { "B4_important_color_via_important_var", 0.1, null, new[] { 0, 0, 255 }, new[] { 26, 26, 255 } },
            { "B4_important_color_via_important_var", 0.5, null, new[] { 0, 0, 255 }, new[] { 128, 128, 255 } },
            { "B5_color_var_with_important_custom", 0.1, null, new[] { 0, 0, 255 }, new[] { 26, 26, 255 } },
            { "B5_color_var_with_important_custom", 0.5, null, new[] { 0, 0, 255 }, new[] { 128, 128, 255 } },
            { "E1_important_color_also_animated", 0.1, null, new[] { 255, 0, 0 }, new[] { 255, 26, 26 } },
            { "E1_important_color_also_animated", 0.5, null, new[] { 255, 0, 0 }, new[] { 255, 128, 128 } },
            { "E2_important_color_only_color_animated", 0.1, null, new[] { 255, 0, 0 }, null },
            { "E2_important_color_only_color_animated", 0.5, null, new[] { 255, 0, 0 }, null },
            { "D1_later_animation_sets_color", 0.1, null, new[] { 230, 0, 26 }, new[] { 252, 230, 232 } },
            { "D1_later_animation_sets_color", 0.5, null, new[] { 128, 0, 128 }, new[] { 191, 128, 191 } },
            { "D2_earlier_animation_sets_color", 0.1, null, new[] { 230, 0, 26 }, new[] { 252, 230, 232 } },
            { "D2_earlier_animation_sets_color", 0.5, null, new[] { 128, 0, 128 }, new[] { 191, 128, 191 } },
            { "D3_three_animations_color_in_middle", 0.1, null, new[] { 230, 0, 26 }, new[] { 252, 230, 232 } },
            { "D3_three_animations_color_in_middle", 0.5, null, new[] { 128, 0, 128 }, new[] { 191, 128, 191 } },
            { "D4_two_animations_both_color", 0.1, null, new[] { 26, 26, 230 }, new[] { 48, 48, 232 } },
            { "D4_two_animations_both_color", 0.5, null, new[] { 128, 128, 128 }, new[] { 191, 191, 191 } },
            { "F1_normal_var_color_then_important_plain", 0.1, null, new[] { 255, 0, 0 }, new[] { 255, 26, 26 } },
            { "F1_normal_var_color_then_important_plain", 0.5, null, new[] { 255, 0, 0 }, new[] { 255, 128, 128 } },
            { "F2_inline_important_over_normal_var", 0.1, null, new[] { 255, 0, 0 }, new[] { 255, 26, 26 } },
            { "F2_inline_important_over_normal_var", 0.5, null, new[] { 255, 0, 0 }, new[] { 255, 128, 128 } },
            { "F3_important_inherit_over_normal_var", 0.1, null, new[] { 255, 136, 0 }, new[] { 255, 148, 26 } },
            { "F3_important_inherit_over_normal_var", 0.5, null, new[] { 255, 136, 0 }, new[] { 255, 196, 128 } },
            { "F4_layered_normal_var_unlayered_important", 0.1, null, new[] { 255, 0, 0 }, new[] { 255, 26, 26 } },
            { "F4_layered_normal_var_unlayered_important", 0.5, null, new[] { 255, 0, 0 }, new[] { 255, 128, 128 } },
            { "F5_important_initial_over_normal_var", 0.1, null, new[] { 0, 0, 0 }, new[] { 26, 26, 26 } },
            { "F5_important_initial_over_normal_var", 0.5, null, new[] { 0, 0, 0 }, new[] { 128, 128, 128 } },
            { "F6_important_plain_over_important_var", 0.1, null, new[] { 255, 0, 0 }, new[] { 255, 26, 26 } },
            { "F6_important_plain_over_important_var", 0.5, null, new[] { 255, 0, 0 }, new[] { 255, 128, 128 } },
            { "F7_animation_name_var_then_important_none", 0.1, 100.0, null, null },
            { "F7_animation_name_var_then_important_none", 0.5, 100.0, null, null },
            { "E3_important_var_color_also_animated", 0.1, null, new[] { 255, 0, 0 }, new[] { 255, 26, 26 } },
            { "E3_important_var_color_also_animated", 0.5, null, new[] { 255, 0, 0 }, new[] { 255, 128, 128 } },
            { "E4_important_inherit_also_animated", 0.1, null, new[] { 255, 136, 0 }, new[] { 255, 148, 26 } },
            { "E4_important_inherit_also_animated", 0.5, null, new[] { 255, 136, 0 }, new[] { 255, 196, 128 } },
            { "G1_registered_property_important", 0.1, 75.0, null, null },
            { "G1_registered_property_important", 0.5, 175.0, null, null },
            { "G4_custom_references_important_custom", 0.1, 75.0, null, null },
            { "G4_custom_references_important_custom", 0.5, 175.0, null, null },
            { "G4b_important_custom_references_important_custom", 0.1, 75.0, null, null },
            { "G4b_important_custom_references_important_custom", 0.5, 175.0, null, null },
            { "G5_important_custom_inherit", 0.1, 75.0, null, null },
            { "G5_important_custom_inherit", 0.5, 175.0, null, null },
            { "G8_important_color_unset", 0.1, null, new[] { 255, 136, 0 }, new[] { 255, 148, 26 } },
            { "G8_important_color_unset", 0.5, null, new[] { 255, 136, 0 }, new[] { 255, 196, 128 } },
            { "D5_color_pass_applies_width_once", 0.1, 55.0, new[] { 230, 0, 26 }, null },
            { "D5_color_pass_applies_width_once", 0.5, 75.0, new[] { 128, 0, 128 }, null },
            { "Z1_plain_currentcolor", 0.1, null, new[] { 0, 0, 255 }, new[] { 26, 26, 255 } },
            { "Z1_plain_currentcolor", 0.5, null, new[] { 0, 0, 255 }, new[] { 128, 128, 255 } },
            { "Z2_plain_var", 0.1, 55.0, null, null },
            { "Z2_plain_var", 0.5, 75.0, null, null },
        };

        [Theory]
        [MemberData(nameof(ChromeValues))]
        public async Task MatchesChrome(string name, double progress, double? width, int[]? color, int[]? background)
        {
            var (css, body) = Cases[name];
            var box = await BuildAsync($"<!DOCTYPE html><html><head><style>html, body {{ margin: 0 }} {css}</style></head><body>{body}</body></html>", progress);

            if (width is { } expectedWidth)
                Assert.InRange((box.ActualRight - box.Location.X) / 0.75, expectedWidth - 1, expectedWidth + 1);

            if (color is not null) AssertRgb(box.ActualColor, color, "color");
            if (background is not null) AssertRgb(box.ActualBackgroundColor, background, "background-color");
        }

        private static void AssertRgb(PaintColor actual, int[] expected, string what)
        {
            Assert.True(Math.Abs(actual.R - expected[0]) <= 2 && Math.Abs(actual.G - expected[1]) <= 2 && Math.Abs(actual.B - expected[2]) <= 2,
                $"{what}: expected rgb({expected[0]}, {expected[1]}, {expected[2]}), got rgb({actual.R}, {actual.G}, {actual.B})");
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
