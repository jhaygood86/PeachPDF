using PeachPDF.CSS;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Utils;
using PeachPDF.Tests.TestSupport;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// An <c>&lt;hr&gt;</c>'s rule is its own border, and paints through the ordinary border path
    /// (<c>BordersDrawHandler.DrawBoxBorders</c>) like every other block-level box. It used to go
    /// through a painter of its own that drew each side as one flat polygon filled with the raw
    /// declared color, which discarded <c>border-style</c> on a rule and only on a rule — every
    /// bevelled, patterned and double rule rendered solid (issue #1225).
    /// <para>
    /// The regression these pin is specifically a *silent* one: the old path painted something
    /// plausible for every style, so nothing crashed and no content-stream token went missing. The
    /// assertions therefore compare a rule against the zero-height <c>&lt;div&gt;</c> that is its
    /// exact equivalent, per this repo's painting-test conventions (assert the draw calls, not a
    /// substring of the PDF).
    /// </para>
    /// </summary>
    public class HrBorderStylePaintIntegrationTests
    {
        private static readonly RColor Gray = RColor.FromArgb(128, 128, 128);

        [Theory]
        [InlineData("inset")]
        [InlineData("outset")]
        [InlineData("groove")]
        [InlineData("ridge")]
        [InlineData("double")]
        [InlineData("dotted")]
        [InlineData("dashed")]
        public async Task Hr_PaintsEveryBorderStyle_ExactlyAsAZeroHeightDivDoes(string style)
        {
            // A zero-height <div> with the same border is the same box: same border widths, same
            // border box, no content. Whatever the rule paints differently from it is the rule's own
            // paint path inventing something, which is exactly the defect.
            var declaration = $"border: 2pt {style} rgb(128,128,128)";

            var rule = await PaintCallsOf($"<hr id='el' style='{declaration}'>");
            var equivalent = await PaintCallsOf($"<div id='el' style='height: 0; {declaration}'></div>");

            Assert.NotEmpty(rule);
            Assert.Equal(equivalent, rule);
        }

        [Fact]
        public async Task HrWithInsetBorder_ShadesItsTwoFacesTheWayABrowserDoes()
        {
            // The issue's own repro, pinned to the bytes Chrome paints for it: an inset rule darkens
            // its top/left face and lightens its bottom/right one, both derived from the single
            // declared color. It used to paint one flat rgb(128,128,128) slab.
            var (root, container) = await LayoutHarness.LayoutAsync(
                "<!DOCTYPE html><html><body><hr id='el' style='border: 2pt inset rgb(128,128,128)'></body></html>");

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, LayoutHarness.FindById(root, "el")!, g);

            var colors = g.FilledShapes.Select(shape => shape.Color).ToList();
            Assert.Equal(
                [BorderBevelColors.Shade(Gray, darken: true), BorderBevelColors.Shade(Gray, darken: false)],
                colors);

            // Chrome's own #2c2c2c / #d4d4d4 for a declared #808080 - see BorderBevelColors, whose
            // transforms were measured against it rather than recalled.
            Assert.Equal(RColor.FromArgb(44, 44, 44), colors[0]);
            Assert.Equal(RColor.FromArgb(212, 212, 212), colors[1]);
        }

        [Fact]
        public async Task HrWithDashedBorder_StrokesAPatternRatherThanFillingASlab()
        {
            // A patterned rule is drawn as a dashed stroke, not a filled band - the old painter had
            // no way to express that at all, so `dashed` came out solid.
            var (root, container) = await LayoutHarness.LayoutAsync(
                "<!DOCTYPE html><html><body><hr id='el' style='border: 2pt dashed rgb(128,128,128)'></body></html>");

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, LayoutHarness.FindById(root, "el")!, g);

            Assert.Empty(g.FilledShapes);
            Assert.Contains(g.Log.OfType<TestRecordingGraphics.DrawLineCall>(),
                line => line.DashPattern is { Count: > 0 });
        }

        [Fact]
        public async Task DefaultHr_KeepsItsCascadedBorderStyleThroughLayout()
        {
            // CssBoxHr.PerformLayoutImp used to overwrite BorderTopStyle/BorderBottomStyle with
            // `solid` during layout whenever the rule was thin - which is every default <hr>, since
            // the UA sheet's 1px is 0.75pt. Layout writing back to computed style is a layer
            // inversion, and it discarded an author's border-style on any thin rule as well.
            var (root, _) = await LayoutHarness.LayoutAsync(
                "<!DOCTYPE html><html><body><hr id='el'></body></html>");

            var hr = LayoutHarness.FindById(root, "el")!;

            Assert.Equal(LineStyle.Inset, hr.BorderTopStyle.Value);
            Assert.Equal(LineStyle.Inset, hr.BorderRightStyle.Value);
            Assert.Equal(LineStyle.Inset, hr.BorderBottomStyle.Value);
            Assert.Equal(LineStyle.Inset, hr.BorderLeftStyle.Value);
        }

        [Theory]
        [InlineData("<hr noshade>")]
        [InlineData("<hr color='red'>")]
        [InlineData("<hr noshade color='red'>")]
        public async Task HrWithAPresentationalColorAttribute_IsFlatNotEngraved(string markup)
        {
            // HTML Standard 15.3.6 pairs the UA sheet's `hr { border-style: inset }` with
            // `hr[color], hr[noshade] { border-style: solid }`. That second rule only started to
            // matter once the rule honored border-style at all - before that every rule painted flat,
            // so `noshade` was right by accident.
            var (root, _) = await LayoutHarness.LayoutAsync(
                $"<!DOCTYPE html><html><body>{markup.Replace("<hr", "<hr id='el'")}</body></html>");

            var hr = LayoutHarness.FindById(root, "el")!;

            Assert.Equal(LineStyle.Solid, hr.BorderTopStyle.Value);
            Assert.Equal(LineStyle.Solid, hr.BorderRightStyle.Value);
            Assert.Equal(LineStyle.Solid, hr.BorderBottomStyle.Value);
            Assert.Equal(LineStyle.Solid, hr.BorderLeftStyle.Value);
        }

        [Fact]
        public async Task AuthorStyledThinHr_KeepsItsOwnBorderStyleAndWidth()
        {
            // The same rewrite also forced border-top/bottom-width to 1px, so an author's own thin
            // rule lost both its style and its width.
            var (root, _) = await LayoutHarness.LayoutAsync(
                "<!DOCTYPE html><html><body>"
                + "<hr id='el' style='border-style: dotted; border-width: 0.5pt'></body></html>");

            var hr = LayoutHarness.FindById(root, "el")!;

            Assert.Equal(LineStyle.Dotted, hr.BorderTopStyle.Value);
            Assert.Equal(LineStyle.Dotted, hr.BorderBottomStyle.Value);
            Assert.Equal(0.5, hr.ActualBorderTopWidth, 3);
            Assert.Equal(0.5, hr.ActualBorderBottomWidth, 3);
        }

        [Fact]
        public async Task BorderlessHr_PaintsNothing()
        {
            // `border: none` used to be rewritten back into a 1px solid top and bottom border, so a
            // rule an author had explicitly switched off still painted two lines.
            var (root, container) = await LayoutHarness.LayoutAsync(
                "<!DOCTYPE html><html><body><hr id='el' style='border: none'></body></html>");

            var hr = LayoutHarness.FindById(root, "el")!;
            Assert.Equal(LineStyle.None, hr.BorderTopStyle.Value);

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, hr, g);

            Assert.Empty(g.FilledShapes);
            Assert.Empty(g.Log.OfType<TestRecordingGraphics.DrawLineCall>());
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        /// <summary>
        /// The ordered paint calls the box with id <c>el</c> makes, with every coordinate taken
        /// relative to that box's own border-box origin - the rule and the &lt;div&gt; it is compared
        /// against sit at different Y positions on the page, and where they sit is not the subject.
        /// </summary>
        private static async Task<List<string>> PaintCallsOf(string body)
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                $"<!DOCTYPE html><html><body>{body}</body></html>");

            var box = LayoutHarness.FindById(root, "el")!;
            var fragment = FragmentPaintHarness.FragmentOf(container, box);
            var origin = fragment.WholeBoxRect.Location;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, box, g);

            return
            [
                .. g.Log.Select(entry => entry switch
                {
                    TestRecordingGraphics.DrawPathCall path =>
                        $"path {path.Color} stroked={path.Stroked} "
                        + string.Join(" ", path.Points.Select(p => Offset(p, origin))),
                    TestRecordingGraphics.DrawPolygonCall polygon =>
                        $"polygon {polygon.Color} "
                        + string.Join(" ", polygon.Points.Select(p => Offset(p, origin))),
                    TestRecordingGraphics.DrawLineCall line =>
                        $"line {line.Color} w={line.Width:F3} dash={line.DashStyle}"
                        + $"[{string.Join(",", (line.DashPattern ?? []).Select(d => d.ToString("F3")))}] "
                        + $"{Offset(new RPoint(line.X1, line.Y1), origin)} "
                        + $"{Offset(new RPoint(line.X2, line.Y2), origin)}",
                    TestRecordingGraphics.DrawRectCall rect =>
                        $"rect {rect.Color} {Offset(new RPoint(rect.X, rect.Y), origin)} "
                        + $"{rect.Width:F3}x{rect.Height:F3}",
                    _ => entry.GetType().Name,
                })
            ];
        }

        private static string Offset(RPoint point, RPoint origin) =>
            $"({point.X - origin.X:F3},{point.Y - origin.Y:F3})";
    }
}
