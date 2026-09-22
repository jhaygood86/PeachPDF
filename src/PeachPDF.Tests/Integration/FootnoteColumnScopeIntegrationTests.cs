using PeachPDF.CSS;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Adapters;
using PeachPDF.Html.Core.Fragments;
using PeachPDF.Tests.TestSupport;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

using static PeachPDF.Tests.TestSupport.LayoutHarness;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Column-scoped footnote areas: <c>float: footnote</c> with CSS Page Floats'
    /// <c>float-reference: column</c> routes a note to the bottom of the column its call landed in,
    /// rather than the bottom of the page.
    /// </summary>
    public class FootnoteColumnScopeIntegrationTests
    {
        [Fact]
        public async Task FloatReferenceColumn_ReachesTheBox()
        {
            // Asserts the non-initial value deliberately: a keyword-only property tested with its own
            // initial value passes by coincidence even when PropertyFactory drops the whole declaration
            // before it ever reaches CssBox.
            var (root, _) = await LayoutAsync(Wrap("<p id='p' style='float-reference: column'>Text</p>"));

            var box = FindById(root, "p");
            Assert.NotNull(box);
            Assert.Equal(FloatReference.Column, box!.FloatReference.Value);
        }

        [Theory]
        [InlineData("inline", nameof(FloatReference.Inline))]
        [InlineData("column", nameof(FloatReference.Column))]
        [InlineData("region", nameof(FloatReference.Region))]
        [InlineData("page", nameof(FloatReference.Page))]
        public async Task FloatReference_ParsesEveryKeyword(string declared, string expected)
        {
            // The enum is internal, so the theory data names the member rather than passing it - xUnit
            // needs a public parameter type.
            var (root, _) = await LayoutAsync(Wrap($"<p id='p' style='float-reference: {declared}'>Text</p>"));

            Assert.Equal(expected, FindById(root, "p")!.FloatReference.Value.ToString());
        }

        [Fact]
        public async Task FloatReference_DefaultsToInline()
        {
            var (root, _) = await LayoutAsync(Wrap("<p id='p'>Text</p>"));

            Assert.Equal(FloatReference.Inline, FindById(root, "p")!.FloatReference.Value);
        }

        [Fact]
        public async Task FloatReference_AnUnknownValue_FallsBackToTheInitialValue()
        {
            var (root, _) = await LayoutAsync(Wrap("<p id='p' style='float-reference: nonsense'>Text</p>"));

            Assert.Equal(FloatReference.Inline, FindById(root, "p")!.FloatReference.Value);
        }

        [Fact]
        public async Task FloatReferenceColumn_PutsTheNoteInItsOwnColumnRatherThanThePage()
        {
            var (_, container) = await LayoutAsync(ColumnDoc("column"), pageHeight: 300);

            // No page-level area at all: every note on this slot was routed into a column.
            Assert.Empty(container.FootnoteAreaHeightsBySlot);
            Assert.Equal(2, container.FootnoteAreaHeightsByColumn.Count);

            var areas = AreasOf(container);
            Assert.Equal(2, areas.Count);
            Assert.All(areas, a => Assert.Equal(FootnoteAreaScope.Column, a.Scope));
        }

        [Fact]
        public async Task FloatReferenceColumn_TwoColumnsGetTwoDisjointAreas()
        {
            var (_, container) = await LayoutAsync(ColumnDoc("column"), pageHeight: 300);

            var areas = AreasOf(container);
            var first = areas[0].DividerRect;
            var second = areas[1].DividerRect;

            // Each area spans its own column, not the page - so the two are side by side and neither is
            // as wide as the content band.
            Assert.True(first.Right <= second.X + 0.01, $"areas overlap: {first.Right} vs {second.X}");
            Assert.True(first.Width < container.PageContentRightOf(container.PageTopOf(0)) - container.MarginLeft - 1);
            Assert.Equal(first.Width, second.Width, 0.01);

            // Both sit on the same row, at their own column's band bottom.
            Assert.Equal(first.Y, second.Y, 0.01);
        }

        [Fact]
        public async Task FloatReferenceColumn_PlacesTheAreaAtTheColumnBandBottomNotThePageBottom()
        {
            var (_, container) = await LayoutAsync(ColumnDoc("column"), pageHeight: 300);

            var record = container.ColumnFragmentainers.First(r => r.Slot == 0);
            var page = Assert.Single(container.FragmentTree!.Fragmentainers);
            var area = AreasOf(container)[0];

            // The reservation shortens the column, so its band bottom sits above the page's - and the
            // area is placed against the column's, which is the whole point.
            Assert.True(record.BandBottom < container.PageBottomOf(0) - 1,
                $"the column band {record.BandBottom} should stop above the page bottom {container.PageBottomOf(0)}");

            var areaBottom = record.BandBottom - page.LocalOriginY;
            Assert.True(area.DividerRect.Y < areaBottom && area.DividerRect.Y > areaBottom - 40,
                $"divider {area.DividerRect.Y} should sit just inside the column band ending at {areaBottom}");
        }

        [Fact]
        public async Task FloatReferenceColumn_NumbersAcrossThePageNotPerColumn()
        {
            // float-reference is a placement property; it must not restart the counter per column, or two
            // notes on one page would both be "1".
            var (_, container) = await LayoutAsync(ColumnDoc("column"), pageHeight: 300);

            Assert.Equal(1, CallOf(container, "fn1").Number);
            Assert.Equal(2, CallOf(container, "fn2").Number);
        }

        [Theory]
        [InlineData("page")]
        [InlineData("inline")]
        [InlineData("region")]
        public async Task FloatReference_OtherValuesInsideAMulticol_KeepThePageArea(string reference)
        {
            // inline is the INITIAL value, so this is also the guard that every existing footnote document
            // is unaffected; region has nothing to resolve against, since there are no CSS Regions here.
            var (_, container) = await LayoutAsync(ColumnDoc(reference), pageHeight: 300);

            Assert.Empty(container.FootnoteAreaHeightsByColumn);
            Assert.Single(container.FootnoteAreaHeightsBySlot);

            var area = Assert.Single(AreasOf(container));
            Assert.Equal(FootnoteAreaScope.Page, area.Scope);
        }

        [Fact]
        public async Task FloatReferenceColumn_OutsideAnyMulticol_FallsBackToThePageArea()
        {
            // css-page-floats: a column reference whose anchor is not inside a column falls back to the
            // anchor's line box - which, for a footnote with no inline note area, degenerates to the page.
            var html = "<!DOCTYPE html><html><head><style>body { margin: 0; font: 10pt sans-serif }</style>"
                + "</head><body><p>Plain text"
                + "<sup style='float: footnote; float-reference: column'>Not in a column at all.</sup>"
                + "</p></body></html>";

            var (_, container) = await LayoutAsync(html, pageHeight: 300);

            Assert.Empty(container.FootnoteAreaHeightsByColumn);
            var area = Assert.Single(AreasOf(container));
            Assert.Equal(FootnoteAreaScope.Page, area.Scope);
        }

        [Fact]
        public async Task AColumnScopedAndAPageScopedNoteOnOnePage_EachGetTheirOwnArea()
        {
            // The partition has to be exact: AttachFootnoteAreas translates every body out of document
            // space with a one-shot OffsetTop, so a call reachable through two areas would be offset
            // twice and paint in the wrong place.
            var html = "<!DOCTYPE html><html><head><style>"
                + "body { margin: 0; font: 10pt sans-serif }"
                + "#mc { column-count: 2; column-gap: 20pt; column-fill: auto }"
                + "#mc p { margin: 0 }"
                + "</style></head><body>"
                + "<p>Page note<sup id='fnPage' style='float: footnote'>A page-scoped note.</sup></p>"
                + "<div id='mc'>" + Filler(4)
                + "<p>Column note<sup id='fnCol' style='float: footnote; float-reference: column'>A column-scoped note.</sup></p>"
                + Filler(4) + "</div></body></html>";

            var (_, container) = await LayoutAsync(html, pageHeight: 300);

            Assert.Single(container.FootnoteAreaHeightsBySlot);
            Assert.Single(container.FootnoteAreaHeightsByColumn);

            var areas = AreasOf(container);
            Assert.Equal(2, areas.Count);
            Assert.Contains(areas, a => a.Scope == FootnoteAreaScope.Page);
            Assert.Contains(areas, a => a.Scope == FootnoteAreaScope.Column);

            // Each body appears in exactly one area, and each area drew exactly its own.
            Assert.All(areas, a => Assert.Single(a.Bodies));

            // The page area spans the whole content band; the column area does not.
            var pageArea = areas.First(a => a.Scope == FootnoteAreaScope.Page);
            var columnArea = areas.First(a => a.Scope == FootnoteAreaScope.Column);
            Assert.True(columnArea.DividerRect.Width < pageArea.DividerRect.Width - 1);
        }

        [Fact]
        public async Task FloatReferenceColumn_AnOverTallNote_DeclinesTheReservationAndStillFinishes()
        {
            // A reservation at or past the column's whole height would leave no usable space, so nothing
            // would be placed, the carry would never advance, and the container would defer page after
            // page into the monolithic last resort. Declining lets the area overflow instead.
            var longNote = string.Concat(Enumerable.Range(0, 60).Select(i => $"Sentence {i} of a very long footnote body. "));
            var html = "<!DOCTYPE html><html><head><style>"
                + "body { margin: 0; font: 10pt sans-serif }"
                + "#mc { column-count: 2; column-gap: 20pt; column-fill: auto }"
                + "#mc p { margin: 0 }"
                + "</style></head><body><div id='mc'>" + Filler(3)
                + $"<p>Note<sup style='float: footnote; float-reference: column'>{longNote}</sup></p>"
                + Filler(3) + "</div></body></html>";

            var (_, container) = await LayoutAsync(html, pageHeight: 260);

            // The document still paginates rather than looping or falling back to the monolithic path.
            Assert.NotEmpty(container.FragmentTree!.Fragmentainers);
            Assert.NotEmpty(container.FootnoteCalls);
        }

        [Fact]
        public async Task FloatReferenceColumn_WithColumnFillBalance_StillScopesToItsColumn()
        {
            var html = ColumnDoc("column").Replace("column-fill: auto", "column-fill: balance");

            var (_, container) = await LayoutAsync(html, pageHeight: 300);

            Assert.NotEmpty(container.FootnoteAreaHeightsByColumn);
            Assert.All(AreasOf(container), a => Assert.Equal(FootnoteAreaScope.Column, a.Scope));
        }

        [Fact]
        public async Task TwoColumnAreas_EachPaintItsOwnDividerAtItsOwnColumn()
        {
            // One ordered log across both areas, per this repo's testing conventions - a per-area count
            // could not tell "two dividers, one per column" apart from "the same divider twice".
            var (_, container) = await LayoutAsync(ColumnDoc("column"), pageHeight: 300);

            var areas = AreasOf(container);
            Assert.Equal(2, areas.Count);

            var g = new RecordingGraphics(new PdfSharpAdapter());
            foreach (var area in areas)
            {
                PdfGenerator.PaintFootnoteArea(g, new PdfSharpAdapter(), container, area);
            }

            var dividers = g.Log.Where(op => op.Kind == PaintOpKind.FillRect).ToList();
            Assert.Equal(2, dividers.Count);

            // Two distinct X positions, each matching its own area's rect - not one rect drawn twice.
            Assert.Equal(areas[0].DividerRect, dividers[0].Bounds);
            Assert.Equal(areas[1].DividerRect, dividers[1].Bounds);
            Assert.True(dividers[1].Bounds.X > dividers[0].Bounds.Right,
                $"the second divider should start right of the first; {dividers[1].Bounds.X} vs {dividers[0].Bounds.Right}");

            // Each area is drawn inside its own clip, so neither can bleed into the other's column.
            Assert.Equal(
                [PaintOpKind.PushClip, PaintOpKind.FillRect, PaintOpKind.PopClip,
                 PaintOpKind.PushClip, PaintOpKind.FillRect, PaintOpKind.PopClip],
                g.Log.Where(op => op.Kind is PaintOpKind.PushClip or PaintOpKind.FillRect or PaintOpKind.PopClip)
                     .Select(op => op.Kind)
                     .ToList());

            // ...and each area's own bodies are drawn inside that same clip, after its divider - the
            // ordering that makes "two areas" rather than "one area drawn twice".
            var pops = g.Log.Select((op, i) => (op, i)).Where(x => x.op.Kind == PaintOpKind.PopClip).Select(x => x.i).ToList();
            var fills = g.Log.Select((op, i) => (op, i)).Where(x => x.op.Kind == PaintOpKind.FillRect).Select(x => x.i).ToList();

            for (var area = 0; area < 2; area++)
            {
                var bodyOps = g.Log.Select((op, i) => (op, i))
                    .Where(x => x.i > fills[area] && x.i < pops[area] && x.op.Kind == PaintOpKind.DrawString)
                    .ToList();

                Assert.NotEmpty(bodyOps);
            }
        }

        private static IReadOnlyList<FootnoteAreaFragment> AreasOf(HtmlContainerInt container)
        {
            var page = Assert.Single(container.FragmentTree!.Fragmentainers);
            Assert.NotNull(page.FootnoteAreas);
            return page.FootnoteAreas!.OrderBy(a => a.DividerRect.X).ToList();
        }

        private static CssBoxFootnoteCall CallOf(HtmlContainerInt container, string id) =>
            container.FootnoteCalls.First(c => c.Body.HtmlTag?.TryGetAttribute("id") == id);

        private static string ColumnDoc(string reference) =>
            "<!DOCTYPE html><html><head><style>"
            + "body { margin: 0; font: 10pt sans-serif }"
            + "#mc { column-count: 2; column-gap: 20pt; column-fill: auto }"
            + "#mc p { margin: 0 }"
            + "</style></head><body>"
            + "<div id='mc'>"
            + Filler(4)
            + $"<p>Note A<sup id='fn1' style='float: footnote; float-reference: {reference}'>Column note one.</sup></p>"
            + Filler(14)
            + $"<p>Note B<sup id='fn2' style='float: footnote; float-reference: {reference}'>Column note two.</sup></p>"
            + Filler(4)
            + "</div></body></html>";

        private static string Filler(int count) =>
            string.Concat(Enumerable.Range(0, count).Select(i => $"<p>Filler line {i}.</p>"));

        [Fact]
        public async Task Supports_FloatReferenceColumn_IsHonoredAsARealCondition()
        {
            var html = "<!DOCTYPE html><html><head><style>"
                + "@supports (float-reference: column) { #p { color: rgb(1, 2, 3); } }"
                + "</style></head><body style='margin:0'><p id='p'>Text</p></body></html>";

            var (root, _) = await LayoutAsync(html);

            Assert.Equal("rgb(1, 2, 3)", FindById(root, "p")!.Color);
        }
    }
}
